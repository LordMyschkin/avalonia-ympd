#!/bin/sh
# Build a self-contained macOS .app bundle for the Avalonia desktop client.
set -eu

repo="$(CDPATH= cd -- "$(dirname -- "$0")" && pwd -P)"
project="$repo/ModernYmpd/ModernYmpd.csproj"
dist="$repo/dist"
dotnet="${DOTNET:-dotnet}"
case "$(uname -m)" in
    arm64) runtime=osx-arm64; architecture=arm64 ;;
    x86_64) runtime=osx-x64; architecture=x86_64 ;;
    *) echo "Unsupported macOS architecture." >&2; exit 1 ;;
esac

publish="$dist/publish-$runtime"
app="$dist/A-mpd.app"
if pgrep -f "$app/Contents/MacOS/A-mpd" >/dev/null 2>&1; then
    echo "Close $app before rebuilding it; replacing a running app can crash .NET." >&2
    exit 1
fi
mkdir -p "$publish" "$app/Contents/MacOS" "$app/Contents/Resources"
"$dotnet" restore "$project" -r "$runtime" --source https://api.nuget.org/v3/index.json
"$dotnet" publish "$project" -c Release -r "$runtime" --self-contained true \
    --no-restore -o "$publish" -p:UseAppHost=true
cp -R "$publish/." "$app/Contents/MacOS/"
mv "$app/Contents/MacOS/ModernYmpd" "$app/Contents/MacOS/A-mpd"
cp "$repo/ModernYmpd/Assets/app.icns" "$app/Contents/Resources/app.icns"
xcrun clang -arch "$architecture" -fobjc-arc -fblocks -dynamiclib \
    -install_name @rpath/libModernYmpdMediaKeys.dylib \
    -framework Foundation -framework MediaPlayer \
    -o "$app/Contents/MacOS/libModernYmpdMediaKeys.dylib" \
    "$repo/ModernYmpd/MacMediaKeys.m"
cat > "$app/Contents/Info.plist" <<PLIST
<?xml version="1.0" encoding="UTF-8"?>
<!DOCTYPE plist PUBLIC "-//Apple//DTD PLIST 1.0//EN" "http://www.apple.com/DTDs/PropertyList-1.0.dtd">
<plist version="1.0"><dict>
  <key>CFBundleName</key><string>A-mpd</string>
  <key>CFBundleDisplayName</key><string>A-mpd</string>
  <key>CFBundleIdentifier</key><string>org.lordmyschkin.ampd</string>
  <key>CFBundleVersion</key><string>12</string>
  <key>CFBundleShortVersionString</key><string>0.2.10</string>
  <key>CFBundlePackageType</key><string>APPL</string>
  <key>CFBundleExecutable</key><string>A-mpd</string>
  <key>CFBundleIconFile</key><string>app.icns</string>
  <key>LSApplicationCategoryType</key><string>public.app-category.music</string>
  <key>NSHighResolutionCapable</key><true/>
</dict></plist>
PLIST
plutil -lint "$app/Contents/Info.plist"
codesign --force --deep --sign - "$app"
codesign --verify --deep --strict "$app"
echo "Built: $app"
