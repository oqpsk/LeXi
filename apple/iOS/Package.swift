// swift-tools-version: 6.2
import PackageDescription

// LeXi for iOS (simulator preview). The app shares LeXiKit with macOS and runs Lexi.Core inside its
// own process as a NativeAOT library (Lexi.Embed), since iOS cannot start the lexi-host process.
// Build and install with apple/scripts/build-ios.sh.
let package = Package(
    name: "LeXiMobile",
    defaultLocalization: "zh-Hans",
    platforms: [.iOS(.v26)],
    dependencies: [.package(name: "LeXi", path: "..")],
    targets: [
        .executableTarget(name: "LeXiMobile", dependencies: [.product(name: "LeXiKit", package: "LeXi")]),
    ]
)
