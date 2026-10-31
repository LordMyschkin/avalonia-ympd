#!/bin/sh
# Package the self-contained macOS app and an Applications shortcut in a DMG.
set -eu

repo="$(CDPATH= cd -- "$(dirname -- "$0")" && pwd -P)"
dist="$repo/dist"
"$repo/build-macos-app.sh"

staging="$dist/dmg-staging"
mkdir -p "$staging"
ditto "$dist/Avalonia YMPD.app" "$staging/Avalonia YMPD.app"
if [ ! -e "$staging/Applications" ]; then
    ln -s /Applications "$staging/Applications"
fi

case "$(uname -m)" in
    arm64) architecture=arm64 ;;
    x86_64) architecture=x64 ;;
    *) echo "Unsupported macOS architecture." >&2; exit 1 ;;
esac
image="$dist/Avalonia-YMPD-macos-$architecture.dmg"
hdiutil create -volname 'Avalonia YMPD' -srcfolder "$staging" -ov -format UDZO "$image"
hdiutil verify "$image"
echo "Built: $image"
