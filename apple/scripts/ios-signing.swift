// Chooses how to sign LeXi for an iPhone or iPad: the connected device (unless one is named), a valid
// development provisioning profile from Xcode's folders that covers the bundle identifier and that
// device, the keychain identity whose certificate the profile lists, and the profile's team. Copies the
// profile into the app and writes the entitlements; prints the device and the identity's SHA-1, one per
// line. Account details come from Xcode's own files and never enter the repository.
// usage: swift ios-signing.swift <bundle id> <device udid | -> <embedded.mobileprovision> <entitlements.plist>
import CryptoKit
import Foundation

func run(_ tool: String, _ arguments: [String]) -> Data {
    let process = Process()
    process.executableURL = URL(fileURLWithPath: tool)
    process.arguments = arguments
    let pipe = Pipe()
    process.standardOutput = pipe
    process.standardError = FileHandle.nullDevice
    guard (try? process.run()) != nil else { return Data() }
    let data = pipe.fileHandleForReading.readDataToEndOfFile()
    process.waitUntilExit()
    return data
}
func fail(_ message: String) -> Never { FileHandle.standardError.write(Data((message + "\n").utf8)); exit(1) }

let arguments = CommandLine.arguments
guard arguments.count == 5 else {
    FileHandle.standardError.write(Data("usage: ios-signing.swift <bundle id> <device udid | -> <embedded.mobileprovision> <entitlements.plist>\n".utf8)); exit(2)
}
let bundle = arguments[1]

// The first paired physical device, when none is named.
var device = arguments[2]
if device == "-" {
    let list = FileManager.default.temporaryDirectory.appending(path: "lexi-devices-\(UUID().uuidString).json")
    defer { try? FileManager.default.removeItem(at: list) }
    _ = run("/usr/bin/xcrun", ["devicectl", "list", "devices", "--json-output", list.path])
    let result = (try? JSONSerialization.jsonObject(with: Data(contentsOf: list))) as? [String: Any]
    let devices = ((result?["result"] as? [String: Any])?["devices"] as? [[String: Any]]) ?? []
    var paired: [String] = []
    for entry in devices {
        let hardware = entry["hardwareProperties"] as? [String: Any] ?? [:]
        let connection = entry["connectionProperties"] as? [String: Any] ?? [:]
        if hardware["reality"] as? String == "physical", connection["pairingState"] as? String == "paired", let udid = hardware["udid"] as? String {
            paired.append(udid)
        }
    }
    guard let first = paired.first else { fail("No paired iPhone or iPad is connected. Connect one with a cable, tap Trust and turn on Developer Mode.") }
    device = first
}

let identities = String(decoding: run("/usr/bin/security", ["find-identity", "-v", "-p", "codesigning"]), as: UTF8.self)
let home = FileManager.default.homeDirectoryForCurrentUser
var best: (exact: Bool, path: URL, identity: String, team: String)?
for folder in ["Library/Developer/Xcode/UserData/Provisioning Profiles", "Library/MobileDevice/Provisioning Profiles"] {
    let paths = (try? FileManager.default.contentsOfDirectory(at: home.appending(path: folder), includingPropertiesForKeys: nil)) ?? []
    for path in paths where path.pathExtension == "mobileprovision" {
        guard let profile = try? PropertyListSerialization.propertyList(from: run("/usr/bin/security", ["cms", "-D", "-i", path.path]), format: nil) as? [String: Any],
              let expiry = profile["ExpirationDate"] as? Date, expiry > .now,
              (profile["ProvisionedDevices"] as? [String])?.contains(device) == true,
              let team = (profile["TeamIdentifier"] as? [String])?.first,
              let entitlements = profile["Entitlements"] as? [String: Any], entitlements["get-task-allow"] as? Bool == true,
              let appID = entitlements["application-identifier"] as? String, appID == "\(team).\(bundle)" || appID == "\(team).*",
              let identity = ((profile["DeveloperCertificates"] as? [Data]) ?? [])
                .map({ Insecure.SHA1.hash(data: $0).map { String(format: "%02X", $0) }.joined() })
                .first(where: { identities.contains($0) })
        else { continue }
        let exact = appID == "\(team).\(bundle)"
        if best == nil || exact && best?.exact == false { best = (exact, path, identity, team) }
    }
}
guard let best else {
    fail("No development provisioning profile covers \(bundle) on device \(device). Sign in to your developer account in Xcode and let it create one (see apple/README.md).")
}

let embedded = URL(fileURLWithPath: arguments[3])
try? FileManager.default.removeItem(at: embedded)
try FileManager.default.copyItem(at: best.path, to: embedded)
let entitlements: [String: Any] = [
    "application-identifier": "\(best.team).\(bundle)", "com.apple.developer.team-identifier": best.team,
    "get-task-allow": true, "keychain-access-groups": ["\(best.team).\(bundle)"],
]
try PropertyListSerialization.data(fromPropertyList: entitlements, format: .xml, options: 0).write(to: URL(fileURLWithPath: arguments[4]))
print(device)
print(best.identity)
