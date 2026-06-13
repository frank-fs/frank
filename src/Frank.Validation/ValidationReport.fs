module Frank.Validation.ValidationReport

open System.Collections.Generic
open System.Text.Json
open VDS.RDF.Shacl.Validation

/// Serialize a SHACL validation Report as application/problem+json.
/// Violation paths cite vocabulary IRIs from the shapes graph.
let serialize (report: Report) : string =
    let violations =
        report.Results
        |> Seq.map (fun r ->
            let path =
                if isNull r.ResultPath then
                    r.SourceConstraintComponent |> string
                else
                    r.ResultPath |> string

            let focus = if isNull r.FocusNode then "" else r.FocusNode |> string

            let doc = Dictionary<string, obj>()
            doc["path"] <- path
            doc["focusNode"] <- focus
            doc["constraintComponent"] <- (r.SourceConstraintComponent |> string)
            doc :> obj)
        |> Seq.toArray

    let body = Dictionary<string, obj>()
    body["type"] <- "https://www.w3.org/TR/shacl/#results-validation-report"
    body["title"] <- "Validation failed"
    body["status"] <- 422
    body["violations"] <- violations
    JsonSerializer.Serialize(body)
