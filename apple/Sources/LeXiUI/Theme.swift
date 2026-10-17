import AppKit
import SwiftUI

public enum LexiTheme {
    // The one brand pink (#FFADD2). Solid pink fills always carry dark text.
    public static let pink = Color(nsColor: pinkColor)
    static let pinkColor = NSColor(srgbRed: 1, green: 0xAD / 255.0, blue: 0xD2 / 255.0, alpha: 1)
    // Accent text is pink on dark backgrounds; on light backgrounds pale pink is unreadable, so text
    // falls back to the label color and pink stays a fill or outline.
    public static let accentText = Color(nsColor: NSColor(name: "LexiAccentText") { appearance in
        appearance.bestMatch(from: [.darkAqua, .aqua]) == .darkAqua ? pinkColor : .labelColor
    })
    // Warm part-of-speech and warning color, matching the Windows palette in dark appearance.
    public static let warm = Color(nsColor: NSColor(name: "LexiWarm") { appearance in
        appearance.bestMatch(from: [.darkAqua, .aqua]) == .darkAqua
            ? NSColor(srgbRed: 0xDD / 255.0, green: 0xBD / 255.0, blue: 0x82 / 255.0, alpha: 1)
            : NSColor(srgbRed: 0x8A / 255.0, green: 0x63 / 255.0, blue: 0x1F / 255.0, alpha: 1)
    })
    public static func lexical(_ size: CGFloat) -> Font { .system(size: size, design: .serif) }
    public static func chineseExample(_ size: CGFloat) -> Font { .custom("Songti SC", size: size) }
    // Result text sits on a calm panel above the translucent window, like the Windows panels on acrylic.
    public static let panel = Color(nsColor: NSColor(name: "LexiPanel") { appearance in
        appearance.bestMatch(from: [.darkAqua, .aqua]) == .darkAqua
            ? NSColor(srgbRed: 0.10, green: 0.10, blue: 0.11, alpha: 0.62) : NSColor(white: 1, alpha: 0.72)
    })
    public static let stroke = Color.primary.opacity(0.10)
}

// The LeXi mark: a black rounded square with a pink outline and ξ, as on Windows and the icon.
struct BrandMark: View {
    var size: CGFloat = 22
    var body: some View {
        RoundedRectangle(cornerRadius: size * 0.22)
            .fill(.black)
            .overlay { RoundedRectangle(cornerRadius: size * 0.22).strokeBorder(LexiTheme.pink, lineWidth: max(1, size / 20)) }
            .overlay { Text("ξ").font(.system(size: size * 0.66, weight: .semibold, design: .serif)).foregroundStyle(LexiTheme.pink).offset(y: -size * 0.03) }
            .frame(width: size, height: size)
            .accessibilityHidden(true)
    }
}

// The Windows TL;DR badge: a pale-pink square with stacked dark letterforms.
struct QuickReadBadge: View {
    var body: some View {
        VStack(spacing: -2) {
            Text("TL"); Text("DR")
        }
        .font(.system(size: 9, weight: .black, design: .rounded))
        .foregroundStyle(.black)
        .frame(width: 22, height: 22)
        .background(LexiTheme.pink, in: .rect(cornerRadius: 5))
        .accessibilityHidden(true)
    }
}

// Secondary disclosures stay neutral: pink is reserved for brand and state, not for every control.
struct DisclosureStyle: ButtonStyle {
    let open: Bool
    func makeBody(configuration: Configuration) -> some View {
        configuration.label
            .font(.callout)
            .foregroundStyle(open ? .primary : .secondary)
            .padding(.horizontal, 9).padding(.vertical, 4)
            .background(Color.primary.opacity(configuration.isPressed ? 0.12 : open ? 0.08 : 0.04), in: .rect(cornerRadius: 7))
            .overlay { RoundedRectangle(cornerRadius: 7).strokeBorder(Color.primary.opacity(open ? 0.22 : 0.14)) }
            .contentShape(.rect)
    }
}

// Wraps word forms and disclosure buttons onto as many lines as the width needs.
struct FlowLayout: Layout {
    var spacing: CGFloat = 8
    var lineSpacing: CGFloat = 6

    func sizeThatFits(proposal: ProposedViewSize, subviews: Subviews, cache: inout ()) -> CGSize {
        arrange(width: proposal.width ?? .infinity, subviews: subviews).size
    }

    func placeSubviews(in bounds: CGRect, proposal: ProposedViewSize, subviews: Subviews, cache: inout ()) {
        for (index, origin) in arrange(width: bounds.width, subviews: subviews).origins.enumerated() {
            subviews[index].place(at: CGPoint(x: bounds.minX + origin.x, y: bounds.minY + origin.y), proposal: .unspecified)
        }
    }

    private func arrange(width: CGFloat, subviews: Subviews) -> (origins: [CGPoint], size: CGSize) {
        var origins: [CGPoint] = []
        var x: CGFloat = 0, y: CGFloat = 0, line: CGFloat = 0, widest: CGFloat = 0
        for subview in subviews {
            let size = subview.sizeThatFits(.unspecified)
            if x > 0, x + size.width > width { x = 0; y += line + lineSpacing; line = 0 }
            origins.append(CGPoint(x: x, y: y))
            x += size.width + spacing
            line = max(line, size.height)
            widest = max(widest, x - spacing)
        }
        return (origins, CGSize(width: widest, height: y + line))
    }
}

// Opens only https links, like the Windows window.
@MainActor func openSource(_ address: String) {
    guard let url = URL(string: address), url.scheme == "https" else { return }
    NSWorkspace.shared.open(url)
}

@MainActor func copyToPasteboard(_ text: String) {
    NSPasteboard.general.clearContents()
    NSPasteboard.general.setString(text, forType: .string)
}
