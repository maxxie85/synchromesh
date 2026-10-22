#!/usr/bin/env bash
# Renders build/icon/AppIcon.svg into build/icon/AppIcon.icns (the macOS bundle icon), build/icon/AppIcon.ico (Windows:
# installer, Start menu, uninstall entry) and src/Synchro.Desktop/Assets/AppIcon.png (used inside the app), with macOS's own
# tools: Swift/AppKit for rendering, iconutil for packing; the .ico is assembled by a few lines of Python.
# Run it after changing the SVG and commit the outputs; the app build only copies them.
set -euo pipefail

DIR="$(cd "$(dirname "$0")" && pwd)"
WORK="$(mktemp -d)"
trap 'rm -rf "$WORK"' EXIT

# Each size is rendered from the vector art rather than scaled down from one bitmap, so small sizes stay sharp.
render() { swift "$DIR/render-svg.swift" "$DIR/AppIcon.svg" "$1" "$2"; }

ICONSET="$WORK/AppIcon.iconset"
mkdir -p "$ICONSET"
for size in 16 32 128 256 512; do
    render "$ICONSET/icon_${size}x${size}.png" "$size"
    render "$ICONSET/icon_${size}x${size}@2x.png" $((size * 2))
done

iconutil -c icns "$ICONSET" -o "$DIR/AppIcon.icns"
echo "Wrote $DIR/AppIcon.icns"

# Windows: the standard sizes, each holding a PNG (supported since Windows Vista).
ICO_DIR="$WORK/ico"
mkdir -p "$ICO_DIR"
for size in 16 24 32 48 64 128 256; do
    swift "$DIR/render-svg.swift" "$DIR/AppIcon.svg" "$ICO_DIR/$size.png" "$size" windows
done
python3 - "$ICO_DIR" "$DIR/AppIcon.ico" <<'PY'
import os, struct, sys
folder, out = sys.argv[1], sys.argv[2]
sizes = [16, 24, 32, 48, 64, 128, 256]
images = [open(os.path.join(folder, f"{s}.png"), "rb").read() for s in sizes]
offset = 6 + 16 * len(sizes)
header = struct.pack("<HHH", 0, 1, len(sizes))
entries = b""
for size, data in zip(sizes, images):
    dim = 0 if size == 256 else size  # 0 means 256 in the ICO format
    entries += struct.pack("<BBBBHHII", dim, dim, 0, 0, 1, 32, len(data), offset)
    offset += len(data)
with open(out, "wb") as f:
    f.write(header + entries + b"".join(images))
PY
echo "Wrote $DIR/AppIcon.ico"

ASSETS="$DIR/../../src/Synchro.Desktop/Assets"
mkdir -p "$ASSETS"
render "$ASSETS/AppIcon.png" 512
echo "Wrote $ASSETS/AppIcon.png"
