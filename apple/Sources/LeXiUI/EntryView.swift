import LeXiKit
import SwiftUI

// One dictionary or translation page, drawn exactly from the QueryView Lexi.Core published.
public struct EntryView: View {
    @Bindable var store: LexiStore
    var speech: SpeechController

    public init(store: LexiStore, speech: SpeechController) { self.store = store; self.speech = speech }

    public var body: some View {
        let view = store.view
        VStack(alignment: .leading, spacing: 14) {
            HeadingRow(view: view, store: store, speech: speech)
            if !view.wordForms.isEmpty { WordFormsView(rows: view.wordForms) }
            if view.quickRead.visible { QuickReadCard(quickRead: view.quickRead) }
            if view.kind == .translation {
                if let translation = view.translation { TranslationCard(translation: translation) }
            } else {
                if let answers = view.answers, !answers.empty { AnswersCard(answers: answers, store: store) }
                LocalSourcesView(view: view, store: store)
                OnlineSourceView(view: view, store: store)
            }
        }
        .frame(maxWidth: .infinity, alignment: .leading)
    }
}

struct HeadingRow: View {
    let view: QueryView
    let store: LexiStore
    var speech: SpeechController
    @State private var showEtymology = false

    var body: some View {
        HStack(alignment: .firstTextBaseline, spacing: 10) {
            Text(view.heading)
                .font(.system(size: view.kind == .translation ? 24 : 28, weight: .semibold))
                .textSelection(.enabled)
            if !view.pronunciation.text.isEmpty {
                Text(view.pronunciation.text)
                    .font(view.kind == .translation ? .system(size: 15) : LexiTheme.lexical(17))
                    .foregroundStyle(.secondary)
                    .textSelection(.enabled)
                    .help(view.pronunciation.attribution)
                if !view.pronunciation.label.isEmpty {
                    Text(view.pronunciation.label.trimmingCharacters(in: .whitespaces))
                        .font(.caption).foregroundStyle(.secondary)
                        .help(view.pronunciation.labelAttribution)
                        .accessibilityLabel(view.pronunciation.labelAttribution)
                }
            }
            if !view.speechText.isEmpty {
                Button { speech.toggle(view.speechText, using: store) } label: {
                    Image(systemName: speech.speaking ? "stop.circle" : "speaker.wave.2")
                }
                .buttonStyle(.borderless)
                .help(speech.speaking ? "停止播放" : "播放合成发音")
                .accessibilityLabel(speech.speaking ? "停止合成发音" : "播放合成发音")
            }
            if !view.etymology.isEmpty {
                Button { showEtymology.toggle() } label: { Image(systemName: "scroll") }
                    .buttonStyle(.borderless)
                    .help("词源")
                    .accessibilityLabel("查看词源")
                    .popover(isPresented: $showEtymology, arrowEdge: .bottom) { EtymologyPopover(items: view.etymology) }
            }
        }
    }
}

struct EtymologyPopover: View {
    let items: [Etymology]
    var body: some View {
        ScrollView {
            VStack(alignment: .leading, spacing: 8) {
                ForEach(Array(items.enumerated()), id: \.offset) { _, item in
                    if item.generated {
                        Text(item.text).font(.system(size: 15)).textSelection(.enabled)
                        Text(item.caption).font(.caption).foregroundStyle(.secondary)
                    } else {
                        Text(item.caption).font(.caption).foregroundStyle(.secondary)
                        Text(item.text).font(.system(size: 15)).textSelection(.enabled)
                        SourceLink(item.linkLabel, url: item.sourceUrl)
                    }
                }
            }
            .padding(16)
            .frame(width: 380, alignment: .leading)
        }
        .frame(maxHeight: 420)
    }
}

struct WordFormsView: View {
    let rows: [WordFormRow]
    var body: some View {
        VStack(alignment: .leading, spacing: 6) {
            ForEach(Array(rows.enumerated()), id: \.offset) { _, row in
                FlowLayout(spacing: 16, lineSpacing: 4) {
                    ForEach(Array(row.cells.enumerated()), id: \.offset) { _, cell in
                        Text("\(Text(cell.label + "  ").font(.caption).foregroundStyle(.secondary))\(Text(cell.spelling).font(LexiTheme.lexical(16)).foregroundStyle(cell.matches ? LexiTheme.accentText : .primary))")
                            .help(cell.tooltip)
                            .contextMenu { Button("复制词形") { copyToPasteboard(cell.copyText) } }
                    }
                }
            }
        }
    }
}

struct QuickReadCard: View {
    let quickRead: QuickRead
    var body: some View {
        HStack(alignment: .center, spacing: 12) {
            Text("TL;DR")
                .font(.system(size: 11, weight: .heavy, design: .rounded))
                .foregroundStyle(.black)
                .padding(.horizontal, 6).padding(.vertical, 3)
                .background(LexiTheme.pink, in: .rect(cornerRadius: 5))
                .accessibilityHidden(true)
            Text(quickRead.text).font(.system(size: 17)).textSelection(.enabled).help(quickRead.tooltip)
            if quickRead.showState {
                Text(quickRead.state).font(.caption).foregroundStyle(.secondary)
            }
        }
        .padding(.horizontal, 14).padding(.vertical, 10)
        .frame(maxWidth: .infinity, alignment: .leading)
        .background(LexiTheme.pink.opacity(0.14), in: .rect(cornerRadius: 10))
        .help(quickRead.caption + "\n" + quickRead.explanation)
        .accessibilityElement(children: .combine)
        .accessibilityLabel(quickRead.caption + "。" + quickRead.text)
    }
}

struct TranslationCard: View {
    let translation: TranslationResult
    var body: some View {
        VStack(alignment: .leading, spacing: 10) {
            if !translation.error.isEmpty {
                Text(translation.error).foregroundStyle(.secondary)
            } else {
                HStack {
                    Text("翻译").font(.headline).foregroundStyle(LexiTheme.accentText)
                    Spacer()
                    Text(translation.source).font(.caption).foregroundStyle(.secondary)
                }
                Text(translation.hint).font(.callout).foregroundStyle(.secondary)
                ForEach(Array(translation.paragraphs.enumerated()), id: \.offset) { _, paragraph in
                    Text(paragraph).font(.system(size: 17)).lineSpacing(5).textSelection(.enabled)
                }
                if !translation.note.trimmingCharacters(in: .whitespaces).isEmpty {
                    Text(translation.note).font(.callout).foregroundStyle(.secondary)
                }
                if translation.complete {
                    Button("复制译文") { copyToPasteboard(translation.paragraphs.joined(separator: "\n\n")) }
                }
            }
        }
        .padding(16)
        .frame(maxWidth: .infinity, alignment: .leading)
        .background(.quaternary.opacity(0.5), in: .rect(cornerRadius: 10))
    }
}

struct SourceLink: View {
    let label: String
    let url: String
    init(_ label: String, url: String) { self.label = label; self.url = url }
    var body: some View {
        Button { openSource(url) } label: { Label(label, systemImage: "arrow.up.right").labelStyle(TrailingIcon()) }
            .buttonStyle(.link)
            .help(url)
    }
}

struct TrailingIcon: LabelStyle {
    func makeBody(configuration: Configuration) -> some View {
        HStack(spacing: 3) { configuration.title; configuration.icon.imageScale(.small) }
    }
}
