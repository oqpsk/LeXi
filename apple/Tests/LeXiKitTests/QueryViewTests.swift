import Foundation
import Testing
@testable import LeXiKit

@Suite struct QueryViewDecoding {
    @Test func decodesEveryProtocolView() {
        let dictionary = Fixture.view("dictionary")
        #expect(dictionary.kind == .dictionary)
        #expect(dictionary.progress?.outcome == .complete && dictionary.progress?.busy == false)
        #expect(dictionary.progress?.stageLabels.count == 4)
        #expect(dictionary.answers?.senses.first?.alternatives.count == 1)
        #expect(dictionary.answers?.senses.first?.alternativesLabel == "其他表述 · 1")
        #expect(dictionary.answers?.originals == .collapse)
        #expect(dictionary.quickRead.caption == "TL;DR · 已整理")
        #expect(dictionary.local.first?.chinese == "偶然发现有用事物")
        let translation = Fixture.view("translation")
        #expect(translation.kind == .translation && translation.translation?.paragraphs.count == 2 && translation.translation?.complete == true)
        #expect(Fixture.view("preview").translation?.complete == false)
        #expect(Fixture.view("streaming").progress?.busy == true)
        #expect(Fixture.view("home").kind == .home && Fixture.view("home").progress == nil)
    }
}

@MainActor @Suite struct StoreBehavior {
    @Test func rejectsStaleRevisions() {
        let store = LexiStore()
        store.receive(Fixture.view("dictionary"))
        store.receive(Fixture.view("streaming"))
        #expect(store.view == Fixture.view("dictionary"))
    }

    @Test func resetsDisclosuresForEachNewQueryPage() throws {
        let store = LexiStore()
        let streaming = Fixture.view("streaming")
        store.receive(streaming)
        store.setOpen("lookup:notes", true)
        let final = Fixture.view("dictionary")
        #expect(final.request == streaming.request)
        store.receive(final)
        #expect(store.isOpen("lookup:notes"), "streaming and merging keep what the reader opened")
        store.receive(Fixture.view("translation"))
        #expect(!store.isOpen("lookup:notes"), "a new page starts with every disclosure closed")
    }

    @Test func collapsesOriginalSourcesOnceDefinitionsArrive() throws {
        let store = LexiStore()
        store.receive(Fixture.view("streaming"))
        store.receive(Fixture.view("dictionary"))
        #expect(store.localWrapped && !store.localExpanded, "dictionary text moves into a collapsed group beside definitions")
        store.localExpanded = true
        let later = try reencode(Fixture.view("dictionary"), revision: 1_000, status: "status only")
        store.receive(later)
        #expect(store.localExpanded, "later updates keep the reader's expansion")
    }

    @Test func choosesReferenceAndAlternativeDefaultsFromReaderState() throws {
        let store = LexiStore()
        let sense = try #require(Fixture.view("dictionary").answers?.senses.first)
        store.receive(Fixture.view("dictionary"))
        #expect(!store.alternativesOpenByDefault(sense))
        store.setOpen(sense.alternatives[0].key + ":examples", true)
        #expect(store.alternativesOpenByDefault(sense), "reading inside an alternative keeps the group open after merging")
    }

    private func reencode(_ view: QueryView, revision: Int64, status: String) throws -> QueryView {
        var object = try #require(JSONSerialization.jsonObject(with: Data(contentsOf: Fixture.repository
            .appending(path: "apple/Tests/LeXiKitTests/Fixtures/views.json"))) as? [String: Any])
        var dictionary = try #require(object["dictionary"] as? [String: Any])
        dictionary["revision"] = revision; dictionary["status"] = status
        object["dictionary"] = dictionary
        return try JSONDecoder().decode(QueryView.self, from: JSONSerialization.data(withJSONObject: dictionary))
    }
}

@Suite struct HostLocationTests {
    @Test func developmentOverrideAndMissingBundleHelper() {
        let resolved = HostLocation.resolve(bundle: .main, environment: ["LEXI_HOST": "/tmp/lexi-host"])
        #expect(resolved?.0.path == "/tmp/lexi-host")
        #expect(HostLocation.resolve(bundle: Bundle(for: Marker.self), environment: [:]) == nil)
    }
    final class Marker {}
}
