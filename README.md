# Muselly

A cross-platform music player built with **.NET 10** and **Avalonia 12**, themed end-to-end with
[Catppuccin](https://catppuccin.com) and built from a library of bespoke, theme-aware controls.

## Features

- **Library scanning** — point Muselly at one or more folders (via the native OS folder picker) and it
  extracts full metadata from every track: title, artist, album artist, album, track/disc numbers,
  year, genres, record label, codec info and embedded cover art. Formats: **MP3, WAV, FLAC, ALAC, AAC,
  Opus** (and Ogg).
- **Organised browsing** — the library auto-organises into **Albums**, **Artists** and **Songs**, plus a
  **Folders** tree for browsing by directory. Live search across everything.
- **Queue** — add whole albums, whole artists, folders or individual tracks; "add to queue" and
  **"add to queue (shuffled)"** everywhere; reorder/remove from the always-available queue drawer.
- **Playlists** — create and persist playlists, **randomize ordering** or sort by title / artist / album /
  year / duration / date added.
- **Always-on player bar** — mini cover art, play/prev/next, shuffle, repeat, a custom **scrobbler**
  (draggable seek bar), volume and the queue toggle.
- **Settings** — choose scan folders, rescan with live progress, switch Catppuccin flavour and UI scale,
  and a **10-band graphic equaliser** (with presets) applied to all playback, cross-platform.
- **Touch + desktop** — generous hit targets, hover affordances and right-click / long-press context menus.

Business logic lives in `Muselly.Core` (no UI dependency); the UI, custom controls and theming live in
`Muselly.App`. Audio is **dependency-free**: `Muselly.Audio` decodes files to PCM (WAV natively; other
formats via the `ffmpeg` CLI) and plays them through native output drivers — **CoreAudio**
(macOS), **WASAPI** (Windows) and **ALSA** (Linux). No media framework to install. The browser demo uses a
silent simulator.

### ffmpeg

`.wav` plays with no external tools. For MP3/FLAC/ALAC/AAC/Opus, Muselly shells out to `ffmpeg`, which it
**bundles** (built from source, minimal + static + LGPL — see [licensing](#ffmpeg-licensing)).

```bash
./scripts/build-ffmpeg.sh            # build ffmpeg for the host RID into third_party/ffmpeg/<rid>/
dotnet run --project Muselly.Desktop # the build copies the binary next to the app automatically
```

- The desktop project auto-copies `third_party/ffmpeg/<rid>/ffmpeg` to the output for the current RID, so
  `dotnet run` and per-RID `dotnet publish` both pick it up.
- `publish-desktop.sh` builds the matching ffmpeg per RID; **CI builds + caches it on each native runner**.
- If no bundled binary is present, Muselly falls back to an `ffmpeg` on the `PATH`.
- Cross-building: `osx-x64` cross-compiles from Apple Silicon; `win-x64` cross-compiles from Linux via
  mingw-w64. Otherwise build each RID on its own OS.

#### ffmpeg licensing

The bundle is configured **without** `--enable-gpl`/`--enable-nonfree`, so it's **LGPL-2.1+**, and Muselly
invokes it as a separate process (no linking) — so Muselly stays MIT. The binary ships with its licence and
[`THIRD_PARTY_NOTICES.md`](THIRD_PARTY_NOTICES.md) points to the source + the reproducible build recipe.

## Custom control library

`Muselly.App/Controls` holds the reusable, Catppuccin-aware controls: `IconButton`, `Scrobbler`,
`LevelSlider`, `AlbumArt`, plus a vector `Icons.axaml` glyph set and shared control themes/styles in
`Muselly.App/Theming`.

## Project layout

| Project | Role |
|---|---|
| `Muselly.Core` | Portable business logic (no UI dependency). |
| `Muselly.App` | Shared Avalonia UI library — windows, views, view models, theming, platform seam. |
| `Muselly.Desktop` | Desktop head (Windows / Linux / macOS). |
| `Muselly.Web` | Browser / WebAssembly head (Avalonia `browser-wasm`). |
| `Muselly.Android` | Android head *(not yet scaffolded)*. |

The shared `Muselly.App` library references only cross-platform Avalonia. Each
platform "head" injects platform-specific services through the single
`IPlatformServices` seam and starts the appropriate Avalonia lifetime.

## Building

```bash
dotnet build Muselly.sln
```

### Run the desktop app

```bash
dotnet run --project Muselly.Desktop
```

### Run the web app

The web head is a **demo** (no native folder scanning or audio in the browser sandbox). It requires the
`wasm-tools` workload:

```bash
dotnet workload install wasm-tools
dotnet run --project Muselly.Web
```

## Publishing

```bash
./publish-desktop.sh          # all desktop RIDs into dist/
./publish-web.sh              # WASM bundle into dist/web/
```

## Theming

Muselly ships the [Catppuccin](https://catppuccin.com) palette (Mocha,
Macchiato, Frappé, Latte). Themes recolor live without restart — see
`Muselly.App/Theming/`.
