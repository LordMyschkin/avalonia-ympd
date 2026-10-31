#!/bin/sh
# Build a self-contained Windows x64 executable from macOS or Linux.
set -eu

repo="$(CDPATH= cd -- "$(dirname -- "$0")" && pwd -P)"
project="$repo/ModernYmpd/ModernYmpd.csproj"
dist="$repo/dist"
dotnet="${DOTNET:-dotnet}"

"$dotnet" restore "$project" -r win-x64 --source https://api.nuget.org/v3/index.json
"$dotnet" publish "$project" -c Release -r win-x64 --self-contained true \
    --no-restore -o "$dist/publish-win-x64" \
    -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true \
    -p:EnableCompressionInSingleFile=true -p:DebugType=none -p:DebugSymbols=false
cp "$dist/publish-win-x64/ModernYmpd.exe" "$dist/Avalonia-YMPD-win-x64.exe"
echo "Built: $dist/Avalonia-YMPD-win-x64.exe"
