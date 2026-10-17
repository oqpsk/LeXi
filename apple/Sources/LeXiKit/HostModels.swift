import Foundation

// Decodable mirrors of the Lexi.Host protocol (docs/host-protocol.md, Lexi.Core/QueryViews.cs).
// Every decision is already made by Lexi.Core; these types only carry it to SwiftUI.

public enum QueryViewKind: String, Decodable, Sendable { case home, dictionary, translation }
public enum QueryStage: String, Decodable, Sendable { case dictionary, supplement, merge, finished }
public enum QueryOutcome: String, Decodable, Sendable { case running, complete, partial, failed }
public enum OriginalsMode: String, Decodable, Sendable { case keep, collapse, expand }

public struct QueryProgress: Decodable, Equatable, Sendable {
    public let stage: QueryStage
    public let title: String
    public let detail: String
    public let outcome: QueryOutcome
    public let busy: Bool
    public let warning: Bool
    public let stageLabels: [String]
    public let accessibleName: String
}

public struct Pronunciation: Decodable, Equatable, Sendable {
    public let text: String
    public let label: String
    public let attribution: String
    public let labelAttribution: String
    public let sourced: Bool
    public static let none = Pronunciation(text: "", label: "", attribution: "", labelAttribution: "", sourced: false)
}

public struct NavigationState: Decodable, Equatable, Sendable {
    public let canBack: Bool
    public let canForward: Bool
    public let backLabel: String
    public let forwardLabel: String
}

public struct LocalSource: Decodable, Equatable, Sendable, Identifiable {
    public let name: String
    public let headword: String
    public let chinese: String
    public let english: String
    public let original: String
    public let sourceUrl: String
    public let attribution: String
    public var id: String { name + "\u{1f}" + headword }
}

public struct OnlineExample: Decodable, Equatable, Sendable { public let text: String; public let chinese: Bool }
public struct OnlineSense: Decodable, Equatable, Sendable {
    public let label: String
    public let definition: String
    public let examples: [OnlineExample]
}
public struct OnlineSource: Decodable, Equatable, Sendable {
    public let header: String
    public let senses: [OnlineSense]
    public let url: String
    public let footer: String
    public let cached: Bool
}

public struct SenseHeading: Decodable, Equatable, Sendable {
    public let prefix: String
    public let partOfSpeech: String
    public let form: String
    public let usage: String
    public let abbreviation: Bool
    public let provisional: Bool
    public let tooltip: String
}
public struct Citation: Decodable, Equatable, Sendable, Identifiable {
    public let number: Int
    public let label: String
    public let definition: String
    public let url: String
    public var id: Int { number }
}
public struct Brief: Decodable, Equatable, Sendable { public let caption: String; public let chinese: String; public let english: String }
public struct Sense: Decodable, Equatable, Sendable, Identifiable {
    public let key: String
    public let heading: SenseHeading
    public let expansion: String
    public let chinese: String
    public let english: String
    public let example: String
    public let exampleChinese: String
    public let provenance: String
    public let originalLabels: String
    public let citations: [Citation]
    public let briefs: [Brief]
    public let referenceKeys: [String]
    public let sourceNote: String
    public let alternativesNote: String
    public let sourceLabel: String
    public let alternatives: [Sense]
    public let alternativesLabel: String
    public var id: String { key }
}
public struct Reference: Decodable, Equatable, Sendable, Identifiable {
    public let key: String
    public let caption: String
    public let expansion: String
    public let chinese: String
    public let english: String
    public let sourceNote: String
    public let sourceName: String
    public let sourceUrl: String
    public var id: String { key }
}
public struct Notice: Decodable, Equatable, Sendable { public let text: String; public let warning: Bool }
public struct Answers: Decodable, Equatable, Sendable {
    public let empty: Bool
    public let references: [Reference]
    public let senses: [Sense]
    public let notices: [Notice]
    public let notes: [String]
    public let mergeWarning: String
    public let originals: OriginalsMode
}

public struct QuickRead: Decodable, Equatable, Sendable {
    public let visible: Bool
    public let text: String
    public let tooltip: String
    public let state: String
    public let showState: Bool
    public let caption: String
    public let explanation: String
}
public struct WordFormCell: Decodable, Equatable, Sendable {
    public let label: String
    public let spelling: String
    public let tooltip: String
    public let matches: Bool
    public let copyText: String
}
public struct WordFormRow: Decodable, Equatable, Sendable { public let cells: [WordFormCell] }
public struct Etymology: Decodable, Equatable, Sendable {
    public let text: String
    public let caption: String
    public let linkLabel: String
    public let sourceUrl: String
    public let generated: Bool
}
public struct TranslationResult: Decodable, Equatable, Sendable {
    public let source: String
    public let hint: String
    public let paragraphs: [String]
    public let note: String
    public let complete: Bool
    public let error: String
}

public struct QueryView: Decodable, Equatable, Sendable {
    public let request: Int64
    public let revision: Int64
    public let kind: QueryViewKind
    public let query: String
    public let input: String
    public let speechText: String
    public let heading: String
    public let pronunciation: Pronunciation
    public let progress: QueryProgress?
    public let progressGeneration: Int64
    public let status: String
    public let canRegenerate: Bool
    public let navigation: NavigationState
    public let local: [LocalSource]
    public let localHint: String
    public let online: OnlineSource?
    public let onlineHint: String
    public let answers: Answers?
    public let quickRead: QuickRead
    public let wordForms: [WordFormRow]
    public let etymology: [Etymology]
    public let translation: TranslationResult?

    // Shown until the host publishes its first view.
    public static let starting = QueryView(request: -1, revision: -1, kind: .home, query: "", input: "", speechText: "", heading: "",
        pronunciation: .none, progress: nil, progressGeneration: -1, status: "正在启动…", canRegenerate: false,
        navigation: NavigationState(canBack: false, canForward: false, backLabel: "", forwardLabel: ""),
        local: [], localHint: "", online: nil, onlineHint: "", answers: nil,
        quickRead: QuickRead(visible: false, text: "", tooltip: "", state: "", showState: false, caption: "", explanation: ""),
        wordForms: [], etymology: [], translation: nil)
}

// ---- Non-query methods ----

public struct SubmitResult: Decodable, Sendable { public let request: Int64; public let revision: Int64 }
public struct QueryPage: Decodable, Sendable { public let query: String; public let translation: Bool; public let scrollOffset: Double }
public struct NavigateResult: Decodable, Sendable { public let page: QueryPage? }
public struct Suggestions: Decodable, Sendable { public let suggestions: [String] }

public struct HistoryEntry: Decodable, Equatable, Sendable, Identifiable {
    public let query: String
    public let translation: Bool
    public let preview: String
    public let kind: String
    public var id: String { (translation ? "t:" : "d:") + query }
}
public struct HistoryList: Decodable, Equatable, Sendable {
    public let entries: [HistoryEntry]
    public let error: String
    public static let empty = HistoryList(entries: [], error: "")
}
public struct HistoryRemoval: Decodable, Sendable { public let removed: Bool; public let history: HistoryList }

public struct AppSettings: Decodable, Equatable, Sendable {
    public let online: Bool
    public let autoAi: Bool
    public let offlineEnabled: Bool
    public let disabledDictionaries: [String]
    public let codexPath: String
    public let model: String
    public let provider: String
    public let parallelLookup: Bool
    public let secondaryProvider: String
    public let secondaryModel: String
    public let mergeSenses: Bool
    public let judgeModel: String
}
public struct ModelChoice: Decodable, Equatable, Sendable, Hashable, Identifiable {
    public let label: String
    public let id: String
    public let provider: String
    public var key: String { provider + ":" + id }
}
public struct ControlLabels: Decodable, Equatable, Sendable {
    public let onlineLabel: String
    public let onlineTooltip: String
    public let regenerateTooltip: String
}
public struct SettingsPayload: Decodable, Equatable, Sendable {
    public let settings: AppSettings
    public let models: [ModelChoice]
    public let judgeModels: [ModelChoice]
    public let controls: ControlLabels
}

public struct HostInfo: Decodable, Sendable {
    public let protocolVersion: Int
    public let version: String
    public let platform: String
    public let view: QueryView
    public let settings: SettingsPayload
    enum CodingKeys: String, CodingKey { case protocolVersion = "protocol", version, platform, view, settings }
}

public struct InstalledDictionary: Decodable, Equatable, Sendable, Identifiable {
    public let id: String
    public let name: String
    public let languages: String
    public let entryCount: Int
    public let bytes: Int64
    public let folder: String
    public let sourceUrl: String
    public let license: String
    public let status: String
    public let available: Bool
    public let version: String
    public let attribution: String
}
public struct DictionaryList: Decodable, Sendable { public let dictionaries: [InstalledDictionary]; public let folder: String }

public struct LoginStatus: Decodable, Equatable, Sendable { public let installed: Bool; public let signedIn: Bool; public let message: String }
public struct LoginCommand: Decodable, Equatable, Sendable {
    public let fileName: String
    public let arguments: [String]
    public let path: String
    public let clearEnvironment: [String]
}

// Settings changes are sent as a partial JSON object; only these value shapes are needed.
public enum SettingValue: Encodable, Equatable, Sendable {
    case bool(Bool), string(String), strings([String])
    public func encode(to encoder: Encoder) throws {
        var container = encoder.singleValueContainer()
        switch self {
        case .bool(let value): try container.encode(value)
        case .string(let value): try container.encode(value)
        case .strings(let value): try container.encode(value)
        }
    }
}
