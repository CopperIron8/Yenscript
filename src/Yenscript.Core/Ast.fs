namespace Yenscript.Core

/// End is exclusive; positions are one-based.
type SourceSpan = { Start: Position; End: Position }

type TypeRef =
    | NamedType of path: string list
    | TableType of elementType: TypeRef

type Expr = { Kind: ExprKind; Span: SourceSpan }

and ExprKind =
    | Integer of int
    | Text of string
    | Boolean of bool
    | Null
    | Name of string
    | Member of receiver: Expr * name: string
    | Call of callee: Expr * arguments: Expr list
    | ColonCall of receiver: Expr * name: string * arguments: Expr list
    | Table of elements: Expr list
    | Binary of operator: string * left: Expr * right: Expr
    | Unary of operator: string * operand: Expr

type Statement =
    { Kind: StatementKind
      Span: SourceSpan }

and StatementKind =
    | Variable of name: string * variableType: TypeRef * initialValue: Expr option
    | Assignment of target: Expr * value: Expr
    | ExpressionStatement of Expr
    | Return of Expr
    | If of condition: Expr * thenBody: Statement list * elseBody: Statement list
    | While of condition: Expr * body: Statement list
    | For of initializer: Statement * condition: Expr * increment: Statement * body: Statement list

type FunctionParameter =
    { Name: string
      Type: TypeRef
      Span: SourceSpan }

type FunctionDeclaration =
    { Name: string
      Parameters: FunctionParameter list
      ReturnType: TypeRef
      Body: Statement list
      Span: SourceSpan }

type TopLevel =
    | UsingPackage of packageName: string * alias: string option * span: SourceSpan
    | ImportAll of packageName: string * span: SourceSpan
    | Function of FunctionDeclaration
    | Statement of Statement

type CompilationUnit = { Members: TopLevel list }
