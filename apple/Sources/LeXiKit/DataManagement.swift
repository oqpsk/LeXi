import Foundation

public struct CacheUsage: Decodable, Sendable, Identifiable {
    public let id, name: String
    public let bytes: Int64
    public let files: Int
}
public struct BackupPreview: Decodable, Sendable {
    public let version, history, results: Int
    public let hasSettings: Bool
}
public struct SpeechProfile: Decodable, Equatable, Sendable {
    public let provider, model, voice, name: String
}
