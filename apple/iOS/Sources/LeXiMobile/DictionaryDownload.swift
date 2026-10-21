import LeXiKit
import SwiftUI
import UIKit

// Downloads a dictionary pack and has Core install it, one at a time. The welcome sheet and
// 设置 → 离线词典 share it, so both show the same progress; iOS lends time to finish in the background.
@MainActor @Observable final class DictionaryDownload {
    enum Phase: Equatable { case idle, downloading(Double), installing, failed(String) }
    private(set) var phase: Phase = .idle
    private var task: URLSessionDownloadTask?
    private var progress: NSKeyValueObservation?
    private var background: UIBackgroundTaskIdentifier = .invalid

    var busy: Bool { switch phase { case .downloading, .installing: true; default: false } }

    func start(_ pack: DictionaryPack, store: LexiStore) {
        guard !busy, let url = URL(string: pack.url) else { return }
        // The archive, the unpacked files beside it, and some room to spare.
        let needed = pack.bytes + pack.installedBytes + 50_000_000
        if let free = try? URL.documentsDirectory.resourceValues(forKeys: [.volumeAvailableCapacityForImportantUsageKey]).volumeAvailableCapacityForImportantUsage,
           free < needed {
            phase = .failed("存储空间不足，需要约 " + DataSection.size(needed) + "。")
            return
        }
        phase = .downloading(0)
        background = UIApplication.shared.beginBackgroundTask(withName: "LeXi dictionary") { [weak self] in self?.cancel() }
        let task = URLSession.shared.downloadTask(with: url) { [weak self] location, response, error in
            // The downloaded file is removed once this returns, so it is moved out first.
            var archive: URL?
            if let location, (response as? HTTPURLResponse)?.statusCode == 200 {
                let target = FileManager.default.temporaryDirectory.appending(path: "dictionary-\(UUID().uuidString).zip")
                if (try? FileManager.default.moveItem(at: location, to: target)) != nil { archive = target }
            }
            let cancelled = (error as? URLError)?.code == .cancelled
            Task { @MainActor in await self?.finish(pack, archive: archive, cancelled: cancelled, store: store) }
        }
        progress = task.progress.observe(\.fractionCompleted) { [weak self] progress, _ in
            let fraction = progress.fractionCompleted
            Task { @MainActor in if case .downloading = self?.phase { self?.phase = .downloading(fraction) } }
        }
        self.task = task
        task.resume()
    }

    func cancel() { task?.cancel() }

    private func finish(_ pack: DictionaryPack, archive: URL?, cancelled: Bool, store: LexiStore) async {
        task = nil
        progress = nil
        defer { endBackground() }
        guard let archive else { phase = cancelled ? .idle : .failed("下载没有完成，请检查网络后重试。"); return }
        defer { try? FileManager.default.removeItem(at: archive) }
        phase = .installing
        do { try await store.installDictionary(pack.id, archive: archive.path); phase = .idle }
        catch let error as HostError { phase = .failed(error.message) }
        catch { phase = .failed("安装没有完成，请重试。") }
    }

    private func endBackground() {
        guard background != .invalid else { return }
        UIApplication.shared.endBackgroundTask(background)
        background = .invalid
    }
}

// A downloadable dictionary: its size and the download button, its progress, what went wrong, or that
// it is installed.
struct DictionaryPackRow: View {
    let pack: DictionaryPack
    @Bindable var store: LexiStore
    @Environment(DictionaryDownload.self) private var download

    var body: some View {
        VStack(alignment: .leading, spacing: 8) {
            HStack {
                VStack(alignment: .leading, spacing: 2) {
                    Text(pack.name)
                    Text(detail).font(.caption).foregroundStyle(.secondary)
                }
                Spacer()
                switch download.phase {
                case .downloading: Button("取消") { download.cancel() }.buttonStyle(.borderless)
                case .installing: ProgressView()
                case .idle where pack.installed: Image(systemName: "checkmark.circle.fill").foregroundStyle(.secondary).accessibilityLabel("已安装")
                case .idle, .failed: Button(failed ? "重试" : "下载") { download.start(pack, store: store) }.buttonStyle(.borderless)
                }
            }
            if case .downloading(let fraction) = download.phase { ProgressView(value: fraction) }
            if case .failed(let message) = download.phase { Text(message).font(.caption).foregroundStyle(.red) }
        }
    }

    private var failed: Bool { if case .failed = download.phase { true } else { false } }
    private var detail: String {
        switch download.phase {
        case .downloading(let fraction): "已下载 \(Int(fraction * 100))% · 共 " + DataSection.size(pack.bytes)
        case .installing: "正在安装…"
        case .idle where pack.installed: "已安装 · 占用 " + DataSection.size(pack.installedBytes)
        default: "下载 " + DataSection.size(pack.bytes) + "，安装后占用 " + DataSection.size(pack.installedBytes) + " · " + pack.license + " 许可"
        }
    }
}
