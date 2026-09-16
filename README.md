# Jellyfin Smart Favorites

A Jellyfin server plugin that maintains a playlist of the **next unwatched episodes from every series you have favorited**.

Favorite a show, and its next episode shows up in a single "Favorites Up Next" playlist. Watch that episode, and the playlist advances to the following one. Each user gets their own playlist built from their own favorites and their own watch state.

## How it works

For every user, the plugin:

1. Finds all series marked as favorite.
2. For each one, takes the next N unwatched episodes in season/episode order.
3. Writes them into a playlist named after the `Playlist name` setting, rebuilding it in place so the playlist ID and any client bookmarks survive.

Series with nothing left unwatched are skipped, so the playlist is only ever a list of things you can actually watch.

## Filters

Each playlist is defined by a set of rules, in the style of a Plex smart playlist. A series contributes episodes when it satisfies **all** of the rules, or **any** of them, depending on the playlist's match mode. No rules means every series qualifies.

| Field | Type | Example |
| --- | --- | --- |
| Favorited | yes/no | `Favorited is yes` — the original behaviour |
| Genre, Studio, Tag | text, multi-valued | `Genre is Comedy` |
| Series name | text | `Series name contains Star` |
| Content rating | text | `Content rating is TV-MA` |
| Status | Continuing / Ended / Unreleased | `Status is Continuing` |
| Community rating | number, 0–10 | `Community rating is greater than 8` |
| Critic rating | number, 0–100 | `Critic rating is greater than 75` |
| Year | number | `Year is greater than 2015` |
| Days since added | number | `Days since added is less than 30` |
| Days since last watched | number | `Days since last watched is less than 14` |

Operators are `is`, `is not`, `contains`, `does not contain`, `is greater than` and `is less than`. The last two apply to numeric fields only.

Two behaviours worth knowing:

- **Negation on multi-valued fields must hold for every value.** `Genre is not Comedy` rejects a series tagged both Drama and Comedy, rather than accepting it because Drama matched.
- **A missing value only satisfies a negative rule.** A series with no critic rating never matches `Critic rating is greater than 50`, and a series you have never watched never matches a `Days since last watched` comparison.

Each playlist separately controls its episode count, total cap, sort order and direction, specials and unaired handling, visibility, and which users it is built for.

## Sorting

Pick a field to order series by, and a direction. Leaving direction on **Default** uses whichever way round reads as natural for that field:

| Order by | Default direction |
| --- | --- |
| Last watched | Newest first |
| Episode air date | Oldest first |
| Date added | Newest first |
| Community rating | Highest first |
| Series name | A to Z |

Set it to **Ascending** or **Descending** to override. Series with no value for the chosen field — no air date, no rating, never watched — sort to the end in *both* directions, so reversing the order does not drag everything unrated to the top.

Renaming a playlist creates a new one; the previously generated playlist is left in your library untouched. The same applies when you delete a playlist from the configuration — the plugin stops maintaining it but never deletes it for you.

## When it refreshes

Refreshes are event-driven. About 15 seconds after any of these, the affected user's playlist is rebuilt:

- A series is favorited or unfavorited.
- An episode is finished, or its watched state is toggled.
- An episode is added to or removed from the library (a scan that adds a new season, for example).
- The plugin's configuration is saved.

Events arriving in bursts are collapsed into a single rebuild, and a rebuild is forced after 5 minutes so a long library scan can't defer one indefinitely.

The scheduled task is only a backstop for changes made while the server was down. It runs on start and once every 24 hours, and can be run on demand via **Refresh playlists now** on the config page or **Dashboard → Scheduled Tasks → Refresh favorites playlist**.

## Settings

| Setting | Default | Description |
| --- | --- | --- |
| Playlist name | `Favorites Up Next` | Name of the generated playlist. One per user. |
| Episodes per series | `1` | How many consecutive unwatched episodes to take from each favorited series. |
| Order series by | Recently watched first | `LastWatched`, `SeriesName`, `EpisodeAirDate`, or `Random`. |
| Include specials | off | Whether season 0 episodes are eligible. |
| Include unaired episodes | off | Whether episodes with a future premiere date are eligible. |
| Make playlists public | off | Public playlists are visible to every user on the server. |
| Users | all | Restrict generation to specific users. |

## Requirements

- Jellyfin **12.0 or newer** (`targetAbi 12.0.0.0`, .NET 10)

Servers on 10.11.x stay on the 1.2.1.0 release. Jellyfin filters the manifest by
`targetAbi`, so a 10.11 server simply never sees the 2.x versions and keeps running
the last build that targeted it — nothing breaks, it just stops receiving updates
until the server is upgraded.

## Building

```sh
dotnet publish Jellyfin.Plugin.SmartFavorites/Jellyfin.Plugin.SmartFavorites.csproj -c Release -o publish
```

## Installing

### From the plugin repository (recommended)

In Jellyfin: **Dashboard → Plugins → Repositories → +**, then add:

| Field | Value |
| --- | --- |
| Repository Name | `Smart Favorites` |
| Repository URL | `https://raw.githubusercontent.com/joostschriek/playlists/master/manifest.json` |

Then **Dashboard → Plugins → Catalog → Smart Favorites → Install**, and restart Jellyfin. Updates show up in the catalog automatically.

### Manually

Copy `publish/Jellyfin.Plugin.SmartFavorites.dll` into a new folder under your Jellyfin plugin directory, named `<PluginName>_<Version>`:

```
<jellyfin-config>/plugins/Smart Favorites_2.0.0.0/Jellyfin.Plugin.SmartFavorites.dll
```

Restart Jellyfin. The plugin appears under **Dashboard → Plugins → Smart Favorites**.

## Releasing

Releases are cut by CI, which is the only thing that writes checksums into `manifest.json`:

```sh
git tag v1.0.1.0 && git push origin v1.0.1.0
```

The `release` workflow builds the plugin at that version, zips the DLL (flat, as Jellyfin expects), publishes a GitHub release with the zip attached, records the zip's MD5 in `manifest.json`, and commits that back to `master`. You can also run it from the Actions tab via **Run workflow** if you'd rather not tag.

This requires **Settings → Actions → General → Workflow permissions → Read and write permissions** on the repo, otherwise the manifest commit is rejected.

## Notes

- The plugin only claims a playlist that is **owned by that user** and matches the configured name. Renaming the setting leaves the old playlist behind untouched — delete it manually if you don't want it.
- Removing a show from your favorites removes its episodes from the playlist on the next refresh.
- Editing the playlist by hand is pointless: it is fully regenerated on every refresh.
