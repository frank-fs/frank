namespace Frank.Cli.Core.Extraction

open System
open VDS.RDF
open Frank.Cli.Core.Rdf
open Frank.Cli.Core.Rdf.FSharpRdf
open Frank.Cli.Core.Rdf.Vocabularies
open Frank.Cli.Core.Analysis
open Frank.Statecharts.Unified
open Frank.Cli.Core.Extraction.UriHelpers

/// Generates validation-grade SHACL shapes from analyzed F# types.
module ShapeGenerator =

    let private baseStr (config: TypeMapper.MappingConfig) = config.BaseUri.ToString().TrimEnd('/')

    // Validation-style URIs (urn:frank:* scheme matching Frank.Validation conventions)
    let private validationShapeUri (fullName: string) =
        let encoded = Uri.EscapeDataString(fullName)
        Uri(sprintf "urn:frank:shape:%s" encoded)

    let private validationPropertyUri (fieldName: string) =
        Uri(sprintf "urn:frank:property:%s" fieldName)

    let private validationRequestUri = Uri "urn:frank:validation:request"

    let private uuidPattern =
        "^[0-9a-fA-F]{8}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{12}$"

    let private fieldMinCount (kind: FieldKind) : int =
        match kind with
        | Primitive _ -> 1
        | Guid -> 1
        | Optional _ -> 0
        | Collection _ -> 0
        | Reference _ -> 1

    /// Build an RDF list (rdf:first/rdf:rest/rdf:nil) from a sequence of nodes.
    let private buildRdfList (graph: IGraph) (items: INode list) : INode =
        let rdfFirst =
            graph.CreateUriNode(UriFactory.Create("http://www.w3.org/1999/02/22-rdf-syntax-ns#first"))

        let rdfRest =
            graph.CreateUriNode(UriFactory.Create("http://www.w3.org/1999/02/22-rdf-syntax-ns#rest"))

        let rdfNil =
            graph.CreateUriNode(UriFactory.Create("http://www.w3.org/1999/02/22-rdf-syntax-ns#nil"))

        let rec build items =
            match items with
            | [] -> rdfNil :> INode
            | head :: tail ->
                let node = graph.CreateBlankNode()
                let restNode = build tail
                graph.Assert(Triple(node, rdfFirst, head)) |> ignore
                graph.Assert(Triple(node, rdfRest, restNode)) |> ignore
                node :> INode

        build items

    let private assertPropertyShape
        (graph: IGraph)
        (config: TypeMapper.MappingConfig)
        (shapeNode: INode)
        (typeName: string)
        (field: AnalyzedField)
        (index: int)
        =
        let blankId = sprintf "ps_%s_%s_%d" typeName field.Name index
        let psNode = createBlankNode graph blankId

        // Link shape to property shape
        assertTriple graph (shapeNode, createUriNode graph (Uri Shacl.Property), psNode)

        // sh:path — use urn:frank:property scheme
        let propUri = validationPropertyUri field.Name
        assertTriple graph (psNode, createUriNode graph (Uri Shacl.Path), createUriNode graph propUri)

        // sh:datatype or sh:class
        let b = baseStr config
        let range, isObj = fieldKindToRange b field.Kind

        if isObj then
            assertTriple graph (psNode, createUriNode graph (Uri Shacl.Class), createUriNode graph range)

            // sh:node reference to the nested shape
            match field.Kind with
            | Reference refTypeName ->
                let nestedShapeUri = validationShapeUri refTypeName
                assertTriple graph (psNode, createUriNode graph (Uri Shacl.Node), createUriNode graph nestedShapeUri)
            | _ -> ()
        else
            assertTriple graph (psNode, createUriNode graph (Uri Shacl.Datatype), createUriNode graph range)

        // sh:minCount
        let minCount = fieldMinCount field.Kind

        assertTriple
            graph
            (psNode,
             createUriNode graph (Uri Shacl.MinCount),
             createLiteralNode graph (string minCount) (Some(Uri Xsd.Integer)))

        // sh:maxCount 1 for scalar fields
        if field.IsScalar then
            assertTriple
                graph
                (psNode, createUriNode graph (Uri Shacl.MaxCount), createLiteralNode graph "1" (Some(Uri Xsd.Integer)))

        // sh:pattern for Guid fields
        match field.Kind with
        | Guid ->
            assertTriple
                graph
                (psNode, createUriNode graph (Uri Shacl.Pattern), createLiteralNode graph uuidPattern None)
        | _ -> ()

        // Emit custom constraint triples from attributes
        for c in field.Constraints do
            match c with
            | PatternAttr regex ->
                assertTriple graph (psNode, createUriNode graph (Uri Shacl.Pattern), createLiteralNode graph regex None)
            | MinInclusiveAttr value ->
                assertTriple
                    graph
                    (psNode,
                     createUriNode graph (Uri Shacl.MinInclusive),
                     createLiteralNode graph (string value) (Some(Uri Xsd.Decimal)))
            | MaxInclusiveAttr value ->
                assertTriple
                    graph
                    (psNode,
                     createUriNode graph (Uri Shacl.MaxInclusive),
                     createLiteralNode graph (string value) (Some(Uri Xsd.Decimal)))
            | MinLengthAttr length ->
                assertTriple
                    graph
                    (psNode,
                     createUriNode graph (Uri Shacl.MinLength),
                     createLiteralNode graph (string length) (Some(Uri Xsd.Integer)))
            | MaxLengthAttr length ->
                assertTriple
                    graph
                    (psNode,
                     createUriNode graph (Uri Shacl.MaxLength),
                     createLiteralNode graph (string length) (Some(Uri Xsd.Integer)))

    let private generateShapeForType
        (graph: IGraph)
        (config: TypeMapper.MappingConfig)
        (analyzedType: AnalyzedType)
        (visited: Set<string> ref)
        (depth: int)
        (maxDepth: int)
        =
        if (!visited).Contains(analyzedType.FullName) then
            () // Already emitted — sh:node references point here
        elif depth > maxDepth then
            // Depth limit: emit minimal NodeShape with no properties
            let sUri = validationShapeUri analyzedType.FullName
            let sNode = createUriNode graph sUri
            let rdfType = createUriNode graph (Uri Rdf.Type)
            assertTriple graph (sNode, rdfType, createUriNode graph (Uri Shacl.NodeShape))

            assertTriple
                graph
                (sNode, createUriNode graph (Uri Shacl.TargetNode), createUriNode graph validationRequestUri)
        else
            visited.Value <- Set.add analyzedType.FullName !visited

            let fields =
                match analyzedType.Kind with
                | Record fields -> [ (analyzedType.FullName, analyzedType.ShortName, fields) ]
                | DiscriminatedUnion cases ->
                    cases
                    |> List.filter (fun c -> not c.Fields.IsEmpty)
                    |> List.map (fun c -> (sprintf "%s.%s" analyzedType.FullName c.Name, c.Name, c.Fields))
                    |> fun caseShapes -> (analyzedType.FullName, analyzedType.ShortName, []) :: caseShapes
                | Enum _ -> [ (analyzedType.FullName, analyzedType.ShortName, []) ]

            for (fullName, shortName, typeFields) in fields do
                let sUri = validationShapeUri fullName
                let sNode = createUriNode graph sUri
                let rdfType = createUriNode graph (Uri Rdf.Type)

                // rdf:type sh:NodeShape
                assertTriple graph (sNode, rdfType, createUriNode graph (Uri Shacl.NodeShape))

                // sh:targetNode urn:frank:validation:request
                assertTriple
                    graph
                    (sNode, createUriNode graph (Uri Shacl.TargetNode), createUriNode graph validationRequestUri)

                // sh:closed true for records
                if analyzedType.IsClosed then
                    assertTriple
                        graph
                        (sNode,
                         createUriNode graph (Uri Shacl.Closed),
                         createLiteralNode graph "true" (Some(Uri Xsd.Boolean)))

                // Property shapes
                typeFields
                |> List.iteri (fun i field -> assertPropertyShape graph config sNode shortName field i)

            // Emit sh:in for simple DUs (all cases have no fields)
            match analyzedType.Kind with
            | DiscriminatedUnion cases when cases |> List.forall (fun c -> c.Fields.IsEmpty) ->
                let sUri = validationShapeUri analyzedType.FullName
                let sNode = createUriNode graph sUri

                let caseNodes = cases |> List.map (fun c -> createLiteralNode graph c.Name None)

                let rdfList = buildRdfList graph caseNodes
                assertTriple graph (sNode, createUriNode graph (Uri Shacl.In), rdfList)
            | DiscriminatedUnion cases when cases |> List.exists (fun c -> not c.Fields.IsEmpty) ->
                let sUri = validationShapeUri analyzedType.FullName
                let sNode = createUriNode graph sUri

                let caseShapeNodes =
                    cases
                    |> List.filter (fun c -> not c.Fields.IsEmpty)
                    |> List.map (fun c ->
                        let caseUri = validationShapeUri (sprintf "%s.%s" analyzedType.FullName c.Name)

                        createUriNode graph caseUri)

                let rdfList = buildRdfList graph caseShapeNodes
                assertTriple graph (sNode, createUriNode graph (Uri Shacl.Or), rdfList)
            | _ -> ()

    let generateShapes (config: TypeMapper.MappingConfig) (types: AnalyzedType list) : IGraph =
        let graph = createGraph ()
        let visited = ref Set.empty

        for t in types do
            generateShapeForType graph config t visited 0 5

        graph
