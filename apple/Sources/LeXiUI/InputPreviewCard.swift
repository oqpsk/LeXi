import LeXiKit
import SwiftUI

struct InputPreviewCard: View {
    let preview: InputPreview
    var body: some View {
        HStack(alignment: .center, spacing: 12) {
            QuickReadBadge()
            Text(preview.label + "：" + preview.text).font(.system(size: 17, weight: .medium))
                .lineLimit(2).frame(maxWidth: .infinity, alignment: .leading)
        }
        .padding(.horizontal, 16).padding(.vertical, 10)
        .background(LexiTheme.pink.opacity(0.12), in: .rect(cornerRadius: 18, style: .continuous))
        .help(preview.source + "\n" + preview.label + "：" + preview.text + "\nReturn 或点击查看完整释义")
        .accessibilityElement(children: .combine)
        .accessibilityLabel("TL;DR · " + preview.label + "：" + preview.text)
    }
}
