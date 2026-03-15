namespace Frank.Validation

open VDS.RDF
open VDS.RDF.Shacl

/// Core SHACL validation logic: takes a shapes graph and data graph,
/// executes dotNetRdf SHACL validation, and returns an F# ValidationReport.
module Validator =

    let private shViolation = "http://www.w3.org/ns/shacl#Violation"
    let private shWarning = "http://www.w3.org/ns/shacl#Warning"
    let private shInfo = "http://www.w3.org/ns/shacl#Info"

    /// Map a dotNetRdf severity INode to our ValidationSeverity DU.
    let private mapSeverity (severity: INode) : ValidationSeverity =
        match severity with
        | null -> Violation
        | node ->
            let s = node.ToString()

            if s = shWarning then Warning
            elif s = shInfo then Info
            else Violation // Default to Violation for unknown or sh:Violation

    /// Convert a single dotNetRdf validation result to our F# ValidationResult record.
    let private mapResult (r: VDS.RDF.Shacl.Validation.Result) : ValidationResult =
        let focusNode =
            let fn = r.FocusNode
            if isNull fn then "" else fn.ToString()

        let resultPath =
            let rp = r.ResultPath
            if isNull (box rp) then "" else rp.ToString()

        let value =
            let rv = r.ResultValue
            if isNull rv then None else Some(box rv)

        let sourceConstraint =
            let sc = r.SourceConstraintComponent
            if isNull sc then "" else sc.ToString()

        let message =
            let m = r.Message
            if isNull m then "" else m.Value

        let severity = mapSeverity r.Severity

        { FocusNode = focusNode
          ResultPath = resultPath
          Value = value
          SourceConstraint = sourceConstraint
          Message = message
          Severity = severity }

    /// Validate a data graph against a shapes graph.
    /// Returns a ValidationReport indicating conformance and any violations.
    let validate (shapesGraph: ShapesGraph) (shapeUri: System.Uri) (dataGraph: IGraph) : ValidationReport =
        let report = shapesGraph.Validate(dataGraph)

        { Conforms = report.Conforms
          Results = report.Results |> Seq.map mapResult |> Seq.toList
          ShapeUri = shapeUri }
