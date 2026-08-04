namespace Frank.Validation

open System
open Frank.Rdf

/// One SHACL validation-report result, typed. See ResultPath's doc comment for a disclosed
/// simplification versus a fully round-tripped PropertyPath.
type Violation =
    {
        FocusNode: Node
        /// Some uri for a simple-predicate path; None when the violated property's path is complex
        /// (sh:alternativePath/sh:inversePath/...) -- not round-tripped back to PropertyPath in v1.
        ResultPath: Uri option
        Severity: Severity
        Message: string
        ConstraintComponent: Uri
        SourceShape: Node
    }

[<RequireQualifiedAccess>]
type ValidationOutcome =
    | Conforms
    | Violates of Violation list
