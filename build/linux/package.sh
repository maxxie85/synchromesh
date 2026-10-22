#!/usr/bin/env bash
# Packages Synchromesh for Linux: a .tar.gz with the self-contained app and a per-user install script.
#
#   build/linux/package.sh <version> [rid]
#
#   version  e.g. 1.2.0 or 1.2.0-beta.1
#   rid      linux-x64 (default) or linux-arm64
#
# Runs on Linux or macOS (cross-publishing works the same). Needs only dotnet and tar.
#
# Output: artifacts/Synchromesh-<version>-linux-<arch>.tar.gz, which unpacks to one folder with install.sh.
set -euo pipefail

VERSION="${1:?usage: package.sh <version> [rid]}"
RID="${2:-linux-x64}"
case "$RID" in
    linux-x64) ARCH="x64" ;;
    linux-arm64) ARCH="arm64" ;;
    *) echo "Unsupported runtime identifier: $RID (use linux-x64 or linux-arm64)" >&2; exit 2 ;;
esac

ROOT="$(cd "$(dirname "$0")/../.." && pwd)"
OUT="$ROOT/artifacts"
NAME="Synchromesh-$VERSION-linux-$ARCH"
STAGE="$OUT/linux-stage/$NAME"
TARBALL="$OUT/$NAME.tar.gz"

rm -rf "$OUT/linux-stage" "$TARBALL"
mkdir -p "$STAGE"

# 1. Self-contained folder: no .NET install needed on the user's machine.
dotnet publish "$ROOT/src/Synchro.Desktop/Synchro.Desktop.csproj" \
    -c Release -r "$RID" --self-contained true \
    -p:Version="$VERSION" \
    -o "$STAGE/app"
find "$STAGE/app" -name "*.pdb" -delete
chmod +x "$STAGE/app/Synchromesh"

# 2. What install.sh needs: the .desktop template, the icon, and the licenses.
cp "$ROOT/build/linux/install.sh" "$STAGE/install.sh"
chmod +x "$STAGE/install.sh"
cp "$ROOT/build/linux/nl.vinch.synchromesh.desktop" "$STAGE/"
cp "$ROOT/src/Synchro.Desktop/Assets/AppIcon.png" "$STAGE/nl.vinch.synchromesh.png"
cp "$ROOT/THIRD-PARTY-NOTICES.txt" "$STAGE/"
cp "$ROOT/LICENSE" "$STAGE/LICENSE.txt"

# 3. The archive. COPYFILE_DISABLE keeps macOS's ._ metadata files out when built on a Mac.
COPYFILE_DISABLE=1 tar -czf "$TARBALL" -C "$OUT/linux-stage" "$NAME"
rm -rf "$OUT/linux-stage"

echo "Built $TARBALL"
