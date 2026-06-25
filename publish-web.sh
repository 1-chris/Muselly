#!/bin/bash
#
# Publishes the WebAssembly browser client (Muselly.Web) and stages the static bundle into dist/web/.
#
# NOTE: The browser app is no longer a standalone static demo — it is a client of a Muselly web server
# (the desktop app's web server or Muselly.Headless). Serve the staged bundle by pointing the host at it:
#
#   export MUSELLY_WEBROOT="$(pwd)/dist/web"
#   dotnet run --project Muselly.Headless        # or enable the web server in the desktop app
#
# The host then serves these files AND the API the app talks to. A plain static host will NOT work, since
# there is no backend to authenticate against, stream audio from, or read the library.
#
# Usage:
#   ./publish-web.sh        # Release build of Muselly.Web into dist/web/
#
# Requires the wasm-tools workload:  dotnet workload install wasm-tools

set -eu
ROOT="$(cd "$(dirname "$0")" && pwd)"
cd "$ROOT"

PROJ="$ROOT/Muselly.Web/Muselly.Web.csproj"
BUNDLE="$ROOT/Muselly.Web/bin/Release/net10.0-browser/browser-wasm/AppBundle"
DEST="$ROOT/dist/web"

echo "=== Publishing Muselly.Web (Release) ==="
dotnet publish "$PROJ" -c Release

rm -rf "$DEST"
mkdir -p "$DEST"
cp -r "$BUNDLE/." "$DEST/"

echo ""
echo "Web bundle staged in: $DEST"
echo "Serve it through a Muselly host, e.g.:"
echo "  MUSELLY_WEBROOT=\"$DEST\" dotnet run --project Muselly.Headless"
