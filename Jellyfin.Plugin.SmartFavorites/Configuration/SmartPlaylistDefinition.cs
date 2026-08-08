using System;
using System.Collections.ObjectModel;

namespace Jellyfin.Plugin.SmartFavorites.Configuration;

/// <summary>
/// One generated playlist: the rules a series must satisfy, and how the matching series
/// contribute episodes.
/// </summary>
public class SmartPlaylistDefinition
{
    /// <summary>
    /// Initializes a new instance of the <see cref="SmartPlaylistDefinition"/> class.
    /// </summary>
    public SmartPlaylistDefinition()
    {
        Name = "Up Next";
        Enabled = true;
        Match = MatchMode.All;
        Rules = [];
        EpisodesPerSeries = 1;
        MaxItems = 0;
        SortOrder = PlaylistSortOrder.LastWatched;
        IncludeSpecials = false;
        IncludeUnairedEpisodes = false;
        MakePublic = false;
        UserIds = [];
    }

    /// <summary>
    /// Gets or sets the playlist name, as it appears in the library. This is also how the
    /// generated playlist is located, so renaming leaves the old one behind untouched.
    /// </summary>
    public string Name { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether this playlist is generated at all.
    /// </summary>
    public bool Enabled { get; set; }

    /// <summary>
    /// Gets or sets whether every rule must match or only one.
    /// </summary>
    public MatchMode Match { get; set; }

    /// <summary>
    /// Gets or sets the rules a series must satisfy. No rules means every series qualifies.
    /// </summary>
#pragma warning disable CA2227 // XML-serialized configuration, must be a settable collection.
    public Collection<FilterRule> Rules { get; set; }
#pragma warning restore CA2227

    /// <summary>
    /// Gets or sets how many consecutive unwatched episodes to take from each matching series.
    /// </summary>
    public int EpisodesPerSeries { get; set; }

    /// <summary>
    /// Gets or sets the maximum number of episodes in the playlist. Zero means no limit.
    /// </summary>
    public int MaxItems { get; set; }

    /// <summary>
    /// Gets or sets the order in which series contribute their episodes.
    /// </summary>
    public PlaylistSortOrder SortOrder { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether episodes in season 0 are eligible.
    /// </summary>
    public bool IncludeSpecials { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether episodes that have not aired yet are eligible.
    /// </summary>
    public bool IncludeUnairedEpisodes { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether the playlist is visible to every user.
    /// </summary>
    public bool MakePublic { get; set; }

    /// <summary>
    /// Gets or sets the users this playlist is generated for. Empty means every user the
    /// plugin is configured to cover.
    /// </summary>
#pragma warning disable CA1819 // XML-serialized configuration, must be a settable array.
    public string[] UserIds { get; set; }
#pragma warning restore CA1819
}
