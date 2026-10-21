#!/bin/zsh
# Builds LeXi.app for this Mac's architecture: the self-contained Lexi.Host, the SwiftUI app,
# model schemas, icon and licenses. Needs Xcode 26+ and the .NET 10 SDK.
#   apple/scripts/build-app.sh [output-folder]      (default: artifacts/macos)
set -euo pipefail
root=${0:A:h:h:h}
out=${1:-$root/artifacts/macos}
work=$root/.cache/macos-build
arch=$(uname -m)
rid=osx-${arch/x86_64/x64}
version=$(sed -n 's:.*<Version>\(.*\)</Version>.*:\1:p' "$root/Directory.Build.props")
rm -rf "$work" && mkdir -p "$work" "$out"
dotnet=${DOTNET:-$(command -v dotnet || true)}
for candidate in $HOME/.dotnet/dotnet /usr/local/share/dotnet/dotnet /opt/homebrew/bin/dotnet; do
  [[ -z $dotnet && -x $candidate ]] && dotnet=$candidate
done
[[ -n $dotnet ]] || { echo "The .NET 10 SDK (dotnet) is required." >&2; exit 1 }

# The repository's NuGet.Config has no sources; runtime packs for a self-contained host come from nuget.org.
"$dotnet" publish "$root/Lexi.Host/Lexi.Host.csproj" -c Release -r "$rid" --self-contained \
  -p:PublishSingleFile=true -p:EnableCompressionInSingleFile=true -p:DebugType=none \
  --source https://api.nuget.org/v3/index.json -o "$work/host"
swift build -c release --package-path "$root/apple" --product LeXi
bin=$(swift build -c release --package-path "$root/apple" --show-bin-path)

app=$out/LeXi.app
rm -rf "$app"
mkdir -p "$app/Contents/MacOS" "$app/Contents/Helpers" "$app/Contents/Resources"
cp "$bin/LeXi" "$app/Contents/MacOS/LeXi"
cp "$work/host/lexi-host" "$app/Contents/Helpers/lexi-host"
cp -R "$work/host/assets" "$app/Contents/Resources/assets"
sed "s/__VERSION__/$version/g" "$root/apple/Resources/Info.plist" > "$app/Contents/Info.plist"
# An Icon Composer icon: macOS draws its rounded shape, glass and shadow, so the corners match every
# other app. actool writes Assets.car and an AppIcon.icns fallback.
icon="$work/AppIcon.icon"
rm -rf "$icon"; cp -R "$root/apple/Resources/AppIcon.icon" "$icon"
swift "$root/apple/scripts/make-icon.swift" "$root/docs/brandmark.svg" "$icon"
xcrun actool "$icon" --compile "$app/Contents/Resources" --platform macosx --minimum-deployment-target 26.0 \
    --app-icon AppIcon --output-partial-info-plist "$work/AppIcon.plist" > "$work/actool.log"
[ -f "$app/Contents/Resources/Assets.car" ] || { cat "$work/actool.log" >&2; echo "actool did not compile AppIcon.icon" >&2; exit 1; }
cp "$root/LICENSE" "$root/THIRD_PARTY_NOTICES.md" "$app/Contents/Resources/"
cp -R "$root/licenses" "$app/Contents/Resources/licenses"

# Ad-hoc signature for running on this Mac. Distribution needs a Developer ID signature,
# the hardened runtime and notarization (apple/README.md). The keychain treats every ad-hoc build
# as a new app, so speech keys must be saved again after a rebuild; LEXI_SIGN_IDENTITY (for example
# an "Apple Development" certificate) keeps one identity across rebuilds.
identity=${LEXI_SIGN_IDENTITY:--}
codesign --force --sign "$identity" "$app/Contents/Helpers/lexi-host"
codesign --force --sign "$identity" "$app"
# A bundle rebuilt at the same path can keep a stale LaunchServices record, so `open` would not start it.
/System/Library/Frameworks/CoreServices.framework/Frameworks/LaunchServices.framework/Support/lsregister -f "$app"
echo "Built $app ($version, $arch)"
