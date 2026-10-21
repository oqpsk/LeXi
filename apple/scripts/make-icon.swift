// Writes the ξ layer of apple/Resources/AppIcon.icon from docs/brandmark.svg, so the repository keeps
// one drawing of the mark. macOS draws the icon's shape, glass and shadow itself from icon.json, which
// sets the black fill; the brandmark's rounded plate and pink outline belong to Windows and the README.
// With --ios it renders one full-bleed 1024-pixel PNG for the iOS asset catalog instead.
// usage: swift make-icon.swift <brandmark.svg> <AppIcon.icon>
//        swift make-icon.swift <brandmark.svg> <output.png> --ios
import AppKit

let arguments = CommandLine.arguments
// iOS masks the icon itself: fill the square with the mark's black and scale the 256-unit artwork so the
// canvas sits inside its pink outline, rounded corners included (22...234), leaving the ξ centred on black.
if arguments.count == 4 && arguments[3] == "--ios" {
    guard let mark = NSImage(contentsOf: URL(fileURLWithPath: arguments[1])) else {
        FileHandle.standardError.write(Data("usage: make-icon.swift <brandmark.svg> <output.png> --ios\n".utf8)); exit(2)
    }
    let output = URL(fileURLWithPath: arguments[2])
    let pixels = 1024
    guard let bitmap = NSBitmapImageRep(bitmapDataPlanes: nil, pixelsWide: pixels, pixelsHigh: pixels, bitsPerSample: 8,
                                        samplesPerPixel: 4, hasAlpha: true, isPlanar: false, colorSpaceName: .deviceRGB, bytesPerRow: 0, bitsPerPixel: 0)
    else { exit(1) }
    NSGraphicsContext.saveGraphicsState()
    NSGraphicsContext.current = NSGraphicsContext(bitmapImageRep: bitmap)
    NSGraphicsContext.current?.imageInterpolation = .high
    NSColor.black.setFill()
    NSRect(x: 0, y: 0, width: pixels, height: pixels).fill()
    let scale = CGFloat(pixels) / 212
    mark.draw(in: NSRect(x: -22 * scale, y: -22 * scale, width: 256 * scale, height: 256 * scale))
    NSGraphicsContext.restoreGraphicsState()
    try bitmap.representation(using: .png, properties: [:])!.write(to: output)
    exit(0)
}
guard arguments.count == 3, let svg = try? String(contentsOfFile: arguments[1], encoding: .utf8),
      let glyph = svg.range(of: ##"<path fill="#FFADD2" d="[^"]+"/>"##, options: .regularExpression) else {
    FileHandle.standardError.write(Data("usage: make-icon.swift <brandmark.svg> <AppIcon.icon>\n".utf8)); exit(2)
}
// The 256-point brandmark maps onto the 1024-point icon canvas.
let path = svg[glyph].replacingOccurrences(of: "<path ", with: #"<path transform="scale(4)" "#)
let layer = #"<svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 1024 1024" width="1024" height="1024">"# + path + "</svg>\n"
let assets = URL(fileURLWithPath: arguments[2]).appending(path: "Assets")
try FileManager.default.createDirectory(at: assets, withIntermediateDirectories: true)
try layer.write(to: assets.appending(path: "xi.svg"), atomically: true, encoding: .utf8)
