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
