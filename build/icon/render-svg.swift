// Renders an SVG to a square PNG with a transparent background and the app-icon drop shadow
// (Quick Look would flatten it onto white; AppKit's SVG support ignores SVG filters).
// Usage: swift render-svg.swift <input.svg> <output.png> <size> [windows]
//   windows: crop to the icon body (the 824 px square at 100,100 on the 1024 grid) without the shadow, because
//            Windows icons fill their square while macOS icons keep a margin for the shadow.
import AppKit

let args = CommandLine.arguments
let windows = args.count == 5 && args[4] == "windows"
guard args.count == 4 || windows, let size = Int(args[3]),
      let image = NSImage(contentsOf: URL(fileURLWithPath: args[1])) else {
    FileHandle.standardError.write("usage: render-svg.swift <input.svg> <output.png> <size>\n".data(using: .utf8)!)
    exit(1)
}

guard let bitmap = NSBitmapImageRep(
    bitmapDataPlanes: nil, pixelsWide: size, pixelsHigh: size, bitsPerSample: 8, samplesPerPixel: 4,
    hasAlpha: true, isPlanar: false, colorSpaceName: .deviceRGB, bytesPerRow: 0, bitsPerPixel: 0) else { exit(1) }

NSGraphicsContext.saveGraphicsState()
NSGraphicsContext.current = NSGraphicsContext(bitmapImageRep: bitmap)
NSColor.clear.set()
NSRect(x: 0, y: 0, width: size, height: size).fill()

let target = NSRect(x: 0, y: 0, width: size, height: size)
if windows {
    // The body square, in the image's own coordinates (its SVG size, 1024 x 1024).
    let unit = image.size.width / 1024
    image.draw(in: target, from: NSRect(x: 100 * unit, y: 100 * unit, width: 824 * unit, height: 824 * unit),
               operation: .sourceOver, fraction: 1)
} else {
    // The soft drop shadow macOS app icons have; scaled so every size looks the same.
    let scale = CGFloat(size) / 1024
    let shadow = NSShadow()
    shadow.shadowOffset = NSSize(width: 0, height: -12 * scale)
    shadow.shadowBlurRadius = 28 * scale
    shadow.shadowColor = NSColor.black.withAlphaComponent(0.3)
    shadow.set()
    image.draw(in: target, from: .zero, operation: .sourceOver, fraction: 1)
}
NSGraphicsContext.restoreGraphicsState()

guard let png = bitmap.representation(using: .png, properties: [:]) else { exit(1) }
try png.write(to: URL(fileURLWithPath: args[2]))
