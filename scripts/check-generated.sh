#!/usr/bin/env bash
# Fails if generated files are out of date with schema.yaml (used in CI).
set -euo pipefail
cd "$(dirname "$0")/.."
./scripts/generate.sh >/dev/null
if ! git diff --quiet -- c csharp kotlin ts tests/c testdata; then
    echo "generated code is out of date: run scripts/generate.sh and commit" >&2
    git diff --stat -- c csharp kotlin ts tests/c testdata >&2
    exit 1
fi
echo "generated code is up to date"
