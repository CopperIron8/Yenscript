"""Minimal Wasmtime embedder: python -I scripts/run-wasm.py program.wasm.
Requires wasmtime==38.0.0. No WASI imports or OS permissions are linked.
"""
import hashlib
import json
from pathlib import Path
import sys
import wasmtime


def run(filename):
    path = Path(filename)
    binary = path.read_bytes()
    metadata_path = path.with_suffix(".meta.json")
    metadata = json.loads(metadata_path.read_text()) if metadata_path.exists() else None
    if metadata and metadata["wasmSha256"] != hashlib.sha256(binary).hexdigest():
        raise ValueError("Stale source metadata: binary checksum mismatch")
    config = wasmtime.Config()
    config.consume_fuel = True
    engine = wasmtime.Engine(config)
    store = wasmtime.Store(engine)
    store.set_fuel(1_000_000)
    store.set_limits(memory_size=64 * 1024 * 1024)
    linker = wasmtime.Linker(engine)
    for level in ("print", "warning", "error"):
        def callback(caller, ptr, length, level=level):
            memory = caller.get("memory")
            if ptr < 0 or length < 0 or ptr + length > memory.data_len(caller):
                raise wasmtime.Trap("Invalid console string range")
            text = bytes(memory.read(caller, ptr, ptr + length)).decode("utf-8")
            print(text if level == "print" else level + ": " + text,
                  file=sys.stdout if level == "print" else sys.stderr)
        linker.define_func("env", "console_" + level,
                           wasmtime.FuncType([wasmtime.ValType.i32(), wasmtime.ValType.i32()], []),
                           callback, access_caller=True)
    module = wasmtime.Module(engine, binary)
    instance = linker.instantiate(store, module)
    try:
        instance.exports(store)["_start"](store)
    except wasmtime.Trap as trap:
        print("WASM trap:", trap, file=sys.stderr)
        if metadata:
            for frame in trap.frames:
                locations = [m for m in metadata["mappings"]
                             if m["functionIndex"] == frame.func_index and m["offset"] == frame.module_offset]
                if locations:
                    location = locations[0]
                    print(f"  --> {metadata['source']}:{location['line']}:{location['column']}", file=sys.stderr)
                    break
        return 1
    return 0


if __name__ == "__main__":
    if len(sys.argv) != 2:
        raise SystemExit("Usage: python -I scripts/run-wasm.py program.wasm")
    try:
        raise SystemExit(run(sys.argv[1]))
    except (OSError, ValueError, wasmtime.WasmtimeError) as error:
        print("Host error:", error, file=sys.stderr)
        raise SystemExit(1)
