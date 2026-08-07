using System;
using System.Collections.Concurrent;
using System.Threading;
using System.Threading.Tasks;
using MediaBrowser.Controller.Entities.TV;
using MediaBrowser.Controller.Library;
using MediaBrowser.Model.Entities;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.SmartFavorites;

/// <summary>
/// Rebuilds a user's playlist shortly after they favorite a series or finish an episode,
/// so the playlist does not sit stale until the next scheduled run.
/// </summary>
public sealed class FavoriteChangeMonitor : IHostedService, IDisposable
{
    private static readonly TimeSpan _debounce = TimeSpan.FromSeconds(15);

    private readonly IUserDataManager _userDataManager;
    private readonly IUserManager _userManager;
    private readonly SmartFavoritesPlaylistBuilder _builder;
    private readonly ILogger<FavoriteChangeMonitor> _logger;
    private readonly ConcurrentDictionary<Guid, byte> _pendingUsers = new();
    private readonly Timer _timer;

    /// <summary>
    /// Initializes a new instance of the <see cref="FavoriteChangeMonitor"/> class.
    /// </summary>
    /// <param name="userDataManager">Instance of the <see cref="IUserDataManager"/> interface.</param>
    /// <param name="userManager">Instance of the <see cref="IUserManager"/> interface.</param>
    /// <param name="builder">The playlist builder.</param>
    /// <param name="logger">Instance of the <see cref="ILogger{TCategoryName}"/> interface.</param>
    public FavoriteChangeMonitor(
        IUserDataManager userDataManager,
        IUserManager userManager,
        SmartFavoritesPlaylistBuilder builder,
        ILogger<FavoriteChangeMonitor> logger)
    {
        _userDataManager = userDataManager;
        _userManager = userManager;
        _builder = builder;
        _logger = logger;
        _timer = new Timer(_ => _ = FlushAsync(), null, Timeout.InfiniteTimeSpan, Timeout.InfiniteTimeSpan);
    }

    /// <inheritdoc />
    public Task StartAsync(CancellationToken cancellationToken)
    {
        _userDataManager.UserDataSaved += OnUserDataSaved;
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task StopAsync(CancellationToken cancellationToken)
    {
        _userDataManager.UserDataSaved -= OnUserDataSaved;
        return Task.CompletedTask;
    }

    private void OnUserDataSaved(object? sender, UserDataSaveEventArgs e)
    {
        var relevant = e.SaveReason switch
        {
            UserDataSaveReason.UpdateUserRating or UserDataSaveReason.UpdateUserData => e.Item is Series or Episode,
            UserDataSaveReason.PlaybackFinished or UserDataSaveReason.TogglePlayed => e.Item is Episode,
            _ => false
        };

        if (!relevant)
        {
            return;
        }

        _pendingUsers[e.UserId] = 0;
        _timer.Change(_debounce, Timeout.InfiniteTimeSpan);
    }

    private async Task FlushAsync()
    {
        foreach (var userId in _pendingUsers.Keys)
        {
            try
            {
                if (!_pendingUsers.TryRemove(userId, out _))
                {
                    continue;
                }

                var user = _userManager.GetUserById(userId);
                if (user is not null)
                {
                    await _builder.BuildForUserAsync(user, CancellationToken.None).ConfigureAwait(false);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Live refresh failed for user {UserId}", userId);
            }
        }
    }

    /// <inheritdoc />
    public void Dispose()
    {
        _timer.Dispose();
    }
}
