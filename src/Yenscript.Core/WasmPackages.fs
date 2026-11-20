namespace Yenscript.Core

open FParsec
open Lexer

type HostSignature =
    { Name: string
      Parameters: (string * TypeRef) list
      ReturnType: TypeRef
      Module: string
      Import: string }

type NativeProperty =
    { Name: string
      Type: TypeRef
      Getter: string * string
      Setter: (string * string) option }

type NativeTypeInfo =
    { Methods: HostSignature list
      Properties: NativeProperty list
      Statics: HostSignature list }

type WasmPackage =
    { Name: string
      Version: string
      Dependencies: (string * string) list
      Functions: HostSignature list
      Objects: Map<string, HostSignature list>
      Types: Map<string, NativeTypeInfo>
      Enums: Map<string, string list> }

module WasmPackages =
    type private Declaration =
        | Func of HostSignature
        | Object of string * HostSignature list
        | Native of string * NativeTypeInfo
        | Enum of string * string list

    let private typ, private typImpl = createParserForwardedToRef<TypeRef, unit> ()

    do
        typImpl.Value <-
            (keyword "table" >>. between (symbol "<") (symbol ">") typ |>> TableType)
            <|> (sepBy1 identifier (symbol ".") |>> NamedType)

    let private parameter = pipe2 (identifier .>> symbol ":") typ (fun n t -> n, t)

    let private binding name =
        symbol "@"
        >>. keyword name
        >>. pipe2 stringLiteral stringLiteral (fun m n -> m, n)

    let private signature =
        pipe4
            identifier
            (between (symbol "(") (symbol ")") (sepBy parameter (symbol ",")))
            (symbol ":" >>. typ)
            (binding "host")
            (fun n p t (m, i) ->
                { Name = n
                  Parameters = p
                  ReturnType = t
                  Module = m
                  Import = i })
        .>> symbol ";"

    let private property =
        keyword "property"
        >>. pipe4 (identifier .>> symbol ":") typ (binding "get") (opt (binding "set")) (fun n t g s ->
            { Name = n
              Type = t
              Getter = g
              Setter = s })
        .>> symbol ";"

    let private staticMember =
        keyword "static"
        >>. pipe3 (identifier .>> symbol ":") typ (binding "host") (fun n t (m, i) ->
            { Name = n
              Parameters = []
              ReturnType = t
              Module = m
              Import = i })
        .>> symbol ";"

    let private nativeMember =
        (keyword "method" >>. signature |>> Choice1Of3)
        <|> (property |>> Choice2Of3)
        <|> (staticMember |>> Choice3Of3)

    let private declaration =
        keyword "native"
        >>. choice
                [ keyword "function" >>. signature |>> Func
                  keyword "object"
                  >>. pipe2
                      identifier
                      (between (symbol "{") (symbol "}") (many (keyword "method" >>. signature)))
                      (fun n m -> Object(n, m))
                  keyword "type"
                  >>. pipe2 identifier (between (symbol "{") (symbol "}") (many nativeMember)) (fun n members ->
                      Native(
                          n,
                          { Methods =
                              members
                              |> List.choose (function
                                  | Choice1Of3 m -> Some m
                                  | _ -> None)
                            Properties =
                              members
                              |> List.choose (function
                                  | Choice2Of3 p -> Some p
                                  | _ -> None)
                            Statics =
                              members
                              |> List.choose (function
                                  | Choice3Of3 s -> Some s
                                  | _ -> None) }
                      ))
                  keyword "enum"
                  >>. pipe2
                      identifier
                      (between (symbol "{") (symbol "}") (many1 (identifier .>> symbol ";")))
                      (fun n c -> Enum(n, c)) ]

    let private parser =
        whitespace
        >>. keyword "package"
        >>. pipe4
            (identifier .>> symbol ";")
            (keyword "version" >>. stringLiteral .>> symbol ";")
            (many (
                keyword "requires"
                >>. pipe2 stringLiteral (keyword "version" >>. stringLiteral .>> symbol ";") (fun p v -> p, v)
            ))
            (many declaration)
            (fun n v deps declarations -> n, v, deps, declarations)
        .>> eof

    let parse source : Result<WasmPackage, Diagnostic list> =
        runDiagnostic "E210" parser source
        |> Result.bind (fun (n, v, deps, decls) ->
            let unique names =
                List.length names = (List.distinct names).Length

            let names =
                decls
                |> List.map (function
                    | Func f -> f.Name
                    | Object(n, _)
                    | Native(n, _)
                    | Enum(n, _) -> n)

            let invalid =
                not (unique names)
                || System.String.IsNullOrWhiteSpace v
                || (decls
                    |> List.exists (function
                        | Object(_, methods) -> not (unique (methods |> List.map (fun m -> m.Name)))
                        | Native(_, t) ->
                            not (
                                unique (
                                    (t.Methods @ t.Statics |> List.map (fun m -> m.Name))
                                    @ (t.Properties |> List.map (fun p -> p.Name))
                                )
                            )
                        | Enum(_, cases) -> not (unique cases)
                        | Func f -> not (unique (f.Parameters |> List.map fst))))

            if invalid then
                Result.Error
                    [ { Severity = Severity.Error
                        Code = "E211"
                        Message = "Duplicate declaration or invalid package version"
                        Position = { Line = 1; Column = 1 } } ]
            else
                Result.Ok
                    { Name = n
                      Version = v
                      Dependencies = deps
                      Functions =
                        decls
                        |> List.choose (function
                            | Func f -> Some f
                            | _ -> None)
                      Objects =
                        decls
                        |> List.choose (function
                            | Object(n, m) -> Some(n, m)
                            | _ -> None)
                        |> Map.ofList
                      Types =
                        decls
                        |> List.choose (function
                            | Native(n, t) -> Some(n, t)
                            | _ -> None)
                        |> Map.ofList
                      Enums =
                        decls
                        |> List.choose (function
                            | Enum(n, c) -> Some(n, c)
                            | _ -> None)
                        |> Map.ofList })

    let loadSystem () =
        use stream =
            typeof<WasmPackage>.Assembly.GetManifestResourceStream("Yenscript.Core.Wasm.system.ysp")
            |> nonNull

        let bytes = Array.zeroCreate<byte> (int stream.Length)
        stream.ReadExactly bytes
        System.Text.Encoding.UTF8.GetString bytes |> parse
