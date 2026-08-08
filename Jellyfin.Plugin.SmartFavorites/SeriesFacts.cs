using System;
using MediaBrowser.Controller.Entities.TV;

namespace Jellyfin.Plugin.SmartFavorites;

/// <summary>
/// The per-series facts a rule can be evaluated against. Gathered once per series so the
/// user-data lookups behind <see cref="IsFavorite"/> and <see cref="LastWatchedUtc"/> are
/// not repeated for every rule of every playlist.
/// </summary>
/// <param name="Series">The series being tested.</param>
/// <param name="IsFavorite">Whether the user favorited it.</param>
/// <param name="LastWatchedUtc">When the user last finished an episode of it, if ever.</param>
public readonly record struct SeriesFacts(Series Series, bool IsFavorite, DateTime? LastWatchedUtc);
