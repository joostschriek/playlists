using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using MediaBrowser.Model.Tasks;

namespace Jellyfin.Plugin.SmartFavorites.ScheduledTasks;

/// <summary>
/// Rebuilds every user's favorites playlist.
/// </summary>
public class RefreshFavoritesPlaylistTask : IScheduledTask, IConfigurableScheduledTask
{
    private readonly SmartFavoritesPlaylistBuilder _builder;

    /// <summary>
    /// Initializes a new instance of the <see cref="RefreshFavoritesPlaylistTask"/> class.
    /// </summary>
    /// <param name="builder">The playlist builder.</param>
    public RefreshFavoritesPlaylistTask(SmartFavoritesPlaylistBuilder builder)
    {
        _builder = builder;
    }

    /// <inheritdoc />
    public string Name => "Refresh favorites playlist";

    /// <inheritdoc />
    public string Key => "SmartFavoritesRefresh";

    /// <inheritdoc />
    public string Description => "Rebuilds the up-next playlist from the series each user has favorited.";

    /// <inheritdoc />
    public string Category => "Smart Favorites";

    /// <inheritdoc />
    public bool IsHidden => false;

    /// <inheritdoc />
    public bool IsEnabled => true;

    /// <inheritdoc />
    public bool IsLogged => true;

    /// <inheritdoc />
    public Task ExecuteAsync(IProgress<double> progress, CancellationToken cancellationToken)
        => _builder.BuildForAllUsersAsync(progress, cancellationToken);

    /// <inheritdoc />
    /// <remarks>
    /// Favorites, playback and library changes are picked up live by
    /// <see cref="PlaylistRefreshMonitor"/>; these triggers only cover events missed
    /// while the server was down.
    /// </remarks>
    public IEnumerable<TaskTriggerInfo> GetDefaultTriggers()
    {
        yield return new TaskTriggerInfo
        {
            Type = TaskTriggerInfoType.StartupTrigger
        };

        yield return new TaskTriggerInfo
        {
            Type = TaskTriggerInfoType.IntervalTrigger,
            IntervalTicks = TimeSpan.FromHours(24).Ticks
        };
    }
}
