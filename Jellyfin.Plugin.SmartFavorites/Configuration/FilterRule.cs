namespace Jellyfin.Plugin.SmartFavorites.Configuration;

/// <summary>
/// Whether every rule in a playlist must match, or only one of them.
/// </summary>
public enum MatchMode
{
    /// <summary>
    /// A series qualifies only when every rule matches.
    /// </summary>
    All,

    /// <summary>
    /// A series qualifies when at least one rule matches.
    /// </summary>
    Any
}

/// <summary>
/// The series property a rule inspects.
/// </summary>
public enum RuleField
{
    /// <summary>
    /// Whether the user has favorited the series. Compares against "true" or "false".
    /// </summary>
    IsFavorite,

    /// <summary>
    /// A genre on the series. Matches when any one genre satisfies the rule.
    /// </summary>
    Genre,

    /// <summary>
    /// A studio on the series. Matches when any one studio satisfies the rule.
    /// </summary>
    Studio,

    /// <summary>
    /// A tag on the series. Matches when any one tag satisfies the rule.
    /// </summary>
    Tag,

    /// <summary>
    /// The series name.
    /// </summary>
    Name,

    /// <summary>
    /// The content rating, for example TV-14.
    /// </summary>
    OfficialRating,

    /// <summary>
    /// Continuing, Ended or Unreleased.
    /// </summary>
    SeriesStatus,

    /// <summary>
    /// The community rating, 0 to 10.
    /// </summary>
    CommunityRating,

    /// <summary>
    /// The critic rating, 0 to 100.
    /// </summary>
    CriticRating,

    /// <summary>
    /// The year the series was first produced.
    /// </summary>
    ProductionYear,

    /// <summary>
    /// How long ago the series was added to the library, in days. Use "less than 30" for
    /// recently added series.
    /// </summary>
    DaysSinceAdded,

    /// <summary>
    /// How long ago the user last watched an episode, in days. Series never watched never
    /// match a numeric comparison.
    /// </summary>
    DaysSinceLastWatched
}

/// <summary>
/// How a rule compares the field against its value.
/// </summary>
public enum RuleOperator
{
    /// <summary>
    /// Exact, case-insensitive equality.
    /// </summary>
    Is,

    /// <summary>
    /// Negated <see cref="Is"/>.
    /// </summary>
    IsNot,

    /// <summary>
    /// Case-insensitive substring match.
    /// </summary>
    Contains,

    /// <summary>
    /// Negated <see cref="Contains"/>.
    /// </summary>
    DoesNotContain,

    /// <summary>
    /// Numeric greater-than. Only meaningful for numeric fields.
    /// </summary>
    GreaterThan,

    /// <summary>
    /// Numeric less-than. Only meaningful for numeric fields.
    /// </summary>
    LessThan
}

/// <summary>
/// A single condition a series must satisfy to contribute episodes to a playlist.
/// </summary>
public class FilterRule
{
    /// <summary>
    /// Initializes a new instance of the <see cref="FilterRule"/> class.
    /// </summary>
    public FilterRule()
    {
        Field = RuleField.IsFavorite;
        Operator = RuleOperator.Is;
        Value = "true";
    }

    /// <summary>
    /// Gets or sets the series property this rule inspects.
    /// </summary>
    public RuleField Field { get; set; }

    /// <summary>
    /// Gets or sets how the field is compared against <see cref="Value"/>.
    /// </summary>
    public RuleOperator Operator { get; set; }

    /// <summary>
    /// Gets or sets the value to compare against.
    /// </summary>
    public string Value { get; set; }
}
