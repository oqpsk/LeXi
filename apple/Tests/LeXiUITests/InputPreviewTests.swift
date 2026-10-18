import AppKit
import SwiftUI
import Testing
@testable import LeXiKit
@testable import LeXiUI

@MainActor @Suite struct InputPreviewChecks {
    @Test func matchedSpellingAndTwoLineCardRender() throws {
        let preview=InputPreview(query:"ABS",headword:"abs",text:"腹肌",source:"词典摘录")
        #expect(preview.label=="ABS → abs")
        for width in [300.0,540.0,900.0] {
            let renderer=ImageRenderer(content:InputPreviewCard(preview:preview).frame(width:width))
            let image=try #require(renderer.nsImage)
            #expect(image.size.width==width && image.size.height>20 && image.size.height<100)
        }
    }
    @Test func dismissedOrSubmittedDraftCannotRestoreLatePreview() async throws {
        let store=LexiStore()
        store.previewInput("hello"); store.clearSuggestions()
        try await Task.sleep(for:.milliseconds(330))
        #expect(store.inputPreview==nil && store.suggestions.isEmpty)
        store.previewInput("ABS"); store.clearInputPreview()
        try await Task.sleep(for:.milliseconds(330))
        #expect(store.inputPreview==nil)
    }
}
