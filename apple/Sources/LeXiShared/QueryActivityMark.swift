import LeXiKit
import SwiftUI

// Core chooses the activity; the native front end only draws its soft matrix.
public struct QueryActivityMark: View {
    var activity: QueryActivity
    var size: CGFloat = 18
    public init(activity: QueryActivity, size: CGFloat = 18) { self.activity = activity; self.size = size }
    @Environment(\.accessibilityReduceMotion) private var reduceMotion
    @Environment(\.scenePhase) private var scenePhase
    @State private var visible = false
    @State private var origin = Date()

    public var body: some View {
        Group {
            if activity == .idle {
                Text("ξ").font(.custom("Georgia", size: size * 0.85))
            } else {
                let moving = visible && !reduceMotion && scenePhase != .background
                TimelineView(.animation(minimumInterval: 1.0 / 30, paused: !moving)) { context in
                    Canvas { drawing, bounds in
                        let marks = QueryActivityLayout.marks(activity: activity,
                            time: moving ? context.date.timeIntervalSince(origin) : nil,
                            in: CGRect(origin: .zero, size: bounds))
                        for mark in marks {
                            drawing.opacity = mark.opacity
                            let radius = min(mark.rect.width, mark.rect.height) / 2
                            drawing.fill(Path(roundedRect: mark.rect, cornerRadius: radius), with: .foreground)
                        }
                    }
                }
            }
        }
        .frame(width: size, height: size)
        .allowsHitTesting(false).accessibilityHidden(true)
        .onAppear { visible = true; origin = Date() }
        .onDisappear { visible = false }
        .onChange(of: activity) { _, _ in origin = Date() }
    }
}

struct QueryActivityLayout {
    struct Mark { let rect: CGRect; let opacity: Double }

    static func marks(activity: QueryActivity, time: TimeInterval?, in rect: CGRect) -> [Mark] {
        let side = min(rect.width, rect.height)
        let x = rect.midX-side/2, y = rect.midY-side/2
        func dot(_ cx: Double, _ cy: Double, _ diameter: Double, _ opacity: Double) -> Mark {
            Mark(rect: CGRect(x: x+side*(cx-diameter/2), y: y+side*(cy-diameter/2), width: side*diameter, height: side*diameter), opacity: opacity)
        }
        switch activity {
        case .idle: return []
        case .querying:
            return (0..<9).map { index in
                let row = index/3, col = index%3
                let phase = fraction((time ?? 0)/2.2 + Double(row+col)*0.12)
                let opacity = time == nil ? 0.75 : value(phase, [(0,0.26),(0.30,1),(0.72,0.26),(1,0.26)], (0.4,0,0.3,1))
                let scale = time == nil ? 1 : value(phase, [(0,0.8),(0.30,1.1),(0.72,0.8),(1,0.8)], (0.4,0,0.3,1))
                return dot(0.27+Double(col)*0.23, 0.27+Double(row)*0.23, 0.10*scale, opacity)
            }
        case .generating:
            return (0..<3).map { row in
                let phase = fraction((time ?? 0)/2.2 + Double(row)*0.18)
                let scale = time == nil ? 1 : value(phase, [(0,0.32),(0.44,1),(0.60,1),(1,0.32)], (0.4,0,0.2,1))
                let opacity = time == nil ? 0.85 : value(phase, [(0,0.35),(0.44,0.95),(0.60,0.95),(1,0.35)], (0.4,0,0.2,1))
                return Mark(rect: CGRect(x: x+side*0.24, y: y+side*(0.28+Double(row)*0.22),
                    width: side*(0.52-Double(row)*0.07)*scale, height: side*0.07), opacity: opacity)
            }
        case .merging:
            let phase = fraction((time ?? 0)/2.2)
            let offset = time == nil ? 0.24 : value(phase, [(0,0.24),(0.12,0.24),(0.58,0.045),(0.72,0.045),(1,0.24)], (0.45,0,0.25,1))
            let opacity = time == nil ? 0.70 : value(phase, [(0,0.4),(0.12,0.4),(0.58,0.95),(0.72,0.95),(1,0.4)], (0.45,0,0.25,1))
            var marks = (0..<4).map { index in
                dot(0.5 + (index%2 == 0 ? -1 : 1)*offset, 0.5 + (index<2 ? -1 : 1)*offset, 0.12, opacity)
            }
            let centerScale = time == nil ? 1 : value(phase, [(0,0.55),(0.15,0.55),(0.58,1),(0.72,1),(1,0.55)], (0.42,0,0.58,1))
            let centerOpacity = time == nil ? 0.85 : value(phase, [(0,0.3),(0.15,0.3),(0.58,1),(0.72,1),(1,0.3)], (0.42,0,0.58,1))
            marks.append(dot(0.5,0.5,0.16*centerScale,centerOpacity))
            return marks
        }
    }

    private static func fraction(_ time: Double) -> Double { max(0,time).truncatingRemainder(dividingBy: 1) }
    private static func value(_ phase: Double, _ frames: [(Double,Double)], _ curve: (Double,Double,Double,Double)) -> Double {
        for index in 1..<frames.count where phase <= frames[index].0 {
            let a = frames[index-1], b = frames[index]
            let progress = (phase-a.0)/(b.0-a.0)
            var low = 0.0, high = 1.0
            for _ in 0..<16 {
                let t = (low+high)/2, u = 1-t
                let x = 3*u*u*t*curve.0 + 3*u*t*t*curve.2 + t*t*t
                if x < progress { low = t } else { high = t }
            }
            let t = (low+high)/2, u = 1-t
            let eased = 3*u*u*t*curve.1 + 3*u*t*t*curve.3 + t*t*t
            return a.1 + (b.1-a.1)*eased
        }
        return frames[0].1
    }
}
