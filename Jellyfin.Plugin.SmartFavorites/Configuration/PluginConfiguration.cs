using System.Collections.ObjectModel;
using MediaBrowser.Model.Plugins;

namespace Jellyfin.Plugin.SmartFavorites.Configuration;

/// <summary>
/// Determines the order in which matching series contribute their episodes to a playlist.
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
    /// Most recently added series first.
    /// </summary>
    RecentlyAdded,

    /// <summary>
    /// Highest community rating first.
    /// </summary>
    CommunityRating,

    /// <summary>
    /// Shuffled on every refresh.
    /// </summary>
    Random
}

/// <summary>
/// The direction a playlist's sort is applied in.
/// </summary>
public enum SortDirection
{
    /// <summary>
    /// Whatever reads as natural for the chosen field: newest first for dates and highest
    /// first for ratings, oldest first for air date, A to Z for names. Existing
    /// configurations deserialize to this, so upgrading changes nothing.
    /// </summary>
    Default,

    /// <summary>
    /// Oldest, lowest or A-to-Z first.
    /// </summary>
    Ascending,

    /// <summary>
    /// Newest, highest or Z-to-A first.
    /// </summary>
    Descending
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
        Playlists = [];
        UserIds = [];
        PlaylistName = "Favorites Up Next";
        EpisodesPerSeries = 1;
        IncludeSpecials = false;
        IncludeUnairedEpisodes = false;
        MakePlaylistPublic = false;
        SortOrder = PlaylistSortOrder.LastWatched;
    }

    /// <summary>
    /// Gets or sets the playlists to generate.
    /// </summary>
#pragma warning disable CA2227 // XML-serialized configuration, must be a settable collection.
    public Collection<SmartPlaylistDefinition> Playlists { get; set; }
#pragma warning restore CA2227

    /// <summary>
    /// Gets or sets the users the plugin covers. An empty list means every user on the
    /// server. Individual playlists can narrow this further.
    /// </summary>
#pragma warning disable CA1819 // XML-serialized configuration, must be a settable array.
    public string[] UserIds { get; set; }
#pragma warning restore CA1819

    /// <summary>
    /// Gets or sets the pre-1.1 playlist name, kept so existing configuration files still
    /// deserialize and can be migrated into <see cref="Playlists"/>.
    /// </summary>
    public string PlaylistName { get; set; }

    /// <summary>
    /// Gets or sets the pre-1.1 episode count. See <see cref="PlaylistName"/>.
    /// </summary>
    public int EpisodesPerSeries { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether specials were included, pre-1.1. See <see cref="PlaylistName"/>.
    /// </summary>
    public bool IncludeSpecials { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether unaired episodes were included, pre-1.1. See <see cref="PlaylistName"/>.
    /// </summary>
    public bool IncludeUnairedEpisodes { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether playlists were public, pre-1.1. See <see cref="PlaylistName"/>.
    /// </summary>
    public bool MakePlaylistPublic { get; set; }

    /// <summary>
    /// Gets or sets the pre-1.1 sort order. See <see cref="PlaylistName"/>.
    /// </summary>
    public PlaylistSortOrder SortOrder { get; set; }

    /// <summary>
    /// Seeds <see cref="Playlists"/> from the pre-1.1 settings when it is empty. A fresh
    /// install lands here too, and gets the same favorites playlist the plugin has always
    /// produced.
    /// </summary>
    /// <returns><c>true</c> when a playlist was added and the configuration should be saved.</returns>
    public bool EnsureDefaultPlaylist()
    {
        if (Playlists.Count > 0)
        {
            return false;
        }

        Playlists.Add(new SmartPlaylistDefinition
        {
            Name = string.IsNullOrWhiteSpace(PlaylistName) ? "Favorites Up Next" : PlaylistName,
            Match = MatchMode.All,
            Rules =
            [
                new FilterRule { Field = RuleField.IsFavorite, Operator = RuleOperator.Is, Value = "true" }
            ],
            EpisodesPerSeries = EpisodesPerSeries,
            SortOrder = SortOrder,
            IncludeSpecials = IncludeSpecials,
            IncludeUnairedEpisodes = IncludeUnairedEpisodes,
            MakePublic = MakePlaylistPublic
        });

        return true;
    }
}
