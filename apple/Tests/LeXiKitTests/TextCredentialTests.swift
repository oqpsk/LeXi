import Foundation
import Testing
@testable import LeXiKit

@Suite struct CustomTextCredentials {
    @Test func validatesEndpointAndRequiredHeaders() throws {
        #expect(try TextCredentials.endpoint("https://EXAMPLE.org:443/proxy/v1/") == "https://example.org/proxy/v1")
        for value in ["http://example.org/v1", "https://user:secret@example.org/v1", "https://example.org/v1?key=value", "https://example.org/v1#fragment"] {
            #expect(throws: TextCredentialError.self) { _ = try TextCredentials.endpoint(value) }
        }
        #expect(throws: TextCredentialError.self) { try TextCredentials().validate() }
        #expect(throws: TextCredentialError.self) { try TextCredentials(clientId: "id", clientSecret: "").validate() }
        #expect(throws: TextCredentialError.self) { try TextCredentials(clientId: "id\r\nheader", clientSecret: "secret").validate() }
        try TextCredentials(clientId: "synthetic-id", clientSecret: "synthetic-secret").validate()
    }
    @Test func redactsDescriptionsAndRejectsUnsafeKeychainAccounts() throws {
        let value = TextCredentials(clientId: "synthetic-id", clientSecret: "synthetic-secret")
        #expect(!String(describing: value).contains("synthetic"))
        #expect(!String(reflecting: value).contains("synthetic"))
        let keys = KeychainTextCredentials(service: "io.github.oqpsk.lexi.text.tests")
        #expect(throws: TextCredentialError.self) { _ = try keys.read("http://example.org") }
        #expect(!keys.hasSaved("http://example.org"))
    }
    @Test(.enabled(if: ProcessInfo.processInfo.environment["LEXI_KEYCHAIN_TESTS"] == "1", "writes synthetic text credentials to the login keychain"))
    func roundTripsAndBindsSecretsToTheEndpoint() throws {
        let keys = KeychainTextCredentials(service: "io.github.oqpsk.lexi.text.tests.\(UUID().uuidString)")
        let endpoint = "https://example.org/v1"
        defer { try? keys.remove(endpoint) }
        let value = TextCredentials(clientId: "synthetic-id", clientSecret: "synthetic-secret")
        try keys.save(endpoint, value)
        #expect(try keys.read(endpoint + "/") == value)
        #expect(try keys.read("https://other.example.org/v1") == nil)
        #expect(try keys.read(endpoint + "/other") == nil)
        try keys.remove(endpoint)
        #expect(!keys.hasSaved(endpoint))
    }
}
