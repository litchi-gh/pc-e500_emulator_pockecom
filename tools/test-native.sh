#!/bin/sh
# Builds and runs the core's tests on Linux and macOS.
set -e
cd "$(dirname "$0")/.."
out=$(mktemp -d)
trap 'rm -rf "$out"' EXIT
"${CXX:-g++}" -std=c++17 -O2 src/native/sc62015.cpp src/native/machine.cpp src/native/ce140f.cpp src/native/ce126p.cpp tests/test_core.cpp -o "$out/test-core"
(cd "$out" && ./test-core)
"${CXX:-g++}" -std=c++17 -O2 tests/test_cassette.cpp -o "$out/test-cassette"
(cd "$out" && ./test-cassette)
