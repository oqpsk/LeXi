import LeXiKit
import SwiftUI

// The actors and their states arrive from Core. Only the one-shot CRT retirement is local.
// Shared by the Mac and iOS windows.
public struct ResultProgressBar: View {
    let progress: QueryProgress?
    let generation: Int64
    let retry: () -> Void
    public init(progress: QueryProgress?, generation: Int64, retry: @escaping () -> Void) {
        self.progress = progress; self.generation = generation; self.retry = retry
    }
    @Environment(\.accessibilityReduceMotion) private var reduceMotion
    @Environment(\.scenePhase) private var scenePhase
    @State private var observedGeneration: Int64 = -1
    @State private var liveSeen = false
    @State private var retired = true
    @State private var started: Date?
    @State private var showDetails = false

    private struct Run: Equatable {
        let generation: Int64
        let outcome: QueryOutcome?
        let reduceMotion, background: Bool
    }

    public var body: some View {
        TimelineView(.animation(minimumInterval: 1.0 / 30, paused: started == nil || reduceMotion || scenePhase == .background)) { context in
            let frame = CRTShutdown.frame(started.map { context.date.timeIntervalSince($0) / 0.95 } ?? 0)
            ZStack {
                HStack(spacing: 12) {
                    ForEach(progress?.items ?? []) { item in
                        Button {
                            if item.state == .failed { retry() } else { showDetails.toggle() }
                        } label: {
                            HStack(spacing: 6) {
                                workMark(item).frame(width: 20, height: 20)
                                Text(item.name).font(.callout).lineLimit(1).truncationMode(.middle)
                            }
                            .frame(maxWidth: .infinity, minHeight: 32)
                            .contentShape(.rect)
                        }
                        .buttonStyle(.plain)
                        .foregroundStyle(item.state == .failed ? AnyShapeStyle(.orange) : item.activity == .idle ? AnyShapeStyle(.secondary) : AnyShapeStyle(.primary))
                        .help(item.accessibleName + (item.state == .failed ? " · 点击仅重试未完成步骤" : " · 点击查看阶段详情"))
                        .accessibilityLabel(item.accessibleName)
                    }
                    if progress?.items?.isEmpty != false, let progress {
                        Text(progress.title).font(.callout).foregroundStyle(.secondary)
                    }
                }
                .padding(.horizontal, 12)
                .frame(height: 36)
                .background(Color.primary.opacity(0.045), in: .rect(cornerRadius: 8, style: .continuous))
                .scaleEffect(x: frame.width, y: frame.height)
                .opacity(frame.screenOpacity)
                Capsule().fill(.primary).frame(height: 2)
                    .shadow(color: .accentColor.opacity(0.7), radius: 7)
                    .scaleEffect(x: frame.lineWidth, y: 1).opacity(frame.lineOpacity)
                Circle().fill(.primary).frame(width: 4, height: 4)
                    .shadow(color: .accentColor.opacity(0.8), radius: 9)
                    .scaleEffect(frame.dotScale).opacity(frame.dotOpacity)
            }
            .frame(height: 36)
        }
        .frame(height: retired ? 0 : 36, alignment: .top)
        .clipped()
        .padding(.bottom, retired ? 0 : 10)
        .allowsHitTesting(!retired && started == nil)
        .accessibilityHidden(retired)
        .popover(isPresented: $showDetails, arrowEdge: .bottom) {
            if let progress {
                VStack(alignment: .leading, spacing: 6) {
                    Text(progress.title).font(.headline)
                    Text(progress.detail).font(.callout).foregroundStyle(.secondary)
                    Text(progress.stageLabels.joined(separator: "\n")).font(.caption).foregroundStyle(.secondary)
                }
                .padding(14).frame(maxWidth: 360, alignment: .leading)
                #if os(iOS)
                .presentationCompactAdaptation(.popover)
                #endif
            }
        }
        .onChange(of: progress) { _, value in
            if value?.busy == true { liveSeen = liveSeen || (value?.items?.contains { $0.activity != .idle } ?? false) }
        }
        .onChange(of: progress?.title) { _, title in
            if let title { AccessibilityNotification.Announcement(title).post() }
        }
        .task(id: Run(generation: generation, outcome: progress?.outcome, reduceMotion: reduceMotion, background: scenePhase == .background)) {
            if observedGeneration != generation { observedGeneration = generation; liveSeen = false; started = nil; showDetails = false }
            guard let progress else { retired = true; started = nil; return }
            if progress.busy {
                liveSeen = liveSeen || (progress.items?.contains { $0.activity != .idle } ?? false)
                retired = false; started = nil; return
            }
            if progress.warning { retired = false; started = nil; return }
            guard progress.restored != true && liveSeen && !reduceMotion && scenePhase != .background else { retired = true; started = nil; return }
            // A cache restore with no observed live model work never replays completion.
            liveSeen = false; retired = false; started = Date()
            do { try await Task.sleep(for: .milliseconds(950)) } catch { return }
            guard !Task.isCancelled else { return }
            withAnimation(.easeOut(duration: 0.22)) { retired = true }
            do { try await Task.sleep(for: .milliseconds(220)) } catch { return }
            started = nil
        }
        .onDisappear { started = nil; retired = true }
    }

    @ViewBuilder private func workMark(_ item: QueryWorkItem) -> some View {
        if item.activity != .idle { QueryActivityMark(activity: item.activity, size: 20) }
        else { Image(systemName: item.state == .complete ? "checkmark" : item.state == .failed ? "exclamationmark.circle" : "ellipsis").accessibilityHidden(true) }
    }
}

// Vertical collapse, horizontal collapse, phosphor dot. Pure geometry also checked without windows.
enum CRTShutdown {
    struct Frame {
        let width, height, screenOpacity, lineWidth, lineOpacity, dotScale, dotOpacity: Double
    }
    static func frame(_ time: Double) -> Frame {
        let t = min(1, max(0, time))
        func value(_ frames: [(Double, Double)]) -> Double {
            for index in 1..<frames.count where t <= frames[index].0 {
                let a = frames[index - 1], b = frames[index]
                let p = (t - a.0) / (b.0 - a.0), eased = p * p * (3 - 2 * p)
                return a.1 + (b.1 - a.1) * eased
            }
            return frames.last!.1
        }
        return Frame(width: value([(0,1),(0.51,1),(0.76,0.004),(1,0.004)]),
            height: value([(0,1),(0.10,1),(0.37,0.055),(0.51,0.04),(1,0.04)]),
            screenOpacity: value([(0,1),(0.37,1),(0.51,0.7),(0.76,0),(1,0)]),
            lineWidth: value([(0,1),(0.51,1),(0.76,0.005),(0.81,0),(1,0)]),
            lineOpacity: value([(0,0),(0.27,0),(0.37,1),(0.76,1),(0.81,0),(1,0)]),
            dotScale: value([(0,0.6),(0.70,0.6),(0.78,1.25),(0.88,0.7),(1,0)]),
            dotOpacity: value([(0,0),(0.70,0),(0.78,1),(0.88,0.6),(1,0)]))
    }
}
