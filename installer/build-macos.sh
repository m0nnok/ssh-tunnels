#!/usr/bin/env bash
# Publishes a self-contained build of SSH Tunneler for macOS, wraps it into SshTunneler.app,
# signs it ad hoc and packs it into a .dmg. Runs on a Mac with the .NET 10 SDK.
#
#   installer/build-macos.sh [version] [runtime]
#   installer/build-macos.sh 1.1.0            # Apple Silicon (osx-arm64)
#   installer/build-macos.sh 1.1.0 osx-x64    # Intel Macs
#
# The result is artifacts/SshTunneler-<version>-<runtime>.dmg. The signature is ad hoc, so a copy
# downloaded from the internet has to be cleared once on the target Mac:
#   xattr -dr com.apple.quarantine /Applications/SshTunneler.app
set -euo pipefail

version="${1:-1.0.0}"
runtime="${2:-osx-arm64}"

# The version ends up in an MSBuild property and a sed expression, so only plain versions pass.
if [[ ! "$version" =~ ^[0-9]+(\.[0-9]+){1,3}$ ]]; then
    echo "Version must look like 1.2.3, got: $version" >&2
    exit 1
fi
if [[ "$runtime" != "osx-arm64" && "$runtime" != "osx-x64" ]]; then
    echo "Runtime must be osx-arm64 or osx-x64, got: $runtime" >&2
    exit 1
fi

root="$(cd "$(dirname "$0")/.." && pwd)"
artifacts="$root/artifacts"
publish="$artifacts/publish-$runtime"
app="$artifacts/SshTunneler.app"
dmg="$artifacts/SshTunneler-$version-$runtime.dmg"

if [[ "$(uname)" != "Darwin" ]]; then
    echo "build-macos.sh needs macOS: codesign and hdiutil are part of the system there." >&2
    exit 1
fi

rm -rf "$publish" "$app" "$dmg"
mkdir -p "$artifacts"

echo "Publishing $runtime self-contained build..."
dotnet publish "$root/src/SshTunneler/SshTunneler.csproj" \
    -c Release -r "$runtime" --self-contained true \
    -p:Version="$version" -p:DebugType=none \
    -o "$publish"

echo "Assembling SshTunneler.app..."
mkdir -p "$app/Contents/MacOS" "$app/Contents/Resources"
cp -R "$publish/." "$app/Contents/MacOS/"
sed "s/@VERSION@/$version/g" "$root/installer/macos/Info.plist" > "$app/Contents/Info.plist"
chmod +x "$app/Contents/MacOS/SshTunneler"

# Apple Silicon runs nothing unsigned; an ad hoc signature over the whole bundle is enough to
# start it locally. Hardened runtime stays off: the .NET JIT would need extra entitlements.
echo "Signing ad hoc..."
codesign --force --deep --sign - "$app"
codesign --verify --deep --strict "$app"

echo "Building disk image..."
staging="$(mktemp -d)"
trap 'rm -rf "$staging"' EXIT
cp -R "$app" "$staging/"
ln -s /Applications "$staging/Applications"
hdiutil create -volname "SSH Tunneler" -srcfolder "$staging" -ov -format UDZO "$dmg" >/dev/null

echo "Done: $dmg ($(du -h "$dmg" | cut -f1))"
