#!/usr/bin/env bash
#
# Builds a minimal, static, LGPL FFmpeg `ffmpeg` binary for one runtime identifier (RID) and installs it
# to third_party/ffmpeg/<rid>/. Muselly bundles this binary and invokes it as a separate process to decode
# compressed audio (MP3/FLAC/ALAC/AAC/Opus/Vorbis) to PCM — see Muselly.Audio/Decoding/FfmpegDecoder.cs —
# and to ENCODE to Opus for the integrated server's on-the-fly transcoding (Muselly.Server). libopus is
# built statically and linked in (see build_opus below).
#
# LICENSING: we configure WITHOUT --enable-gpl and WITHOUT --enable-nonfree, so the result is LGPL-2.1+.
# libopus is BSD-licensed (LGPL-compatible) and does not change that classification. Muselly only ever runs
# ffmpeg as a separate executable, so Muselly's own MIT licence is unaffected; we just redistribute this
# LGPL binary alongside it (its licence is copied next to the binary, and the build is fully reproducible
# from this script — satisfying LGPL source availability).
#
# Usage:
#   scripts/build-ffmpeg.sh [rid]        # rid defaults to the host (e.g. osx-arm64)
#   FORCE=1 scripts/build-ffmpeg.sh ...  # rebuild even if the binary already exists
#
# Supported RIDs and where they build:
#   osx-arm64 / osx-x64   on macOS  (osx-x64 cross-compiles from Apple Silicon via `clang -arch x86_64`)
#   linux-x64 / linux-arm64 on Linux (native)
#   win-x64               on Linux  (cross via mingw-w64) or on Windows (MSYS2, native)
#
# Build tools required: a C compiler + make + curl + tar(xz) + pkg-config. pkg-config is how ffmpeg's
# configure locates the bundled libopus — without it --enable-libopus silently drops out and the binary
# can't ENCODE Opus (the server's transcoder then fails with "Requested output format 'ogg' is not known").
# x86 targets also need nasm/yasm (if absent, the script falls back to --disable-x86asm so the build still
# succeeds, just without hand-tuned asm).

set -euo pipefail

FFMPEG_VERSION="${FFMPEG_VERSION:-7.1}"
# libopus is bundled so ffmpeg can ENCODE to Opus (Muselly.Server transcodes remote listens to Opus). It is
# BSD-licensed, so it is LGPL-compatible and does not affect the LGPL classification of the ffmpeg binary.
OPUS_VERSION="${OPUS_VERSION:-1.5.2}"
SCRIPT_DIR="$(cd "$(dirname "$0")" && pwd)"
ROOT="$(cd "$SCRIPT_DIR/.." && pwd)"

host_rid() {
    local os arch
    case "$(uname -s)" in
        Darwin) os=osx ;;
        Linux)  os=linux ;;
        MINGW*|MSYS*|CYGWIN*) os=win ;;
        *) os=unknown ;;
    esac
    case "$(uname -m)" in
        arm64|aarch64) arch=arm64 ;;
        x86_64|amd64)  arch=x64 ;;
        *) arch="$(uname -m)" ;;
    esac
    echo "$os-$arch"
}

RID="${1:-$(host_rid)}"
HOST_OS="$(uname -s)"

BIN_NAME="ffmpeg"
case "$RID" in win-*) BIN_NAME="ffmpeg.exe" ;; esac

DEST="$ROOT/third_party/ffmpeg/$RID"
if [ -f "$DEST/$BIN_NAME" ] && [ -z "${FORCE:-}" ]; then
    echo "ffmpeg for $RID already present at $DEST/$BIN_NAME (set FORCE=1 to rebuild)."
    exit 0
fi

# --- preflight: required build tools ------------------------------------------------------------------
# Fail loudly (with an install hint) rather than letting configure quietly produce an Opus-less binary.
preflight_tools() {
    local missing=()
    command -v cc >/dev/null 2>&1 || command -v clang >/dev/null 2>&1 || command -v gcc >/dev/null 2>&1 || missing+=("a C compiler")
    command -v make       >/dev/null 2>&1 || missing+=(make)
    command -v curl       >/dev/null 2>&1 || missing+=(curl)
    command -v pkg-config >/dev/null 2>&1 || missing+=(pkg-config)
    [ "${#missing[@]}" -eq 0 ] && return 0

    echo "ERROR: missing required build tool(s): ${missing[*]}" >&2
    case "$HOST_OS" in
        Darwin) echo "  Install on macOS:  brew install pkg-config nasm" >&2 ;;
        Linux)  echo "  Install on Debian/Ubuntu:  sudo apt-get install -y build-essential nasm pkg-config make curl xz-utils" >&2 ;;
        MINGW*|MSYS*) echo "  Install in MSYS2:  pacman -S --needed base-devel mingw-w64-x86_64-toolchain nasm pkgconf" >&2 ;;
    esac
    exit 1
}
preflight_tools

WORK="$ROOT/build/ffmpeg"
SRC="$WORK/ffmpeg-$FFMPEG_VERSION"
mkdir -p "$WORK"

# --- fetch source -------------------------------------------------------------------------------------
if [ ! -d "$SRC" ]; then
    TARBALL="$WORK/ffmpeg-$FFMPEG_VERSION.tar.xz"
    if [ ! -f "$TARBALL" ]; then
        echo "Downloading FFmpeg $FFMPEG_VERSION source…"
        curl -fSL "https://ffmpeg.org/releases/ffmpeg-$FFMPEG_VERSION.tar.xz" -o "$TARBALL"
    fi
    echo "Extracting…"
    tar -xf "$TARBALL" -C "$WORK"
fi

# --- assemble configure flags -------------------------------------------------------------------------
# Audio-only, minimal build: start from --disable-everything and switch on exactly the containers + codecs
# Muselly needs (MP3/WAV/FLAC/ALAC/AAC/Opus/Vorbis, plus common lossless extras), the WAV/pcm_f32le output
# path, and the resampling filters the CLI inserts. This keeps the binary small and the build fast.
COMMON=(
    --disable-shared --enable-static
    --disable-debug --disable-doc --disable-autodetect
    --disable-programs --enable-ffmpeg
    --disable-avdevice --disable-swscale --disable-postproc --disable-network --disable-devices
    --disable-everything
    # Containers we read from.
    --enable-demuxer=mp3,wav,w64,aiff,flac,ogg,mov,aac,matroska,asf,wv,ape,tta,caf
    # Audio decoders (the six required + common companions).
    --enable-decoder=mp3,mp3float,mp2,mp2float,mp1,mp1float,flac,alac,aac,aac_latm,opus,vorbis,wavpack,tta,ape,wmav1,wmav2,wmalossless,wmapro,pcm_s16le,pcm_s16be,pcm_s24le,pcm_s24be,pcm_s32le,pcm_s32be,pcm_u8,pcm_f32le,pcm_f32be,pcm_f64le,pcm_alaw,pcm_mulaw
    --enable-parser=aac,aac_latm,flac,mpegaudio,opus,vorbis
    # Output paths: a float WAV for the player's decoder, and Opus-in-Ogg for the server's transcoder.
    --enable-encoder=pcm_f32le,pcm_s16le,libopus
    --enable-muxer=wav,ogg,opus
    --enable-libopus
    --enable-filter=aresample,aformat,anull,atrim,achannelmap
    --enable-protocol=file,pipe
    --pkg-config-flags=--static
    --disable-gpl --disable-nonfree
)

CROSS=()
EXTRA_CFLAGS=""
EXTRA_LDFLAGS=""
CROSS_PREFIX=""

have_asm() { command -v nasm >/dev/null 2>&1 || command -v yasm >/dev/null 2>&1; }

case "$RID" in
    osx-arm64)
        [ "$HOST_OS" = "Darwin" ] || { echo "osx-arm64 must build on macOS"; exit 1; }
        EXTRA_CFLAGS="-arch arm64"; EXTRA_LDFLAGS="-arch arm64"
        CROSS=(--arch=arm64 --enable-cross-compile --cc="clang -arch arm64" --target-os=darwin)
        # ARM64 uses the integrated assembler — nasm not required.
        ;;
    osx-x64)
        [ "$HOST_OS" = "Darwin" ] || { echo "osx-x64 must build on macOS"; exit 1; }
        EXTRA_CFLAGS="-arch x86_64"; EXTRA_LDFLAGS="-arch x86_64"
        CROSS=(--arch=x86_64 --enable-cross-compile --cc="clang -arch x86_64" --target-os=darwin)
        have_asm || CROSS+=(--disable-x86asm)
        ;;
    linux-x64)
        [ "$HOST_OS" = "Linux" ] || { echo "linux-x64 must build on Linux"; exit 1; }
        have_asm || CROSS+=(--disable-x86asm)
        ;;
    linux-arm64)
        [ "$HOST_OS" = "Linux" ] || { echo "linux-arm64 must build on Linux"; exit 1; }
        # On an x86_64 host this would need an aarch64 cross toolchain; CI builds it on an arm64 runner.
        ;;
    win-x64)
        case "$HOST_OS" in
            Linux)
                CROSS_PREFIX="x86_64-w64-mingw32-"
                CROSS=(--arch=x86_64 --target-os=mingw32 --cross-prefix="$CROSS_PREFIX" --enable-cross-compile)
                EXTRA_LDFLAGS="-static -static-libgcc"
                have_asm || CROSS+=(--disable-x86asm)
                # FFmpeg prepends the cross-prefix to pkg-config (looking for
                # x86_64-w64-mingw32-pkg-config), which the mingw-w64 apt packages don't ship — it then
                # silently falls back to a disabled pkg-config and can't find our cross-built libopus.
                # Point it at the host pkg-config; PKG_CONFIG_PATH (set below) directs it to the right .pc.
                CROSS+=(--pkg-config=pkg-config)
                ;;
            MINGW*|MSYS*)
                EXTRA_LDFLAGS="-static -static-libgcc"
                have_asm || CROSS+=(--disable-x86asm)
                ;;
            *) echo "win-x64 must build on Linux (mingw) or Windows (MSYS2)"; exit 1 ;;
        esac
        ;;
    *)
        echo "Unsupported RID: $RID"; exit 1 ;;
esac

# --- build libopus (static) so ffmpeg can encode Opus -------------------------------------------------
OPUS_PREFIX="$WORK/opus-install-$RID"
OPUS_HOST=""
case "$RID" in
    osx-x64) OPUS_HOST="x86_64-apple-darwin" ;;
    win-x64) [ -n "$CROSS_PREFIX" ] && OPUS_HOST="x86_64-w64-mingw32" ;;
esac
OPUS_CC=""
[ -n "$CROSS_PREFIX" ] && OPUS_CC="${CROSS_PREFIX}gcc"

build_opus() {
    local src="$WORK/opus-$OPUS_VERSION"
    if [ ! -d "$src" ]; then
        local tb="$WORK/opus-$OPUS_VERSION.tar.gz"
        if [ ! -f "$tb" ]; then
            echo "Downloading libopus $OPUS_VERSION source…"
            curl -fSL "https://downloads.xiph.org/releases/opus/opus-$OPUS_VERSION.tar.gz" -o "$tb"
        fi
        tar -xf "$tb" -C "$WORK"
    fi

    if [ -f "$OPUS_PREFIX/lib/pkgconfig/opus.pc" ] && [ -z "${FORCE:-}" ]; then
        echo "libopus for $RID already built at $OPUS_PREFIX."
        return
    fi

    local obuild="$WORK/opus-build-$RID"
    rm -rf "$obuild"; mkdir -p "$obuild"
    pushd "$obuild" >/dev/null
    local oargs=(--disable-shared --enable-static --disable-doc --disable-extra-programs --prefix="$OPUS_PREFIX")
    [ -n "$OPUS_HOST" ] && oargs+=(--host="$OPUS_HOST")
    echo "Configuring libopus for ${RID}…"
    env ${OPUS_CC:+CC="$OPUS_CC"} \
        ${EXTRA_CFLAGS:+CFLAGS="$EXTRA_CFLAGS"} \
        ${EXTRA_LDFLAGS:+LDFLAGS="$EXTRA_LDFLAGS"} \
        "$src/configure" "${oargs[@]}" </dev/null
    echo "Building libopus…"
    make -j"$(getconf _NPROCESSORS_ONLN 2>/dev/null || echo 4)" </dev/null
    make install </dev/null
    popd >/dev/null
}

build_opus

# Point ffmpeg's configure at the freshly-built static libopus.
EXTRA_CFLAGS="${EXTRA_CFLAGS} -I$OPUS_PREFIX/include"
EXTRA_LDFLAGS="${EXTRA_LDFLAGS} -L$OPUS_PREFIX/lib"
export PKG_CONFIG_PATH="$OPUS_PREFIX/lib/pkgconfig${PKG_CONFIG_PATH:+:$PKG_CONFIG_PATH}"

# --- configure + build --------------------------------------------------------------------------------
BUILD="$WORK/build-$RID"
rm -rf "$BUILD"; mkdir -p "$BUILD"
pushd "$BUILD" >/dev/null

echo "Configuring FFmpeg for ${RID}…"
# Redirect stdin from /dev/null so configure's compiler probes never block waiting on input (which can
# hang when the script runs non-interactively, e.g. backgrounded or in CI).
"$SRC/configure" \
    "${COMMON[@]}" "${CROSS[@]}" \
    ${EXTRA_CFLAGS:+--extra-cflags="$EXTRA_CFLAGS"} \
    ${EXTRA_LDFLAGS:+--extra-ldflags="$EXTRA_LDFLAGS"} </dev/null

echo "Building (this can take several minutes)…"
make -j"$(getconf _NPROCESSORS_ONLN 2>/dev/null || echo 4)" </dev/null

popd >/dev/null

# --- install ------------------------------------------------------------------------------------------
mkdir -p "$DEST"
cp "$BUILD/$BIN_NAME" "$DEST/$BIN_NAME"
# Strip to shrink the binary (best-effort; respects the cross toolchain's strip).
if command -v "${CROSS_PREFIX}strip" >/dev/null 2>&1; then
    "${CROSS_PREFIX}strip" "$DEST/$BIN_NAME" || true
fi
chmod +x "$DEST/$BIN_NAME" || true

# Ship FFmpeg's licence next to the binary (LGPL compliance).
cp "$SRC/COPYING.LGPLv2.1" "$DEST/COPYING.LGPLv2.1" 2>/dev/null || true
cp "$SRC/COPYING.LGPLv3"   "$DEST/COPYING.LGPLv3"   2>/dev/null || true

echo ""
echo "Built ffmpeg $FFMPEG_VERSION for $RID -> $DEST/$BIN_NAME"
ls -lh "$DEST/$BIN_NAME"
