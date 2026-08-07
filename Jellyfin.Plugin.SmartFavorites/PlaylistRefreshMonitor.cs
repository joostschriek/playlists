using System;
using System.Collections.Concurrent;
using System.Threading;
using System.Threading.Tasks;
using MediaBrowser.Controller.Entities.TV;
using MediaBrowser.Controller.Library;
using MediaBrowser.Model.Entities;
using MediaBrowser.Model.Plugins;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.SmartFavorites;

/// <summary>
/// Rebuilds playlists in response to the things that can change their contents, so the
/// scheduled task is only a backstop rather than the mechanism.
/// </summary>
public sealed class PlaylistRefreshMonitor : IHostedService, IDisposable
{
    /// <summary>Quiet period after the last event before rebuilding.</summary>
    private static readonly TimeSpan _debounce = TimeSpan.FromSeconds(15);

    /// <summary>
    /// Upper bound on how long queued work may be deferred. A library scan emits events
    /// continuously, which would otherwise keep pushing the debounce window back forever.
    /// </summary>
    private static readonly TimeSpan _maxDelay = TimeSpan.FromMinutes(5);

    private readonly IUserDataManager _userDataManager;
    private readonly IUserManager _userManager;
    private readonly ILibraryManager _libraryManager;
    private readonly SmartFavoritesPlaylistBuilder _builder;
    private readonly ILogger<PlaylistRefreshMonitor> _logger;

    private readonly ConcurrentDictionary<Guid, byte> _pendingUsers = new();
    private readonly Lock _scheduleLock = new();
    private readonly Timer _timer;

    private DateTime? _firstQueuedUtc;
    private int _allUsersPending;

    /// <summary>
    /// Initializes a new instance of the <see cref="PlaylistRefreshMonitor"/> class.
    /// </summary>
    /// <param name="userDataManager">Instance of the <see cref="IUserDataManager"/> interface.</param>
    /// <param name="userManager">Instance of the <see cref="IUserManager"/> interface.</param>
    /// <param name="libraryManager">Instance of the <see cref="ILibraryManager"/> interface.</param>
    /// <param name="builder">The playlist builder.</param>
    /// <param name="logger">Instance of the <see cref="ILogger{TCategoryName}"/> interface.</param>
    public PlaylistRefreshMonitor(
        IUserDataManager userDataManager,
        IUserManager userManager,
        ILibraryManager libraryManager,
        SmartFavoritesPlaylistBuilder builder,
        ILogger<PlaylistRefreshMonitor> logger)
    {
        _userDataManager = userDataManager;
        _userManager = userManager;
        _libraryManager = libraryManager;
        _builder = builder;
        _logger = logger;
        _timer = new Timer(_ => _ = FlushAsync(), null, Timeout.InfiniteTimeSpan, Timeout.InfiniteTimeSpan);
    }

    /// <inheritdoc />
    public Task StartAsync(CancellationToken cancellationToken)
    {
        _userDataManager.UserDataSaved += OnUserDataSaved;
        _libraryManager.ItemAdded += OnLibraryChanged;
        _libraryManager.ItemRemoved += OnLibraryChanged;

        if (Plugin.Instance is not null)
        {
            Plugin.Instance.ConfigurationChanged += OnConfigurationChanged;
        }

        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task StopAsync(CancellationToken cancellationToken)
    {
        _userDataManager.UserDataSaved -= OnUserDataSaved;
        _libraryManager.ItemAdded -= OnLibraryChanged;
        _libraryManager.ItemRemoved -= OnLibraryChanged;

        if (Plugin.Instance is not null)
        {
            Plugin.Instance.ConfigurationChanged -= OnConfigurationChanged;
        }

        return Task.CompletedTask;
    }

    /// <summary>
    /// Favoriting a series, or finishing an episode, only affects the user who did it.
    /// </summary>
    private void OnUserDataSaved(object? sender, UserDataSaveEventArgs e)
    {
        var relevant = e.SaveReason switch
        {
            UserDataSaveReason.UpdateUserRating or UserDataSaveReason.UpdateUserData => e.Item is Series or Episode,
            UserDataSaveReason.PlaybackFinished or UserDataSaveReason.TogglePlayed => e.Item is Episode,
            _ => false
        };

        if (relevant)
        {
            _pendingUsers[e.UserId] = 0;
            Schedule();
        }
    }

    /// <summary>
    /// A new or deleted episode can change what is "next up" for anyone who favorited its
    /// series, and no user-data event fires for it.
    /// </summary>
    private void OnLibraryChanged(object? sender, ItemChangeEventArgs e)
    {
        if (e.Item is Episode)
        {
            QueueAllUsers();
        }
    }

    private void OnConfigurationChanged(object? sender, BasePluginConfiguration e)
    {
        _logger.LogDebug("Configuration changed, queueing a rebuild");
        QueueAllUsers();
    }

    /// <summary>
    /// Defers resolving the user list until the flush, so a scan emitting thousands of
    /// events does not hit the user store once per event.
    /// </summary>
    private void QueueAllUsers()
    {
        Interlocked.Exchange(ref _allUsersPending, 1);
        Schedule();
    }

    private void Schedule()
    {
        lock (_scheduleLock)
        {
            var now = DateTime.UtcNow;
            _firstQueuedUtc ??= now;

            var remaining = _maxDelay - (now - _firstQueuedUtc.Value);
            var wait = remaining < _debounce ? remaining : _debounce;
            if (wait < TimeSpan.Zero)
            {
                wait = TimeSpan.Zero;
            }

            _timer.Change(wait, Timeout.InfiniteTimeSpan);
        }
    }

    private async Task FlushAsync()
    {
        lock (_scheduleLock)
        {
            _firstQueuedUtc = null;
        }

        if (Interlocked.Exchange(ref _allUsersPending, 0) == 1)
        {
            try
            {
                foreach (var user in _builder.GetTargetUsers())
                {
                    _pendingUsers[user.Id] = 0;
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Could not resolve the users to refresh");
            }
        }

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
