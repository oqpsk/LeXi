// swift-tools-version: 6.2
import PackageDescription

// LeXi for macOS. Lexi.Core is the only logic implementation: the app bundles the
// self-contained Lexi.Host and renders the views it streams (docs/host-protocol.md).
let package = Package(
    name: "LeXi",
    defaultLocalization: "zh-Hans",
    platforms: [.macOS(.v26)],
    products: [.executable(name: "LeXi", targets: ["LeXi"])],
    targets: [
        .target(name: "LeXiKit"),
        .target(name: "LeXiUI", dependencies: ["LeXiKit"]),
        .executableTarget(name: "LeXi", dependencies: ["LeXiKit", "LeXiUI"]),
        .testTarget(name: "LeXiKitTests", dependencies: ["LeXiKit"], exclude: ["Fixtures"]),
        .testTarget(name: "LeXiUITests", dependencies: ["LeXiKit", "LeXiUI"]),
    ]
)
