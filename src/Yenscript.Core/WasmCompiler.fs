namespace Yenscript.Core

open System.Text

/// Pure typed frontend/lowering pipeline. File access and WABT invocation belong to the host.
module WasmCompiler =
    let private failure code message position =
        Error
            [ { Severity = Severity.Error
                Code = code
                Message = message
                Position = position } ]

    exception private CompilationError of Diagnostic

    let private abort code message pos =
        raise (
            CompilationError
                { Severity = Severity.Error
                  Code = code
                  Message = message
                  Position = pos }
        )

    let rec private compatible (expected: string) (actual: string) =
        expected = actual
        || (actual = "null"
            && (expected.StartsWith("table<", System.StringComparison.Ordinal)
                || expected.StartsWith("native:", System.StringComparison.Ordinal)))
        || (expected.StartsWith("table<", System.StringComparison.Ordinal)
            && actual.StartsWith("table<", System.StringComparison.Ordinal)
            && (actual = "table<?>"
                || compatible (expected.Substring(6, expected.Length - 7)) (actual.Substring(6, actual.Length - 7))))

    let private typeBindings packages (syntax: CompilationUnit) =
        let aliases =
            syntax.Members
            |> List.choose (function
                | UsingPackage(reference, alias, _) ->
                    Map.tryFind reference packages
                    |> Option.map (fun (p: WasmPackage) -> defaultArg alias p.Name, reference)
                | _ -> None)
            |> Map.ofList

        let imports =
            syntax.Members
            |> List.choose (function
                | ImportAll(n, _) -> Map.tryFind n aliases
                | _ -> None)
            |> List.distinct

        aliases, imports

    let rec private resolveType (packages: Map<string, WasmPackage>) (syntax: CompilationUnit) owner pos typeRef =
        let aliases, imports = typeBindings packages syntax

        let native reference name =
            let p = packages.[reference]

            if Map.containsKey name p.Types then
                "native:" + p.Name + "." + name
            elif Map.containsKey name p.Enums then
                "enum:" + p.Name + "." + name
            else
                abort "E102" ("Unknown package type: " + name) pos

        match typeRef with
        | TableType element ->
            let t = resolveType packages syntax owner pos element

            if t = "void" then
                abort "E104" "table<void> is invalid" pos

            "table<" + t + ">"
        | NamedType [ t ] when List.contains t [ "int"; "bool"; "string"; "void" ] -> t
        | NamedType [ name ] ->
            match owner with
            | Some reference -> native reference name
            | None ->
                let candidates =
                    imports
                    |> List.filter (fun r ->
                        Map.containsKey name packages.[r].Types
                        || Map.containsKey name packages.[r].Enums)

                match candidates with
                | [ reference ] -> native reference name
                | [] -> abort "E102" ("Unknown type: " + name) pos
                | _ -> abort "E103" ("Ambiguous type: " + name) pos
        | NamedType [ alias; name ] ->
            let reference =
                Map.tryFind alias aliases
                |> Option.orElseWith (fun () ->
                    if owner.IsSome then
                        packages
                        |> Map.toList
                        |> List.tryPick (fun (r, p) -> if p.Name = alias then Some r else None)
                    else
                        None)

            match reference with
            | Some r -> native r name
            | None -> abort "E101" ("Unknown package alias: " + alias) pos
        | _ -> abort "E102" "Invalid type reference" pos

    let private nativeInfo (packages: Map<string, WasmPackage>) (typ: string) pos =
        packages
        |> Map.toList
        |> List.tryPick (fun (r, p) ->
            p.Types
            |> Map.toList
            |> List.tryPick (fun (n, t) ->
                if typ = "native:" + p.Name + "." + n then
                    Some(r, n, t)
                else
                    None))
        |> Option.defaultWith (fun () -> abort "E106" "Receiver is not a native object" pos)

    let private lowerFunction
        (packages: Map<string, WasmPackage>)
        hostSignatures
        signatures
        functionIndex
        startOffset
        (syntax: CompilationUnit)
        (main: FunctionDeclaration)
        =
        let bindings =
            syntax.Members
            |> List.fold
                (fun state memberSyntax ->
                    Result.bind
                        (fun (aliases, imported) ->
                            match memberSyntax with
                            | UsingPackage(reference, alias, span) ->
                                match Map.tryFind reference packages with
                                | None -> failure "E101" ("Package source not supplied: " + reference) span.Start
                                | Some package ->
                                    let name = defaultArg alias package.Name

                                    if Map.containsKey name aliases then
                                        failure "E112" "Duplicate package binding" span.Start
                                    else
                                        Ok(Map.add name reference aliases, imported)
                            | ImportAll(name, span) ->
                                if Map.containsKey name aliases then
                                    Ok(aliases, Set.add aliases.[name] imported)
                                else
                                    failure
                                        "E101"
                                        "Add the system package before importing it; use its declared alias"
                                        span.Start
                            | Function _ -> Ok(aliases, imported)
                            | Statement s ->
                                failure "E200" "Executable statements must be inside fn main()" s.Span.Start)
                        state)
                (Ok(Map.empty, Set.empty))

        bindings
        |> Result.bind (fun (aliases, imported) ->
            let primitive position typeRef =
                resolveType packages syntax None position typeRef

            let importedHost name pos =
                let matches =
                    imported
                    |> Set.toList
                    |> List.choose (fun reference -> Map.tryFind (reference, name) hostSignatures)

                match matches with
                | [ signature ] -> Some signature
                | [] -> None
                | _ -> abort "E103" ("Ambiguous imported function: " + name) pos

            let nextLocal = ref main.Parameters.Length
            let returnType = primitive main.Span.Start main.ReturnType
            let stringOffset = ref startOffset
            let stringData = ResizeArray<WasmData>()

            let stringLiteral (text: string) =
                let bytes = Encoding.UTF8.GetBytes text
                let address = stringOffset.Value
                let payload = address + 8

                let word n =
                    [| byte (n &&& 255)
                       byte ((n >>> 8) &&& 255)
                       byte ((n >>> 16) &&& 255)
                       byte ((n >>> 24) &&& 255) |]

                let packed = Array.concat [ word payload; word bytes.Length; bytes ]
                stringData.Add { Offset = address; Bytes = packed }
                stringOffset.Value <- address + packed.Length
                address

            let staticType locals (receiver: Expr) =
                match receiver.Kind with
                | Member({ Kind = Name alias }, name) when not (Map.containsKey alias locals) ->
                    Map.tryFind alias aliases |> Option.map (fun r -> r, name)
                | Name name when not (Map.containsKey name locals) ->
                    let matches =
                        imported
                        |> Set.toList
                        |> List.filter (fun r ->
                            Map.containsKey name packages.[r].Types
                            || Map.containsKey name packages.[r].Enums)

                    match matches with
                    | [ r ] -> Some(r, name)
                    | [] -> None
                    | _ -> abort "E103" "Ambiguous static type" receiver.Span.Start
                | _ -> None

            let rec expression locals (expr: Expr) =
                let require wanted actual =
                    if not (compatible wanted actual) then
                        abort "E105" ("Expected " + wanted + ", got " + actual) expr.Span.Start

                match expr.Kind with
                | Null -> "null", [ I32Const 0 ]
                | Text text -> "string", [ I32Const(stringLiteral text) ]
                | Table elements ->
                    let values = elements |> List.map (expression locals)

                    if values |> List.exists (fun (t, _) -> t = "void") then
                        abort "E104" "A table cannot contain void" expr.Span.Start

                    let elementType =
                        match values with
                        | [] -> "?"
                        | _ ->
                            values
                            |> List.map fst
                            |> List.tryFind (fun candidate ->
                                values |> List.forall (fun (actual, _) -> compatible candidate actual))
                            |> Option.defaultWith (fun () ->
                                abort "E107" "Table elements must have compatible types" expr.Span.Start)

                    let temp = nextLocal.Value
                    nextLocal.Value <- temp + 1
                    "table<" + elementType + ">", [ NewTable(temp, values |> List.map snd) ]
                | Member(receiver, name) when staticType locals receiver |> Option.isSome ->
                    let reference, typ = staticType locals receiver |> Option.get

                    match Map.tryFind typ packages.[reference].Enums with
                    | Some cases ->
                        match List.tryFindIndex ((=) name) cases with
                        | Some index -> "enum:" + packages.[reference].Name + "." + typ, [ I32Const index ]
                        | None -> abort "E106" ("Unknown enum case: " + name) expr.Span.Start
                    | None ->
                        match Map.tryFind (reference, typ + ".static." + name) hostSignatures with
                        | Some(index, _, ret) -> ret, [ HostFunctionCall index ]
                        | _ -> abort "E106" ("Unknown static member: " + name) expr.Span.Start
                | Member(receiver, "Length") ->
                    let t, code = expression locals receiver

                    if t = "string" then
                        "int", code @ [ LoadI32 4 ]
                    elif t.StartsWith("table<", System.StringComparison.Ordinal) then
                        "int", code @ [ LoadI32 0 ]
                    else
                        let reference, native, _ = nativeInfo packages t receiver.Span.Start

                        match Map.tryFind (reference, native + ".get.Length") hostSignatures with
                        | Some(index, _, ret) -> ret, code @ [ HostFunctionCall index ]
                        | _ -> abort "E106" "Unknown Length property" expr.Span.Start
                | Member(receiver, name) ->
                    let typ, value = expression locals receiver
                    let reference, native, _ = nativeInfo packages typ receiver.Span.Start

                    match Map.tryFind (reference, native + ".get." + name) hostSignatures with
                    | Some(index, _, ret) -> ret, value @ [ HostFunctionCall index ]
                    | _ -> abort "E106" ("Unknown property: " + name) expr.Span.Start
                | Integer n -> "int", [ I32Const n ]
                | Boolean b -> "bool", [ I32Const(if b then 1 else 0) ]
                | Call({ Kind = Name name }, args) ->
                    if Map.containsKey name locals then
                        abort "E108" "A local value is not callable" expr.Span.Start

                    let target =
                        match Map.tryFind name signatures with
                        | Some(index, p, r) -> Some(FunctionCall index, p, r)
                        | None ->
                            importedHost name expr.Span.Start
                            |> Option.map (fun (index, p, r) -> HostFunctionCall index, p, r)

                    match target with
                    | None -> abort "E100" ("Unknown function '" + name + "'") expr.Span.Start
                    | Some(instruction, parameters, ret) ->
                        if List.length args <> List.length parameters then
                            abort "E110" "Function argument count mismatch" expr.Span.Start

                        let arguments =
                            List.zip args parameters
                            |> List.collect (fun (arg, expected) ->
                                let actual, code = expression locals arg

                                if not (compatible expected actual) then
                                    abort "E105" ("Expected " + expected + ", got " + actual) arg.Span.Start

                                code)

                        ret, arguments @ [ instruction ]
                | ColonCall({ Kind = Name alias }, name, args) when
                    Map.containsKey alias aliases && not (Map.containsKey alias locals)
                    ->

                    let reference =
                        Map.tryFind alias aliases
                        |> Option.defaultWith (fun () ->
                            abort "E101" ("Unknown package alias: " + alias) expr.Span.Start)

                    match Map.tryFind (reference, name) hostSignatures with
                    | None -> abort "E108" ("Unknown package function: " + name) expr.Span.Start
                    | Some(index, parameters, ret) ->
                        if args.Length <> parameters.Length then
                            abort "E110" "Host argument count mismatch" expr.Span.Start

                        let arguments =
                            List.zip args parameters
                            |> List.collect (fun (arg, expected) ->
                                let actual, code = expression locals arg

                                if not (compatible expected actual) then
                                    abort "E105" ("Expected " + expected + ", got " + actual) arg.Span.Start

                                code)

                        ret, arguments @ [ HostFunctionCall index ]
                | ColonCall(receiver, name, args) ->
                    let objectTarget =
                        match receiver.Kind with
                        | Name n when not (Map.containsKey n locals) ->
                            imported
                            |> Set.toList
                            |> List.choose (fun r ->
                                if Map.containsKey n packages.[r].Objects then
                                    Some(r, n)
                                else
                                    None)
                        | Member({ Kind = Name alias }, n) when Map.containsKey alias aliases ->
                            let r = aliases.[alias]

                            if Map.containsKey n packages.[r].Objects then
                                [ r, n ]
                            else
                                []
                        | _ -> []

                    let self =
                        if objectTarget.IsEmpty then
                            Some(expression locals receiver)
                        else
                            None

                    match self with
                    | Some(typ, value) when typ.StartsWith("table<", System.StringComparison.Ordinal) ->
                        let element = typ.Substring(6, typ.Length - 7)
                        let values = args |> List.map (expression locals)

                        match name, values with
                        | "Get", [ ("int", index) ] -> element, value @ index @ [ TableGet ]
                        | "Set", [ ("int", index); (actual, item) ] when compatible element actual ->
                            "void", value @ index @ item @ [ TableSet ]
                        | _ -> abort "E105" "Use table:Get(int) or table:Set(int, element)" expr.Span.Start
                    | _ ->
                        let reference, memberName, receiverValues =
                            match objectTarget, self with
                            | [ r, n ], _ -> r, n + "." + name, []
                            | _ :: _ :: _, _ -> abort "E103" "Ambiguous native object" expr.Span.Start
                            | _, Some(typ, value) ->
                                let r, n, _ = nativeInfo packages typ receiver.Span.Start
                                r, n + "." + name, [ typ, value ]
                            | _ -> abort "E108" "Invalid receiver" expr.Span.Start

                        match Map.tryFind (reference, memberName) hostSignatures with
                        | None -> abort "E108" ("Unknown native method: " + name) expr.Span.Start
                        | Some(index, parameters, ret) ->
                            let values = receiverValues @ (args |> List.map (expression locals))

                            if values.Length <> parameters.Length then
                                abort "E110" "Method argument count mismatch" expr.Span.Start

                            List.zip values parameters
                            |> List.iter (fun ((actual, _), expected) ->
                                if not (compatible expected actual) then
                                    abort "E105" "Method argument type mismatch" expr.Span.Start)

                            ret, (values |> List.collect snd) @ [ HostFunctionCall index ]
                | Name n ->
                    match Map.tryFind n locals with
                    | Some(index, t) -> t, [ LocalGet index ]
                    | None -> abort "E100" ("Unknown variable '" + n + "'") expr.Span.Start
                | Unary("-", operand) ->
                    let t, code = expression locals operand
                    require "int" t
                    "int", I32Const 0 :: code @ [ IntegerOperator "i32.sub" ]
                | Unary("!", operand) ->
                    let t, code = expression locals operand
                    require "bool" t
                    "bool", code @ [ IntegerOperator "i32.eqz" ]
                | Binary(("&&" | "||" as op), left, right) ->
                    let lt, lc = expression locals left
                    let rt, rc = expression locals right
                    require "bool" lt
                    require "bool" rt

                    let branches =
                        if op = "&&" then
                            IfValue(rc, [ I32Const 0 ])
                        else
                            IfValue([ I32Const 1 ], rc)

                    "bool", lc @ [ branches ]
                | Binary(op, left, right) ->
                    let lt, lc = expression locals left
                    let rt, rc = expression locals right
                    require lt rt

                    if
                        lt = "string"
                        || lt = "void"
                        || lt.StartsWith("table<", System.StringComparison.Ordinal)
                    then
                        abort "E105" "This operator requires numeric or boolean operands" expr.Span.Start

                    let resultType, instruction =
                        match op with
                        | "+" ->
                            require "int" lt
                            "int", "i32.add"
                        | "-" ->
                            require "int" lt
                            "int", "i32.sub"
                        | "*" ->
                            require "int" lt
                            "int", "i32.mul"
                        | "/" ->
                            require "int" lt
                            "int", "i32.div_s"
                        | "%" ->
                            require "int" lt
                            "int", "i32.rem_s"
                        | "==" -> "bool", "i32.eq"
                        | "!=" -> "bool", "i32.ne"
                        | "<" ->
                            require "int" lt
                            "bool", "i32.lt_s"
                        | ">" ->
                            require "int" lt
                            "bool", "i32.gt_s"
                        | "<=" ->
                            require "int" lt
                            "bool", "i32.le_s"
                        | ">=" ->
                            require "int" lt
                            "bool", "i32.ge_s"
                        | _ -> abort "E204" "This operator is not implemented by the WASM backend yet" expr.Span.Start

                    resultType, lc @ rc @ [ IntegerOperator instruction ]
                | _ -> abort "E204" "Unsupported WASM value expression" expr.Span.Start

            // Allocation is compilation-local; block scopes can reuse names, never indices.

            let checkDuplicates statements =
                let declarations =
                    statements
                    |> List.choose (fun s ->
                        match s.Kind with
                        | Variable(n, _, _) -> Some(n, s.Span.Start)
                        | _ -> None)

                declarations
                |> List.groupBy fst
                |> List.iter (fun (name, values) ->
                    if values.Length > 1 then
                        abort "E112" ("Duplicate variable '" + name + "'") (snd values.[1]))

            let rec statementCompile (locals, offset, code, data) (statement: Statement) =
                statementCore (locals, offset, code @ [ SourcePoint statement.Span.Start ], data) statement

            and statementCore (locals, offset, code, data) (statement: Statement) =
                match statement.Kind with
                | Return expr ->
                    if returnType = "void" then
                        abort "E113" "A void function cannot return a value" statement.Span.Start

                    let actual, value = expression locals expr

                    if not (compatible returnType actual) then
                        abort "E105" ("Expected " + returnType + ", got " + actual) expr.Span.Start

                    locals, offset, code @ value @ [ FunctionReturn ], data
                | ExpressionStatement({ Kind = Call _ } as expr) ->
                    let actual, value = expression locals expr
                    locals, offset, code @ value @ (if actual = "void" then [] else [ Drop ]), data
                | If(condition, yes, no) ->
                    let t, conditionCode = expression locals condition

                    if t <> "bool" then
                        abort "E105" "if condition must be bool" condition.Span.Start

                    let _, nextOffset, yesCode, yesData = block locals offset data yes
                    let _, finalOffset, noCode, finalData = block locals nextOffset yesData no
                    locals, finalOffset, code @ conditionCode @ [ IfBlock(yesCode, noCode) ], finalData
                | For(initial, condition, increment, body) ->
                    let loopLocals, initialOffset, initialCode, initialData =
                        statementCompile (locals, offset, [], data) initial

                    let t, conditionCode = expression loopLocals condition

                    if t <> "bool" then
                        abort "E105" "for condition must be bool" condition.Span.Start

                    let _, bodyOffset, bodyCode, bodyData =
                        block loopLocals initialOffset initialData body

                    let _, finalOffset, stepCode, finalData =
                        statementCompile (loopLocals, bodyOffset, [], bodyData) increment

                    locals,
                    finalOffset,
                    code @ initialCode @ [ WhileBlock(conditionCode, bodyCode @ stepCode) ],
                    finalData
                | While(condition, body) ->
                    let t, conditionCode = expression locals condition

                    if t <> "bool" then
                        abort "E105" "while condition must be bool" condition.Span.Start

                    let _, nextOffset, bodyCode, nextData = block locals offset data body
                    locals, nextOffset, code @ [ WhileBlock(conditionCode, bodyCode) ], nextData
                | Variable(n, typeRef, initial) ->
                    let t = primitive statement.Span.Start typeRef

                    if t = "void" then
                        abort "E104" "A variable cannot have type void" statement.Span.Start

                    let actual, value =
                        match initial with
                        | Some e -> expression locals e
                        | None when t = "string" -> t, [ I32Const(stringLiteral "") ]
                        | None -> t, [ I32Const 0 ]

                    if not (compatible t actual) then
                        abort "E105" ("Expected " + t + ", got " + actual) statement.Span.Start

                    let index = nextLocal.Value
                    nextLocal.Value <- index + 1
                    Map.add n (index, t) locals, offset, code @ value @ [ LocalSet index ], data
                | Assignment({ Kind = Member(receiver, name) }, rhs) ->
                    let typ, target = expression locals receiver
                    let reference, native, _ = nativeInfo packages typ receiver.Span.Start

                    match Map.tryFind (reference, native + ".set." + name) hostSignatures with
                    | None -> abort "E111" ("Property is not writable: " + name) statement.Span.Start
                    | Some(index, parameters, _) ->
                        let actual, value = expression locals rhs

                        if not (compatible parameters.[1] actual) then
                            abort "E105" "Property assignment type mismatch" rhs.Span.Start

                        locals, offset, code @ target @ value @ [ HostFunctionCall index ], data
                | Assignment({ Kind = Name n }, rhs) ->
                    match Map.tryFind n locals with
                    | None -> abort "E100" ("Unknown variable '" + n + "'") statement.Span.Start
                    | Some(index, t) ->
                        let actual, value = expression locals rhs

                        if not (compatible t actual) then
                            abort "E105" ("Expected " + t + ", got " + actual) rhs.Span.Start

                        locals, offset, code @ value @ [ LocalSet index ], data
                | ExpressionStatement({ Kind = ColonCall _ } as expr) ->
                    let actual, value = expression locals expr
                    locals, offset, code @ value @ (if actual = "void" then [] else [ Drop ]), data
                | _ -> abort "E204" "Unsupported statement in the current WASM backend" statement.Span.Start

            and block locals offset data statements =
                checkDuplicates statements
                List.fold statementCompile (locals, offset, [], data) statements

            try
                let parameterNames = main.Parameters |> List.map (fun p -> p.Name)

                if List.distinct parameterNames |> List.length <> parameterNames.Length then
                    abort "E112" "Duplicate parameter" main.Span.Start

                let parameters =
                    main.Parameters
                    |> List.mapi (fun index p -> p.Name, (index, primitive p.Span.Start p.Type))
                    |> Map.ofList

                for s in main.Body do
                    match s.Kind with
                    | Variable(n, _, _) when Map.containsKey n parameters ->
                        abort "E112" "Variable duplicates a parameter" s.Span.Start
                    | _ -> ()

                let rec returns statements =
                    statements
                    |> List.exists (fun s ->
                        match s.Kind with
                        | Return _ -> true
                        | If(_, yes, no) -> returns yes && returns no
                        | _ -> false)

                if returnType <> "void" && not (returns main.Body) then
                    abort "E114" "Not all function paths return a value" main.Span.Start

                let _, offset, code, data = block parameters startOffset [] main.Body
                let body = if returnType = "void" then code else code @ [ Unreachable ]

                Ok(
                    { Index = functionIndex
                      ParameterCount = main.Parameters.Length
                      ReturnsValue = returnType <> "void"
                      Body = body
                      LocalCount = nextLocal.Value - main.Parameters.Length },
                    max offset stringOffset.Value,
                    data @ List.ofSeq stringData
                )
            with CompilationError diagnostic ->
                Error [ diagnostic ])

    let private compilePackages (packages: Map<string, WasmPackage>) source : Result<string, Diagnostic list> =
        Parser.parse source
        |> Result.bind (fun syntax ->
            let functions =
                syntax.Members
                |> List.choose (function
                    | Function f -> Some f
                    | _ -> None)

            match
                syntax.Members
                |> List.tryPick (function
                    | Statement s -> Some s
                    | _ -> None)
            with
            | Some s -> failure "E200" "Executable statements must be inside fn main()" s.Span.Start
            | None ->
                match functions |> List.filter (fun f -> f.Name = "main") with
                | [] -> failure "E201" "A fn main(): void entry point is required" { Line = 1; Column = 1 }
                | _ :: _ :: _ -> failure "E202" "Only one main function is allowed" { Line = 1; Column = 1 }
                | [ main ] when not main.Parameters.IsEmpty || main.ReturnType <> NamedType [ "void" ] ->
                    failure "E203" "main must have no parameters and return void" main.Span.Start
                | [ main ] ->
                    try
                        let resolve position typ =
                            resolveType packages syntax None position typ

                        let signatures =
                            functions
                            |> List.mapi (fun index f ->
                                let parameters =
                                    f.Parameters
                                    |> List.map (fun p ->
                                        let t = resolve p.Span.Start p.Type

                                        if t = "void" then
                                            abort "E104" "A parameter cannot be void" p.Span.Start

                                        t)

                                f.Name, (index, parameters, resolve f.Span.Start f.ReturnType))

                        if signatures |> List.map fst |> List.distinct |> List.length <> functions.Length then
                            abort "E112" "Duplicate function name" main.Span.Start

                        let position = main.Span.Start

                        let hostEntries =
                            packages
                            |> Map.toList
                            |> List.collect (fun (reference, package) ->
                                let methods =
                                    package.Objects
                                    |> Map.toList
                                    |> List.collect (fun (name, methods) ->
                                        methods
                                        |> List.map (fun method ->
                                            { method with
                                                Name = name + "." + method.Name }))

                                let typed =
                                    package.Types
                                    |> Map.toList
                                    |> List.collect (fun (name, typ) ->
                                        let self = "self", NamedType [ name ]

                                        let methods =
                                            typ.Methods
                                            |> List.map (fun m ->
                                                { m with
                                                    Name = name + "." + m.Name
                                                    Parameters = self :: m.Parameters })

                                        let statics =
                                            typ.Statics
                                            |> List.map (fun m ->
                                                { m with
                                                    Name = name + ".static." + m.Name })

                                        let properties =
                                            typ.Properties
                                            |> List.collect (fun p ->
                                                let gm, gn = p.Getter

                                                let getter =
                                                    { Name = name + ".get." + p.Name
                                                      Parameters = [ self ]
                                                      ReturnType = p.Type
                                                      Module = gm
                                                      Import = gn }

                                                let setter =
                                                    p.Setter
                                                    |> Option.map (fun (sm, sn) ->
                                                        { Name = name + ".set." + p.Name
                                                          Parameters = [ self; "value", p.Type ]
                                                          ReturnType = NamedType [ "void" ]
                                                          Module = sm
                                                          Import = sn })

                                                getter :: Option.toList setter)

                                        methods @ statics @ properties)

                                (package.Functions @ methods @ typed)
                                |> List.map (fun signature -> reference, signature))

                        if
                            hostEntries
                            |> List.exists (fun (reference, signature) ->
                                reference <> "system" && signature.Module = "__yenscript")
                        then
                            abort "E212" "Reserved runtime host module" position

                        let hosts =
                            hostEntries
                            |> List.mapi (fun index (reference, signature) ->
                                let types =
                                    signature.Parameters
                                    |> List.map (fun (_, t) ->
                                        let resolved = resolveType packages syntax (Some reference) position t

                                        if resolved = "void" then
                                            abort "E104" "Host parameter cannot be void" position

                                        resolved)

                                { Index = index
                                  Module = signature.Module
                                  Name = signature.Import
                                  ParameterTypes = types
                                  ReturnType =
                                    resolveType packages syntax (Some reference) position signature.ReturnType })

                        let hostMap =
                            List.zip hostEntries hosts
                            |> List.map (fun ((reference, signature), host) ->
                                (reference, signature.Name), (host.Index, host.ParameterTypes, host.ReturnType))
                            |> Map.ofList

                        let signatureMap = Map.ofList signatures
                        let entry, _, _ = signatureMap.["main"]

                        let lowered =
                            functions
                            |> List.mapi (fun index f -> index, f)
                            |> List.fold
                                (fun result (index, f) ->
                                    result
                                    |> Result.bind (fun (output, offset, data) ->
                                        lowerFunction packages hostMap signatureMap index offset syntax f
                                        |> Result.map (fun (fn, nextOffset, nextData) ->
                                            output @ [ fn ], nextOffset, data @ nextData)))
                                (Ok([], 1024, []))

                        lowered
                        |> Result.map (fun (output, offset, data) ->
                            WasmEmitter.emit
                                { Hosts = hosts
                                  Functions = output
                                  EntryPoint = entry
                                  Data = data
                                  MemoryPages = max 1 (int ((int64 offset + 65535L) / 65536L)) })
                    with CompilationError diagnostic ->
                        Error [ diagnostic ])

    /// Package sources are keyed by the exact source import reference.
    /// The host resolves paths and dependencies; the compiler validates their manifests.
    let compileWithPackages (sources: Map<string, string>) source =
        let parsed =
            sources
            |> Map.toList
            |> List.map (fun (reference, text) ->
                WasmPackages.parse text |> Result.map (fun package -> reference, package))

        let errors =
            parsed
            |> List.collect (function
                | Error e -> e
                | _ -> [])

        if not errors.IsEmpty then
            Error errors
        elif Map.containsKey "system" sources then
            failure "E212" "The built-in system package cannot be replaced" { Line = 1; Column = 1 }
        else
            WasmPackages.loadSystem ()
            |> Result.bind (fun system ->
                let packages =
                    parsed
                    |> List.choose (function
                        | Ok p -> Some p
                        | _ -> None)
                    |> Map.ofList
                    |> Map.add "system" system

                let missing =
                    packages
                    |> Map.toList
                    |> List.tryPick (fun (_, package) ->
                        package.Dependencies
                        |> List.tryPick (fun (reference, version) ->
                            match Map.tryFind reference packages with
                            | None -> Some("Missing dependency: " + reference)
                            | Some dependency when dependency.Version <> version ->
                                Some("Dependency version mismatch: " + reference)
                            | _ -> None))

                let reserved =
                    packages
                    |> Map.exists (fun reference p ->
                        let declarations =
                            p.Functions
                            @ (p.Objects |> Map.toList |> List.collect snd)
                            @ (p.Types |> Map.toList |> List.collect (fun (_, t) -> t.Methods @ t.Statics))

                        reference <> "system"
                        && (p.Name = "system"
                            || (declarations |> List.exists (fun f -> f.Module = "__yenscript"))))

                let rec cycle active reference =
                    Set.contains reference active
                    || (packages.[reference].Dependencies
                        |> List.exists (fun (dependency, _) ->
                            Map.containsKey dependency packages
                            && cycle (Set.add reference active) dependency))

                let cyclic = packages |> Map.exists (fun r _ -> cycle Set.empty r)

                let conflicts =
                    packages
                    |> Map.toList
                    |> List.groupBy (fun (_, p) -> p.Name)
                    |> List.exists (fun (_, versions) -> versions |> List.map snd |> List.distinct |> List.length > 1)

                match missing with
                | Some message -> failure "E212" message { Line = 1; Column = 1 }
                | None when reserved ->
                    failure "E212" "Reserved system namespace cannot be overridden" { Line = 1; Column = 1 }
                | None when cyclic -> failure "E212" "Cyclic package dependencies" { Line = 1; Column = 1 }
                | None when conflicts ->
                    failure "E212" "Conflicting package manifests with the same name" { Line = 1; Column = 1 }
                | None -> compilePackages packages source)

    let compile source = compileWithPackages Map.empty source
