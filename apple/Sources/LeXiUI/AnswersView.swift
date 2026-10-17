import LeXiKit
import SwiftUI

// Generated senses, reference fallbacks and model notices. Secondary information sits behind
// disclosures whose state survives streaming and merging, as on Windows.
struct AnswersCard: View {
    let answers: Answers
    @Bindable var store: LexiStore

    var body: some View {
        VStack(alignment: .leading, spacing: 0) {
            ForEach(Array(answers.references.enumerated()), id: \.element.key) { index, reference in
                Row(first: index == 0) { ReferenceRow(reference: reference, store: store) }
            }
            ForEach(Array(answers.senses.enumerated()), id: \.element.key) { index, sense in
                Row(first: index + answers.references.count == 0) {
                    VStack(alignment: .leading, spacing: 4) {
                        SenseHeadingText(heading: sense.heading)
                        SenseContent(sense: sense, store: store)
                    }
                }
            }
            ForEach(Array(answers.notices.enumerated()), id: \.offset) { _, notice in
                Text(notice.text).font(.callout).foregroundStyle(notice.warning ? LexiTheme.warm : .secondary).padding(.top, 6)
            }
            if !answers.notes.isEmpty {
                Disclosure(label: "补充说明", key: "lookup:notes", store: store) {
                    ForEach(answers.notes, id: \.self) { Text($0).font(.callout).foregroundStyle(.secondary) }
                }
                .padding(.top, 6)
            }
            if !answers.mergeWarning.isEmpty {
                Text(answers.mergeWarning).font(.callout).foregroundStyle(LexiTheme.warm).padding(.top, 6)
            }
        }
        .padding(16)
        .frame(maxWidth: .infinity, alignment: .leading)
        .background(.quaternary.opacity(0.5), in: .rect(cornerRadius: 10))
    }
}

private struct Row<Content: View>: View {
    let first: Bool
    @ViewBuilder let content: Content
    var body: some View {
        VStack(alignment: .leading, spacing: 0) {
            if !first { Divider().padding(.vertical, 10) }
            content
        }
    }
}

struct SenseHeadingText: View {
    let heading: SenseHeading
    var body: some View {
        var text = Text(heading.prefix + "  ").foregroundStyle(LexiTheme.accentText)
        text = Text("\(text)\(Text(heading.partOfSpeech).font(LexiTheme.lexical(16)).italic().foregroundStyle(LexiTheme.warm))")
        if !heading.form.isEmpty { text = Text("\(text)\(Text("  " + heading.form).font(LexiTheme.lexical(14)).italic())") }
        if !heading.usage.isEmpty { text = Text("\(text)\(Text("  " + heading.usage))") }
        if heading.abbreviation { text = Text("\(text)\(Text("  缩写"))") }
        if heading.provisional { text = Text("\(text)\(Text(" · 生成中"))") }
        return text.font(.callout).foregroundStyle(.secondary).help(heading.tooltip)
    }
}

struct SenseContent: View {
    let sense: Sense
    @Bindable var store: LexiStore

    var body: some View {
        VStack(alignment: .leading, spacing: 4) {
            if !sense.expansion.isEmpty {
                Text(sense.expansion).font(LexiTheme.lexical(15)).foregroundStyle(LexiTheme.accentText).help("英文全称").textSelection(.enabled)
            }
            Text(sense.chinese).font(.system(size: 17)).textSelection(.enabled)
            Text(sense.english).font(.system(size: 15)).textSelection(.enabled)
            DisclosureBar(sense: sense, store: store)
        }
    }
}

// The three secondary disclosures of a sense, with their bodies below the buttons.
struct DisclosureBar: View {
    let sense: Sense
    @Bindable var store: LexiStore

    private var hasExamples: Bool { !sense.example.trimmingCharacters(in: .whitespaces).isEmpty || !sense.exampleChinese.trimmingCharacters(in: .whitespaces).isEmpty }
    private var examplesKey: String { sense.key + ":examples" }
    private var sourcesKey: String { sense.key + ":sources" }
    private var alternativesKey: String { sense.key + ":alternatives" }
    private var sourcesOpen: Bool { store.isOpen(sourcesKey, default: store.sourcesOpenByDefault(sense)) }
    private var alternativesOpen: Bool { store.isOpen(alternativesKey, default: store.alternativesOpenByDefault(sense)) }

    var body: some View {
        VStack(alignment: .leading, spacing: 6) {
            FlowLayout(spacing: 8) {
                if hasExamples { DisclosureButton(label: "例句", open: store.isOpen(examplesKey)) { store.setOpen(examplesKey, $0) } }
                DisclosureButton(label: sense.sourceLabel, open: sourcesOpen) { store.setOpen(sourcesKey, $0) }
                if !sense.alternatives.isEmpty { DisclosureButton(label: sense.alternativesLabel, open: alternativesOpen) { store.setOpen(alternativesKey, $0) } }
            }
            .padding(.top, 2)
            if hasExamples && store.isOpen(examplesKey) {
                DetailBody {
                    if !sense.example.isEmpty { Text(sense.example).font(LexiTheme.lexical(15)).italic().textSelection(.enabled) }
                    if !sense.exampleChinese.isEmpty { Text(sense.exampleChinese).font(LexiTheme.chineseExample(15)).italic().textSelection(.enabled) }
                }
            }
            if sourcesOpen {
                DetailBody {
                    Text(sense.provenance).font(.callout).foregroundStyle(.secondary)
                    Text(sense.originalLabels).font(.callout).foregroundStyle(.secondary)
                    if !sense.citations.isEmpty {
                        FlowLayout(spacing: 12) {
                            ForEach(sense.citations) { citation in SourceLink(citation.label, url: citation.url).help(citation.definition) }
                        }
                    }
                    ForEach(Array(sense.briefs.enumerated()), id: \.offset) { _, brief in
                        Text(brief.caption).font(.callout).foregroundStyle(.secondary)
                        Text(brief.chinese).textSelection(.enabled)
                        Text(brief.english).textSelection(.enabled)
                    }
                    if !sense.sourceNote.isEmpty { Text(sense.sourceNote).font(.callout).foregroundStyle(.secondary) }
                    if !sense.alternativesNote.isEmpty { Text(sense.alternativesNote).font(.callout).foregroundStyle(.secondary) }
                }
            }
            if !sense.alternatives.isEmpty && alternativesOpen {
                DetailBody {
                    ForEach(sense.alternatives) { alternative in
                        VStack(alignment: .leading, spacing: 4) {
                            SenseHeadingText(heading: alternative.heading)
                            SenseContent(sense: alternative, store: store)
                        }
                        .padding(.bottom, 4)
                    }
                }
            }
        }
    }
}

struct ReferenceRow: View {
    let reference: Reference
    @Bindable var store: LexiStore
    var body: some View {
        VStack(alignment: .leading, spacing: 4) {
            Text(reference.caption).font(.callout).foregroundStyle(.secondary)
            if !reference.expansion.isEmpty { Text(reference.expansion).font(LexiTheme.lexical(15)).foregroundStyle(LexiTheme.accentText) }
            Text(reference.chinese).font(.system(size: 17)).textSelection(.enabled)
            Text(reference.english).font(.system(size: 15)).textSelection(.enabled)
            Disclosure(label: "参考资料", key: reference.key, store: store) {
                Text(reference.sourceNote).font(.callout).foregroundStyle(.secondary)
                SourceLink(reference.sourceName, url: reference.sourceUrl)
            }
        }
    }
}

struct Disclosure<Content: View>: View {
    let label: String
    let key: String
    @Bindable var store: LexiStore
    @ViewBuilder let content: Content
    var body: some View {
        VStack(alignment: .leading, spacing: 6) {
            DisclosureButton(label: label, open: store.isOpen(key)) { store.setOpen(key, $0) }
            if store.isOpen(key) { DetailBody { content } }
        }
    }
}

struct DisclosureButton: View {
    let label: String
    let open: Bool
    let toggle: (Bool) -> Void
    var body: some View {
        Button { toggle(!open) } label: {
            Label(label, systemImage: open ? "chevron.down" : "chevron.right").labelStyle(LeadingIcon())
        }
        .buttonStyle(.bordered)
        .controlSize(.small)
        .help((open ? "收起" : "展开") + label)
        .accessibilityLabel(label)
        .accessibilityValue(open ? "已展开" : "已收起")
    }
}

private struct LeadingIcon: LabelStyle {
    func makeBody(configuration: Configuration) -> some View {
        HStack(spacing: 4) { configuration.icon.imageScale(.small); configuration.title }
    }
}

struct DetailBody<Content: View>: View {
    @ViewBuilder let content: Content
    var body: some View {
        VStack(alignment: .leading, spacing: 4) { content }
            .padding(.leading, 10)
            .overlay(alignment: .leading) { Rectangle().fill(.separator).frame(width: 2) }
            .padding(.vertical, 2)
    }
}

// Original dictionary text: shown directly, then kept in one collapsed group once definitions exist.
struct LocalSourcesView: View {
    let view: QueryView
    @Bindable var store: LexiStore
    var body: some View {
        if !view.localHint.isEmpty {
            Text(view.localHint).font(.callout).foregroundStyle(.secondary)
        } else if !view.local.isEmpty {
            if store.localWrapped {
                DisclosureGroup("离线词典原文 · \(view.local.count) 部", isExpanded: $store.localExpanded) { cards.padding(.top, 6) }
            } else {
                cards
            }
        }
    }
    private var cards: some View {
        VStack(alignment: .leading, spacing: 0) {
            ForEach(view.local) { source in LocalSourceCard(source: source) }
        }
    }
}

struct LocalSourceCard: View {
    let source: LocalSource
    @State private var showOriginal = false
    var body: some View {
        VStack(alignment: .leading, spacing: 6) {
            HStack {
                Text(source.name).font(.headline)
                Spacer()
                Text("离线原文").font(.caption).foregroundStyle(.secondary)
            }
            if !source.headword.isEmpty { Text(source.headword).font(.callout).foregroundStyle(.secondary) }
            if !source.chinese.trimmingCharacters(in: .whitespacesAndNewlines).isEmpty { Text(source.chinese).font(.system(size: 16)).textSelection(.enabled) }
            if !source.english.trimmingCharacters(in: .whitespacesAndNewlines).isEmpty { Text(source.english).font(.system(size: 16)).textSelection(.enabled) }
            if !source.original.trimmingCharacters(in: .whitespacesAndNewlines).isEmpty {
                DisclosureGroup("完整原始条目", isExpanded: $showOriginal) { Text(source.original).textSelection(.enabled).padding(.top, 4) }
            }
            SourceLink("词典来源", url: source.sourceUrl)
            Text(source.attribution).font(.caption).foregroundStyle(.secondary)
        }
        .padding(.vertical, 12)
        .overlay(alignment: .bottom) { Divider() }
    }
}

struct OnlineSourceView: View {
    let view: QueryView
    @Bindable var store: LexiStore
    var body: some View {
        if let online = view.online {
            DisclosureGroup(online.header, isExpanded: $store.onlineExpanded) {
                VStack(alignment: .leading, spacing: 4) {
                    ForEach(Array(online.senses.enumerated()), id: \.offset) { _, sense in
                        Text(sense.label).foregroundStyle(LexiTheme.accentText).padding(.top, 6)
                        Text(sense.definition).font(.system(size: 16)).textSelection(.enabled)
                        ForEach(Array(sense.examples.enumerated()), id: \.offset) { _, example in
                            Text(example.text).font(example.chinese ? LexiTheme.chineseExample(15) : LexiTheme.lexical(15)).italic()
                                .foregroundStyle(.secondary).textSelection(.enabled)
                        }
                    }
                    SourceLink("查看 Wiktionary 来源", url: online.url).padding(.top, 6)
                    Text(online.footer).font(.caption).foregroundStyle(.secondary)
                }
                .padding(.top, 6)
            }
        } else if !view.onlineHint.isEmpty {
            Text(view.onlineHint).font(.callout).foregroundStyle(.secondary)
        }
    }
}
