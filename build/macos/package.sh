#!/usr/bin/env bash
# Packages Synchromesh for macOS: the .app bundle and an installable .dmg.
#
#   build/macos/package.sh <version> [rid] [--app-only]
#
#   version     e.g. 1.2.0 (goes into Info.plist and the file name)
#   rid         osx-arm64 (default) or osx-x64
#   --app-only  stop after the .app (local builds; see make-app.sh)
#
# Runs on macOS and on Linux, so CI needs no Mac:
#   signing   macOS: codesign            Linux: rcodesign (apple-codesign)
#   disk      macOS: hdiutil             Linux: mkfs.hfsplus (hfsprogs) + hfsplus/dmg (libdmg-hfsplus)
# build/ci/install-macos-packaging-tools.sh installs the Linux tools.
#
# The signature is ad-hoc: enough for Apple Silicon to run the app, but a downloaded copy is still stopped by
# Gatekeeper on first open (right-click > Open). A Developer ID signature and notarization would remove that.
#
# Output in artifacts/: "Synchromesh.app" and Synchromesh-<version>-macos-<arch>.dmg
set -euo pipefail

VERSION="${1:?usage: package.sh <version> [rid] [--app-only]}"
RID="osx-arm64"
APP_ONLY=false
for arg in "${@:2}"; do
    case "$arg" in
        --app-only) APP_ONLY=true ;;
        osx-*) RID="$arg" ;;
        *) echo "Unknown argument: $arg" >&2; exit 2 ;;
    esac
done

case "$RID" in
    osx-arm64) ARCH="arm64" ;;
    osx-x64) ARCH="x64" ;;
    *) echo "Unsupported runtime identifier: $RID (use osx-arm64 or osx-x64)" >&2; exit 2 ;;
esac

NAME="Synchromesh"
ROOT="$(cd "$(dirname "$0")/../.." && pwd)"
OUT="$ROOT/artifacts"
PUBLISH="$OUT/publish/$RID"
APP="$OUT/$NAME.app"
DMG="$OUT/Synchromesh-$VERSION-macos-$ARCH.dmg"
HOST="$(uname -s)"

rm -rf "$PUBLISH" "$APP" "$DMG"
mkdir -p "$OUT"

# 1. Self-contained, single-file publish: no .NET install needed on the user's Mac, and Contents/MacOS ends up with
#    only code (the executable and native .dylib files). That matters for signing: rcodesign seals the code in
#    Contents/MacOS but not other files there, and macOS reports any unsealed file as "damaged".
#    Cross-publishing from Linux works the same.
dotnet publish "$ROOT/src/Synchro.Desktop/Synchro.Desktop.csproj" \
    -c Release -r "$RID" --self-contained true \
    -p:UseAppHost=true -p:PublishSingleFile=true -p:Version="$VERSION" \
    -o "$PUBLISH"

# 2. The bundle: executable and libraries in MacOS/, icon in Resources/, Info.plist with the version filled in.
mkdir -p "$APP/Contents/MacOS" "$APP/Contents/Resources"
cp -R "$PUBLISH/." "$APP/Contents/MacOS/"
rm -f "$APP/Contents/MacOS/"*.pdb
cp "$ROOT/build/icon/AppIcon.icns" "$APP/Contents/Resources/AppIcon.icns"
# The licenses of what the app includes (also in the app: About > License texts). Resources/, not MacOS/: see step 1.
cp "$ROOT/THIRD-PARTY-NOTICES.txt" "$APP/Contents/Resources/"
cp "$ROOT/LICENSE" "$APP/Contents/Resources/LICENSE.txt"
# macOS wants only numbers in the bundle version: 1.2.0-beta.1 becomes 1.2.0 there (the file name keeps it all).
sed -e "s/@VERSION@/${VERSION%%-*}/g" "$ROOT/build/macos/Info.plist" > "$APP/Contents/Info.plist"
python3 -c 'import plistlib, sys; plistlib.load(open(sys.argv[1], "rb"))' "$APP/Contents/Info.plist"

# Anything in Contents/MacOS that isn't code would be left out of rcodesign's seal (see step 1). Fail early.
STRAY=$(cd "$APP/Contents/MacOS" && for f in *; do
    case "$(head -c 4 "$f" | od -An -tx1 | tr -d ' \n')" in
        (cffaedfe|cefaedfe|cafebabe) ;;  # Mach-O (64-bit, 32-bit, universal)
        (*) echo "$f" ;;
    esac
done)
if [ -n "$STRAY" ]; then
    echo "Not code, but in Contents/MacOS (would break the signature): $STRAY" >&2
    exit 1
fi

# 3. Sign. Apple Silicon refuses to run unsigned code; an ad-hoc signature over the whole bundle is the minimum.
#    MACOS_SIGNER=rcodesign forces the Linux signer on a Mac, to test what CI produces.
SIGNER="${MACOS_SIGNER:-$([ "$HOST" = "Darwin" ] && echo codesign || echo rcodesign)}"
if [ "$SIGNER" = "codesign" ]; then
    codesign --force --deep --sign - "$APP"
else
    rcodesign sign "$APP"
fi

# On a Mac, let Apple's own tool judge the result: a broken seal is what users see as "damaged".
if [ "$HOST" = "Darwin" ]; then
    codesign --verify --deep --strict "$APP"
fi
echo "Built $APP"

if [ "$APP_ONLY" = true ]; then exit 0; fi

# 4. The disk image: the app plus a link to /Applications to drag it onto.
STAGE="$OUT/dmg-stage"
rm -rf "$STAGE"
mkdir -p "$STAGE"
cp -R "$APP" "$STAGE/"

if [ "$HOST" = "Darwin" ]; then
    ln -s /Applications "$STAGE/Applications"
    hdiutil create -volname "$NAME" -srcfolder "$STAGE" -fs HFS+ -format UDZO -ov "$DMG" > /dev/null
else
    # The way Firefox builds its .dmg on Linux: an HFS+ image filled with hfsplus, compressed by dmg.
    # The Applications link is added inside the image: addall would follow a link to /Applications on this machine.
    IMAGE="$OUT/dmg-stage.hfs"
    SIZE_MB=$(( $(du -sm "$STAGE" | cut -f1) * 12 / 10 + 16 ))
    rm -f "$IMAGE"
    truncate -s "${SIZE_MB}M" "$IMAGE"
    mkfs.hfsplus -v "$NAME" "$IMAGE" > /dev/null
    hfsplus "$IMAGE" addall "$STAGE" > /dev/null
    hfsplus "$IMAGE" symlink /Applications /Applications
    dmg build "$IMAGE" "$DMG" > /dev/null
    rm -f "$IMAGE"
fi
rm -rf "$STAGE"

echo "Built $DMG"
