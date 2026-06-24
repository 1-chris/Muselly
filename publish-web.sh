#!/bin/bash
#
# Publishes the WebAssembly head (Muselly.Web) and stages the static bundle into dist/web/, ready to
# serve from any static host (or open via a local server).
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
echo "Serve it with any static server, e.g.:"
echo "  (cd dist/web && python3 -m http.server 8000)   # then open http://localhost:8000"
