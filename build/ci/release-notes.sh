#!/usr/bin/env bash
# Prints the release notes for a version: its CHANGELOG.md section, then how to install. Used by the release scripts.
#
#   build/ci/release-notes.sh <version>     e.g. 0.12.0 (a leading v is dropped)
set -euo pipefail

VERSION="${1:?usage: release-notes.sh <version>}"
VERSION="${VERSION#v}"

bash "$(dirname "$0")/changelog-notes.sh" "$VERSION"
cat <<'NOTES'

## Installing

**macOS** (Apple Silicon): open the .dmg and drag Synchromesh to Applications. The app is not notarized by Apple, so
macOS stops it the first time. Open it once, then go to System Settings > Privacy & Security and click Open Anyway
next to the message about Synchromesh.

**Windows**: run the setup for your PC (x64 for most PCs, arm64 for Windows on ARM). No admin rights needed. The setup
is not code-signed, so Windows SmartScreen may warn first: click More info, then Run anyway.

**Linux** (x64 or arm64): unpack the .tar.gz and run ./install.sh in the folder. It installs for your user only (no
root) and adds the app to your application menu; ~/.local/share/nl.vinch.synchromesh/uninstall.sh removes it again.
Passwords go to your keyring (GNOME Keyring, KWallet or KeePassXC); without one, such as on a Steam Deck, the app
offers its own encrypted password file.
NOTES
