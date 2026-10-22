#!/usr/bin/env bash
# Installs what build/macos/package.sh needs on a Debian/Ubuntu runner to build the macOS .app and .dmg:
#   rcodesign       ad-hoc signing (apple-codesign, prebuilt, checksum-pinned)
#   mkfs.hfsplus    an empty HFS+ volume (hfsprogs)
#   hfsplus, dmg    fill the volume and compress it into a .dmg (libdmg-hfsplus, built from a pinned commit)
set -euo pipefail

RCODESIGN_VERSION="0.29.0"
declare -A RCODESIGN_SHA256=(
    [x86_64]="dbe85cedd8ee4217b64e9a0e4c2aef92ab8bcaaa41f20bde99781ff02e600002"
    [aarch64]="4af92c87ddf52f5f2d1258a3b4e56c7dcb8f1b2468df744976c5f139e031961f"
)
LIBDMG_COMMIT="ec239599c1f234a4e01ae3fe51214d0c77e5baa3" # mozilla/libdmg-hfsplus, January 2026

SUDO=""
if [ "$(id -u)" -ne 0 ]; then SUDO="sudo"; fi

$SUDO apt-get update -qq
$SUDO apt-get install -y -qq --no-install-recommends \
    ca-certificates curl python3 hfsprogs cmake make gcc g++ libssl-dev zlib1g-dev libbz2-dev > /dev/null

WORK="$(mktemp -d)"
trap 'rm -rf "$WORK"' EXIT

# rcodesign
ARCH="$(uname -m)"
EXPECTED="${RCODESIGN_SHA256[$ARCH]:?no rcodesign build pinned for $ARCH}"
ARCHIVE="apple-codesign-$RCODESIGN_VERSION-$ARCH-unknown-linux-musl.tar.gz"
curl -fsSL -o "$WORK/$ARCHIVE" \
    "https://github.com/indygreg/apple-platform-rs/releases/download/apple-codesign/$RCODESIGN_VERSION/$ARCHIVE"
echo "$EXPECTED  $WORK/$ARCHIVE" | sha256sum -c - > /dev/null
tar -xzf "$WORK/$ARCHIVE" -C "$WORK"
$SUDO install -m 755 "$WORK/apple-codesign-$RCODESIGN_VERSION-$ARCH-unknown-linux-musl/rcodesign" /usr/local/bin/rcodesign

# libdmg-hfsplus (hfsplus and dmg)
curl -fsSL -o "$WORK/libdmg.tar.gz" "https://github.com/mozilla/libdmg-hfsplus/archive/$LIBDMG_COMMIT.tar.gz"
tar -xzf "$WORK/libdmg.tar.gz" -C "$WORK"
cmake -S "$WORK/libdmg-hfsplus-$LIBDMG_COMMIT" -B "$WORK/libdmg-build" -DCMAKE_BUILD_TYPE=Release > /dev/null
cmake --build "$WORK/libdmg-build" --parallel > /dev/null
$SUDO install -m 755 "$WORK/libdmg-build/hfs/hfsplus" /usr/local/bin/hfsplus
$SUDO install -m 755 "$WORK/libdmg-build/dmg/dmg" /usr/local/bin/dmg

echo "Installed: $(rcodesign --version | head -1), mkfs.hfsplus, hfsplus, dmg"
