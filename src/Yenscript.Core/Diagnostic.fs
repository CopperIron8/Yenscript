namespace Yenscript.Core

type Position = { Line: int; Column: int }

[<RequireQualifiedAccess>]
type Severity =
    | Error
    | Warning

type Diagnostic =
    { Severity: Severity
      Code: string
      Message: string
      Position: Position }
