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

## Share your library — built-in server & client

Any Muselly install can **host its library** for other Muselly clients, and connect to other people's
servers — so your whole collection follows you between machines, or you share it with friends. It's all
built in (`Muselly.Server`); there's no separate daemon to run.

- **One-click server** — flip on the server in **Settings**. It picks a stable port, and you get a friendly
  name plus a certificate **fingerprint** to share. Headless-friendly: the host is built purely on
  `Muselly.Core` abstractions, so the same server can be reused without the UI.
- **Encrypted & pinned** — every connection runs inside **TLS** using a self-signed certificate (1.3 where
  both peers support it; macOS hosts negotiate TLS 1.2, since .NET's macOS TLS stack has no server-side 1.3
  yet). Clients **pin the SHA-256 fingerprint on first use** (trust-on-first-use) and refuse to connect if it
  ever changes — no certificate authority required.
- **Accounts & roles** — per-user logins with **PBKDF2-hashed** passwords (cleartext never hits disk) and
  three roles: **Guest**, **User** and **Admin**. Optional anonymous **guest access** can be toggled on.
- **Reach beyond the LAN** — optional **UPnP / NAT-PMP** automatic router port-mapping makes the server
  reachable from the internet (best-effort; never required).
- **Remote libraries feel local** — add a server by host/port, connect (remembering credentials and
  auto-reconnecting if you like), and its tracks **merge straight into your library** alongside local ones,
  with full metadata, album art, artist images, biographies and lyrics fetched on demand and cached.
- **On-the-fly Opus streaming** — the server transcodes tracks to **Opus** (libopus, configurable
  **64–256 kbps**, default 128) as you listen, backed by a size-bounded on-disk **LRU transcode cache**.
  Want the original? Full-quality **downloads** stream the source file untouched.
- **Remote administration** — admins can add/remove music folders, trigger rescans, change server settings
  and manage user accounts **over the wire**.

Under the hood it speaks a small, length-prefixed **framed protocol** (UTF-8 JSON envelopes plus raw binary
chunks for audio and images), spoken only inside the TLS tunnel.

### ffmpeg

`.wav` plays with no external tools. For MP3/FLAC/ALAC/AAC/Opus, Muselly shells out to `ffmpeg`, which it
**bundles** (built from source, minimal + static + LGPL — see [licensing](#ffmpeg-licensing)).

```bash
./scripts/build-ffmpeg.sh            # build ffmpeg for the host RID into third_party/ffmpeg/<rid>/
dotnet run --project Muselly.Desktop # the build copies the binary next to the app automatically
```

- **Build prerequisites:** a C compiler, `make`, `curl`, `xz`/`tar` and **`pkg-config`** (the script needs
  pkg-config to link libopus for Opus encoding — install it with `brew install pkg-config` on macOS or
  `sudo apt-get install -y pkg-config` on Linux). x86 targets also want `nasm`. The script preflight-checks
  these and tells you what's missing. See [`DEVELOPMENT.md`](DEVELOPMENT.md) for full per-platform steps.
- The desktop project auto-copies `third_party/ffmpeg/<rid>/ffmpeg` to the output for the current RID, so
  `dotnet run` and per-RID `dotnet publish` both pick it up.
- `publish-desktop.sh` builds the matching ffmpeg per RID; **CI builds + caches it on each native runner**.
- If no bundled binary is present, Muselly falls back to an `ffmpeg` on the `PATH`.
- The bundled ffmpeg both **decodes** (for playback) and **encodes Opus** (for the server's streaming).
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
| `Muselly.Audio` | Dependency-free audio: ffmpeg-CLI decoding + native CoreAudio/WASAPI/ALSA output. |
| `Muselly.Server` | Integrated library server **and** remote client (TLS, auth, Opus streaming). The transport-agnostic `ServerEngine` + `MusellyApiService` are reused by the web host and headless. |
| `Muselly.WebHost` | ASP.NET Core host that serves the browser app **and** a JSON/streaming API onto the same engine (auth, roles, IP firewall, Opus). |
| `Muselly.App` | Shared Avalonia UI library — windows, views, view models, theming, platform seam. |
| `Muselly.Desktop` | Desktop head (Windows / Linux / macOS). |
| `Muselly.Web` | Browser / WebAssembly head (Avalonia `browser-wasm`) — a real client of a Muselly web host. |
| `Muselly.Headless` | UI-less host that runs the TLS server + web server (shares the engine; ideal for a NAS/box). |
| `Muselly.Android` | Android head *(not yet scaffolded)*. |

The shared `Muselly.App` library references only cross-platform Avalonia. Each
platform "head" injects platform-specific services through the single
`IPlatformServices` seam and starts the appropriate Avalonia lifetime.

## Building

```bash
dotnet build Muselly.sln
```

For full, per-platform build instructions (toolchains, the bundled ffmpeg, cross-compiling and publishing),
see [`DEVELOPMENT.md`](DEVELOPMENT.md).

### Run the desktop app

```bash
dotnet run --project Muselly.Desktop
```

### The web app & headless host

The browser app is **not** a static demo anymore — it's a real client of a Muselly **web server**. Enable the
web server from the desktop app's **Connect** page (set the HTTP/HTTPS ports and flip it on), or run the
**headless** host. The web server serves the published browser bundle *and* the API it talks to: it honours
the same accounts/roles (guests browse if guest access is on; otherwise visitors sign in), streams audio as
Opus, and lets admins manage the host's folders/rescans from the browser. The IP firewall (Connect page)
applies to the built-in server, the web server, or both.

Build the browser bundle (needs the `wasm-tools` workload), then point a host at it:

```bash
dotnet workload install wasm-tools
./publish-web.sh                                   # stages the bundle into dist/web/

# Run the headless host, serving that bundle + API:
MUSELLY_WEBROOT="$(pwd)/dist/web" dotnet run --project Muselly.Headless -- --add /path/to/music
```

`Muselly.Headless` runs the same engine as the desktop with no UI: `--add <folder>` registers music folders,
`--http`/`--https` set ports, and `--no-web` / `--no-server` disable either listener.

## Publishing

```bash
./publish-desktop.sh          # all desktop RIDs into dist/
./publish-web.sh              # WASM browser-client bundle into dist/web/ (served by a Muselly host)
```

## Theming

Muselly ships the [Catppuccin](https://catppuccin.com) palette (Mocha,
Macchiato, Frappé, Latte). Themes recolor live without restart — see
`Muselly.App/Theming/`.
