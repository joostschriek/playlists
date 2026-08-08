using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Jellyfin.Plugin.SmartFavorites.Configuration;

namespace Jellyfin.Plugin.SmartFavorites;

/// <summary>
/// Decides whether a series satisfies a playlist's rules.
/// </summary>
public static class RuleEvaluator
{
    /// <summary>
    /// Evaluates a playlist's rules against a series.
    /// </summary>
    /// <param name="definition">The playlist definition supplying the rules and match mode.</param>
    /// <param name="facts">The series under test.</param>
    /// <returns><c>true</c> when the series qualifies.</returns>
    public static bool Matches(SmartPlaylistDefinition definition, SeriesFacts facts)
    {
        ArgumentNullException.ThrowIfNull(definition);

        // No rules is "everything", which mirrors an empty filter set in the UI.
        if (definition.Rules.Count == 0)
        {
            return true;
        }

        return definition.Match == MatchMode.Any
            ? definition.Rules.Any(rule => Matches(rule, facts))
            : definition.Rules.All(rule => Matches(rule, facts));
    }

    private static bool Matches(FilterRule rule, SeriesFacts facts)
    {
        return rule.Field switch
        {
            RuleField.IsFavorite => MatchesBool(rule, facts.IsFavorite),
            RuleField.Genre => MatchesAny(rule, facts.Series.Genres),
            RuleField.Studio => MatchesAny(rule, facts.Series.Studios),
            RuleField.Tag => MatchesAny(rule, facts.Series.Tags),
            RuleField.Name => MatchesText(rule, facts.Series.Name),
            RuleField.OfficialRating => MatchesText(rule, facts.Series.OfficialRating),
            RuleField.SeriesStatus => MatchesText(rule, facts.Series.Status?.ToString()),
            RuleField.CommunityRating => MatchesNumber(rule, facts.Series.CommunityRating),
            RuleField.CriticRating => MatchesNumber(rule, facts.Series.CriticRating),
            RuleField.ProductionYear => MatchesNumber(rule, facts.Series.ProductionYear),
            RuleField.DaysSinceAdded => MatchesNumber(rule, DaysSince(facts.Series.DateCreated)),
            RuleField.DaysSinceLastWatched => MatchesNumber(rule, DaysSince(facts.LastWatchedUtc)),
            _ => false
        };
    }

    private static double? DaysSince(DateTime? value)
    {
        if (!value.HasValue)
        {
            return null;
        }

        var utc = value.Value.Kind == DateTimeKind.Utc ? value.Value : value.Value.ToUniversalTime();
        return (DateTime.UtcNow - utc).TotalDays;
    }

    private static bool MatchesBool(FilterRule rule, bool actual)
    {
        var expected = string.Equals(rule.Value?.Trim(), "true", StringComparison.OrdinalIgnoreCase);

        return rule.Operator switch
        {
            RuleOperator.IsNot or RuleOperator.DoesNotContain => actual != expected,
            _ => actual == expected
        };
    }

    /// <summary>
    /// Applies a rule across a multi-valued field. Negative operators must hold for every
    /// value — "genre is not Comedy" should reject a show tagged both Drama and Comedy —
    /// while positive operators need only one match.
    /// </summary>
    private static bool MatchesAny(FilterRule rule, IReadOnlyList<string>? values)
    {
        var items = values ?? [];
        var negated = rule.Operator is RuleOperator.IsNot or RuleOperator.DoesNotContain;

        if (items.Count == 0)
        {
            // Nothing to contradict a negative rule; a positive one has nothing to match.
            return negated;
        }

        return negated
            ? items.All(value => MatchesText(rule, value))
            : items.Any(value => MatchesText(rule, value));
    }

    private static bool MatchesText(FilterRule rule, string? actual)
    {
        var expected = rule.Value?.Trim() ?? string.Empty;
        var value = actual ?? string.Empty;

        return rule.Operator switch
        {
            RuleOperator.Is => string.Equals(value, expected, StringComparison.OrdinalIgnoreCase),
            RuleOperator.IsNot => !string.Equals(value, expected, StringComparison.OrdinalIgnoreCase),
            RuleOperator.Contains => value.Contains(expected, StringComparison.OrdinalIgnoreCase),
            RuleOperator.DoesNotContain => !value.Contains(expected, StringComparison.OrdinalIgnoreCase),

            // Numeric comparisons against text are meaningless; treat them as no match
            // rather than silently letting everything through.
            _ => false
        };
    }

    private static bool MatchesNumber(FilterRule rule, double? actual)
    {
        if (!double.TryParse(rule.Value?.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out var expected))
        {
            return false;
        }

        if (!actual.HasValue)
        {
            // An absent value can only satisfy a negative rule.
            return rule.Operator is RuleOperator.IsNot or RuleOperator.DoesNotContain;
        }

        return rule.Operator switch
        {
            RuleOperator.Is => Math.Abs(actual.Value - expected) < 0.0001,
            RuleOperator.IsNot => Math.Abs(actual.Value - expected) >= 0.0001,
            RuleOperator.GreaterThan => actual.Value > expected,
            RuleOperator.LessThan => actual.Value < expected,

            // Substring operators do not apply to numbers.
            _ => false
        };
    }
}
