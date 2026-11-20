"""Run a compiled WASM fixture with bounded fuel and no WASI permissions.
Development dependency: wasmtime==38.0.0. Arguments: path/to/fixture.wasm.
"""
import hashlib
import json
from pathlib import Path
import sys
import wasmtime


def run(filename):
    path = Path(filename)
    config = wasmtime.Config()
    config.consume_fuel = True
    engine = wasmtime.Engine(config)
    store = wasmtime.Store(engine)
    store.set_fuel(1_000_000)
    linker = wasmtime.Linker(engine)
    messages = []
    def read(caller, pointer, length):
        memory = caller.get("memory")
        if pointer < 0 or length < 0 or pointer + length > memory.data_len(caller):
            raise wasmtime.Trap("Invalid host string range")
        return bytes(memory.read(caller, pointer, pointer + length)).decode("utf-8")
    for level in ("print", "warning", "error"):
        def callback(caller, pointer, length, level=level):
            messages.append((level, read(caller, pointer, length)))
        signature = wasmtime.FuncType([wasmtime.ValType.i32(), wasmtime.ValType.i32()], [])
        linker.define_func("env", "console_" + level, signature, callback, access_caller=True)
    i32 = wasmtime.ValType.i32()
    if path.stem == "external":
        linker.define_func("host", "add", wasmtime.FuncType([i32, i32], [i32]), lambda a,b:a+b)
        linker.define_func("host", "identity", wasmtime.FuncType([i32], [i32]), lambda a:a)
        linker.define_func("host", "record", wasmtime.FuncType([i32,i32], []),
                           lambda caller,p,n:messages.append(("record",read(caller,p,n))), access_caller=True)
    if path.stem == "objects":
        counters = {1:0}
        linker.define_func("host", "counter_create", wasmtime.FuncType([], [i32]), lambda:1)
        linker.define_func("host", "counter_default", wasmtime.FuncType([], [i32]), lambda:1)
        linker.define_func("host", "counter_get", wasmtime.FuncType([i32], [i32]), lambda handle:counters[handle])
        linker.define_func("host", "counter_set", wasmtime.FuncType([i32,i32], []), lambda h,v:counters.__setitem__(h,v))
        def add(handle, amount):
            counters[handle] += amount
            return counters[handle]
        linker.define_func("host", "counter_add", wasmtime.FuncType([i32,i32], [i32]), add)
    module = wasmtime.Module.from_file(engine, str(path))
    instance = linker.instantiate(store, module)
    assert not messages, "Instantiation must not execute main"
    meta = json.loads(path.with_suffix(".meta.json").read_text())
    assert hashlib.sha256(path.read_bytes()).hexdigest() == meta["wasmSha256"]
    trap_lines = {"trap": 2, "bounds": 3, "null": 3}
    trapped = False
    try:
        instance.exports(store)["_start"](store)
    except wasmtime.Trap as trap:
        if path.stem not in trap_lines:
            raise
        trapped = True
        locations = []
        for frame in trap.frames:
            locations = [m for m in meta["mappings"] if m["functionIndex"] == frame.func_index and m["offset"] == frame.module_offset]
            if locations:
                break
        assert locations and locations[0]["line"] == trap_lines[path.stem], locations
        print("Trap mapped to source line", locations[0]["line"], "at binary offset", frame.module_offset)
    if path.stem in trap_lines:
        assert trapped, "Expected integer division trap"
    else:
        expected = {
            "control": [("print", "precedence-ok")] + [("print", "loop")] * 3 + [("print", "grouping-ok")],
            "scopes": [("print", "short-circuit-ok"), ("print", "for-ok"), ("print", "scope-ok")],
            "functions": [("print", "functions-ok")],
            "strings": [("print", "Hello, 世界"), ("warning", "changed"), ("error", "")],
            "tables": [("print", "tables-ok")],
            "external": [("print", "external-ok"), ("record", "host UTF-8: 世界")],
            "objects": [("print", "objects-ok")],
            "stdlib": [("print", "value=32"), ("warning", "-2147483648"), ("error", "false"), ("print", "true"), ("print", "utf8-ok")],
        }[path.stem]
        assert messages == expected, messages
    exports = instance.exports(store)
    pointer = exports["alloc"](store, 70_000)
    assert pointer % 8 == 0
    exports["memory"].write(store, b"host-roundtrip", pointer)
    assert bytes(exports["memory"].read(store, pointer, pointer + 14)) == b"host-roundtrip"
    exports["free"](store, pointer, 70_000)
    assert exports["alloc"](store, 70_000) == pointer
    first = exports["alloc"](store,32)
    second = exports["alloc"](store,32)
    exports["free"](store,first,32)
    assert exports["alloc"](store,24) == first
    for args in [(-1,), (2147483647,)]:
        try:
            exports["alloc"](store,*args)
        except wasmtime.Trap:
            pass
        else:
            raise AssertionError("Invalid allocation did not trap")
    try:
        exports["free"](store,second,8)
    except wasmtime.Trap:
        pass
    else:
        raise AssertionError("Mismatched free size did not trap")
    assert exports["alloc"](store,8)>0
    print(path.stem + ": WASM runtime integration passed")


if __name__ == "__main__":
    if len(sys.argv) != 2:
        raise SystemExit("Usage: python -I scripts/wasm-runtime-test.py fixture.wasm")
    run(sys.argv[1])
