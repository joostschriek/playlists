using MediaBrowser.Model.Plugins;

namespace Jellyfin.Plugin.SmartFavorites.Configuration;

/// <summary>
/// Determines the order in which favorited series contribute their episodes to the playlist.
/// </summary>
public enum PlaylistSortOrder
{
    /// <summary>
    /// Alphabetically by series sort name.
    /// </summary>
    SeriesName,

    /// <summary>
    /// Series watched most recently come first, series never watched come last.
    /// </summary>
    LastWatched,

    /// <summary>
    /// Oldest air date of the next episode first.
    /// </summary>
    EpisodeAirDate,

    /// <summary>
    /// Shuffled on every refresh.
    /// </summary>
    Random
}

/// <summary>
/// Plugin configuration.
/// </summary>
public class PluginConfiguration : BasePluginConfiguration
{
    /// <summary>
    /// Initializes a new instance of the <see cref="PluginConfiguration"/> class.
    /// </summary>
    public PluginConfiguration()
    {
        PlaylistName = "Favorites Up Next";
        EpisodesPerSeries = 1;
        IncludeSpecials = false;
        IncludeUnairedEpisodes = false;
        MakePlaylistPublic = false;
        SortOrder = PlaylistSortOrder.LastWatched;
        UserIds = [];
    }

    /// <summary>
    /// Gets or sets the name of the generated playlist. One playlist with this name is maintained per user.
    /// </summary>
    public string PlaylistName { get; set; }

    /// <summary>
    /// Gets or sets how many consecutive unwatched episodes to take from each favorited series.
    /// </summary>
    public int EpisodesPerSeries { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether episodes in season 0 are eligible.
    /// </summary>
    public bool IncludeSpecials { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether episodes whose premiere date is in the future are eligible.
    /// </summary>
    public bool IncludeUnairedEpisodes { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether the generated playlists are visible to all users.
    /// </summary>
    public bool MakePlaylistPublic { get; set; }

    /// <summary>
    /// Gets or sets the order in which series contribute their episodes to the playlist.
    /// </summary>
    public PlaylistSortOrder SortOrder { get; set; }

    /// <summary>
    /// Gets or sets the users to generate playlists for. An empty list means every user on the server.
    /// </summary>
#pragma warning disable CA1819 // XML-serialized configuration, must be a settable array.
    public string[] UserIds { get; set; }
#pragma warning restore CA1819
}
