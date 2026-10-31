# avalonia-ympd

An independent Avalonia desktop client for MPD. It connects directly to MPD;
the ympd web service is not required.

## Features

- Queue with drag-and-drop reordering, library folders, stored playlists, and search
- Previous, stop, play/pause, next, seek, volume, MPD random, repeat, and consume
- Composer, bitrate, and optional MPD file path in Now Playing
- Add a search or library result next, add it to the end, or play it immediately
- Keep the queue filled to a chosen size with random library songs
- Keep running in the macOS menu bar after closing the window; use the menu to
  reopen the window or quit

The random filler uses MPD's `searchcount` and `search ... window` commands. It
chooses random library positions, then avoids songs already queued and the last
50 selections when possible. It has no separate music database or library cache.
It checks the queue every five seconds while the app is running. When it fills
an empty, stopped queue, it starts playback.

## Build and run

Install the .NET 10 SDK, then run from the repository root:

```sh
dotnet restore ModernYmpd/ModernYmpd.csproj
dotnet run --project ModernYmpd/ModernYmpd.csproj
```

On macOS, build a self-contained `.app` bundle with:

```sh
./build-macos-app.sh
open "dist/Modern YMPD.app"
```

The bundle contains its .NET runtime. It is built for the Mac's current CPU
architecture, includes the app icon, and can be moved to `Applications`. The
build output under `dist` is ignored by Git.

The default MPD address is `127.0.0.1:6600`. Connection and shuffle settings
are saved in the user's application-data folder under
`modern-ympd/settings.json`. The MPD password is held only until the app quits.
If the original ympd service is also set to fill the queue, disable that setting
there to avoid two independent fillers.

For a read-only connection check, run:

```sh
dotnet run --project ModernYmpd/ModernYmpd.csproj -- --check-mpd Bach
```

This project is based on the interface and playback workflow of
[modern-ympd](https://github.com/LordMyschkin/modern-ympd). It is licensed under
GPLv2; see [LICENSE](LICENSE).
