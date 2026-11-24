#!/bin/sh
# Builds the core for Linux: bin/libe500core.so
set -e
cd "$(dirname "$0")/.."
mkdir -p bin
"${CXX:-g++}" -std=c++17 -O2 -fPIC -shared -fvisibility=hidden src/native/sc62015.cpp src/native/machine.cpp src/native/ce140f.cpp src/native/ce126p.cpp -static-libstdc++ -static-libgcc -o bin/libe500core.so
echo "bin/libe500core.so"
