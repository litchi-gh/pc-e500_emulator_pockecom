#!/bin/sh
# Builds pokecom (the command line and MCP server) for macOS into dist/, for Apple silicon and Intel.
# Run on macOS with the .NET 10 SDK and Xcode's command line tools.
# dist/ に macOS 用の pokecom（コマンドラインと MCP サーバー）を作る。macOS で、.NET 10 SDK と Xcode のコマンドラインツールを使う。
set -e
cd "$(dirname "$0")/.."
export DOTNET_CLI_TELEMETRY_OPTOUT=1
VERSION=$(sed -n 's/.*<Version>\(.*\)<\/Version>.*/\1/p' src/cli/*.csproj | tr -d '\r')
WORK=$(mktemp -d)
trap 'rm -rf "$WORK"' EXIT
mkdir -p dist
sh tools/build-native.sh
for rid in osx-arm64 osx-x64; do
    NAME=pokecom-$VERSION-$rid
    dotnet publish src/cli/*.csproj -c Release -r $rid --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=false -p:DebugType=none -o "$WORK/$rid" --nologo -v quiet
    OUT=$WORK/$NAME
    mkdir -p "$OUT/rom"
    cp "$WORK/$rid/pokecom" "$WORK/$rid/libe500core.dylib" LICENSE THIRD_PARTY_NOTICES.md third_party/dotnet/DOTNET-*.txt "$OUT/"
    sed "s/@VERSION@/$VERSION/" packaging/macos/README.txt > "$OUT/README.txt"
    chmod 755 "$OUT/pokecom"
    # Signed ad hoc, without an Apple developer's name: Apple silicon runs nothing unsigned.
    codesign --force --sign - "$OUT/libe500core.dylib"
    codesign --force --sign - "$OUT/pokecom"
    tar czf "dist/$NAME.tar.gz" -C "$WORK" "$NAME"
    ls -l "dist/$NAME.tar.gz"
done
