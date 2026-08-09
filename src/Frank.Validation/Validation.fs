namespace Frank.Validation

open System
open Frank.Rdf

type Violation =
    { FocusNode: Value
      ResultPath: Uri option
      Severity: Severity
      Message: string
      ConstraintComponent: Uri
      SourceShape: Node }

[<Struct>]
[<RequireQualifiedAccess>]
type ValidationOutcome =
    | Conforms
    | Violates of Violation list
