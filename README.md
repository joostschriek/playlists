# Jellyfin Smart Favorites

A Jellyfin server plugin that maintains a playlist of the **next unwatched episodes from every series you have favorited**.

Favorite a show, and its next episode shows up in a single "Favorites Up Next" playlist. Watch that episode, and the playlist advances to the following one. Each user gets their own playlist built from their own favorites and their own watch state.

## How it works

For every user, the plugin:

1. Finds all series marked as favorite.
2. For each one, takes the next N unwatched episodes in season/episode order.
3. Writes them into a playlist named after the `Playlist name` setting, rebuilding it in place so the playlist ID and any client bookmarks survive.

Series with nothing left unwatched are skipped, so the playlist is only ever a list of things you can actually watch.

## When it refreshes

- On server start.
- Every 6 hours.
- About 15 seconds after you favorite/unfavorite a show, finish an episode, or toggle an episode's watched state.
- On demand, via **Refresh playlists now** on the plugin's config page, or **Dashboard → Scheduled Tasks → Refresh favorites playlist**.

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

- Jellyfin **10.11.x** (`targetAbi 10.11.0.0`, .NET 9)

## Building

```sh
dotnet publish Jellyfin.Plugin.SmartFavorites/Jellyfin.Plugin.SmartFavorites.csproj -c Release -o publish
```

## Installing

Copy `publish/Jellyfin.Plugin.SmartFavorites.dll` into a new folder under your Jellyfin plugin directory:

```
<jellyfin-config>/plugins/SmartFavorites_1.0.0.0/Jellyfin.Plugin.SmartFavorites.dll
```

Restart Jellyfin. The plugin appears under **Dashboard → Plugins → Smart Favorites**.

## Notes

- The plugin only claims a playlist that is **owned by that user** and matches the configured name. Renaming the setting leaves the old playlist behind untouched — delete it manually if you don't want it.
- Removing a show from your favorites removes its episodes from the playlist on the next refresh.
- Editing the playlist by hand is pointless: it is fully regenerated on every refresh.
