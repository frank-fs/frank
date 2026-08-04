namespace Frank.Validation

open System
open Frank.Rdf

type Violation =
    { FocusNode: Node
      ResultPath: Uri option
      Severity: Severity
      Message: string
      ConstraintComponent: Uri
      SourceShape: Node }

[<RequireQualifiedAccess>]
type ValidationOutcome =
    | Conforms
    | Violates of Violation list
