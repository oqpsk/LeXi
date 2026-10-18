import CryptoKit
import Foundation
import Security

// These values are encoded only into Keychain data or the private stdin pipe of the bundled
// local host. They never belong in AppSettings, view provenance or error/debug descriptions.
public struct TextCredentials: Codable, Sendable, Equatable, CustomStringConvertible, CustomDebugStringConvertible {
    public let clientId: String
    public let clientSecret: String
    public let apiKey: String
    public init(clientId: String = "", clientSecret: String = "", apiKey: String = "") {
        self.clientId = clientId.trimmingCharacters(in: .whitespacesAndNewlines)
        self.clientSecret = clientSecret.trimmingCharacters(in: .whitespacesAndNewlines)
        self.apiKey = apiKey.trimmingCharacters(in: .whitespacesAndNewlines)
    }
    public var description: String { "[protected text credentials]" }
    public var debugDescription: String { description }
    public func validate() throws {
        guard !clientId.isEmpty, !clientSecret.isEmpty else { throw TextCredentialError("请填写 CF Access Client ID 和 Client Secret。") }
        guard [clientId, clientSecret, apiKey].allSatisfy({ $0.count <= 4096 && $0.rangeOfCharacter(from: .whitespacesAndNewlines.union(.controlCharacters)) == nil })
        else { throw TextCredentialError("凭据格式无效，请检查是否混入空格或换行。") }
    }
    public static func endpoint(_ value: String) throws -> String {
        guard var parts = URLComponents(string: value.trimmingCharacters(in: .whitespacesAndNewlines)),
              parts.scheme?.lowercased() == "https", let host = parts.host, !host.isEmpty,
              parts.user == nil, parts.password == nil, parts.query == nil, parts.fragment == nil else {
            throw TextCredentialError("Base URL 必须是 HTTPS 地址，且不含账号、查询参数或片段。")
        }
        parts.scheme = "https"; parts.host = host.lowercased()
        if parts.port == 443 { parts.port = nil }
        guard var result = parts.url?.absoluteString else { throw TextCredentialError("Base URL 无效。") }
        while result.hasSuffix("/") { result.removeLast() }
        return result
    }
}

public struct TextCredentialError: Error, LocalizedError, Sendable {
    private let message: String
    public init(_ message: String = "自定义文本服务凭据无法从钥匙串读取或保存，请重新填写并保存。") { self.message = message }
    public var errorDescription: String? { message }
}

public protocol TextCredentialStore: Sendable {
    func read(_ baseUrl: String) throws -> TextCredentials?
    func save(_ baseUrl: String, _ value: TextCredentials) throws
    func remove(_ baseUrl: String) throws
    func hasSaved(_ baseUrl: String) -> Bool
}

// Generic-password storage available on macOS and iOS. Endpoint-bound and device-local.
public struct KeychainTextCredentials: TextCredentialStore {
    private let service: String
    public init(service: String = "io.github.oqpsk.lexi.text") { self.service = service }
    private func item(_ baseUrl: String) throws -> [String: Any] {
        let endpoint = try TextCredentials.endpoint(baseUrl)
        let account = SHA256.hash(data: Data(endpoint.utf8)).map { String(format: "%02x", $0) }.joined()
        return [kSecClass as String: kSecClassGenericPassword, kSecAttrService as String: service, kSecAttrAccount as String: account]
    }
    public func read(_ baseUrl: String) throws -> TextCredentials? {
        var query = try item(baseUrl)
        query[kSecReturnData as String] = true; query[kSecMatchLimit as String] = kSecMatchLimitOne
        var result: CFTypeRef?
        let status = SecItemCopyMatching(query as CFDictionary, &result)
        if status == errSecItemNotFound { return nil }
        guard status == errSecSuccess, let data = result as? Data,
              let value = try? JSONDecoder().decode(TextCredentials.self, from: data) else { throw TextCredentialError() }
        return value
    }
    public func save(_ baseUrl: String, _ value: TextCredentials) throws {
        try value.validate()
        let data = try JSONEncoder().encode(value)
        var add = try item(baseUrl)
        // Replacing trusts the build that saves it, including a new ad-hoc signed build.
        try remove(baseUrl)
        add[kSecValueData as String] = data
        add[kSecAttrLabel as String] = "LeXi 自定义文本服务"
        add[kSecAttrSynchronizable as String] = false
        #if os(iOS)
        add[kSecAttrAccessible as String] = kSecAttrAccessibleWhenUnlockedThisDeviceOnly
        #endif
        guard SecItemAdd(add as CFDictionary, nil) == errSecSuccess else { throw TextCredentialError() }
    }
    public func remove(_ baseUrl: String) throws {
        let status = SecItemDelete(try item(baseUrl) as CFDictionary)
        guard status == errSecSuccess || status == errSecItemNotFound else { throw TextCredentialError() }
    }
    public func hasSaved(_ baseUrl: String) -> Bool {
        guard let query = try? item(baseUrl) else { return false }
        return SecItemCopyMatching(query as CFDictionary, nil) == errSecSuccess
    }
}
