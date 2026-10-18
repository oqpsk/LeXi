// Writes the ξ layer of apple/Resources/AppIcon.icon from docs/brandmark.svg, so the repository keeps
// one drawing of the mark. macOS draws the icon's shape, glass and shadow itself from icon.json, which
// sets the black fill; the brandmark's rounded plate and pink outline belong to Windows and the README.
// usage: swift make-icon.swift <brandmark.svg> <AppIcon.icon>
import Foundation

let arguments = CommandLine.arguments
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
