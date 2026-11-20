namespace Yenscript.Core

open FParsec

module Lexer =
    // Avoid FParsec.run's general-purpose printf/reflection error formatter.
    // Read the structured error nodes explicitly; never format arbitrary objects.
    let rec private describeErrors (errors: ErrorMessageList) =
        if obj.ReferenceEquals(errors, null) then
            []
        else
            let messages =
                match errors.Head with
                | :? ErrorMessage.Expected as e -> [ "Expected " + e.Label ]
                | :? ErrorMessage.ExpectedString as e -> [ "Expected '" + e.String + "'" ]
                | :? ErrorMessage.ExpectedCaseInsensitiveString as e -> [ "Expected '" + e.CaseInsensitiveString + "'" ]
                | :? ErrorMessage.Unexpected as e -> [ "Unexpected " + e.Label ]
                | :? ErrorMessage.UnexpectedString as e -> [ "Unexpected '" + e.String + "'" ]
                | :? ErrorMessage.UnexpectedCaseInsensitiveString as e ->
                    [ "Unexpected '" + e.CaseInsensitiveString + "'" ]
                | :? ErrorMessage.Message as e -> [ e.String ]
                | :? ErrorMessage.NestedError as e -> describeErrors e.Messages
                | :? ErrorMessage.CompoundError as e ->
                    ("Expected " + e.LabelOfCompound) :: describeErrors e.NestedErrorMessages
                | _ -> []

            messages @ describeErrors errors.Tail

    let runDiagnostic code (parser: Parser<'a, unit>) (source: string) : Result<'a, Diagnostic list> =
        use stream = new CharStream<unit>(source, 0, source.Length)
        let reply = parser stream

        if reply.Status = ReplyStatus.Ok then
            Result.Ok reply.Result
        else
            let messages = describeErrors reply.Error |> List.distinct

            let message =
                if List.isEmpty messages then
                    "Invalid syntax"
                else
                    String.concat "; " messages

            Result.Error
                [ { Severity = Severity.Error
                    Code = code
                    Message = message
                    Position =
                      { Line = int stream.Position.Line
                        Column = int stream.Position.Column } } ]

    let whitespace: Parser<unit, unit> =
        skipMany (
            skipMany1Satisfy System.Char.IsWhiteSpace
            <|> (pstring "//" >>. skipRestOfLine true)
            <|> (pstring "/*" >>. skipCharsTillString "*/" true System.Int32.MaxValue)
        )

    let lexeme p = p .>> whitespace
    let symbol s = lexeme (pstring s)

    let identifier: Parser<string, unit> =
        lexeme (
            many1Satisfy2L
                (fun c -> System.Char.IsLetter c || c = '_')
                (fun c -> System.Char.IsLetterOrDigit c || c = '_')
                "identifier"
        )

    let keyword s =
        lexeme (
            attempt (
                pstring s
                .>> notFollowedBy (satisfy (fun c -> System.Char.IsLetterOrDigit c || c = '_'))
            )
        )

    let stringLiteral: Parser<string, unit> =
        let escaped =
            pchar '\\' >>. anyOf "\"\\nrt"
            |>> function
                | 'n' -> '\n'
                | 'r' -> '\r'
                | 't' -> '\t'
                | c -> c

        lexeme (
            between
                (pchar '"')
                (pchar '"')
                (manyChars (escaped <|> satisfy (fun c -> c <> '"' && c <> '\\' && c <> '\n' && c <> '\r')))
        )
