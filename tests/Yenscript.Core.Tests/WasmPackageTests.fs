namespace Yenscript.Core.Tests

open Xunit
open Yenscript.Core

module WasmPackageTests =
    let host =
        "package host; version \"1.0.0\"; native function add(a: int, b: int): int @host \"host\" \"add\";"

    [<Fact>]
    let ``system console is declared in embedded package data`` () =
        let package = WasmPackages.loadSystem () |> TestHelpers.unwrap
        Assert.Equal("system", package.Name)
        Assert.Equal(3, package.Objects.["console"].Length)
        Assert.Equal("console_print", package.Objects.["console"].Head.Import)

    [<Fact>]
    let ``external declarations become typed host imports`` () =
        let source =
            "using package \"../host.ysp\" as h; from h import *; fn main() { var x: int = h:add(1,2); var y: int = add(x,3); }"

        let wat =
            WasmCompiler.compileWithPackages (Map.ofList [ "../host.ysp", host ]) source
            |> TestHelpers.unwrap

        Assert.Contains("call $host", wat)

    [<Theory>]
    [<InlineData("package p; version \"1\"; requires \"missing.ysp\" version \"1\";")>]
    [<InlineData("package p; version \"1\"; requires \"host.ysp\" version \"2\";")>]
    let ``missing and mismatched dependencies are rejected`` package =
        match WasmCompiler.compileWithPackages (Map.ofList [ "p", package; "host.ysp", host ]) "fn main() {}" with
        | Error [ d ] -> Assert.Equal("E212", d.Code)
        | other -> failwithf "%A" other

    [<Fact>]
    let ``system cannot be replaced`` () =
        match WasmCompiler.compileWithPackages (Map.ofList [ "system", host ]) "fn main() {}" with
        | Error [ d ] -> Assert.Equal("E212", d.Code)
        | other -> failwithf "%A" other

    [<Fact>]
    let ``dependency cycles are rejected by pure embedding API`` () =
        let a = "package a; version \"1\"; requires \"b\" version \"1\";"
        let b = "package b; version \"1\"; requires \"a\" version \"1\";"

        match WasmCompiler.compileWithPackages (Map.ofList [ "a", a; "b", b ]) "fn main() {}" with
        | Error [ d ] -> Assert.Equal("E212", d.Code)
        | _ -> failwith "Expected cycle diagnostic"

    [<Fact>]
    let ``native types enums and properties typecheck`` () =
        let package =
            "package objects; version \"1\"; native enum Mode { First; Second; } native type Box { property Value: int @get \"host\" \"get\" @set \"host\" \"set\"; method Add(n: int): int @host \"host\" \"add\"; static Default: Box @host \"host\" \"default\"; }"

        let source =
            "using package objects as o; from o import *; fn main() { var b: Box = Box.Default; b.Value = 2; var n: int = b:Add(1); var m: Mode = o.Mode.Second; }"

        Assert.True(
            WasmCompiler.compileWithPackages (Map.ofList [ "objects", package ]) source
            |> Result.isOk
        )

    [<Theory>]
    [<InlineData("package p; version \"1\"; native enum E { A; A; }")>]
    [<InlineData("package p; version \"1\"; native object x {} native object x {}")>]
    let ``duplicate package declarations are diagnosed`` source =
        match WasmPackages.parse source with
        | Error [ d ] -> Assert.Equal("E211", d.Code)
        | _ -> failwith "Expected duplicate"

    [<Fact>]
    let ``portable system library functions compile`` () =
        let source =
            "using package system; from system import *; fn main() { var x: int = math:pow(2,3); console:print(strings:concat(\"x=\", convert:intToString(x))); }"

        let wat = WasmCompiler.compile source |> TestHelpers.unwrap
        Assert.Contains("call $builtin_pow", wat)

    [<Fact>]
    let ``host argument types are validated`` () =
        match
            WasmCompiler.compileWithPackages
                (Map.ofList [ "host", host ])
                "using package host as h; fn main() { h:add(true,2); }"
        with
        | Error [ d ] -> Assert.Equal("E105", d.Code)
        | other -> failwithf "%A" other
