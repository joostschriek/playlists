using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Data.Enums;
using Jellyfin.Database.Implementations.Entities;
using Jellyfin.Database.Implementations.Enums;
using Jellyfin.Plugin.SmartFavorites.Configuration;
using MediaBrowser.Controller.Dto;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Entities.TV;
using MediaBrowser.Controller.Library;
using MediaBrowser.Controller.Playlists;
using MediaBrowser.Model.Entities;
using MediaBrowser.Model.Playlists;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.SmartFavorites;

/// <summary>
/// Rebuilds the "up next" playlist for a user from the series they have favorited.
/// </summary>
public sealed class SmartFavoritesPlaylistBuilder : IDisposable
{
    private readonly ILibraryManager _libraryManager;
    private readonly IUserManager _userManager;
    private readonly IUserDataManager _userDataManager;
    private readonly IPlaylistManager _playlistManager;
    private readonly ILogger<SmartFavoritesPlaylistBuilder> _logger;
    private readonly SemaphoreSlim _buildLock = new(1, 1);

    /// <summary>
    /// Initializes a new instance of the <see cref="SmartFavoritesPlaylistBuilder"/> class.
    /// </summary>
    /// <param name="libraryManager">Instance of the <see cref="ILibraryManager"/> interface.</param>
    /// <param name="userManager">Instance of the <see cref="IUserManager"/> interface.</param>
    /// <param name="userDataManager">Instance of the <see cref="IUserDataManager"/> interface.</param>
    /// <param name="playlistManager">Instance of the <see cref="IPlaylistManager"/> interface.</param>
    /// <param name="logger">Instance of the <see cref="ILogger{TCategoryName}"/> interface.</param>
    public SmartFavoritesPlaylistBuilder(
        ILibraryManager libraryManager,
        IUserManager userManager,
        IUserDataManager userDataManager,
        IPlaylistManager playlistManager,
        ILogger<SmartFavoritesPlaylistBuilder> logger)
    {
        _libraryManager = libraryManager;
        _userManager = userManager;
        _userDataManager = userDataManager;
        _playlistManager = playlistManager;
        _logger = logger;
    }

    private static PluginConfiguration Configuration =>
        Plugin.Instance?.Configuration ?? new PluginConfiguration();

    /// <summary>
    /// Gets the users the plugin is configured to maintain a playlist for.
    /// </summary>
    /// <returns>The users to build playlists for.</returns>
    public IReadOnlyList<User> GetTargetUsers()
    {
        var configured = Configuration.UserIds;
        var users = _userManager.GetUsers().ToList();

        if (configured.Length == 0)
        {
            return users;
        }

        var allowed = configured
            .Select(id => Guid.TryParse(id, out var parsed) ? parsed : Guid.Empty)
            .Where(id => !id.Equals(Guid.Empty))
            .ToHashSet();

        return users.Where(u => allowed.Contains(u.Id)).ToList();
    }

    /// <summary>
    /// Rebuilds the playlist for every configured user.
    /// </summary>
    /// <param name="progress">Progress reporter.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>A <see cref="Task"/> representing the asynchronous operation.</returns>
    public async Task BuildForAllUsersAsync(IProgress<double>? progress, CancellationToken cancellationToken)
    {
        var users = GetTargetUsers();
        var completed = 0;

        foreach (var user in users)
        {
            cancellationToken.ThrowIfCancellationRequested();
            await BuildForUserAsync(user, cancellationToken).ConfigureAwait(false);
            completed++;
            progress?.Report(completed * 100.0 / users.Count);
        }
    }

    /// <summary>
    /// Rebuilds the playlist for a single user.
    /// </summary>
    /// <param name="user">The user.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>A <see cref="Task"/> representing the asynchronous operation.</returns>
    public async Task BuildForUserAsync(User user, CancellationToken cancellationToken)
    {
        await _buildLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var config = Configuration;
            var episodeIds = CollectEpisodes(user, config);
            await SyncPlaylistAsync(user, episodeIds, config).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to build the favorites playlist for {Username}", user.Username);
        }
        finally
        {
            _buildLock.Release();
        }
    }

    private List<Guid> CollectEpisodes(User user, PluginConfiguration config)
    {
        var favorites = _libraryManager.GetItemList(new InternalItemsQuery(user)
        {
            IncludeItemTypes = [BaseItemKind.Series],
            IsFavorite = true,
            Recursive = true,
            DtoOptions = new DtoOptions(false)
        }).OfType<Series>().ToList();

        _logger.LogDebug("Found {Count} favorited series for {Username}", favorites.Count, user.Username);

        var take = Math.Max(1, config.EpisodesPerSeries);
        var entries = new List<(Series Series, DateTime? LastWatched, IReadOnlyList<Episode> Episodes)>();

        foreach (var series in favorites)
        {
            var episodes = GetNextUnwatchedEpisodes(user, series, config, take);
            if (episodes.Count == 0)
            {
                continue;
            }

            var lastWatched = config.SortOrder == PlaylistSortOrder.LastWatched
                ? GetLastWatchedDate(user, series)
                : null;

            entries.Add((series, lastWatched, episodes));
        }

        var ordered = config.SortOrder switch
        {
            PlaylistSortOrder.LastWatched => entries
                .OrderByDescending(e => e.LastWatched.HasValue)
                .ThenByDescending(e => e.LastWatched ?? DateTime.MinValue)
                .ThenBy(e => e.Series.SortName, StringComparer.OrdinalIgnoreCase),
            PlaylistSortOrder.EpisodeAirDate => entries
                .OrderBy(e => e.Episodes[0].PremiereDate ?? DateTime.MaxValue)
                .ThenBy(e => e.Series.SortName, StringComparer.OrdinalIgnoreCase),
            PlaylistSortOrder.Random => entries
                .OrderBy(_ => Random.Shared.Next())
                .ThenBy(e => e.Series.SortName, StringComparer.OrdinalIgnoreCase),
            _ => entries
                .OrderBy(e => e.Series.SortName, StringComparer.OrdinalIgnoreCase)
                .ThenBy(e => e.Series.Id)
        };

        return ordered.SelectMany(e => e.Episodes).Select(e => e.Id).ToList();
    }

    private List<Episode> GetNextUnwatchedEpisodes(User user, Series series, PluginConfiguration config, int take)
    {
        var query = new InternalItemsQuery(user)
        {
            AncestorIds = [series.Id],
            IncludeItemTypes = [BaseItemKind.Episode],
            IsPlayed = false,
            IsVirtualItem = false,
            Recursive = true,
            OrderBy =
            [
                (ItemSortBy.ParentIndexNumber, SortOrder.Ascending),
                (ItemSortBy.IndexNumber, SortOrder.Ascending)
            ],
            DtoOptions = new DtoOptions(false)
        };

        if (!config.IncludeSpecials)
        {
            query.ParentIndexNumberNotEquals = 0;
        }

        if (config.IncludeUnairedEpisodes)
        {
            query.Limit = take;
        }

        var episodes = _libraryManager.GetItemList(query).OfType<Episode>();

        if (!config.IncludeUnairedEpisodes)
        {
            var now = DateTime.UtcNow;
            episodes = episodes.Where(e => !e.PremiereDate.HasValue || e.PremiereDate.Value <= now);
        }

        return episodes.Take(take).ToList();
    }

    private DateTime? GetLastWatchedDate(User user, Series series)
    {
        var lastPlayed = _libraryManager.GetItemList(new InternalItemsQuery(user)
        {
            AncestorIds = [series.Id],
            IncludeItemTypes = [BaseItemKind.Episode],
            IsPlayed = true,
            Recursive = true,
            Limit = 1,
            OrderBy = [(ItemSortBy.DatePlayed, SortOrder.Descending)],
            DtoOptions = new DtoOptions(false)
        });

        return lastPlayed.Count == 0 ? null : _userDataManager.GetUserData(user, lastPlayed[0])?.LastPlayedDate;
    }

    private async Task SyncPlaylistAsync(User user, List<Guid> episodeIds, PluginConfiguration config)
    {
        var name = string.IsNullOrWhiteSpace(config.PlaylistName) ? "Favorites Up Next" : config.PlaylistName.Trim();

        var existing = _playlistManager.GetPlaylists(user.Id)
            .FirstOrDefault(p => p.OwnerUserId.Equals(user.Id)
                && string.Equals(p.Name, name, StringComparison.OrdinalIgnoreCase));

        if (existing is null)
        {
            if (episodeIds.Count == 0)
            {
                _logger.LogDebug("No unwatched episodes for {Username}, not creating a playlist", user.Username);
                return;
            }

            await _playlistManager.CreatePlaylist(new PlaylistCreationRequest
            {
                Name = name,
                ItemIdList = episodeIds,
                UserId = user.Id,
                MediaType = MediaType.Video,
                Public = config.MakePlaylistPublic
            }).ConfigureAwait(false);

            _logger.LogInformation("Created playlist {PlaylistName} for {Username} with {Count} episodes", name, user.Username, episodeIds.Count);
            return;
        }

        var current = existing.LinkedChildren
            .Select(c => c.ItemId)
            .Where(id => id.HasValue)
            .Select(id => id!.Value)
            .ToList();

        if (current.SequenceEqual(episodeIds))
        {
            _logger.LogDebug("Playlist {PlaylistName} for {Username} is already up to date", name, user.Username);
            return;
        }

        var playlistId = existing.Id.ToString("N", CultureInfo.InvariantCulture);

        if (current.Count > 0)
        {
            await _playlistManager.RemoveItemFromPlaylistAsync(
                playlistId,
                current.Select(id => id.ToString("N", CultureInfo.InvariantCulture))).ConfigureAwait(false);
        }

        if (episodeIds.Count > 0)
        {
            await _playlistManager.AddItemToPlaylistAsync(existing.Id, episodeIds, user.Id).ConfigureAwait(false);
        }

        _logger.LogInformation("Refreshed playlist {PlaylistName} for {Username}: {Count} episodes", name, user.Username, episodeIds.Count);
    }

    /// <inheritdoc />
    public void Dispose()
    {
        _buildLock.Dispose();
    }
}
