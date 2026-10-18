import Testing
@testable import LeXiUI

@Suite struct ResultProgressChecks {
    @Test func crtClosesVerticallyThenHorizontallyThenFadesTheDot() {
        let start = CRTShutdown.frame(0)
        #expect(start.width == 1 && start.height == 1 && start.screenOpacity == 1)
        let line = CRTShutdown.frame(0.45)
        #expect(line.width == 1 && line.height < 0.06 && line.lineOpacity == 1 && line.dotOpacity == 0)
        let point = CRTShutdown.frame(0.78)
        #expect(point.width < 0.01 && point.screenOpacity == 0 && point.dotOpacity == 1)
        let end = CRTShutdown.frame(1)
        #expect(end.screenOpacity == 0 && end.lineOpacity == 0 && end.dotOpacity == 0)
        for t in stride(from: -0.2, through: 1.2, by: 0.013) {
            let frame = CRTShutdown.frame(t)
            #expect([frame.width,frame.height,frame.screenOpacity,frame.lineWidth,frame.lineOpacity,frame.dotOpacity].allSatisfy { $0.isFinite && (0...1).contains($0) })
        }
    }
}
