import Foundation
import Testing
@testable import LeXiKit

// Runs the real lexi-host offline against the fixture dictionary.
@MainActor @Suite(.enabled(if: Fixture.host != nil && Fixture.dictionary != nil, "needs a published lexi-host and the test dictionary"))
struct HostIntegration {
    private func launch() throws -> (LexiStore, URL) {
        let user = FileManager.default.temporaryDirectory.appending(path: "lexi-host-tests-\(UUID().uuidString)")
        try FileManager.default.createDirectory(at: user, withIntermediateDirectories: true)
        try Data(#"{"online":false}"#.utf8).write(to: user.appending(path: "settings.json"))
        return (LexiStore(), user)
    }

    @Test func queriesOfflineThroughTheHost() async throws {
        let (store, user) = try launch()
        await store.start(executable: Fixture.host!, arguments: ["--data", Fixture.dictionary!.path, "--user", user.path])
        #expect(store.phase == .ready)
        #expect(store.settings?.settings.online == false)
        await store.submit("walked")
        #expect(store.view.kind == .dictionary)
        #expect(store.view.local.first?.chinese == "步行")
        #expect(store.view.wordForms.first?.cells.first?.spelling == "walked → walk")
        #expect(store.view.progress?.busy == false)
        await store.suggest("wal")
        #expect(store.suggestions.contains("walk"))
        await store.home()
        #expect(store.view.kind == .home && store.history.entries.first?.query == "walked")
        let page = await store.navigate(-1)
        #expect(page?.query == "walked" && store.view.query == "walked")
        let rejected = await store.update(["parallelLookup": .bool(true), "secondaryProvider": .string("codex"), "secondaryModel": .string("")])
        #expect(rejected == "请选择两个不同的查询模型。")
        #expect(await store.update(["mergeSenses": .bool(false)]) == nil && store.settings?.settings.mergeSenses == false)
        await store.stop()
    }

    // Offline, so no request can leave the machine: every online speech answer comes from Core's rules.
    @Test func speechThroughTheHost() async throws {
        let (store, user) = try launch()
        await store.start(executable: Fixture.host!, arguments: ["--data", Fixture.dictionary!.path, "--user", user.path])
        let models = await store.speechModels()
        #expect(models.first == SpeechModel(provider: "macos", id: "avspeech", name: "macOS 本地发音", local: true))
        #expect(models.filter { $0.provider == "elevenlabs" }.count == 5 && models.dropFirst().allSatisfy { !$0.local })
        #expect(store.speechSelection == SpeechSelection(provider: "macos", model: "avspeech", voice: "auto"))
        #expect(await store.speechVoices(provider: "openai", model: "gpt-4o-mini-tts").count == 13)
        #expect(await store.speechVoices(provider: "macos", model: "avspeech").isEmpty)
        #expect(try await store.cachedSpeech("你好", store.speechSelection) == SpeechCache(path: nil, local: true))

        let coral = SpeechSelection(provider: "openai", model: "gpt-4o-mini-tts", voice: "coral")
        #expect(try await store.cachedSpeech("word", coral) == SpeechCache(path: nil, local: false))
        await #expect(throws: HostError(code: "speech_failed", message: "离线模式下没有此声线的缓存发音。")) {
            _ = try await store.synthesize("word", coral, key: "synthetic")
        }
        await #expect(throws: HostError(code: "speech_failed", message: "请先开启在线补充，再读取声线列表。")) {
            _ = try await store.elevenVoices(key: "synthetic")
        }
        await #expect(throws: HostError(code: "speech_failed", message: "请在设置中选择这个模型支持的声线。")) {
            _ = try await store.cachedSpeech("word", SpeechSelection(provider: "openai", model: "tts-1", voice: "marin"))
        }
        #expect(await store.update(["speechProvider": .string("openai"), "speechModel": .string("tts-1"), "speechVoice": .string("marin")])
            == "请在设置中选择这个模型支持的声线。")
        #expect(await store.update(["speechProvider": .string("elevenlabs"), "speechModel": .string("eleven_v3"),
                                    "speechVoice": .string("custom_voice"), "speechVoiceName": .string("自定义声线")]) == nil)
        #expect(store.speechSelection == SpeechSelection(provider: "elevenlabs", model: "eleven_v3", voice: "custom_voice"))
        #expect(store.settings?.settings.speechVoiceName == "自定义声线")
        await store.stop()
    }

    @Test func reportsProtocolErrorsAndCancellation() async throws {
        let (_, user) = try launch()
        let client = HostClient(executable: Fixture.host!, arguments: ["--data", Fixture.dictionary!.path, "--user", user.path]) { _ in }
        try await client.start()
        await #expect(throws: HostError.self) { _ = try await client.call("query.unknown", as: SubmitResult.self) }
        let task = Task { try await client.call("connection.status", ["provider": "codex", "codexPath": "/usr/bin/false"], as: LoginStatus.self) }
        task.cancel()
        _ = await task.result
        let view = try await client.call("query.view", as: QueryView.self)
        #expect(view.kind == .home)
        await client.stop()
        await #expect(throws: HostError.self) { _ = try await client.call("query.view", as: QueryView.self) }
    }
}
