# Yenscript roadmap

The current development line is an embeddable core WebAssembly language.
This is a breaking migration. Previous published tags are not rewritten.
Checkboxes describe development capabilities, not published release availability.

## WASM migration

- [x] F# compiler core, C# CLI, pure source/package-text embedding API.
- [x] Mandatory `fn main()` with explicit `_start` host invocation.
- [x] WAT generation from separate WASM IR and external WABT assembly.
- [x] Typed int/bool/string/table/native values and enum declarations.
- [x] Arithmetic, comparisons, unary operators, short-circuit boolean expressions.
- [x] If/else, while, for, lexical scopes and shadowing.
- [x] Runtime functions, parameters, returns, recursion and return-path validation.
- [x] UTF-8 literals/descriptors, table headers/elements and nested references.
- [x] Exported memory, aligned allocation and reusable free-list blocks.
- [x] Bounds/null checks for tables and host-side trap containment examples.
- [x] Package manifests, exact dependency versions, cycles and conflict diagnostics.
- [x] Package resolution by name, relative path and absolute path in the CLI.
- [x] Host functions/objects, opaque types, methods, properties, static members and enums.
- [x] Embedded system console, integer math, string concatenation and conversions.
- [x] Binary instruction offsets and statement-level source metadata with checksums.
- [x] Runtime fixtures for flow, recursion, strings, tables, packages, objects and traps.
- [x] Language/package/embedding documentation and new CLI examples.

## Further reliability work

- [ ] Expression-level source spans and debugger-oriented variable metadata.
- [ ] Stronger bounds for deeply nested syntax and very large dependency graphs.
- [ ] Host-configurable compilation budgets and cancellation in the pure frontend.
- [ ] Exhaustive malformed-binary fuzzing for metadata reader.
- [ ] Coalescing/splitting allocator blocks and optional reference ownership support.
- [ ] Consider GC only with an explicit ownership and host interoperability design.
- [ ] More precise expected-type propagation for mixed nested empty table literals.
- [ ] Structured package diagnostics naming dependency source files and spans.
- [ ] Automated Rust host compilation tests in addition to executed Python hosts.
- [ ] Stable AOT-compatible parser dependency and dependency security reviews.
- [ ] Publish this development line only after hosted Windows/Linux verification.

## Optional future language work

- [ ] Floating-point values and explicit numeric conversions.
- [ ] Named imports, generic package signatures and richer string operations.
- [ ] Break/continue and optional for-header clauses, with tested scope semantics.
- [ ] Separate library export declarations for directly callable user functions.
- [ ] Additional standard capabilities supplied by explicit host packages.
- [ ] Additional platforms with native build and smoke-test coverage.

No filesystem/network capability is implicit. No web UI, plugin loader or language
server is required for embedding. Limits and intentional non-features are documented
in [language](docs/language.md), [packages](docs/packages.md), and [ABI](docs/embedding.md).

## Release policy

Use a new version for incompatible changes. Keep published tags immutable.
Build and test before publishing artifacts; include LICENSE and checksums.
Do not claim runtime verification based only on text snapshots or unit tests.
