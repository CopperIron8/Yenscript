namespace Yenscript.Core.Tests

open Xunit
open FsCheck.Xunit
open Yenscript.Core

module ParserTests =
    [<Theory>]
    [<InlineData("using package system; from system import *; fn main() {}")>]
    [<InlineData("using package \"../packages/host.ysp\" as h; fn main() { h:run(); }")>]
    [<InlineData("using package (\"C:/packages/host.ysp\") as h; fn main() {}")>]
    [<InlineData("fn twice(x: int): int { return x * 2; } fn main() { var x: int = twice(3); }")>]
    [<InlineData("fn main() { if (true) { var x: int; } else {} while (false) {} for (var i: int=0; i<3; i=i+1) {} }")>]
    [<InlineData("fn main() { var a: table<table<int>> = {{1}, {2}}; var x: string = \"hello\\n\"; }")>]
    let ``language syntax parses`` source =
        Assert.True(Parser.parse source |> Result.isOk)

    [<Theory>]
    [<InlineData("fn main() { var x = 1; }")>]
    [<InlineData("fn main() { var x: int = 1 }")>]
    [<InlineData("from system import console;")>]
    [<InlineData("fn main() { while true {} }")>]
    [<InlineData("fn main() { var x: table<int> = {1,}; }")>]
    [<InlineData("/* unclosed")>]
    let ``invalid source reports syntax position`` source =
        match Parser.parse source with
        | Error [ d ] ->
            Assert.Equal("E020", d.Code)
            Assert.True(d.Position.Line > 0)
        | _ -> failwith "Expected syntax error"

    [<Property(MaxTest = 100)>]
    let ``integer literals retain exact values`` (n: int) =
        match Parser.parse (sprintf "var x: int = %d;" n) with
        | Ok { Members = [ Statement { Kind = Variable(_, _, Some { Kind = Integer actual }) } ] } -> actual = n
        | _ -> false
