import Foundation

public struct HostError: Error, Equatable, Sendable, Decodable, LocalizedError {
    public let code: String
    public let message: String
    public init(code: String, message: String) { self.code = code; self.message = message }
    public var errorDescription: String? { message }
    static let exited = HostError(code: "host_exited", message: "LeXi 查询服务已停止。")
}

public enum HostEvent: Sendable {
    case view(QueryView)
    case exited(Int32)
}

// One lexi-host process. Requests are matched to responses by id; "view" events are delivered
// in the order the host wrote them. Cancelling a Swift task cancels its request in the host.
public actor HostClient {
    private let process = Process()
    private let input = Pipe(), output = Pipe()
    private let encoder = JSONEncoder()
    private let decoder = JSONDecoder()
    private let events: @Sendable (HostEvent) -> Void
    private var pending: [Int: CheckedContinuation<Data, Error>] = [:]
    private var next = 0
    private var stopped = false

    public init(executable: URL, arguments: [String] = [], events: @escaping @Sendable (HostEvent) -> Void) {
        process.executableURL = executable
        process.arguments = arguments
        self.events = events
    }

    public func start() throws {
        // A closed pipe must surface as a write error, not terminate the app.
        signal(SIGPIPE, SIG_IGN)
        process.standardInput = input
        process.standardOutput = output
        // Diagnostics carry no protocol data; discarding them means the host can never block on stderr.
        process.standardError = FileHandle.nullDevice
        process.terminationHandler = { [weak self] finished in
            let status = finished.terminationStatus
            Task { await self?.exited(status) }
        }
        try process.run()
        let lines = output.fileHandleForReading.bytes.lines
        Task.detached { [weak self] in
            do { for try await line in lines { await self?.receive(Data(line.utf8)) } } catch {}
        }
    }

    public func call<Result: Decodable & Sendable>(_ method: String, _ parameters: some Encodable & Sendable = [String: String](),
                                                    as type: Result.Type = Result.self) async throws -> Result {
        let envelope: Response<Result> = try await send(method, parameters)
        guard let result = envelope.result else { throw HostError(code: "invalid_response", message: "查询服务返回了空结果。") }
        return result
    }

    // For methods whose result may be null.
    public func callOptional<Result: Decodable & Sendable>(_ method: String, _ parameters: some Encodable & Sendable = [String: String](),
                                                            as type: Result.Type = Result.self) async throws -> Result? {
        let envelope: Response<Result> = try await send(method, parameters)
        return envelope.result
    }

    public func stop() async {
        guard !stopped else { return }
        _ = try? await send("shutdown", [String: String]()) as Response<Empty>
        try? input.fileHandleForWriting.close()
        if process.isRunning { process.terminate() }
    }

    private func send<Result: Decodable>(_ method: String, _ parameters: some Encodable & Sendable) async throws -> Response<Result> {
        if stopped { throw HostError.exited }
        next += 1
        let id = next
        var line = try encoder.encode(Request(id: id, method: method, params: parameters))
        line.append(0x0A)
        let data: Data = try await withTaskCancellationHandler {
            try await withCheckedThrowingContinuation { continuation in
                pending[id] = continuation
                do { try input.fileHandleForWriting.write(contentsOf: line) }
                catch { pending.removeValue(forKey: id)?.resume(throwing: HostError.exited) }
            }
        } onCancel: {
            Task { await self.cancel(id) }
        }
        let envelope = try decoder.decode(Response<Result>.self, from: data)
        if let error = envelope.error { throw error }
        return envelope
    }

    private func cancel(_ id: Int) {
        guard pending[id] != nil, !stopped else { return }
        next += 1
        var line = (try? encoder.encode(Request(id: next, method: "cancel", params: ["id": id]))) ?? Data()
        line.append(0x0A)
        try? input.fileHandleForWriting.write(contentsOf: line)
    }

    private func receive(_ line: Data) {
        guard let probe = try? decoder.decode(Probe.self, from: line) else { return }
        if probe.event == "view" {
            if let event = try? decoder.decode(ViewEvent.self, from: line) { events(.view(event.data)) }
        } else if let id = probe.id, let continuation = pending.removeValue(forKey: id) {
            continuation.resume(returning: line)
        }
    }

    private func exited(_ status: Int32) {
        stopped = true
        for continuation in pending.values { continuation.resume(throwing: HostError.exited) }
        pending.removeAll()
        events(.exited(status))
    }

    private struct Request<Parameters: Encodable>: Encodable { let id: Int; let method: String; let params: Parameters }
    private struct Probe: Decodable { let id: Int?; let event: String? }
    private struct ViewEvent: Decodable { let data: QueryView }
    private struct Response<Result: Decodable>: Decodable { let result: Result?; let error: HostError? }
    private struct Empty: Decodable {}
}

public enum HostLocation {
    // The app bundle carries lexi-host in Contents/Helpers and its model files in Resources/assets.
    // LEXI_HOST points development runs (swift run, tests) at a published host instead.
    public static func resolve(bundle: Bundle = .main, environment: [String: String] = ProcessInfo.processInfo.environment) -> (URL, [String])? {
        if let path = environment["LEXI_HOST"], !path.isEmpty {
            let url = URL(fileURLWithPath: path)
            let assets = url.deletingLastPathComponent().appending(path: "assets")
            return (url, FileManager.default.fileExists(atPath: assets.path) ? ["--assets", assets.path] : [])
        }
        let helper = bundle.bundleURL.appending(path: "Contents/Helpers/lexi-host")
        guard FileManager.default.isExecutableFile(atPath: helper.path) else { return nil }
        let assets = bundle.resourceURL?.appending(path: "assets").path ?? ""
        return (helper, assets.isEmpty ? [] : ["--assets", assets])
    }
}
