import AppKit
import SwiftUI

// LeXi for macOS follows Apple's design language rather than the Windows look: system colors that
// adapt to light, dark and the user's accent, continuous corners, Liquid Glass for the floating
// controls, and content on quiet grouped surfaces. Pink appears only as the brand TL;DR mark.
public enum LexiTheme {
    // The one brand pink (#FFADD2). Solid pink fills always carry dark text.
    public static let pink = Color(nsColor: NSColor(srgbRed: 1, green: 0xAD / 255.0, blue: 0xD2 / 255.0, alpha: 1))
    public static func lexical(_ size: CGFloat, weight: Font.Weight = .regular) -> Font { .system(size: size, weight: weight, design: .serif) }
    // A neutral window background. macOS tints the standard window background with the wallpaper's
    // color (in dark mode especially); LeXi keeps its windows neutral so glass and text stay grey.
    public static let windowBackground = Color(nsColor: NSColor(name: "LexiWindowBackground") { appearance in
        let match = appearance.bestMatch(from: [.aqua, .darkAqua, .accessibilityHighContrastAqua, .accessibilityHighContrastDarkAqua])
        return match == .darkAqua || match == .accessibilityHighContrastDarkAqua
            ? NSColor(srgbRed: 0.118, green: 0.118, blue: 0.125, alpha: 1)
            : NSColor(srgbRed: 0.965, green: 0.965, blue: 0.972, alpha: 1)
    })
    public static func chineseExample(_ size: CGFloat) -> Font { .custom("Songti SC", size: size) }
}

// Light, dark or the system appearance, for every LeXi window. Stored in the app's preferences.
public enum AppearanceChoice: String, CaseIterable, Identifiable, Sendable {
    case system, light, dark
    public static let key = "appearance"
    public var id: String { rawValue }
    var title: String { switch self { case .system: "跟随系统"; case .light: "浅色"; case .dark: "深色" } }
    @MainActor public static func apply(_ stored: String) {
        NSApp.appearance = switch AppearanceChoice(rawValue: stored) ?? .system {
        case .system: nil
        case .light: NSAppearance(named: .aqua)
        case .dark: NSAppearance(named: .darkAqua)
        }
    }
}

extension View {
    // A grouped content surface, like the groups in System Settings.
    func contentGroup(cornerRadius: CGFloat = 18) -> some View {
        background(.quinary, in: .rect(cornerRadius: cornerRadius, style: .continuous))
    }
}

// The TL;DR mark: a pale-pink rounded square with stacked dark letterforms.
struct QuickReadBadge: View {
    var body: some View {
        VStack(spacing: -2) {
            Text("TL"); Text("DR")
        }
        .font(.system(size: 9, weight: .black, design: .rounded))
        .foregroundStyle(.black)
        .frame(width: 22, height: 22)
        .background(LexiTheme.pink, in: .rect(cornerRadius: 6, style: .continuous))
        .accessibilityHidden(true)
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
