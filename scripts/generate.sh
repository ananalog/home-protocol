#!/usr/bin/env bash
# Regenerates all bindings from schema.yaml and re-encodes test vectors.
set -euo pipefail
cd "$(dirname "$0")/.."
dotnet run --project gen/Home.ProtoGen -- .
dotnet run --project gen/Home.ProtoVectors -- .
