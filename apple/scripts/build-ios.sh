#!/bin/zsh
# Builds LeXi for iOS: Lexi.Core as a NativeAOT library (Lexi.Embed), the SwiftUI app, and the signed
# app bundle. Needs Xcode 26+ and the .NET 10 SDK.
#   apple/scripts/build-ios.sh [--run [simulator] | --device [udid]] [output-folder]
# By default it builds for the iOS Simulator (artifacts/ios), with no Apple developer account.
# --run boots the named simulator if needed, then installs and opens the app (default: the booted one).
# --device builds for an iPhone or iPad (artifacts/ios-device), signs it for development with the
# certificate and provisioning profile Xcode created for your team, then installs and opens it on the
# named device or the one connected, with this Mac's offline dictionaries (see apple/README.md).
set -euo pipefail
root=${0:A:h:h:h}
run=false; device=booted; target=simulator
if [[ ${1:-} == --run ]]; then
  run=true; shift
  [[ $# -gt 0 && ${1:-} != /* && ${1:-} != .* ]] && { device=$1; shift }
elif [[ ${1:-} == --device ]]; then
  run=true; target=device; device=-; shift
  [[ $# -gt 0 && ${1:-} != /* && ${1:-} != .* ]] && { device=$1; shift }
fi
if [[ $target == device ]]; then
  rid=ios-arm64; destination='generic/platform=iOS'; products=Release-iphoneos; sdk=iphoneos; platform=iPhoneOS
else
  rid=iossimulator-arm64; destination='generic/platform=iOS Simulator'; products=Release-iphonesimulator; sdk=iphonesimulator; platform=iPhoneSimulator
fi
out=${1:-$root/artifacts/ios${${target:#simulator}:+-device}}
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
cp "$work/embed/LexiEmbed.dylib" "$app/Frameworks/LexiEmbed.dylib"
cp -R "$work/embed/assets" "$app/assets"
sed "s/__VERSION__/$version/g" "$root/apple/iOS/Resources/Info.plist" > "$app/Info.plist"
/usr/libexec/PlistBuddy -c "Set :CFBundleSupportedPlatforms:0 $platform" -c "Set :CFBundleVersion $build" "$app/Info.plist"
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
  codesign --force --sign "${signing[2]}" "$app/Frameworks/LexiEmbed.dylib"
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

# Ad-hoc signatures run in the Simulator only.
codesign --force --sign - "$app/Frameworks/LexiEmbed.dylib"
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
