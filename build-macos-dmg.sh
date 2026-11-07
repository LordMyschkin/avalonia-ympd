#!/bin/sh
# Package the self-contained macOS app and an Applications shortcut in a DMG.
set -eu

repo="$(CDPATH= cd -- "$(dirname -- "$0")" && pwd -P)"
dist="$repo/dist"
staging="$dist/dmg-staging"
rm -rf "$staging"
mkdir -p "$staging"
APP_OUTPUT="$staging/A-mpd.app" "$repo/build-macos-app.sh"
if [ ! -e "$staging/Applications" ]; then
    ln -s /Applications "$staging/Applications"
fi

case "$(uname -m)" in
    arm64) architecture=arm64 ;;
    x86_64) architecture=x64 ;;
    *) echo "Unsupported macOS architecture." >&2; exit 1 ;;
esac
image="$dist/A-mpd-macos-$architecture.dmg"
hdiutil create -volname 'A-mpd' -srcfolder "$staging" -ov -format UDZO "$image"
hdiutil verify "$image"
rm -rf "$staging"
echo "Built: $image"
