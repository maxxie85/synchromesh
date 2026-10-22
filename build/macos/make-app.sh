#!/usr/bin/env bash
# Builds "Synchromesh.app" for this Mac into artifacts/ and registers it with LaunchServices.
#
#   build/macos/make-app.sh [version]
#
# For a .dmg (what CI publishes), run build/macos/package.sh instead.
set -euo pipefail

# macOS expects numbers only in CFBundleShortVersionString, so no "-dev" suffix here.
VERSION="${1:-0.0.0}"
ROOT="$(cd "$(dirname "$0")/../.." && pwd)"

"$ROOT/build/macos/package.sh" "$VERSION" osx-arm64 --app-only

# Tell LaunchServices about the bundle so the .torrent and magnet: registrations take effect right away.
/System/Library/Frameworks/CoreServices.framework/Frameworks/LaunchServices.framework/Support/lsregister \
    -f "$ROOT/artifacts/Synchromesh.app"
