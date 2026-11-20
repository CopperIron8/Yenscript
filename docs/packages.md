# Packages and standard library

`.ysp` files are native declarations and manifests, not executable plugins.
Only `system` is embedded. Other packages come from files or host-supplied text.

## Resolution

```yenscript
using package system;
using package local as l;
using package "../packages/custom.ysp" as custom;
using package "C:/Users/Me/Documents/host.ysp" as host;
from system import *;
from custom import *;
fn main() { console:print("ready"); }
```

Named `local` resolves to `local.ysp` beside the source. Explicit relative paths
resolve against the source directory. Dependencies resolve against the declaring
package's directory, not the process working directory. Absolute paths are supported.
Use forward slashes on Windows or escape backslashes inside quoted strings.

`using` registers a package; wildcard `from` exposes its declarations. An alias
replaces the package's source-visible name. `from` must follow its `using`.
Bindings are per source file. Only wildcard imports are supported.

The resolver rejects cycles, missing files, conflicting references, and invalid
manifests. The core validates missing/cyclic dependencies and exact version matches
also for embedding callers. Version constraints are **exact strings**, not semver
ranges or a package manager. A dependency is loaded for type checking/linking, not
automatically wildcard-imported into source scope. Files sharing a manifest name
must have identical declarations/version; conflicting definitions are rejected.

## Manifest and host declarations

```text
package custom;
version "1.0.0";
requires "base.ysp" version "1.0.0";

native function add(left: int, right: int): int @host "host" "add";
native object output {
    method write(message: string): void @host "host" "write";
}
native enum Mode { First; Second; }
native type Counter {
    property Value: int @get "host" "counter_get" @set "host" "counter_set";
    method Add(amount: int): int @host "host" "counter_add";
    static Default: Counter @host "host" "counter_default";
}
native function create(): Counter @host "host" "counter_create";
```

- Package header and version are required; dependencies precede native declarations.
- `@host "module" "name"` identifies a WASM import, not an F# implementation.
- Functions and objects use the listed parameters.
- Instance methods receive an implicit first i32 handle parameter.
- Property getters take the handle; setters take handle then value. Omit `@set`
  for read-only properties. Properties are backed by the host, not compiler memory fields.
- Static members are zero-argument host getters.
- Enum cases are distinct typed constants numbered from zero.
- String host arguments expand to `(ptr, len)`. String returns are a descriptor
  reference. Table/native values are i32 references/handles. See [ABI](embedding.md).
- Opaque type declarations may have empty bodies. User-defined classes or generic
  package types are not implemented. `table<T>` is a built-in type constructor.
- `system` and module namespace `__yenscript` are reserved.

At instantiation the embedder must provide the declared host imports, including
imports of resolved dependencies even when not called by the entry function.
Do not declare operating-system capabilities unless the host intends to grant them.

## Built-in system

```yenscript
using package system;
from system import *;
fn main() {
    console:print(strings:concat("answer=", convert:intToString(math:pow(2, 5))));
    console:warning("warning");
    console:error("error message, not a trap");
}
```

### console

| Method | Signature | Implementation |
| --- | --- | --- |
| print | (message: string): void | env.console_print(ptr,len) |
| warning | (message: string): void | env.console_warning(ptr,len) |
| error | (message: string): void | env.console_error(ptr,len) |

These three functions are supplied by the host. Error logging does not abort the program.
`console` is an object in `system`, not a standalone package.

### math

All methods take and return int and execute in generated WASM.

- `abs(value)` - absolute value; minimum signed i32 traps.
- `min(a,b)`, `max(a,b)` - signed comparison.
- `clamp(value,low,high)` - traps if low > high.
- `pow(value,exponent)` - integer exponentiation; negative exponent traps;
  overflow wraps as i32, and exponent zero returns 1.

### strings

- `byteLength(value: string): int` - UTF-8 byte length, not codepoint count.
- `concat(left: string,right: string): string` - allocates a new immutable string.
- `value.Length` provides the same byte length.

### convert

- `intToString(value: int): string` - signed decimal, including -2147483648.
- `boolToString(value: bool): string` - `"true"` or `"false"`.

### table<T>

A built-in constructor, not an external generic package declaration.

- `values.Length`: element count.
- `values:Get(index: int): T`.
- `values:Set(index: int,value: T): void`.

Accesses are bounds-checked and null-checked. No resize, append, automatic GC,
or keyed-table API is claimed.

## Extending the host

The standard package intentionally has no filesystem, network, process, clock,
or random capability. Declare such APIs in an external `.ysp` and link them in
an embedding application. A package file alone does not implement its functions.
