using System;
using System.Collections.Generic;
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
/// Rebuilds each configured smart playlist from the series that satisfy its rules.
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
    /// Gets the users the plugin is configured to maintain playlists for.
    /// </summary>
    /// <returns>The users to build playlists for.</returns>
    public IReadOnlyList<User> GetTargetUsers()
    {
        var users = _userManager.GetUsers().ToList();
        var allowed = ParseUserIds(Configuration.UserIds);

        return allowed.Count == 0 ? users : users.Where(u => allowed.Contains(u.Id)).ToList();
    }

    private static HashSet<Guid> ParseUserIds(string[]? ids)
    {
        if (ids is null)
        {
            return [];
        }

        return ids
            .Select(id => Guid.TryParse(id, out var parsed) ? parsed : Guid.Empty)
            .Where(id => !id.Equals(Guid.Empty))
            .ToHashSet();
    }

    /// <summary>
    /// Rebuilds every playlist for every configured user.
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
    /// Rebuilds every playlist that applies to a single user.
    /// </summary>
    /// <param name="user">The user.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>A <see cref="Task"/> representing the asynchronous operation.</returns>
    public async Task BuildForUserAsync(User user, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(user);

        await _buildLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var definitions = Configuration.Playlists
                .Where(d => d.Enabled && AppliesTo(d, user))
                .ToList();

            if (definitions.Count == 0)
            {
                return;
            }

            // Every playlist filters the same set of series, so load and annotate it once.
            var facts = LoadSeriesFacts(user, definitions);

            foreach (var definition in definitions)
            {
                cancellationToken.ThrowIfCancellationRequested();

                try
                {
                    var episodeIds = CollectEpisodes(user, definition, facts);
                    await SyncPlaylistAsync(user, definition, episodeIds).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Failed to build playlist {PlaylistName} for {Username}", definition.Name, user.Username);
                }
            }
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to build playlists for {Username}", user.Username);
        }
        finally
        {
            _buildLock.Release();
        }
    }

    private static bool AppliesTo(SmartPlaylistDefinition definition, User user)
    {
        var allowed = ParseUserIds(definition.UserIds);
        return allowed.Count == 0 || allowed.Contains(user.Id);
    }

    /// <summary>
    /// Loads the candidate series and the user-data each rule set needs. When every enabled
    /// playlist requires a favorite, the filter is pushed into the query so large libraries
    /// are not enumerated in full.
    /// </summary>
    private List<SeriesFacts> LoadSeriesFacts(User user, IReadOnlyList<SmartPlaylistDefinition> definitions)
    {
        var query = new InternalItemsQuery(user)
        {
            IncludeItemTypes = [BaseItemKind.Series],
            Recursive = true,
            DtoOptions = new DtoOptions(false)
        };

        if (definitions.All(RequiresFavorite))
        {
            query.IsFavorite = true;
        }

        var series = _libraryManager.GetItemList(query).OfType<Series>().ToList();
        _logger.LogDebug("Evaluating {Count} series for {Username}", series.Count, user.Username);

        var needsLastWatched = definitions.Any(d =>
            d.SortOrder == PlaylistSortOrder.LastWatched
            || d.Rules.Any(r => r.Field == RuleField.DaysSinceLastWatched));

        var facts = new List<SeriesFacts>(series.Count);
        foreach (var item in series)
        {
            var isFavorite = _userDataManager.GetUserData(user, item)?.IsFavorite ?? false;
            var lastWatched = needsLastWatched ? GetLastWatchedDate(user, item) : null;
            facts.Add(new SeriesFacts(item, isFavorite, lastWatched));
        }

        return facts;
    }

    /// <summary>
    /// Whether a definition can only ever match favorited series, which makes pushing
    /// IsFavorite into the query safe.
    /// </summary>
    private static bool RequiresFavorite(SmartPlaylistDefinition definition)
    {
        if (definition.Rules.Count == 0 || definition.Match != MatchMode.All)
        {
            return false;
        }

        return definition.Rules.Any(r =>
            r.Field == RuleField.IsFavorite
            && r.Operator is RuleOperator.Is or RuleOperator.Contains
            && string.Equals(r.Value?.Trim(), "true", StringComparison.OrdinalIgnoreCase));
    }

    private List<Guid> CollectEpisodes(User user, SmartPlaylistDefinition definition, IReadOnlyList<SeriesFacts> allSeries)
    {
        var take = Math.Max(1, definition.EpisodesPerSeries);
        var entries = new List<(SeriesFacts Facts, IReadOnlyList<Episode> Episodes)>();

        foreach (var facts in allSeries)
        {
            if (!RuleEvaluator.Matches(definition, facts))
            {
                continue;
            }

            var episodes = GetNextUnwatchedEpisodes(user, facts.Series, definition, take);
            if (episodes.Count > 0)
            {
                entries.Add((facts, episodes));
            }
        }

        _logger.LogDebug(
            "{Count} series matched {PlaylistName} for {Username}",
            entries.Count,
            definition.Name,
            user.Username);

        var descending = IsDescending(definition);

        var ordered = definition.SortOrder switch
        {
            PlaylistSortOrder.LastWatched =>
                SortByValue(entries, e => e.Facts.LastWatchedUtc, descending)
                    .ThenBy(e => e.Facts.Series.SortName, StringComparer.OrdinalIgnoreCase),
            PlaylistSortOrder.EpisodeAirDate =>
                SortByValue(entries, e => e.Episodes[0].PremiereDate, descending)
                    .ThenBy(e => e.Facts.Series.SortName, StringComparer.OrdinalIgnoreCase),
            PlaylistSortOrder.RecentlyAdded =>
                SortByValue(entries, e => (DateTime?)e.Facts.Series.DateCreated, descending)
                    .ThenBy(e => e.Facts.Series.SortName, StringComparer.OrdinalIgnoreCase),
            PlaylistSortOrder.CommunityRating =>
                SortByValue(entries, e => e.Facts.Series.CommunityRating, descending)
                    .ThenBy(e => e.Facts.Series.SortName, StringComparer.OrdinalIgnoreCase),
            PlaylistSortOrder.Random => entries
                .OrderBy(_ => Random.Shared.Next())
                .ThenBy(e => e.Facts.Series.SortName, StringComparer.OrdinalIgnoreCase),
            _ => (descending
                    ? entries.OrderByDescending(e => e.Facts.Series.SortName, StringComparer.OrdinalIgnoreCase)
                    : entries.OrderBy(e => e.Facts.Series.SortName, StringComparer.OrdinalIgnoreCase))
                .ThenBy(e => e.Facts.Series.Id)
        };

        var ids = ordered.SelectMany(e => e.Episodes).Select(e => e.Id);

        return definition.MaxItems > 0 ? ids.Take(definition.MaxItems).ToList() : ids.ToList();
    }

    /// <summary>
    /// Resolves <see cref="SortDirection.Default"/> to whichever direction reads as natural
    /// for the field, which is what the playlist did before the direction was configurable.
    /// </summary>
    private static bool IsDescending(SmartPlaylistDefinition definition)
    {
        return definition.SortDirection switch
        {
            SortDirection.Ascending => false,
            SortDirection.Descending => true,
            _ => definition.SortOrder is PlaylistSortOrder.LastWatched
                or PlaylistSortOrder.RecentlyAdded
                or PlaylistSortOrder.CommunityRating
        };
    }

    /// <summary>
    /// Sorts on a value that may be missing, keeping series without one at the end in both
    /// directions — reversing the sort should not promote everything unrated to the top.
    /// </summary>
    private static IOrderedEnumerable<TEntry> SortByValue<TEntry, TKey>(
        List<TEntry> entries,
        Func<TEntry, TKey?> key,
        bool descending)
        where TKey : struct
    {
        var present = entries.OrderByDescending(e => key(e).HasValue);

        return descending
            ? present.ThenByDescending(e => key(e) ?? default)
            : present.ThenBy(e => key(e) ?? default);
    }

    private List<Episode> GetNextUnwatchedEpisodes(User user, Series series, SmartPlaylistDefinition definition, int take)
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

        if (!definition.IncludeSpecials)
        {
            query.ParentIndexNumberNotEquals = 0;
        }

        if (definition.IncludeUnairedEpisodes)
        {
            query.Limit = take;
        }

        var episodes = _libraryManager.GetItemList(query).OfType<Episode>();

        if (!definition.IncludeUnairedEpisodes)
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

    private async Task SyncPlaylistAsync(User user, SmartPlaylistDefinition definition, List<Guid> episodeIds)
    {
        var name = string.IsNullOrWhiteSpace(definition.Name) ? "Up Next" : definition.Name.Trim();

        var owned = _playlistManager.GetPlaylists(user.Id)
            .Where(p => p.OwnerUserId.Equals(user.Id)
                && string.Equals(p.Name, name, StringComparison.OrdinalIgnoreCase))
            .ToList();

        if (owned.Count > 1)
        {
            _logger.LogWarning(
                "{Count} playlists named {PlaylistName} belong to {Username}. Only the first is maintained; rename or delete the others.",
                owned.Count,
                name,
                user.Username);
        }

        var existing = owned.FirstOrDefault();

        if (existing is null)
        {
            if (episodeIds.Count == 0)
            {
                _logger.LogDebug("Nothing matched {PlaylistName} for {Username}, not creating a playlist", name, user.Username);
                return;
            }

            await _playlistManager.CreatePlaylist(new PlaylistCreationRequest
            {
                Name = name,
                ItemIdList = episodeIds,
                UserId = user.Id,
                MediaType = MediaType.Video,
                Public = definition.MakePublic
            }).ConfigureAwait(false);

            _logger.LogInformation("Created playlist {PlaylistName} for {Username} with {Count} episodes", name, user.Username, episodeIds.Count);
            return;
        }

        // Read the stored playlist rather than trusting the listing above, which can hand
        // back an instance whose children lag what is on disk. Deciding "already up to
        // date" from a stale copy is how a watched episode survives a refresh.
        var stored = _libraryManager.GetItemById(existing.Id) as Playlist ?? existing;
        var current = stored.LinkedChildren
            .Select(c => c.ItemId)
            .Where(id => id.HasValue)
            .Select(id => id!.Value)
            .ToList();

        if (current.SequenceEqual(episodeIds))
        {
            _logger.LogDebug("Playlist {PlaylistName} for {Username} is already up to date", name, user.Username);
            return;
        }

        // Replace the contents outright. UpdatePlaylist clears LinkedChildren server-side
        // and re-adds, so an episode that is no longer eligible cannot survive; the former
        // remove-then-add pass relied on entry matching and left stale items behind when it
        // did not line up.
        await _playlistManager.UpdatePlaylist(new PlaylistUpdateRequest
        {
            Id = existing.Id,
            UserId = user.Id,
            Ids = episodeIds
        }).ConfigureAwait(false);

        var dropped = current.Except(episodeIds).Count();
        _logger.LogInformation(
            "Refreshed playlist {PlaylistName} for {Username}: {Count} episodes ({Dropped} no longer eligible)",
            name,
            user.Username,
            episodeIds.Count,
            dropped);
    }

    /// <inheritdoc />
    public void Dispose()
    {
        _buildLock.Dispose();
    }
}
