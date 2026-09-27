#!/usr/bin/env bash
# Runs the test vectors against every implementation: C, C#, Kotlin.
set -euo pipefail
cd "$(dirname "$0")/.."
build=$(mktemp -d)
trap 'rm -rf "$build"' EXIT

echo "== C"
cmake -S tests/c -B "$build/c" -DCMAKE_BUILD_TYPE=Debug >/dev/null
cmake --build "$build/c" >/dev/null
"$build/c/test_vectors" testdata/vectors | tail -1

echo "== C#"
dotnet test tests/csharp/Home.Protocol.Tests --nologo -v q

echo "== Kotlin"
(cd kotlin && ./gradlew test --no-daemon -q)
echo "all implementations agree"
