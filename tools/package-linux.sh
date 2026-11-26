#!/bin/sh
# Builds the Linux packages (tar.gz and .deb) into dist/. Run on Ubuntu x86-64 with the .NET 10 SDK and g++.
# dist/ に Linux 用の配布物（tar.gz と .deb）を作る。Ubuntu（x86-64）で、.NET 10 SDK と g++ を使う。
set -e
cd "$(dirname "$0")/.."
export AVALONIA_TELEMETRY_OPTOUT=1 DOTNET_CLI_TELEMETRY_OPTOUT=1
VERSION=$(sed -n 's/.*<Version>\(.*\)<\/Version>.*/\1/p' src/avalonia/PcE500.Avalonia.csproj | tr -d '\r')
NAME=PC-E500-Emulator-$VERSION-linux-x64
WORK=$(mktemp -d)
trap 'rm -rf "$WORK"' EXIT
sh tools/build-native.sh
for project in src/avalonia/*.csproj src/cli/*.csproj; do
    dotnet publish "$project" -c Release -r linux-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=false -p:DebugType=none -o "$WORK/out" --nologo -v quiet
done
OUT=$WORK/$NAME
mkdir -p "$OUT/manual" dist
for f in PC-E500 pokecom libe500core.so libSkiaSharp.so libHarfBuzzSharp.so; do cp "$WORK/out/$f" "$OUT/"; done
cp src/avalonia/pc-e500.png LICENSE docs/THIRD_PARTY_NOTICES.md third_party/dotnet/DOTNET-*.txt third_party/avalonia/*.txt "$OUT/"
sed "s/@VERSION@/$VERSION/" packaging/linux/README.txt > "$OUT/README.txt"
cp -r app/manual/*.html app/manual/images app/manual/images-en "$OUT/manual/"
mkdir "$OUT/rom" "$OUT/ram" "$OUT/drive-x"
find "$OUT" -type d -exec chmod 755 {} +
find "$OUT" -type f -exec chmod 644 {} +
chmod 755 "$OUT/PC-E500" "$OUT/pokecom"
tar czf "dist/$NAME.tar.gz" --owner=0 --group=0 -C "$WORK" "$NAME"
# The .deb: the program in /opt, its data per user in ~/.local/share/PockeComE500 (told by the marker).
DEB=$WORK/deb
mkdir -p "$DEB/DEBIAN" "$DEB/opt/pc-e500" "$DEB/usr/bin" "$DEB/usr/share/applications" "$DEB/usr/share/icons/hicolor/256x256/apps"
cp -r "$OUT/." "$DEB/opt/pc-e500/"
rmdir "$DEB/opt/pc-e500/rom" "$DEB/opt/pc-e500/ram" "$DEB/opt/pc-e500/drive-x"
cp packaging/linux/PockeComE500.installed "$DEB/opt/pc-e500/"
ln -s /opt/pc-e500/PC-E500 "$DEB/usr/bin/pc-e500"
ln -s /opt/pc-e500/pokecom "$DEB/usr/bin/pokecom"
cp packaging/linux/pc-e500.desktop "$DEB/usr/share/applications/"
cp src/avalonia/pc-e500.png "$DEB/usr/share/icons/hicolor/256x256/apps/"
sed -e "s/@VERSION@/$VERSION/" -e "s/@SIZE@/$(du -sk "$DEB/opt" | cut -f1)/" packaging/linux/control > "$DEB/DEBIAN/control"
find "$DEB" -type d -exec chmod 755 {} +
chmod 644 "$DEB/DEBIAN/control" "$DEB"/usr/share/applications/* "$DEB"/usr/share/icons/hicolor/256x256/apps/* "$DEB/opt/pc-e500/PockeComE500.installed"
dpkg-deb --root-owner-group -Zxz --build "$DEB" "dist/pc-e500-emulator_${VERSION}_amd64.deb" >/dev/null
ls -l "dist/$NAME.tar.gz" "dist/pc-e500-emulator_${VERSION}_amd64.deb"
