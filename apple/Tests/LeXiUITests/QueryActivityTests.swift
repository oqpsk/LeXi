import AppKit
import LeXiKit
import SwiftUI
import Testing
@testable import LeXiUI

@MainActor @Suite struct QueryActivityChecks {
    @Test(arguments: [18.0,24.0]) func geometryStaysInsideItsBox(_ size: Double) {
        let box = CGRect(x: 3,y: 5,width: size,height: size)
        for activity in [QueryActivity.querying,.generating,.merging] {
            for time in stride(from: 0.0,through: 5.0,by: 0.047) {
                let marks = QueryActivityLayout.marks(activity: activity,time: time,in: box)
                #expect(marks.count == (activity == .querying ? 9 : activity == .generating ? 3 : 5))
                #expect(marks.allSatisfy { box.contains($0.rect) && $0.rect.width > 0 && $0.rect.height > 0 && (0...1).contains($0.opacity) })
            }
            let still = QueryActivityLayout.marks(activity: activity,time: nil,in: box)
            #expect(still.allSatisfy { box.contains($0.rect) })
        }
        #expect(QueryActivityLayout.marks(activity: .idle,time: nil,in: box).isEmpty)
    }

    @Test func staticStatesRenderWithoutOpeningWindows() throws {
        for activity in [QueryActivity.idle,.querying,.generating,.merging] {
            let renderer = ImageRenderer(content: QueryActivityMark(activity: activity).foregroundStyle(.secondary))
            let image = try #require(renderer.nsImage)
            #expect(image.size == CGSize(width: 18,height: 18))
        }
    }
}
