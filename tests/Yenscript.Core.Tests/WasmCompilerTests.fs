namespace Yenscript.Core.Tests

open Xunit
open Yenscript.Core

module WasmCompilerTests =
    [<Theory>]
    [<InlineData("fn main() {}")>]
    [<InlineData("fn main(): void {}")>]
    let ``main generates explicit start export`` source =
        match WasmCompiler.compile source with
        | Ok wat ->
            Assert.Contains("(export \"_start\")", wat)
            Assert.DoesNotContain("(start ", wat)
        | Error errors -> failwithf "%A" errors

    [<Theory>]
    [<InlineData("using package system; from system import *; fn main() { console:print(\"hello\"); }")>]
    [<InlineData("using package system as s; fn main() { s.console:warning(\"hello\"); }")>]
    let ``system console is an object not a package`` source =
        match WasmCompiler.compile source with
        | Ok wat ->
            Assert.Contains("(import ", wat)
            Assert.Contains("(data ", wat)
        | Error diagnostics -> failwithf "%A" diagnostics

    [<Fact>]
    let ``arithmetic precedence and control flow lower to WASM IR`` () =
        let source =
            "using package system; from system import *; fn main() { var x: int = 1 + 2 * 3; var ok: bool = x == 7; if (ok) { console:print(\"yes\"); } else { console:error(\"bad\"); } while (x > 0) { x = x - 1; } }"

        match WasmCompiler.compile source with
        | Ok wat ->
            Assert.Contains("i32.mul", wat)
            Assert.Contains("i32.add", wat)
            Assert.Contains("br_if 1", wat)
        | Error errors -> failwithf "%A" errors

    [<Theory>]
    [<InlineData("fn main() { var x: int = 1; if (true) { var x: bool = false; } x = 2; }")>]
    [<InlineData("fn main() { for (var i: int = 0; i < 3; i = i + 1) { var j: int = i; } }")>]
    [<InlineData("fn main() { var b: bool = false && (1 / 0 == 0); var c: bool = true || (1 / 0 == 0); }")>]
    [<InlineData("fn main() { var x: int = 3; var y: int = -x * 2; var z: int = -(x + 2); }")>]
    [<InlineData("fn main() { var x: int = -2147483648; }")>]
    let ``scopes for loops and short circuit compile`` source =
        match WasmCompiler.compile source with
        | Ok _ -> ()
        | Error errors -> failwithf "%A" errors

    [<Theory>]
    [<InlineData("fn identity(s: string): string { return s; } fn main() { var s: string = identity(\"hello\"); }")>]
    [<InlineData("fn main() { var a: table<int> = {1,2,3}; var n: int = a.Length; }")>]
    [<InlineData("fn main() { var a: table<table<int>> = {{1},{2,3}}; }")>]
    [<InlineData("fn main() { var a: table<int> = {}; var s: string; }")>]
    [<InlineData("fn accept(a: table<int>) {} fn main() { accept({}); var a: table<int> = {1}; a = {}; a = null; }")>]
    [<InlineData("fn main() { var a: table<table<int>> = {{},{}}; }")>]
    [<InlineData("fn main() { var a: table<table<int>> = {{},{1}}; var b: table<int> = a:Get(1); b:Set(0,2); }")>]
    let ``string and table values use linear memory`` source =
        match WasmCompiler.compile source with
        | Ok wat ->
            Assert.Contains("(export \"alloc\")", wat)
            Assert.Contains("(export \"free\")", wat)
        | Error errors -> failwithf "%A" errors

    [<Theory>]
    [<InlineData("fn main() { var a: table<int> = {1,true}; }", "E107")>]
    [<InlineData("fn main() { var s: string = 1; }", "E105")>]
    [<InlineData("fn main() { var a: table<void>; }", "E104")>]
    [<InlineData("fn main() { var a: int = null; }", "E105")>]
    [<InlineData("fn main() { var a: string = null; }", "E105")>]
    [<InlineData("fn main() { var a: table<int> = {1}; a:Set(0,true); }", "E105")>]
    [<InlineData("fn main() { var a: table<int> = {1}; var x: int = a:Get(false); }", "E105")>]
    let ``memory value type errors are diagnosed`` source code =
        match WasmCompiler.compile source with
        | Error [ d ] -> Assert.Equal(code, d.Code)
        | other -> failwithf "%A" other

    [<Theory>]
    [<InlineData("fn sum(a: int, b: int): int { return a + b; } fn main() { var x: int = sum(2,3); }")>]
    [<InlineData("fn main() { var x: int = fact(5); } fn fact(n: int): int { if (n <= 1) { return 1; } else { return n * fact(n - 1); } }")>]
    [<InlineData("fn helper() {} fn main() { helper(); }")>]
    let ``runtime functions parameters forward calls and recursion compile`` source =
        match WasmCompiler.compile source with
        | Ok _ -> ()
        | Error errors -> failwithf "%A" errors

    [<Theory>]
    [<InlineData("fn f(): int { if (true) { return 1; } } fn main() {}", "E114")>]
    [<InlineData("fn f(): int { return true; } fn main() {}", "E105")>]
    [<InlineData("fn f(x: int) {} fn main() { f(); }", "E110")>]
    [<InlineData("fn f(x: int) {} fn main() { f(true); }", "E105")>]
    [<InlineData("fn f(x: int,x: int) {} fn main() {}", "E112")>]
    [<InlineData("fn f() { return 1; } fn main() {}", "E113")>]
    [<InlineData("fn main() { if (true) { var x: int = 1; } x = 2; }", "E100")>]
    [<InlineData("fn main() { for (var i: int = 0; i < 2; i = i + 1) {} i = 0; }", "E100")>]
    [<InlineData("fn main() { var x: int; var x: bool; }", "E112")>]
    [<InlineData("fn main() { if (true) { var x: int; var x: int; } }", "E112")>]
    [<InlineData("fn main() { var b: bool = true && 1; }", "E105")>]
    [<InlineData("fn main() { for (var i: int = 0; 1; i = i + 1) {} }", "E105")>]
    [<InlineData("fn main() { if (1) {} }", "E105")>]
    [<InlineData("fn main() { while (1) {} }", "E105")>]
    [<InlineData("fn main() { var x: int = true + 1; }", "E105")>]
    [<InlineData("using package console; fn main() {}", "E101")>]
    [<InlineData("from system import *; fn main() {}", "E101")>]
    [<InlineData("fn main() { console:print(\"hello\"); }", "E100")>]
    [<InlineData("", "E201")>]
    [<InlineData("var x: int = 1;", "E200")>]
    [<InlineData("fn main(x: int) {}", "E203")>]
    [<InlineData("fn main(): int { return 1; }", "E203")>]
    [<InlineData("fn main() {} fn main() {}", "E202")>]
    [<InlineData("fn main() { var x: int = true; }", "E105")>]
    let ``unsupported programs are never silently discarded`` source code =
        match WasmCompiler.compile source with
        | Error [ d ] -> Assert.Equal(code, d.Code)
        | other -> failwithf "%A" other
