#!/usr/bin/env sh
# Installs Synchromesh for the current user (no root needed), or removes it.
#
#   ./install.sh              install or update
#   ./install.sh --uninstall  remove the app (settings and saved passwords stay)
#
# Installing also puts a copy of this script in the app folder as uninstall.sh, which removes the app when run (so
# the download isn't needed for that): ~/.local/share/nl.vinch.synchromesh/uninstall.sh
#
# Puts the app in ~/.local/share/nl.vinch.synchromesh, adds it to the application menu, and registers it for
# .torrent files and magnet: links: as the default only when nothing else handles them yet (otherwise choose it in
# your desktop's default-apps settings).
set -eu

APP_ID="nl.vinch.synchromesh"
DATA="${XDG_DATA_HOME:-$HOME/.local/share}"
APP_DIR="$DATA/$APP_ID"
DESKTOP_FILE="$DATA/applications/$APP_ID.desktop"
ICON_FILE="$DATA/icons/hicolor/512x512/apps/$APP_ID.png"
HERE="$(cd "$(dirname "$0")" && pwd)"

refresh() {
    command -v update-desktop-database >/dev/null 2>&1 && update-desktop-database "$DATA/applications" 2>/dev/null || true
    command -v gtk-update-icon-cache >/dev/null 2>&1 && gtk-update-icon-cache -q -t "$DATA/icons/hicolor" 2>/dev/null || true
    # KDE reads menu entries from its own cache.
    for tool in kbuildsycoca6 kbuildsycoca5; do
        if command -v "$tool" >/dev/null 2>&1; then "$tool" >/dev/null 2>&1 || true; break; fi
    done
}

if [ "${1:-}" = "--uninstall" ] || [ "$(basename "$0")" = "uninstall.sh" ]; then
    # Removing the folder this copy runs from is fine: sh has the script open already.
    rm -rf "$APP_DIR"
    rm -f "$DESKTOP_FILE" "$ICON_FILE"
    refresh
    echo "Synchromesh is removed. Your servers and settings stay in ${XDG_CONFIG_HOME:-$HOME/.config}/Synchromesh, and saved"
    echo "passwords in your keyring or in that folder's passwords file. Delete them yourself if you won't reinstall."
    exit 0
fi

if [ ! -x "$HERE/app/Synchromesh" ]; then
    echo "Run this from the unpacked Synchromesh folder (app/Synchromesh is missing)." >&2
    exit 1
fi

# The app: replaced as a whole, so no files of an older version linger.
mkdir -p "$DATA"
rm -rf "$APP_DIR.new"
cp -R "$HERE/app" "$APP_DIR.new"
rm -rf "$APP_DIR"
mv "$APP_DIR.new" "$APP_DIR"
cp "$HERE/LICENSE.txt" "$HERE/THIRD-PARTY-NOTICES.txt" "$APP_DIR/"
cp "$HERE/install.sh" "$APP_DIR/uninstall.sh"
chmod 755 "$APP_DIR/uninstall.sh"

# Application menu entry and icon. Exec is quoted because the path may contain spaces. Icon is the full path to the
# copy in the app folder: a name is looked up in the icon theme, and KDE on a Steam Deck didn't find it in the
# user's hicolor folder (no icon in the launcher or the taskbar). The themed copy stays for lookups by name.
mkdir -p "$(dirname "$DESKTOP_FILE")" "$(dirname "$ICON_FILE")"
cp "$HERE/$APP_ID.png" "$ICON_FILE"
cp "$HERE/$APP_ID.png" "$APP_DIR/$APP_ID.png"
sed -e "s|@EXEC@|\"$APP_DIR/Synchromesh\"|" -e "s|@ICON@|$APP_DIR/$APP_ID.png|" "$HERE/$APP_ID.desktop" > "$DESKTOP_FILE"
chmod 644 "$DESKTOP_FILE"
refresh

# Default for .torrent and magnet: only when nothing handles them yet.
if command -v xdg-mime >/dev/null 2>&1; then
    for type in application/x-bittorrent x-scheme-handler/magnet; do
        current="$(xdg-mime query default "$type" 2>/dev/null)"
        if [ -z "$current" ]; then
            xdg-mime default "$APP_ID.desktop" "$type"
        fi
    done
fi

echo "Synchromesh is installed. Start it from your application menu."
echo "To remove it later: $APP_DIR/uninstall.sh"
echo "To make it open .torrent files and magnet links, choose it in your desktop's default-apps settings if it isn't already."
