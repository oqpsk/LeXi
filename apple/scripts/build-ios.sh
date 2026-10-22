#!/bin/zsh
# Builds LeXi for iOS: Lexi.Core as a NativeAOT library (Lexi.Embed), the SwiftUI app, and the signed
# app bundle. Needs Xcode 26+ and the .NET 10 SDK.
#   apple/scripts/build-ios.sh [--run [simulator] | --device [udid] | --testflight [export]] [output-folder]
# By default it builds for the iOS Simulator (artifacts/ios), with no Apple developer account.
# --run boots the named simulator if needed, then installs and opens the app (default: the booted one).
# --device builds for an iPhone or iPad (artifacts/ios-device), signs it for development with the
# certificate and provisioning profile Xcode created for your team, then installs and opens it on the
# named device or the one connected, with this Mac's offline dictionaries (see apple/README.md).
# --testflight builds an App Store archive (artifacts/ios-testflight) and lets Xcode sign it for
# distribution with your team and upload it to App Store Connect; "export" stops at the .ipa.
# Every build gets a new build number (LEXI_BUILD overrides it), as App Store Connect requires.
set -euo pipefail
root=${0:A:h:h:h}
run=false; device=booted; target=simulator
if [[ ${1:-} == --run ]]; then
  run=true; shift
  [[ $# -gt 0 && ${1:-} != /* && ${1:-} != .* ]] && { device=$1; shift }
elif [[ ${1:-} == --device ]]; then
  run=true; target=device; device=-; shift
  [[ $# -gt 0 && ${1:-} != /* && ${1:-} != .* ]] && { device=$1; shift }
elif [[ ${1:-} == --testflight ]]; then
  target=testflight; device=none; destination_kind=upload; shift
  [[ ${1:-} == export ]] && { destination_kind=export; shift }
fi
if [[ $target != simulator ]]; then
  rid=ios-arm64; destination='generic/platform=iOS'; products=Release-iphoneos; sdk=iphoneos; platform=iPhoneOS
else
  rid=iossimulator-arm64; destination='generic/platform=iOS Simulator'; products=Release-iphonesimulator; sdk=iphonesimulator; platform=iPhoneSimulator
fi
case $target in simulator) out=${1:-$root/artifacts/ios} ;; device) out=${1:-$root/artifacts/ios-device} ;; *) out=${1:-$root/artifacts/ios-testflight} ;; esac
work=$root/.cache/ios-build
version=$(sed -n 's:.*<Version>\(.*\)</Version>.*:\1:p' "$root/Directory.Build.props")
# Each build gets its own number, shown in 设置 → 关于 beside the version: yymmdd.HHMM (UTC), which
# only grows, as App Store Connect requires.
build=${LEXI_BUILD:-$(date -u +%y%m%d.%H%M)}
rm -rf "$work/embed" && mkdir -p "$work" "$out"
dotnet=${DOTNET:-$(command -v dotnet || true)}
for candidate in $HOME/.dotnet/dotnet /usr/local/share/dotnet/dotnet /opt/homebrew/bin/dotnet; do
  [[ -z $dotnet && -x $candidate ]] && dotnet=$candidate
done
[[ -n $dotnet ]] || { echo "The .NET 10 SDK (dotnet) is required." >&2; exit 1 }

# Lexi.Core runs inside the app: iOS cannot start the lexi-host process the Mac app uses.
"$dotnet" publish "$root/Lexi.Embed/Lexi.Embed.csproj" -c Release -r $rid \
  --source https://api.nuget.org/v3/index.json -o "$work/embed"
(cd "$root/apple/iOS" && xcodebuild -quiet -scheme LeXiMobile -destination "$destination" -configuration Release \
  -derivedDataPath "$work/derived" -skipPackagePluginValidation build)
binary=$work/derived/Build/Products/$products/LeXiMobile

app=$out/LeXi.app
rm -rf "$app"
mkdir -p "$app/Frameworks"
cp "$binary" "$app/LeXi"
cp -R "$work/embed/assets" "$app/assets"
cp "$root/apple/iOS/Resources/PrivacyInfo.xcprivacy" "$app/"
# The keys Xcode writes into every bundle it builds; App Store Connect checks them.
sdk_version=$(xcrun --sdk $sdk --show-sdk-version); sdk_build=$(xcrun --sdk $sdk --show-sdk-build-version)
xcode_version=$(xcodebuild -version | sed -n 's/^Xcode \([0-9]*\)\.\([0-9]*\).*/\1\20/p'); xcode_build=$(xcodebuild -version | sed -n 's/^Build version //p')
toolchain() {
  for entry in "DTPlatformName string $sdk" "DTPlatformVersion string $sdk_version" "DTPlatformBuild string $sdk_build" \
    "DTSDKName string $sdk$sdk_version" "DTSDKBuild string $sdk_build" "DTXcode string $xcode_version" "DTXcodeBuild string $xcode_build" \
    "DTCompiler string com.apple.compilers.llvm.clang.1_0" "BuildMachineOSBuild string $(sw_vers -buildVersion)"; do
    /usr/libexec/PlistBuddy -c "Add :${entry% *} ${entry##* }" "$1" >/dev/null 2>&1 || true
  done
}
# iOS loads dynamic code only from frameworks, so Lexi.Core goes in as LexiEmbed.framework.
framework=$app/Frameworks/LexiEmbed.framework
mkdir -p "$framework"
cp "$work/embed/LexiEmbed.dylib" "$framework/LexiEmbed"
install_name_tool -id @rpath/LexiEmbed.framework/LexiEmbed "$framework/LexiEmbed"
cat > "$framework/Info.plist" <<PLIST
<?xml version="1.0" encoding="UTF-8"?>
<!DOCTYPE plist PUBLIC "-//Apple//DTD PLIST 1.0//EN" "http://www.apple.com/DTDs/PropertyList-1.0.dtd">
<plist version="1.0"><dict>
	<key>CFBundleDevelopmentRegion</key><string>en</string>
	<key>CFBundleExecutable</key><string>LexiEmbed</string>
	<key>CFBundleIdentifier</key><string>io.github.oqpsk.lexi.embed</string>
	<key>CFBundleInfoDictionaryVersion</key><string>6.0</string>
	<key>CFBundleName</key><string>LexiEmbed</string>
	<key>CFBundlePackageType</key><string>FMWK</string>
	<key>CFBundleShortVersionString</key><string>$version</string>
	<key>CFBundleVersion</key><string>$build</string>
	<key>CFBundleSupportedPlatforms</key><array><string>$platform</string></array>
	<key>MinimumOSVersion</key><string>26.0</string>
</dict></plist>
PLIST
toolchain "$framework/Info.plist"
sed "s/__VERSION__/$version/g" "$root/apple/iOS/Resources/Info.plist" > "$app/Info.plist"
/usr/libexec/PlistBuddy -c "Set :CFBundleSupportedPlatforms:0 $platform" -c "Set :CFBundleVersion $build" "$app/Info.plist"
toolchain "$app/Info.plist"
# iOS takes the app icon from a compiled asset catalog: one full-bleed 1024-pixel image of the brand mark.
icons=$work/Assets.xcassets/AppIcon.appiconset
rm -rf "$work/Assets.xcassets" && mkdir -p "$icons"
swift "$root/apple/scripts/make-icon.swift" "$root/docs/brandmark.svg" "$icons/AppIcon.png" --ios
cat > "$icons/Contents.json" <<'JSON'
{"images":[{"filename":"AppIcon.png","idiom":"universal","platform":"ios","size":"1024x1024"}],"info":{"author":"xcode","version":1}}
JSON
xcrun actool "$work/Assets.xcassets" --compile "$app" --platform $sdk --minimum-deployment-target 26.0 \
  --app-icon AppIcon --output-partial-info-plist "$work/icons.plist" >/dev/null
/usr/libexec/PlistBuddy -c "Delete :CFBundleIcons" "$app/Info.plist" >/dev/null 2>&1 || true
/usr/libexec/PlistBuddy -c "Merge $work/icons.plist" "$app/Info.plist" >/dev/null
cp "$root/LICENSE" "$root/THIRD_PARTY_NOTICES.md" "$app/"
cp -R "$root/licenses" "$app/licenses"

bundle=$(/usr/libexec/PlistBuddy -c "Print :CFBundleIdentifier" "$app/Info.plist")
if [[ $target == device ]]; then
  # A device runs only code signed for it: the development certificate, the team's profile listing this
  # device, and entitlements in the signature.
  signing=("${(@f)$(swift "$root/apple/scripts/ios-signing.swift" "$bundle" "$device" "$app/embedded.mobileprovision" "$work/device.entitlements")}")
  device=${signing[1]}
  codesign --force --sign "${signing[2]}" "$framework"
  codesign --force --sign "${signing[2]}" --entitlements "$work/device.entitlements" "$app"
  echo "Built $app ($version build $build, iOS device)"
  xcrun devicectl device install app --device "$device" "$app"
  # The offline dictionaries this Mac has installed, so suggestions and local definitions work on the
  # device too; unchanged files are skipped. LEXI_DICTIONARIES names another folder, or "" skips this.
  dictionaries=${LEXI_DICTIONARIES-$HOME/Library/Application Support/LeXi/data}
  if [[ -n $dictionaries && -d $dictionaries ]]; then
    for file in "$dictionaries"/**/*(.N); do
      xcrun devicectl device copy to --device "$device" --domain-type appDataContainer --domain-identifier "$bundle" \
        --source "$file" --destination "Library/Application Support/LeXi/data/${file#$dictionaries/}" >/dev/null
    done
    echo "Copied the offline dictionaries from $dictionaries"
  fi
  xcrun devicectl device process launch --device "$device" "$bundle"
  exit 0
fi

if [[ $target == testflight ]]; then
  # Archived as Xcode would, signed for development; Xcode re-signs the export for distribution with
  # the team's certificate and an App Store profile it creates or updates as needed.
  signing=("${(@f)$(swift "$root/apple/scripts/ios-signing.swift" "$bundle" none "$app/embedded.mobileprovision" "$work/device.entitlements")}")
  team=$(/usr/libexec/PlistBuddy -c "Print :com.apple.developer.team-identifier" "$work/device.entitlements")
  codesign --force --sign "${signing[2]}" "$framework"
  codesign --force --sign "${signing[2]}" --entitlements "$work/device.entitlements" "$app"
  archive=$out/LeXi.xcarchive
  rm -rf "$archive" "$out/export" && mkdir -p "$archive/Products/Applications" "$archive/dSYMs"
  cp -R "$app" "$archive/Products/Applications/"
  # Symbols for both binaries, so App Store Connect can symbolicate crashes in Swift and in Core.
  [[ -d $work/derived/Build/Products/$products/LeXiMobile.dSYM ]] && cp -R "$work/derived/Build/Products/$products/LeXiMobile.dSYM" "$archive/dSYMs/LeXi.app.dSYM"
  [[ -d $work/embed/LexiEmbed.dylib.dSYM ]] && cp -R "$work/embed/LexiEmbed.dylib.dSYM" "$archive/dSYMs/LexiEmbed.framework.dSYM"
  plist=$archive/Info.plist
  /usr/libexec/PlistBuddy -c "Add :ArchiveVersion integer 2" -c "Add :Name string LeXi" -c "Add :SchemeName string LeXi" \
    -c "Add :CreationDate date $(date -u '+%a %b %d %H:%M:%S GMT %Y')" -c "Add :ApplicationProperties dict" \
    -c "Add :ApplicationProperties:ApplicationPath string Applications/LeXi.app" -c "Add :ApplicationProperties:CFBundleIdentifier string $bundle" \
    -c "Add :ApplicationProperties:CFBundleShortVersionString string $version" -c "Add :ApplicationProperties:CFBundleVersion string $build" \
    -c "Add :ApplicationProperties:Team string $team" -c "Add :ApplicationProperties:Architectures array" \
    -c "Add :ApplicationProperties:Architectures:0 string arm64" "$plist" >/dev/null
  exports=$work/export-options.plist
  rm -f "$exports"
  /usr/libexec/PlistBuddy -c "Add :method string app-store-connect" -c "Add :destination string $destination_kind" -c "Add :teamID string $team" \
    -c "Add :signingStyle string automatic" -c "Add :uploadSymbols bool true" -c "Add :manageAppVersionAndBuildNumber bool false" "$exports" >/dev/null
  xcodebuild -exportArchive -archivePath "$archive" -exportOptionsPlist "$exports" -exportPath "$out/export" -allowProvisioningUpdates
  echo "Exported LeXi $version (build $build) for App Store Connect${${destination_kind:#export}:+ and uploaded it}"
  exit 0
fi

# Ad-hoc signatures run in the Simulator only.
codesign --force --sign - "$framework"
# The entitlements are linked into the binary (apple/iOS/Package.swift), as Xcode does for the Simulator.
codesign --force --sign - "$app"
echo "Built $app ($version build $build, iOS Simulator)"

if $run; then
  # Boot the named simulator if it is not running yet, and show it.
  [[ $device != booted ]] && { xcrun simctl boot "$device" 2>/dev/null || true }
  xcrun simctl bootstatus "$device" -b >/dev/null
  open "$(xcode-select -p)/Applications/Simulator.app" 2>/dev/null || true
  xcrun simctl install "$device" "$app"
  xcrun simctl launch "$device" "$bundle"
fi
