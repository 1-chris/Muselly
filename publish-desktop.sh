#!/bin/bash
#
# Builds self-contained Muselly packages for Linux, Windows and macOS, zipping each platform into
# dist/Muselly-<rid>.zip.
#
# Usage:
#   ./publish-desktop.sh                 # all platforms, no debug symbols (smallest), zipped
#   ./publish-desktop.sh --symbols       # keep .pdb debug symbols
#   ./publish-desktop.sh --no-zip        # leave the publish folders, skip zipping
#   ./publish-desktop.sh linux-x64 win-x64   # only the listed RIDs
#
# Self-contained = the .NET runtime is bundled, so target machines need no .NET install.

set -u
ROOT="$(cd "$(dirname "$0")" && pwd)"   # solution root (this script lives here)
cd "$ROOT"

PROJ="$ROOT/Muselly.Desktop/Muselly.Desktop.csproj"
OUTBASE="$ROOT/Muselly.Desktop/bin/Release/net10.0"

ALL_RIDS="linux-x64 linux-arm64 win-x64 osx-arm64 osx-x64"
SYMBOLS=0
DO_ZIP=1
RIDS=""

for arg in "$@"; do
    case "$arg" in
        --symbols)   SYMBOLS=1 ;;
        --no-zip)    DO_ZIP=0 ;;
        linux-x64|linux-arm64|win-x64|osx-arm64|osx-x64) RIDS="$RIDS $arg" ;;
        *) echo "Unknown option: $arg"; exit 1 ;;
    esac
done
[ -n "$RIDS" ] || RIDS="$ALL_RIDS"

# Default build: strip debug symbols to keep the packages small. --symbols keeps them.
SYMBOL_ARGS="-p:DebugType=none -p:DebugSymbols=false"
[ "$SYMBOLS" = "1" ] && SYMBOL_ARGS=""

COMMON="-c Release --self-contained true $SYMBOL_ARGS"
DIST="$ROOT/dist"

rm -rf "$DIST"
mkdir -p "$DIST"

for rid in $RIDS; do
    echo ""
    echo "=== Publishing $rid ==="

    # Build & bundle the matching ffmpeg (best-effort: ffmpeg can't cross-build to a different OS from this
    # host, so locally this only succeeds for the host's OS — CI builds each RID on its native runner). When
    # it isn't bundled, the app falls back to an ffmpeg on PATH.
    echo "  ensuring bundled ffmpeg for ${rid}…"
    "$ROOT/scripts/build-ffmpeg.sh" "$rid" || echo "  (warning) ffmpeg not bundled for $rid — will fall back to PATH"

    out="$OUTBASE/$rid/publish"
    rm -rf "$out"
    dotnet publish "$PROJ" $COMMON -r "$rid" || { echo "publish failed for $rid"; exit 1; }

    # Linux: the apphost is named 'Muselly', which some desktop environments mistake for a .desktop
    # launcher. Rename it to a clear executable name (the managed dll name is embedded in the apphost,
    # so this rename is safe).
    case "$rid" in linux-*)
        if [ -f "$out/Muselly" ]; then
            mv -f "$out/Muselly" "$out/Muselly.bin"
            echo "  renamed Muselly -> Muselly.bin"
        fi ;;
    esac

    # Package: stage into Muselly-<rid>/ so it extracts to a tidy folder, then zip.
    if [ "$DO_ZIP" = "1" ]; then
        stage="$DIST/Muselly-$rid"
        rm -rf "$stage"; mkdir -p "$stage"
        cp -a "$out/." "$stage/"
        if command -v zip >/dev/null 2>&1; then
            (cd "$DIST" && zip -qr "Muselly-$rid.zip" "Muselly-$rid")
            echo "  -> dist/Muselly-$rid.zip"
        else
            (cd "$DIST" && tar -czf "Muselly-$rid.tar.gz" "Muselly-$rid")
            echo "  zip not found — wrote dist/Muselly-$rid.tar.gz instead"
        fi
        rm -rf "$stage"
    fi
done

echo ""
echo "Publishing complete!"
[ "$DO_ZIP" = "1" ] && echo "Packages in: $DIST"
echo "Run targets inside each package:"
echo "  Linux:   ./Muselly.bin"
echo "  Windows: Muselly.exe"
echo "  macOS:   ./Muselly"
