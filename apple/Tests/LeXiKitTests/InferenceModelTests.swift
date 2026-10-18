import Foundation
import Testing
@testable import LeXiKit

@Suite struct InferenceModelTests {
    @Test func decodesCapabilitiesAndSelectionFromSharedCore() throws {
        let data = Data(#"{"label":"Example · 自定义 API","id":"model-a","provider":"compatible","effort":"low","speed":"priority","options":{"efforts":["low","high"],"speeds":[{"id":"default","label":"标准"},{"id":"priority","label":"Fast"}],"note":"服务端能力","effortChoices":[{"id":"","label":"模型默认"},{"id":"low","label":"low"},{"id":"high","label":"high"}],"speedChoices":[{"id":"","label":"服务默认"},{"id":"default","label":"标准"},{"id":"priority","label":"Fast"}]}}"#.utf8)
        let model = try JSONDecoder().decode(ModelChoice.self, from: data)
        #expect(model.options?.efforts == ["low", "high"])
        #expect(model.options?.speedChoices.last?.id == "priority")
        #expect(model.effort == "low" && model.speed == "priority")
        #expect(model.key == "compatible:model-a")
    }
    @Test func acceptsOlderHostModelPayloads() throws {
        let model = try JSONDecoder().decode(ModelChoice.self, from: Data(#"{"label":"Existing","id":"model-a","provider":"codex"}"#.utf8))
        #expect(model.options == nil && model.effort == nil && model.speed == nil)
        #expect(model.key != ModelChoice(label: "API", id: "model-a", provider: "compatible").key)
    }
}
