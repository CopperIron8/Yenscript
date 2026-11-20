# WebAssembly ABI and embedding

The output is core WebAssembly v1, not a component and not a WASI executable.
The compiler runs as a .NET library or CLI; it is not itself shipped as WASM.
WAT is assembled by external WABT `wat2wasm`. The emitter never writes binary opcodes.

## Module contract

Exports:

- `_start() -> ()`: calls the source `main`. The host invokes it once.
- `memory`: linear memory, initially large enough for static literals.
- `alloc(size: i32) -> i32`: returns an eight-byte-aligned allocation.
- `free(ptr: i32, size: i32)`: releases exactly that allocation.

There is no WASM start section. Instantiation does not execute source code.
The source `main` takes no arguments and returns void.

Imports normally include `env.console_print`, `env.console_warning`,
`env.console_error`, each `(i32 ptr, i32 byteLength) -> ()`.
All declarations in resolved packages are linked as imports, not executable plugins.
Portable system math/string/conversion routines do not import host functions.

## Value layout

All scalar slots and references are i32, stored little-endian.

| Value | ABI |
| --- | --- |
| int | signed i32 |
| bool | i32 0/1; the host must return canonical values |
| string inside generated code | pointer to an eight-byte descriptor |
| string argument to external host | two arguments: payload ptr, byte length |
| string returned from host | one i32 pointer to a string descriptor |
| table | i32 pointer to header plus slots |
| native object | opaque i32 handle owned/validated by the host; 0 means null |
| enum | i32 ordinal; host must respect its declaration |

String descriptor: `[payloadPointer: u32, byteLength: u32]`. Literals and their
immutable bytes reside in data segments. A host returning a string uses `alloc`
to allocate descriptor + UTF-8 bytes, writes the descriptor and payload, and returns
the descriptor pointer. Never free a returned string while generated code can use it.
Strings are immutable by language convention, not protected memory regions.

Table layout: `[elementCount: u32, stride: u32=4, elements...]`.
Each element occupies four bytes: an integer, boolean, enum ordinal, native handle,
or reference to a string/nested table. Tables are mutable; aliases share contents.
`Get`/`Set` trap for null or invalid indices. Length of a string is its byte length.

## Allocation and ownership

`alloc` grows memory as needed. A private 16-byte header precedes each returned
payload. A first-fit free list reuses freed blocks without splitting/coalescing.
Negative/overflowing sizes and failed memory growth trap. Zero-size allocation
still gets an aligned block. `free` checks alignment, recorded size and live state;
wrong sizes and double free trap. Pass the original requested size, not capacity.

The host may `alloc`, write bytes, read results, and `free` buffers it owns.
Do not free data-segment literals. Do not retain a pointer after free or free an
object still referenced by source code. Opaque handles belong to host-side storage,
not this allocator. Invalid native handles must be rejected by the host.

There is **no garbage collector**: dynamic strings/tables remain live until freed
by their owner or the instance is discarded. Repeated allocation in loops can
exhaust memory; embeddings should limit memory, fuel, and instance lifetime.
No allocator can make arbitrary host writes safe: a host can corrupt module memory.

## Trap containment

Integer division, invalid table access, null dereference, invalid allocation,
stack exhaustion and fuel exhaustion may trap. Catch the runtime's trap/error at
the host boundary, log it, and decide whether to discard or reuse the instance.
A trap is not a process exit, but it also does not roll back preceding mutations.
Do not automatically rerun `_start` after a partially executed program.
`console:error` is only a message, not a trap.

`scripts/run-wasm.py` is an executable Python/Wasmtime example with bounded fuel,
checked UTF-8 ranges, no WASI filesystem/network grants, and source-map reporting.
`scripts/wasm-runtime-test.py` is the fixture assertion harness, not a general runner.

## Source metadata

`name.meta.json` is generated after successful WABT assembly and contains:

```json
{
  "version": 1,
  "source": "/absolute/path/main.ys",
  "sourceSha256": "...",
  "wasmSha256": "...",
  "offsetConvention": "absolute-byte-offset-and-function-body-relative-offset",
  "mappings": [
    { "functionIndex": 7, "offset": 400, "functionOffset": 8, "line": 2, "column": 5 }
  ]
}
```

Offsets are read from the **actual assembled code section**. The compiler emits
non-optimizing nop markers for statement starts; WABT's ordinary assembly preserves
them. Metadata generation checks marker counts and decodes instruction boundaries.
Do not strip/optimize/rewrite the binary without regenerating maps.

`offset` is absolute from the start of the `.wasm` file; `functionOffset` is relative
to the beginning of the function body, including local declarations. Function index
includes imported functions. Match the conventions of your runtime's stack frames.
Use the binary SHA256 to reject stale metadata. The map is statement-level, not
expression-level. Internal helper frames have no source location: walk caller frames
until a mapped instruction is found. Source paths are local paths; do not publish
metadata containing private paths without considering disclosure.

## .NET embedding

Reference `Yenscript.Core` and call `Core.compile` or `Core.compileWithPackages`.
Both return `Result<string, Diagnostic list>` containing WAT, not a pretend binary.
The host resolves dependency files, supplies package text keyed by exact reference,
invokes WABT, and instantiates WASM with a runtime of its choice.
`WasmPackages.parse` exposes the manifest parser without file I/O.
No new runtime NuGet dependency is forced on embedding applications.

## Rust / Wasmtime example

The following host illustrates Wasmtime 38's typed API; install the SDK separately.
Unlike `wasmtime run`, it links the required custom imports. Review limits for your
application. This Rust snippet is documentation, not a Rust rewrite of the compiler.

```toml
[dependencies]
anyhow = "1"
wasmtime = "38"
```

```rust
use wasmtime::{Caller, Config, Engine, Linker, Module, Store};

fn main() -> anyhow::Result<()> {
    let mut config = Config::new();
    config.consume_fuel(true);
    let engine = Engine::new(&config)?;
    let module = Module::from_file(&engine, "hello.wasm")?;
    let mut linker = Linker::new(&engine);
    for name in ["console_print", "console_warning", "console_error"] {
        linker.func_wrap("env", name,
            move |mut caller: Caller<'_, ()>, ptr: i32, len: i32| -> anyhow::Result<()> {
                let memory = caller.get_export("memory")
                    .and_then(|e| e.into_memory())
                    .ok_or_else(|| anyhow::anyhow!("missing memory"))?;
                let start = usize::try_from(ptr)?;
                let length = usize::try_from(len)?;
                let end = start.checked_add(length)
                    .ok_or_else(|| anyhow::anyhow!("invalid range"))?;
                let bytes = memory.data(&caller).get(start..end)
                    .ok_or_else(|| anyhow::anyhow!("out-of-bounds string"))?;
                println!("{name}: {}", std::str::from_utf8(bytes)?);
                Ok(())
            })?;
    }
    let mut store = Store::new(&engine, ());
    store.set_fuel(1_000_000)?;
    let instance = linker.instantiate(&mut store, &module)?;
    let start = instance.get_typed_func::<(), ()>(&mut store, "_start")?;
    if let Err(trap) = start.call(&mut store, ()) {
        eprintln!("execution trapped: {trap:#}");
    }
    Ok(())
}
```

The Python host and generated fixtures are executed by the repository's integration
checks. The Rust example is illustrative unless independently compiled in your toolchain.
