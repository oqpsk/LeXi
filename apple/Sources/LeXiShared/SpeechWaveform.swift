import SwiftUI

// Same five-bar geometry and spline rhythm as the approved soft waveform on Windows.
// A preparation state is static; only actual audio playback advances the timeline.
public struct SpeechWaveform: View {
    var animate = true
    var size: CGFloat = 18
    public init(animate: Bool = true, size: CGFloat = 18) { self.animate = animate; self.size = size }
    @Environment(\.accessibilityReduceMotion) private var reduceMotion
    @Environment(\.scenePhase) private var scenePhase
    @State private var origin = Date()
    @State private var visible = false

    public var body: some View {
        let moving = animate && !reduceMotion && visible && scenePhase != .background
        TimelineView(.animation(minimumInterval: 1.0 / 30, paused: !moving)) { context in
            SpeechWaveformShape(time: moving ? context.date.timeIntervalSince(origin) : nil)
                .fill(.foreground)
        }
        .frame(width: size, height: size)
        .allowsHitTesting(false)
        .accessibilityHidden(true)
        .onAppear { origin = Date(); visible = true }
        .onDisappear { visible = false }
    }
}

struct SpeechWaveformShape: Shape {
    var time: TimeInterval?
    private static let profile: [(height: Double, period: Double, offset: Double)] =
        [(0.44,1820,430), (0.78,2070,960), (0.94,1940,200), (0.72,2210,1460), (0.43,1890,740)]
    private static let frames: [(time: Double, value: Double)] =
        [(0,0.28), (0.13,0.78), (0.27,0.40), (0.43,1), (0.58,0.35), (0.74,0.72), (0.88,0.24), (1,0.28)]

    func path(in rect: CGRect) -> Path {
        var path = Path()
        for bar in bars(in: rect) {
            path.addRoundedRect(in: bar, cornerSize: CGSize(width: bar.width / 2, height: bar.width / 2))
        }
        return path
    }

    func bars(in rect: CGRect) -> [CGRect] {
        let side = min(rect.width, rect.height)
        let width = side * 0.105, gap = side * 0.075
        let left = rect.midX - (width * 5 + gap * 4) / 2
        return Self.profile.enumerated().map { index, spec in
            let scale: Double
            if let time {
                let phase = ((max(0, time) * 1000 + spec.offset) / spec.period).truncatingRemainder(dividingBy: 1)
                scale = Self.scale(at: phase)
            } else { scale = 0.7 }
            let height = side * spec.height * scale
            return CGRect(x: left + CGFloat(index) * (width + gap), y: rect.midY - height / 2, width: width, height: height)
        }
    }

    private static func scale(at phase: Double) -> Double {
        for index in 1..<frames.count where phase <= frames[index].time {
            let a = frames[index - 1], b = frames[index]
            let progress = (phase - a.time) / (b.time - a.time)
            // Invert the x coordinate of cubic-bezier(.4,0,.3,1), then evaluate y.
            var low = 0.0, high = 1.0
            for _ in 0..<16 {
                let t = (low + high) / 2, u = 1 - t
                let x = 3 * u * u * t * 0.4 + 3 * u * t * t * 0.3 + t * t * t
                if x < progress { low = t } else { high = t }
            }
            let t = (low + high) / 2
            let eased = 3 * (1 - t) * t * t + t * t * t
            return a.value + (b.value - a.value) * eased
        }
        return frames[0].value
    }
}
