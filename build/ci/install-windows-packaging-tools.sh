#!/usr/bin/env bash
# Installs what build/windows/package.sh needs on a Debian/Ubuntu runner: makensis (NSIS 3) to build the setup .exe.
set -euo pipefail

SUDO=""
if [ "$(id -u)" -ne 0 ]; then SUDO="sudo"; fi

$SUDO apt-get update -qq
$SUDO apt-get install -y -qq --no-install-recommends nsis > /dev/null

echo "Installed: $(makensis -VERSION)"
