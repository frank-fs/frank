namespace Frank.Cli.Core.Commands

open System
open System.IO
open VDS.RDF
open Frank.Cli.Core.Rdf
open Frank.Cli.Core.Rdf.Vocabularies
open Frank.Cli.Core.State

/// Validates completeness and consistency of extracted semantic definitions.
module ValidateCommand =

    type ValidationIssue =
        { Severity: string
          Message: string
          Uri: Uri option }

    type ValidateResult =
        { Issues: ValidationIssue list
          CoveragePercent: float
          IsValid: bool }

    let private uriEquals (a: Uri) (b: Uri) =
        a.AbsoluteUri = b.AbsoluteUri

    let private findOwlClasses (graph: IGraph) : Uri list =
        let rdfTypeNode = createUriNode graph (Uri Rdf.Type)
        let owlClassUri = Uri Owl.Class

        triplesWithPredicate graph rdfTypeNode
        |> Seq.filter (fun t ->
            match t.Object with
            | :? IUriNode as on -> uriEquals on.Uri owlClassUri
            | _ -> false)
        |> Seq.choose (fun t ->
            match t.Subject with
            | :? IUriNode as sn -> Some sn.Uri
            | _ -> None)
        |> Seq.toList

    let private checkClassesHaveProperties (graph: IGraph) (classes: Uri list) : ValidationIssue list =
        let domainNode = createUriNode graph (Uri Rdfs.Domain)

        // Collect all URIs that appear as rdfs:domain objects (i.e., classes that have properties)
        let classesWithProperties =
            triplesWithPredicate graph domainNode
            |> Seq.choose (fun t ->
                match t.Object with
                | :? IUriNode as on -> Some on.Uri.AbsoluteUri
                | _ -> None)
            |> Set.ofSeq

        classes
        |> List.choose (fun classUri ->
            if classesWithProperties.Contains classUri.AbsoluteUri then
                None
            else
                Some
                    { Severity = "warning"
                      Message = $"owl:Class '%s{classUri.AbsoluteUri}' has no properties (no rdfs:domain references it)"
                      Uri = Some classUri })

    let private checkShapeTargetClasses (ontology: IGraph) (shapes: IGraph) : ValidationIssue list =
        let rdfTypeNode = createUriNode shapes (Uri Rdf.Type)
        let nodeShapeUri = Uri Shacl.NodeShape
        let targetClassNode = createUriNode shapes (Uri Shacl.TargetClass)

        // Collect all owl:Class URIs from ontology
        let ontologyClasses =
            findOwlClasses ontology
            |> List.map (fun u -> u.AbsoluteUri)
            |> Set.ofList

        // Find all NodeShapes
        let nodeShapes =
            triplesWithPredicate shapes rdfTypeNode
            |> Seq.filter (fun t ->
                match t.Object with
                | :? IUriNode as on -> uriEquals on.Uri nodeShapeUri
                | _ -> false)
            |> Seq.choose (fun t ->
                match t.Subject with
                | :? IUriNode as sn -> Some sn
                | _ -> None)
            |> Seq.toList

        nodeShapes
        |> List.collect (fun shapeNode ->
            triplesWithSubjectPredicate shapes (shapeNode :> INode) targetClassNode
            |> Seq.choose (fun t ->
                match t.Object with
                | :? IUriNode as targetUri ->
                    if ontologyClasses.Contains targetUri.Uri.AbsoluteUri then
                        None
                    else
                        Some
                            { Severity = "error"
                              Message = $"SHACL NodeShape '%s{shapeNode.Uri.AbsoluteUri}' targets class '%s{targetUri.Uri.AbsoluteUri}' which does not exist in the ontology"
                              Uri = Some shapeNode.Uri }
                | _ -> None)
            |> Seq.toList)

    let private checkUnmappedTypes (unmappedTypes: UnmappedType list) : ValidationIssue list =
        unmappedTypes
        |> List.map (fun ut ->
            { Severity = "warning"
              Message = $"Type '%s{ut.TypeName}' was not mapped: %s{ut.Reason} (at %s{ut.Location.File}:%d{ut.Location.Line})"
              Uri = None })

    let execute (projectPath: string) : Result<ValidateResult, string> =
        let statePath = ExtractionState.defaultStatePath (Path.GetDirectoryName projectPath)

        match ExtractionState.load statePath with
        | Error e -> Error $"Failed to load state: {e}"
        | Ok state ->

        let owlClasses = findOwlClasses state.Ontology
        let propertyIssues = checkClassesHaveProperties state.Ontology owlClasses
        let shapeIssues = checkShapeTargetClasses state.Ontology state.Shapes
        let unmappedIssues = checkUnmappedTypes state.UnmappedTypes

        let allIssues = propertyIssues @ shapeIssues @ unmappedIssues

        let mappedClassCount = owlClasses.Length |> float
        let totalAnalyzedTypeCount = (owlClasses.Length + state.UnmappedTypes.Length) |> float

        let coveragePercent =
            if totalAnalyzedTypeCount > 0.0 then
                (mappedClassCount / totalAnalyzedTypeCount) * 100.0
            else
                100.0

        let isValid =
            allIssues
            |> List.exists (fun i -> i.Severity = "error")
            |> not

        Ok
            { Issues = allIssues
              CoveragePercent = coveragePercent
              IsValid = isValid }
