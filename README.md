# Yenscript

An embeddable, statically typed language that compiles `.ys` programs to **core WebAssembly**.
The compiler core uses F# / .NET 9; the CLI uses C# / .NET 9.

Pipeline: `.ys → Parser → type checking and lowering → WASM IR → WAT → wat2wasm → .wasm + .meta.json`.
The core never reads the filesystem or starts processes; the CLI invokes WABT.

## Example

```yenscript
using package system;
from system import *;

fn factorial(n: int): int {
    if (n <= 1) { return 1; }
    else { return n * factorial(n - 1); }
}

fn main() {
    var result: int = factorial(5);
    console:print(strings:concat("result=", convert:intToString(result)));
    for (var i: int = 0; i < 3; i = i + 1) {
        console:warning("iteration");
    }
}
```

The required entry point is `fn main()`, with no parameters and a void return type.
Package and function declarations remain at module scope; executable statements belong
inside functions. The host explicitly calls the `_start` export once; there is no WASM start section.

## Build the compiler

Use the .NET 9 SDK or a compatible newer SDK. Managed execution requires the .NET 9 runtime.

```sh
dotnet restore Yenscript.sln
dotnet build Yenscript.sln -c Release -warnaserror
dotnet test Yenscript.sln
dotnet tool restore
dotnet fantomas src/Yenscript.Core --check
dotnet fantomas tests/Yenscript.Core.Tests --check
```

## Compile a program

Install [WABT](https://github.com/WebAssembly/wabt) with `wat2wasm` on PATH.
The tested toolchain version is WABT 1.0.42. This is an external **compilation**
dependency; generated WASM modules do not require WABT or .NET at runtime.

```sh
dotnet run --project src/Yenscript.Cli -- build examples/hello.ys -o artifacts/hello.wasm
dotnet run --project src/Yenscript.Cli -- check examples/hello.ys
```

After Native AOT publishing:

```sh
yenscriptc build examples/hello.ys -o hello.wasm
yenscriptc build examples/hello.ys -o hello.wasm --wat2wasm /path/to/wat2wasm
yenscriptc check examples/hello.ys
```

Outputs: `hello.wasm`, `hello.wat`, and `hello.meta.json`.
`check` writes no files and does not require WABT. Diagnostics include a code and source position.
Exit codes: 0 for success, 1 for failure. Previous outputs are not replaced until assembly
and metadata generation succeed. The three output files are not a single atomic transaction.

## Packages

`system` is embedded; `console` is an object in that package, not a separate package.
Other `.ysp` files are loaded by the CLI or embedding application:

```yenscript
using package custom as c;
using package "../packages/tools.ysp" as tools;
using package "C:/Users/Me/Documents/host.ysp" as host;
from tools import *;
```

A named package is located beside the `.ys` file as `custom.ysp`. Relative paths are
resolved against the source file; dependencies are resolved against the package file.
Absolute paths are allowed. Restrict resolver access in the host when handling untrusted input.
A package declares a host API but does not implement it: the host links its WASM imports.

## Execution and embedding

Modules export `memory`, `alloc(i32)->i32`, `free(i32,i32)`, and `_start()`.
Three `env` functions are required: `console_print`, `console_warning`, and `console_error`.
Each accepts UTF-8 `(ptr, len)`. `console:error` does not terminate the program.
Additional imports are determined by the connected packages.

`wasmtime run hello.wasm` alone is insufficient: the standard CLI does not supply
our `env.console_*` imports. Use an embedding application or SDK. A tested Python host:

```sh
python -I -m pip install wasmtime==38.0.0
python -I scripts/run-wasm.py hello.wasm
```

The host limits fuel, does not link WASI, catches traps, and uses `.meta.json` to
report source positions. A Rust/Wasmtime example is included in [Embedding](docs/embedding.md).
The application is responsible for linking additional native functions.

Pure .NET API:

```fsharp
Core.compile source // Result<string, Diagnostic list>: WAT
Core.compileWithPackages (Map.ofList ["../packages/host.ysp", packageText]) source
```

The second API does not read files. Keys are exact references from source/dependency declarations.
The public API returns WAT text, not binary WASM disguised as a string.

## Documentation

- [Language reference](docs/language.md)
- [Packages and system](docs/packages.md)
- [ABI, source maps, and embedding](docs/embedding.md)
- [Roadmap](roadmap.md)

## Verification

Unit tests cover parsing, type checking, IR/emission, manifests, resolution, and metadata.
End-to-end fixtures are assembled by real WABT and executed by Wasmtime with bounded fuel:
conditions/loops, short-circuiting, recursion, strings, tables, native objects, external
packages, the standard library, and source-mapped traps.

```sh
python -I -m pip install wasmtime==38.0.0
bash scripts/wasm-integration.sh
bash scripts/publish.sh win-x64   # Windows with C++ Build Tools
bash scripts/publish.sh linux-x64 # Linux with clang and zlib1g-dev
```

The compiler is published with Native AOT for Windows/Linux x64. WABT is not embedded
in that executable. The WASM backend uses i32, UTF-8, and linear memory; automatic
garbage collection of unreachable strings/tables is not implemented. Detailed limitations
are documented in the ABI reference. This backend is incompatible with earlier versions.

## License

[MIT License](LICENSE). Copyright (c) 2026 CopperIron8.
WABT, Wasmtime, and NuGet dependencies retain their respective licenses.
