#!/bin/sh
# Builds the core for Linux (bin/libe500core.so) or macOS (bin/libe500core.dylib, for Apple silicon and Intel in one).
set -e
cd "$(dirname "$0")/.."
mkdir -p bin
if [ "$(uname)" = Darwin ]; then
    "${CXX:-clang++}" -std=c++17 -O2 -fPIC -dynamiclib -fvisibility=hidden -arch arm64 -arch x86_64 -mmacosx-version-min=14.0 -install_name @rpath/libe500core.dylib src/native/sc62015.cpp src/native/machine.cpp src/native/ce140f.cpp src/native/ce126p.cpp -o bin/libe500core.dylib
    echo "bin/libe500core.dylib"
    exit
fi
"${CXX:-g++}" -std=c++17 -O2 -fPIC -shared -fvisibility=hidden src/native/sc62015.cpp src/native/machine.cpp src/native/ce140f.cpp src/native/ce126p.cpp -static-libstdc++ -static-libgcc -o bin/libe500core.so
echo "bin/libe500core.so"
