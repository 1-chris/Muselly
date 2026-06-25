# Development

How to build, run, and package Muselly by hand on every supported platform. For an overview of what Muselly
is and its features, see [`README.md`](README.md).

## Contents

- [Prerequisites](#prerequisites)
- [Quick start](#quick-start)
- [The bundled ffmpeg](#the-bundled-ffmpeg)
  - [Build dependencies per platform](#build-dependencies-per-platform)
  - [Building ffmpeg](#building-ffmpeg)
  - [Cross-compilation matrix](#cross-compilation-matrix)
- [Building the solution](#building-the-solution)
- [Running](#running)
- [Publishing](#publishing)
- [Testing the server / client](#testing-the-server--client)
- [Where Muselly stores data](#where-muselly-stores-data)
- [Troubleshooting](#troubleshooting)

## Prerequisites

| Tool | Version | Notes |
|---|---|---|
| **.NET SDK** | **10.0.x** | Required for every project. `dotnet --version` should report `10.x`. |
| **ffmpeg build toolchain** | — | Only needed to (re)build the bundled ffmpeg; see [below](#build-dependencies-per-platform). |
| **`wasm-tools` workload** | — | `dotnet workload install wasm-tools`. Needed for the browser head **and** for the desktop build, which compiles the browser app and bundles it. Opt out with `-p:MusellyBundleWeb=false`. |
| **`zip`** | — | Only for packaging; `publish-desktop.sh` falls back to `tar.gz` if absent. |

Supported runtime identifiers (RIDs): `linux-x64`, `linux-arm64`, `win-x64`, `osx-arm64`, `osx-x64`.

## Quick start

```bash
# 1. Install the WebAssembly workload (the desktop build compiles + bundles the browser app).
dotnet workload install wasm-tools

# 2. Build the bundled ffmpeg for your machine (decodes audio + encodes Opus for the server).
./scripts/build-ffmpeg.sh

# 3. Build everything.
dotnet build Muselly.sln

# 4. Run the desktop app (auto-copies the ffmpeg you just built next to the app, and the browser app
#    into webapp/ so the integrated web server can serve it).
dotnet run --project Muselly.Desktop
```

If you skip the ffmpeg step, Muselly falls back to an `ffmpeg` on your `PATH`. That binary **must** be built
with the Ogg/Opus muxers and `libopus` encoder, or server streaming will fail — see
[Troubleshooting](#troubleshooting). Building the bundled one is the reliable path.

If you don't want the browser app bundled (e.g. you haven't installed `wasm-tools`), build/run the desktop
with `-p:MusellyBundleWeb=false` to skip it.

## The bundled ffmpeg

Muselly is dependency-free at runtime except for a single `ffmpeg` executable, which it invokes as a separate
process (never linked, so Muselly stays MIT — the binary is LGPL-2.1+). It is used for **two** things:

1. **Decoding** every non-WAV format to PCM for playback (`Muselly.Audio`).
2. **Encoding to Opus** for the integrated server's on-the-fly streaming (`Muselly.Server`).

[`scripts/build-ffmpeg.sh`](scripts/build-ffmpeg.sh) builds a minimal, static, LGPL ffmpeg (with a statically
linked `libopus`) into `third_party/ffmpeg/<rid>/`. The desktop project copies it next to the app on build.

### Build dependencies per platform

The script preflight-checks for these and prints exactly what's missing.

| Platform | Install |
|---|---|
| **macOS** | `brew install pkg-config nasm` (Xcode Command Line Tools provide clang/make/curl) |
| **Debian/Ubuntu** | `sudo apt-get install -y build-essential nasm pkg-config make curl xz-utils` |
| **Windows (MSYS2)** | `pacman -S --needed base-devel mingw-w64-x86_64-toolchain nasm pkgconf` |

> **`pkg-config` is not optional.** ffmpeg's `configure` uses it to locate the freshly built `libopus`.
> Without it, `--enable-libopus` silently drops out and you get an ffmpeg that can decode but **cannot encode
> Opus** — the server then fails with `Requested output format 'ogg' is not known`. `nasm` is only needed for
> hand-tuned x86 assembly; if it's absent the script falls back to `--disable-x86asm` and still succeeds.

### Building ffmpeg

```bash
./scripts/build-ffmpeg.sh                 # host RID (auto-detected)
./scripts/build-ffmpeg.sh osx-arm64       # a specific RID
FORCE=1 ./scripts/build-ffmpeg.sh osx-arm64   # rebuild even if a binary already exists

# Pinned versions can be overridden via env vars:
FFMPEG_VERSION=7.1 OPUS_VERSION=1.5.2 ./scripts/build-ffmpeg.sh
```

Verify a built binary has Opus support:

```bash
BIN=third_party/ffmpeg/osx-arm64/ffmpeg
"$BIN" -hide_banner -muxers   | grep -E 'ogg|opus'   # expect: E ogg, E opus
"$BIN" -hide_banner -encoders | grep -i opus         # expect: libopus
```

### Cross-compilation matrix

ffmpeg can't target a different OS than the build host, so each OS builds its own binaries. The two
exceptions are handled by the script:

| Target RID | Build host | How |
|---|---|---|
| `linux-x64` | Linux x86_64 | native |
| `linux-arm64` | Linux arm64 | native (CI uses an arm64 runner) |
| `osx-arm64` | macOS (Apple Silicon) | native |
| `osx-x64` | macOS (Apple Silicon) | cross via `clang -arch x86_64` |
| `win-x64` | Linux | cross via `mingw-w64` (`gcc-mingw-w64-x86-64`) **or** Windows MSYS2 (native) |

CI builds + caches each RID's ffmpeg on its native runner (see `.github/workflows/desktop-build.yml`).

## Building the solution

```bash
dotnet build Muselly.sln                 # Debug
dotnet build Muselly.sln -c Release      # Release
```

> Building `Muselly.Desktop` also compiles `Muselly.Web` (browser-wasm) and copies its `AppBundle` into the
> desktop output's `webapp/` folder, so the integrated web server serves it with no extra steps. This needs
> the `wasm-tools` workload. To skip it (faster builds, or no workload installed), pass
> `-p:MusellyBundleWeb=false`.

## Running

### Desktop (Windows / Linux / macOS)

```bash
dotnet run --project Muselly.Desktop
```

The build copies `third_party/ffmpeg/<host-rid>/ffmpeg` to the output directory (via `PreserveNewest`), so a
freshly rebuilt ffmpeg is picked up automatically the next time you build or run.

### Web (browser client) + the web server

The browser app is a **real client** of a Muselly web server — it signs in, streams Opus from the host and
(for admins) manages the host's library. It is not a standalone static site; it needs a backing host.

Two ways to serve it:

- **From the desktop app:** open **Connect**, set the HTTP/HTTPS ports and turn the web server on. The desktop
  build already bundled the browser app into `webapp/`, so it's served immediately. Browse to
  `http://localhost:<port>`.
- **From the headless host** (no UI; ideal for a NAS/server box):

```bash
./publish-web.sh                                              # stages the bundle into dist/web/
MUSELLY_WEBROOT="$(pwd)/dist/web" \
  dotnet run --project Muselly.Headless -- --add /path/to/music
```

`Muselly.Headless` runs the same engine as the desktop. Options: `--add <folder>` (repeatable),
`--http <port>`, `--https <port>`, `--no-web`, `--no-server`. It resolves the browser bundle from
`MUSELLY_WEBROOT`, else a `webapp/` folder next to the binary.

To iterate on the browser app itself with the Avalonia dev server (UI only; talks to whatever host origin it
is served from):

```bash
dotnet workload install wasm-tools
dotnet run --project Muselly.Web
```

## Publishing

Self-contained packages (the .NET runtime is bundled; target machines need no .NET install):

```bash
./publish-desktop.sh                      # all desktop RIDs -> dist/Muselly-<rid>.zip
./publish-desktop.sh osx-arm64 linux-x64  # only the listed RIDs
./publish-desktop.sh --symbols            # keep .pdb debug symbols
./publish-desktop.sh --no-zip             # leave publish folders, skip zipping

./publish-web.sh                          # WASM bundle -> dist/web/
```

Each desktop package also includes the browser app under `webapp/` (built via the `wasm-tools` workload), so
enabling the web server in a published build serves the browser app out of the box. Pass
`-p:MusellyBundleWeb=false` to `dotnet publish` to skip it.

`publish-desktop.sh` builds the matching ffmpeg per RID first (best-effort; locally this only succeeds for
RIDs your host can build). Run targets inside each package:

- **Linux:** `./Muselly.bin`
- **Windows:** `Muselly.exe`
- **macOS:** `./Muselly`

## Testing the server / client

You need two Muselly instances (two machines, or two user accounts / data dirs on one box).

**On the host:**

1. Run the desktop app and open **Settings → Server**; enable the server.
2. Note the **port** and the certificate **fingerprint** shown.
3. Add at least one user account (or enable **guest access**), and point the library at some music.
4. (Optional) enable **UPnP** to expose it beyond your LAN.

**On the client:**

1. Open the remote-servers UI and **add a server** by `host:port`.
2. Connect as a user (or guest). On first connect you'll pin the host's fingerprint.
3. The host's tracks merge into your library — play one to stream it (transcoded to Opus on the fly).

Both heads run the same code, so a macOS host and a Linux client (or any mix) interoperate. TLS negotiates
the best mutual version (1.3 between Linux/Windows; 1.2 when a macOS host is involved).

## Where Muselly stores data

Per-user data directory:

| OS | Location |
|---|---|
| Windows | `%AppData%\Muselly` |
| macOS | `~/Library/Application Support/Muselly` |
| Linux | `$XDG_CONFIG_HOME/Muselly` (or `~/.config/Muselly`) |

Server/client files within it: `server.json` (config), `server-users.json` (PBKDF2 hashes only),
`server-cert.pfx` (self-signed TLS cert), `remotes.json` (saved servers + pinned fingerprints), and a
size-bounded `transcode-cache/` of server-side Opus output.

## Troubleshooting

**`Requested output format 'ogg' is not known` (server transcoding fails).**
Your ffmpeg was built without the Ogg/Opus muxers and `libopus` — almost always because `pkg-config` was
missing when ffmpeg was built. Install `pkg-config` (see [above](#build-dependencies-per-platform)) and
rebuild: `FORCE=1 ./scripts/build-ffmpeg.sh`. Confirm with the `-muxers`/`-encoders` checks above, then
rebuild the app so the new binary is copied into the output.

**`The requested security protocol is not supported` during the TLS handshake (server on macOS).**
.NET's `SslStream` on macOS uses Apple SecureTransport, which has no server-side TLS 1.3. Muselly already
allows `TLS 1.2 | 1.3` so the handshake negotiates 1.2 on macOS hosts — make sure you're on a build that
includes this (see `Muselly.Server/Transport/TlsTransport.cs`).

**ffmpeg builds but the app can't find it.**
The desktop project only bundles `third_party/ffmpeg/<host-rid>/ffmpeg`. Make sure you built the RID that
matches your machine, then rebuild the app (the copy uses `PreserveNewest`). Otherwise Muselly falls back to
an `ffmpeg` on your `PATH`, which may lack Opus support.
