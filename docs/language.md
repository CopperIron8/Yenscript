# Yenscript language reference

Yenscript compiles typed source to core WebAssembly through WAT and WABT.
[Packages](packages.md) describes native APIs; [Embedding](embedding.md) describes the ABI.

## Program structure

```yenscript
using package system;
from system import *;

fn twice(value: int): int { return value * 2; }
fn main() {
    var value: int = twice(21);
    console:print(convert:intToString(value));
}
```

Exactly one `fn main()` is required. It takes no arguments and returns void;
`: void` may be explicit or omitted. Executable top-level statements are rejected.
Imports and other function declarations remain at module scope. Functions execute
at runtime, support forward calls and recursion, and have fresh locals per call.
Only `_start`, `memory`, `alloc`, and `free` are exported; other functions are internal.
A host must invoke `_start` once. There is no implicit WASM start section.

## Lexical rules

Names are case-sensitive. Identifiers start with a letter or `_`, then letters,
digits or `_`. Keywords cannot be variable/function names.
`//` line comments and `/* ... */` block comments are supported.
Statements end with `;`. Function/control-flow blocks end with `}` without `;`.

## Packages and imports

```yenscript
using package system;
using package tools as t;
using package "../packages/host.ysp" as host;
using package "C:/Users/Me/Documents/other.ysp" as other;
from system import *;
from host import *;
```

`using package` adds a package under its declared alias or manifest name.
`from alias import *;` imports unqualified declarations from an already added package.
Use the alias if one is set. Named imports and empty `import ;` are rejected.
Each file is its own compilation unit; imports do not leak between files.
Use quoted paths; parenthesized quoted paths are also accepted.
Paths are resolved by the CLI or embedding host, never by the pure compiler core.

## Types and values

| Type | Representation | Default |
| --- | --- | --- |
| int | signed i32 | 0 |
| bool | i32, canonical 0/1 inside generated code | false |
| string | immutable UTF-8 descriptor reference | empty string |
| void | no value | invalid for locals/parameters/table elements |
| table<T> | mutable descriptor/reference collection | null |
| Native package type | opaque i32 host handle | null (handle 0) |
| Package enum | distinct i32 type; case ordinals from declaration order | first case |

```yenscript
fn main() {
    var n: int = 2;
    var message: string;
    var values: table<int> = {1, 2, 3};
    var nested: table<table<int>> = {{1}, {2, 3}};
}
```

Type annotations are mandatory; `var n = 2;` is invalid. `null` is accepted only
for tables and opaque native types, not int/bool/string/enums. There are no implicit
numeric/string conversions. Package types must be qualified or wildcard-imported.
Handles of different native types and distinct enum types are not interchangeable.

## Operators

Precedence, from low to high:

1. `||`
2. `&&`
3. `==`, `!=`
4. `<`, `>`, `<=`, `>=`
5. `+`, `-`
6. `*`, `/`, `%`
7. Unary `!`, `-`
8. Calls and member access

Parentheses group expressions. `&&` and `||` require bool and short-circuit:
`false && (1 / 0 == 0)` does not divide at runtime.
Arithmetic operates on int; ordering comparisons require int.
Int addition/subtraction/multiplication wrap as i32. Division by zero and signed
division overflow trap. `math:abs(-2147483648)` traps rather than silently lying.
String arithmetic/equality and structural table equality are not implemented.
Use `strings:concat` for concatenation. Enum/handle equality compares i32 values
with static type checking. Host-produced bool values must be 0 or 1.

String escapes: `\"`, `\\`, `\n`, `\r`, `\t`. Literal raw newlines are rejected.
UTF-8 length is measured in bytes, not Unicode codepoints.

## Control flow and scopes

```yenscript
fn main() {
    var total: int = 0;
    for (var i: int = 0; i < 5; i = i + 1) {
        var doubled: int = i * 2;
        total = total + doubled;
    }
    while (total > 0) { total = total - 1; }
    if (total == 0) { var ready: bool = true; }
    else { var ready: bool = false; }
}
```

Conditions must be bool. Each block introduces a lexical scope: shadowing an outer
variable is allowed, duplicate declarations in the same scope are rejected.
A `for` initializer variable is visible in its condition/body/increment, not after
the loop. Increment uses assignment syntax (`i = i + 1`), not `i++`.
`else` requires a block; write nested `if` inside that block instead of `else if`.
No break/continue or optional for-header clauses are implemented. Infinite loops
are possible; the embedding host must set fuel/time limits.

## Functions

```yenscript
fn factorial(n: int): int {
    if (n <= 1) { return 1; }
    else { return n * factorial(n - 1); }
}
```

Non-void functions must provably return on every path. The analysis recognizes
return statements and both returning branches of an if/else, not general termination
proofs for loops. Void functions finish at the end; bare `return;` is not supported.
Arguments are passed by value. Strings/tables/native objects pass reference/handle
values, not deep copies. Table mutation is visible to aliases.

## Tables

```yenscript
fn main() {
    var data: table<int> = {1, 2, 3};
    data:Set(1, 42);
    var second: int = data:Get(1);
    var count: int = data.Length;
}
```

Table literals use braces. Entries must be compatible with the element type.
Empty literals need an expected type. Trailing commas, keyed tables and `[]`
index syntax are not supported. `Get`/`Set` use zero-based indices; negative or
out-of-range indices trap. Reading Length/Get/Set on null traps.
Nested tables store references and may be passed/returned from functions.
Memory is not garbage-collected; see allocation ownership in [ABI](embedding.md).

## Native APIs

- `host:Function(args)`: package function.
- `console:print(text)`: imported package object method.
- `object:Method(args)`: method on an opaque host handle.
- `object.Property`: property getter; assignment calls a declared setter.
- `host.Type.Static` / `Type.Static`: static host getter.
- `host.Enum.Case` / `Enum.Case`: enum constant.

Native functions/methods are declarations, not dynamic code loading. The host must
link their imports and validate opaque handles. See [package reference](packages.md).

## Diagnostics

| Code | Meaning |
| --- | --- |
| E020 | Source syntax |
| E100-E114 | Unknown/ambiguous names, type mismatches, call/assignment/return errors |
| E200 | Executable code outside a function |
| E201-E203 | Missing/duplicate main or invalid entry signature |
| E204 | Unsupported expression/statement |
| E205 | Filesystem, WABT or binary metadata error |
| E210-E212 | Package syntax, duplicate declaration, dependency/conflict error |

Positions are one-based. Metadata currently maps runtime instructions to statement
starts; it is not an expression-level debugger. A trap in an internal runtime helper
may have no direct map; use the closest mapped caller frame.

## Deliberate limits

No floats, classes, user-defined generics, named imports, string interpolation,
exception syntax, WASI, direct pointer operations, or async functions.
No filesystem/network access is provided by the built-in system package.
