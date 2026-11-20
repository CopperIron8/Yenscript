namespace Yenscript.Core.Tests

open FParsec
open Xunit
open Yenscript.Core

module LexerTests =
    [<Fact>]
    let ``structured diagnostic preserves expected token and position`` () =
        match Lexer.runDiagnostic "E999" (Lexer.symbol ";" .>> eof) "x" with
        | Result.Error [ d ] ->
            Assert.Contains("Expected ';'", d.Message)
            Assert.Equal(1, d.Position.Line)
            Assert.Equal(1, d.Position.Column)
        | _ -> failwith "Expected a structured parse diagnostic"

    [<Fact>]
    let ``structured diagnostic preserves explicit error messages`` () =
        match Lexer.runDiagnostic "E999" (fail "custom parser error": Parser<unit, unit>) "" with
        | Result.Error [ d ] -> Assert.Equal("custom parser error", d.Message)
        | _ -> failwith "Expected an explicit parser diagnostic"

    [<Theory>]
    [<InlineData("variable")>]
    [<InlineData("returnValue")>]
    [<InlineData("trueValue")>]
    [<InlineData("tableName")>]
    let ``keyword prefix does not consume identifiers`` text =
        let parser =
            (Lexer.keyword "var"
             <|> Lexer.keyword "return"
             <|> Lexer.keyword "true"
             <|> Lexer.keyword "table"
             <|> Lexer.identifier)
            .>> eof

        match run parser text with
        | Success(value, _, _) -> Assert.Equal(text, value)
        | Failure(message, _, _) -> failwith message

    [<Fact>]
    let ``escaped backslash and tab retain values`` () =
        match run (Lexer.stringLiteral .>> eof) "\"a\\\\b\\tc\"" with
        | Success(value, _, _) -> Assert.Equal("a\\b\tc", value)
        | Failure(message, _, _) -> failwith message

    [<Theory>]
    [<InlineData("\"bad\\q\"")>]
    [<InlineData("\"unclosed")>]
    [<InlineData("\"line\nbreak\"")>]
    let ``invalid strings fail`` source =
        match run (Lexer.stringLiteral .>> eof) source with
        | Failure _ -> ()
        | Success _ -> failwith "Expected invalid string"
