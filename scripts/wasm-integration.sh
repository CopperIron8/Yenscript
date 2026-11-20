#!/usr/bin/env bash
set -euo pipefail
root="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
cd "$root"
assembler="${WAT2WASM:-wat2wasm}"
python="${WASM_PYTHON:-python}"
dotnet build Yenscript.sln -c Release -warnaserror
out="$root/artifacts/wasm-integration"
mkdir -p "$out"
for name in control scopes functions strings tables stdlib external objects trap bounds null; do
  dotnet src/Yenscript.Cli/bin/Release/net9.0/yenscriptc.dll build \
    "tests/Yenscript.Core.Tests/Fixtures/Wasm/$name.ys" -o "$out/$name.wasm" --wat2wasm "$assembler"
  if command -v "${WASM_TOOLS:-wasm-tools}" >/dev/null 2>&1; then
    "${WASM_TOOLS:-wasm-tools}" validate "$out/$name.wasm"
  else
    printf 'wasm-tools unavailable; independent validation skipped (Wasmtime still validates).\n' >&2
  fi
  "$python" -I scripts/wasm-runtime-test.py "$out/$name.wasm"
done
printf 'All WebAssembly runtime fixtures passed.\n'
