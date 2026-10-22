#!/usr/bin/env bash
# Packages Synchromesh for Windows: a per-user setup .exe built with NSIS.
#
#   build/windows/package.sh <version> [rid]
#
#   version  e.g. 1.2.0 or 1.2.0-beta.1
#   rid      win-x64 (default) or win-arm64
#
# Runs on Linux or macOS with makensis (NSIS 3); build/ci/install-windows-packaging-tools.sh installs it on a runner.
# The setup isn't code-signed, so Windows SmartScreen asks users to confirm it (More info > Run anyway).
#
# Output: artifacts/Synchromesh-<version>-windows-<arch>-setup.exe
set -euo pipefail

VERSION="${1:?usage: package.sh <version> [rid]}"
RID="${2:-win-x64}"
case "$RID" in
    win-x64) ARCH="x64" ;;
    win-arm64) ARCH="arm64" ;;
    *) echo "Unsupported runtime identifier: $RID (use win-x64 or win-arm64)" >&2; exit 2 ;;
esac

ROOT="$(cd "$(dirname "$0")/../.." && pwd)"
OUT="$ROOT/artifacts"
PUBLISH="$OUT/publish/$RID"
SETUP="$OUT/Synchromesh-$VERSION-windows-$ARCH-setup.exe"

rm -rf "$PUBLISH" "$SETUP"
mkdir -p "$OUT"

# Self-contained: no .NET install needed. A folder rather than single-file: Windows has no bundle seal to worry
# about, and it starts faster. Cross-publishing from Linux or macOS works the same.
dotnet publish "$ROOT/src/Synchro.Desktop/Synchro.Desktop.csproj" \
    -c Release -r "$RID" --self-contained true \
    -p:Version="$VERSION" \
    -o "$PUBLISH"
find "$PUBLISH" -name "*.pdb" -delete
# The licenses of what the app includes, next to the .exe (also in the app: About > License texts).
cp "$ROOT/THIRD-PARTY-NOTICES.txt" "$PUBLISH/"
cp "$ROOT/LICENSE" "$PUBLISH/LICENSE.txt"

# makensis crashes (std::bad_alloc) unless it runs in a UTF-8 locale, so give it one explicitly.
NSIS_LOCALE="C.UTF-8"
if [ "$(uname -s)" = "Darwin" ]; then NSIS_LOCALE="en_US.UTF-8"; fi

LANG="$NSIS_LOCALE" LC_ALL="$NSIS_LOCALE" makensis -V2 \
    -DVERSION="$VERSION" \
    -DVERSION_NUMERIC="${VERSION%%-*}" \
    -DARCH="$ARCH" \
    -DSOURCE="$PUBLISH" \
    -DICON="$ROOT/build/icon/AppIcon.ico" \
    -DOUTFILE="$SETUP" \
    "$ROOT/build/windows/installer.nsi"

echo "Built $SETUP"
