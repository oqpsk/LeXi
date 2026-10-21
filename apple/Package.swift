// swift-tools-version: 6.2
import PackageDescription

// LeXi for macOS. Lexi.Core is the only logic implementation: the app bundles the
// self-contained Lexi.Host and renders the views it streams (docs/host-protocol.md).
let package = Package(
    name: "LeXi",
    defaultLocalization: "zh-Hans",
    platforms: [.macOS(.v26), .iOS(.v26)],
    products: [
        .executable(name: "LeXi", targets: ["LeXi"]),
        // Protocol models, the host client and speech, shared with the iOS app (apple/iOS).
        .library(name: "LeXiKit", targets: ["LeXiKit"]),
        // SwiftUI views both windows draw the same way (the query progress row and its marks).
        .library(name: "LeXiShared", targets: ["LeXiShared"]),
    ],
    targets: [
        .target(name: "LeXiKit"),
        .target(name: "LeXiShared", dependencies: ["LeXiKit"]),
        .target(name: "LeXiUI", dependencies: ["LeXiKit", "LeXiShared"]),
        .executableTarget(name: "LeXi", dependencies: ["LeXiKit", "LeXiUI"]),
        .testTarget(name: "LeXiKitTests", dependencies: ["LeXiKit"], exclude: ["Fixtures"]),
        .testTarget(name: "LeXiUITests", dependencies: ["LeXiKit", "LeXiUI", "LeXiShared"]),
    ]
)
