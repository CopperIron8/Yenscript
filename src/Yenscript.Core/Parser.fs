namespace Yenscript.Core

open FParsec
open Lexer

module Parser =
    let private position (p: FParsec.Position) : Yenscript.Core.Position =
        { Line = int p.Line
          Column = int p.Column }

    let private span start finish : SourceSpan =
        { Start = position start
          End = position finish }

    let private reserved =
        Set.ofList
            [ "using"
              "package"
              "as"
              "from"
              "import"
              "var"
              "fn"
              "return"
              "true"
              "false"
              "null"
              "if"
              "else"
              "while"
              "for"
              "class"
              "interface" ]

    let private name: Parser<string, unit> =
        identifier
        >>= fun value ->
            if reserved.Contains value then
                fail ("reserved keyword: " + value)
            else
                preturn value

    let private typeRef, private typeRefImpl =
        createParserForwardedToRef<TypeRef, unit> ()

    do
        typeRefImpl.Value <-
            (keyword "table" >>. between (symbol "<") (symbol ">") typeRef |>> TableType)
            <|> (sepBy1 name (symbol ".") |>> NamedType)

    let private expression, private expressionImpl =
        createParserForwardedToRef<Expr, unit> ()

    let private locatedExpr p : Parser<Expr, unit> =
        pipe3 getPosition p getPosition (fun start kind finish ->
            { Kind = kind
              Span = span start finish })

    let private arguments =
        between (symbol "(") (symbol ")") (sepBy expression (symbol ","))

    let private atom =
        choice
            [ between (symbol "(") (symbol ")") expression
              locatedExpr (stringLiteral |>> ExprKind.Text)
              locatedExpr (keyword "true" >>% Boolean true)
              locatedExpr (keyword "false" >>% Boolean false)
              locatedExpr (keyword "null" >>% Null)
              locatedExpr (lexeme pint32 |>> Integer)
              locatedExpr (between (symbol "{") (symbol "}") (sepBy expression (symbol ",")) |>> Table)
              locatedExpr (name |>> Name) ]

    let private suffix: Parser<Expr -> Expr, unit> =
        choice
            [ pipe2 (symbol "." >>. name) getPosition (fun n finish receiver ->
                  { Kind = Member(receiver, n)
                    Span =
                      { Start = receiver.Span.Start
                        End = position finish } })
              pipe3 (symbol ":" >>. name) arguments getPosition (fun n args finish receiver ->
                  { Kind = ColonCall(receiver, n, args)
                    Span =
                      { Start = receiver.Span.Start
                        End = position finish } })
              pipe2 arguments getPosition (fun args finish receiver ->
                  { Kind = Call(receiver, args)
                    Span =
                      { Start = receiver.Span.Start
                        End = position finish } }) ]

    let private operators = OperatorPrecedenceParser<Expr, unit, unit>()

    do
        operators.TermParser <-
            pipe2 atom (many suffix) (fun first rest -> List.fold (fun value apply -> apply value) first rest)

        for op, precedence in
            [ "||", 1
              "&&", 2
              "==", 3
              "!=", 3
              "<=", 4
              ">=", 4
              "<", 4
              ">", 4
              "+", 5
              "-", 5
              "*", 6
              "/", 6
              "%", 6 ] do
            operators.AddOperator(
                InfixOperator(
                    op,
                    whitespace,
                    precedence,
                    Associativity.Left,
                    fun left right ->
                        { Kind = Binary(op, left, right)
                          Span =
                            { Start = left.Span.Start
                              End = right.Span.End } }
                )
            )

        operators.AddOperator(
            PrefixOperator(
                "!",
                whitespace,
                7,
                true,
                fun operand ->
                    { Kind = Unary("!", operand)
                      Span = operand.Span }
            )
        )

        // Keep signed integer literals in pint32 (including Int32.MinValue).
        // Prefix negation handles variables and parenthesized expressions.
        operators.AddOperator(
            PrefixOperator(
                "-",
                notFollowedBy digit >>. whitespace,
                7,
                true,
                fun operand ->
                    { Kind = Unary("-", operand)
                      Span = operand.Span }
            )
        )

        expressionImpl.Value <- operators.ExpressionParser

    let private locatedStatement p : Parser<Statement, unit> =
        pipe3 getPosition (p .>> symbol ";") getPosition (fun start kind finish ->
            { Kind = kind
              Span = span start finish })

    let private variable =
        keyword "var"
        >>. pipe3 (name .>> symbol ":") typeRef (opt (symbol "=" >>. expression)) (fun n t v -> Variable(n, t, v))

    let private expressionStatement =
        expression
        >>= fun value ->
            (symbol "=" >>. expression
             >>= fun rhs ->
                 match value.Kind with
                 | Name _
                 | Member _ -> preturn (Assignment(value, rhs))
                 | _ -> fail "assignment target must be a variable or property")
            <|> (match value.Kind with
                 | Call _
                 | ColonCall _ -> preturn (ExpressionStatement value)
                 | _ -> fail "expected assignment or function/method call")

    let private statement, private statementImpl =
        createParserForwardedToRef<Statement, unit> ()

    let private body = between (symbol "{") (symbol "}") (many statement)
    let private condition = between (symbol "(") (symbol ")") expression

    let private headerStatement parser =
        pipe3 getPosition parser getPosition (fun start kind finish ->
            { Kind = kind
              Span = span start finish })

    let private control =
        (keyword "if"
         >>. pipe3 condition body (opt (keyword "else" >>. body)) (fun c t e -> If(c, t, defaultArg e [])))
        <|> (keyword "while" >>. pipe2 condition body (fun c b -> While(c, b)))
        <|> (keyword "for"
             >>. symbol "("
             >>. pipe4
                 (headerStatement (variable <|> expressionStatement) .>> symbol ";")
                 (expression .>> symbol ";")
                 (headerStatement expressionStatement .>> symbol ")")
                 body
                 (fun initial condition increment body -> For(initial, condition, increment, body)))

    do
        statementImpl.Value <-
            pipe3 getPosition control getPosition (fun start kind finish ->
                { Kind = kind
                  Span = span start finish })
            <|> locatedStatement (
                variable
                <|> (keyword "return" >>. expression |>> Return)
                <|> expressionStatement
            )

    let private parameter =
        pipe3
            getPosition
            (pipe2 (name .>> symbol ":") typeRef (fun n t -> n, t))
            getPosition
            (fun start (n, t) finish ->
                { Name = n
                  Type = t
                  Span = span start finish })

    let private functionDeclaration =
        pipe3
            getPosition
            (keyword "fn"
             >>. pipe4
                 name
                 (between (symbol "(") (symbol ")") (sepBy parameter (symbol ",")))
                 (opt (symbol ":" >>. typeRef))
                 (between (symbol "{") (symbol "}") (many statement))
                 (fun n p t body -> n, p, defaultArg t (NamedType [ "void" ]), body))
            getPosition
            (fun start (n, p, t, body) finish ->
                Function
                    { Name = n
                      Parameters = p
                      ReturnType = t
                      Body = body
                      Span = span start finish })

    let private usingPackage =
        pipe3
            getPosition
            (keyword "using"
             >>. keyword "package"
             >>. pipe2
                 (stringLiteral <|> between (symbol "(") (symbol ")") stringLiteral <|> name)
                 (opt (keyword "as" >>. name))
                 (fun n a -> n, a)
             .>> symbol ";")
            getPosition
            (fun start (n, a) finish -> UsingPackage(n, a, span start finish))

    let private importAll =
        pipe3
            getPosition
            (keyword "from" >>. name .>> keyword "import" .>> symbol "*" .>> symbol ";")
            getPosition
            (fun start n finish -> ImportAll(n, span start finish))

    let private compilationUnit =
        whitespace
        >>. many (usingPackage <|> importAll <|> functionDeclaration <|> (statement |>> Statement))
        .>> eof
        |>> fun members -> { Members = members }

    let parse source : Result<CompilationUnit, Diagnostic list> =
        runDiagnostic "E020" compilationUnit source
