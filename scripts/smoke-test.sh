#!/usr/bin/env bash
set -euo pipefail
root="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
if [[ $# != 1 ]]; then printf 'Usage: bash scripts/smoke-test.sh <binary>\n' >&2; exit 1; fi
binary="$(cd "$(dirname "$1")" && pwd)/$(basename "$1")"
work="$(mktemp -d)"
trap 'rm -rf "$work"' EXIT
assembler="${WAT2WASM:-wat2wasm}"
"$binary" --help
for name in hello arithmetic tables; do
  "$binary" check "$root/examples/$name.ys"
  "$binary" build "$root/examples/$name.ys" -o "$work/$name.wasm" --wat2wasm "$assembler"
  test -s "$work/$name.wasm"
  test -s "$work/$name.wat"
  test -s "$work/$name.meta.json"
done
printf 'var broken: int = 1;\n' > "$work/invalid.ys"
if "$binary" check "$work/invalid.ys" > "$work/stdout" 2> "$work/stderr"; then
  printf 'Invalid top-level source unexpectedly passed\n' >&2; exit 1
fi
printf 'Native compiler smoke test passed.\n'
