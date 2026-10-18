import LeXiKit
import SwiftUI

struct InputPreviewCard: View {
    let preview: InputPreview
    private var message: String { "\(preview.label)：\(preview.text)" }
    private var hint: String { "\(preview.source)\n\(message)\nReturn 或点击查看完整释义" }
    var body: some View {
        HStack(alignment: .center, spacing: 12) {
            QuickReadBadge()
            Text(verbatim: message).font(.system(size: 17, weight: .medium))
                .lineLimit(2).frame(maxWidth: .infinity, alignment: .leading)
        }
        .padding(.horizontal, 16).padding(.vertical, 10)
        .background(LexiTheme.pink.opacity(0.12), in: .rect(cornerRadius: 18, style: .continuous))
        .help(hint)
        .accessibilityElement(children: .combine)
        .accessibilityLabel(Text(verbatim: "TL;DR · \(message)"))
    }
}
