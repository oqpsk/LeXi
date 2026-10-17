import Foundation
import Testing
@testable import LeXiKit

@Suite struct SpeechModels {
    @Test func decodesHostSpeechResults() throws {
        let decoder = JSONDecoder()
        let model = try decoder.decode(SpeechModel.self, from: Data(#"{"provider":"macos","id":"avspeech","name":"macOS 本地发音","local":true}"#.utf8))
        #expect(model.local && model.key == "macos:avspeech")
        let cache = try decoder.decode(SpeechCache.self, from: Data(#"{"path":null,"local":false}"#.utf8))
        #expect(cache == SpeechCache(path: nil, local: false))
    }

    @Test func refusesUnknownProvidersWithoutTouchingTheKeychain() {
        let keys = KeychainSpeechKeys(service: "io.github.oqpsk.lexi.speech.tests")
        #expect(throws: SpeechKeyError.self) { try keys.save("codex", "synthetic") }
        #expect(throws: SpeechKeyError.self) { _ = try keys.read("claude") }
        #expect(!keys.hasSaved("codex"))
        #expect(KeychainSpeechKeys.title("xai") == "Grok / xAI API Key")
    }

    // Writes to the login keychain, so it runs only when asked: LEXI_KEYCHAIN_TESTS=1 swift test ...
    @Test(.enabled(if: ProcessInfo.processInfo.environment["LEXI_KEYCHAIN_TESTS"] == "1", "writes a synthetic item to the login keychain"))
    func keysRoundTripThroughTheKeychain() throws {
        let keys = KeychainSpeechKeys(service: "io.github.oqpsk.lexi.speech.tests.\(UUID().uuidString)")
        defer { for provider in KeychainSpeechKeys.providers { try? keys.remove(provider) } }
        #expect(try keys.read("openai") == nil && !keys.hasSaved("openai"))
        try keys.save("openai", "  synthetic-first  ")
        #expect(try keys.read("openai") == "synthetic-first" && keys.hasSaved("openai"))
        try keys.save("openai", "synthetic-second")
        try keys.save("elevenlabs", "synthetic-eleven")
        #expect(try keys.read("openai") == "synthetic-second" && keys.hasSaved("elevenlabs") && !keys.hasSaved("xai"))
        try keys.remove("openai")
        #expect(try keys.read("openai") == nil && keys.hasSaved("elevenlabs"))
        try keys.save("elevenlabs", "   ")
        #expect(!keys.hasSaved("elevenlabs"))
    }
}
