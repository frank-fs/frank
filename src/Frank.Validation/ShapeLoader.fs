namespace Frank.Validation

open System
open System.IO
open System.Reflection
open VDS.RDF
open VDS.RDF.Parsing

/// Deserializes SHACL NodeShapes from a dotNetRdf IGraph into F# ShaclShape values.
/// This is the reverse of ShapeGraphBuilder.buildShapesGraph.
/// TargetType is always None for loaded shapes — the CLR type information is not
/// stored in the Turtle serialization.
module ShapeLoader =

    let private sh = "http://www.w3.org/ns/shacl#"
    let private xsd = "http://www.w3.org/2001/XMLSchema#"
    let private rdf = "http://www.w3.org/1999/02/22-rdf-syntax-ns#"

    /// Predicate URIs used when reading the graph.
    let private shNodeShape = sh + "NodeShape"
    let private rdfType = rdf + "type"
    let private shClosed = sh + "closed"
    let private shProperty = sh + "property"
    let private shPath = sh + "path"
    let private shDatatype = sh + "datatype"
    let private shMinCount = sh + "minCount"
    let private shMaxCount = sh + "maxCount"
    let private shNode = sh + "node"
    let private shIn = sh + "in"
    let private shOr = sh + "or"
    let private shPattern = sh + "pattern"
    let private rdfFirst = rdf + "first"
    let private rdfRest = rdf + "rest"
    let private rdfNil = rdf + "nil"

    /// Create a URI node for lookup purposes.
    let private uriNode (g: IGraph) (uri: string) = g.CreateUriNode(UriFactory.Create(uri))

    /// Get all objects of triples (subject, predicate, *).
    let private objectsOf (g: IGraph) (subject: INode) (predicate: INode) =
        g.GetTriplesWithSubjectPredicate(subject, predicate)
        |> Seq.map (fun t -> t.Object)

    /// Get the single object of (subject, predicate, *), or None.
    let private singleObjectOf (g: IGraph) (subject: INode) (predicate: INode) =
        objectsOf g subject predicate |> Seq.tryHead

    /// Flatten an RDF list (rdf:first / rdf:rest / rdf:nil) into a sequence of nodes.
    let private flattenRdfList (g: IGraph) (listHead: INode) : INode list =
        let nilNode = uriNode g rdfNil :> INode
        let firstPred = uriNode g rdfFirst :> INode
        let restPred = uriNode g rdfRest :> INode

        let rec loop (node: INode) acc =
            if node = nilNode then
                List.rev acc
            else
                let firstOpt = singleObjectOf g node firstPred
                let restOpt = singleObjectOf g node restPred

                match firstOpt, restOpt with
                | Some first, Some rest -> loop rest (first :: acc)
                | _ -> List.rev acc

        loop listHead []

    /// Map an XSD datatype URI string to an XsdDatatype DU case.
    let private parseXsdDatatype (dtUri: string) : XsdDatatype =
        let suffix = dtUri.Replace(xsd, "")

        match suffix with
        | "string" -> XsdString
        | "integer" -> XsdInteger
        | "long" -> XsdLong
        | "double" -> XsdDouble
        | "decimal" -> XsdDecimal
        | "boolean" -> XsdBoolean
        | "dateTimeStamp" -> XsdDateTimeStamp
        | "dateTime" -> XsdDateTime
        | "date" -> XsdDate
        | "time" -> XsdTime
        | "duration" -> XsdDuration
        | "anyURI" -> XsdAnyUri
        | "base64Binary" -> XsdBase64Binary
        | _ -> Custom(Uri(dtUri))

    /// Deserialize a single property shape blank node into a PropertyShape record.
    let private loadPropertyShape (g: IGraph) (propNode: INode) : PropertyShape =
        let pathPred = uriNode g shPath :> INode
        let datatypePred = uriNode g shDatatype :> INode
        let minCountPred = uriNode g shMinCount :> INode
        let maxCountPred = uriNode g shMaxCount :> INode
        let nodePred = uriNode g shNode :> INode
        let inPred = uriNode g shIn :> INode
        let orPred = uriNode g shOr :> INode
        let patternPred = uriNode g shPattern :> INode

        // sh:path — required, URI node giving the property path
        let path =
            match singleObjectOf g propNode pathPred with
            | Some(:? IUriNode as u) ->
                let uriStr = u.Uri.ToString()
                // Strip the urn:frank:property: prefix to recover the field name
                let prefix = "urn:frank:property:"

                if uriStr.StartsWith(prefix, StringComparison.Ordinal) then
                    uriStr.Substring(prefix.Length)
                else
                    uriStr
            | _ -> ""

        // sh:datatype — optional
        let datatype =
            match singleObjectOf g propNode datatypePred with
            | Some(:? IUriNode as u) -> Some(parseXsdDatatype (u.Uri.ToString()))
            | _ -> None

        // sh:minCount — optional, defaults to 0
        let minCount =
            match singleObjectOf g propNode minCountPred with
            | Some(:? ILiteralNode as lit) ->
                match Int32.TryParse(lit.Value) with
                | true, n -> n
                | _ -> 0
            | _ -> 0

        // sh:maxCount — optional
        let maxCount =
            match singleObjectOf g propNode maxCountPred with
            | Some(:? ILiteralNode as lit) ->
                match Int32.TryParse(lit.Value) with
                | true, n -> Some n
                | _ -> None
            | _ -> None

        // sh:node — optional, URI reference to another NodeShape
        let nodeReference =
            match singleObjectOf g propNode nodePred with
            | Some(:? IUriNode as u) -> Some u.Uri
            | _ -> None

        // sh:in — optional, RDF list of literal strings
        let inValues =
            match singleObjectOf g propNode inPred with
            | Some listHead ->
                let items = flattenRdfList g listHead

                let strings =
                    items
                    |> List.choose (function
                        | :? ILiteralNode as lit -> Some lit.Value
                        | _ -> None)

                if strings.IsEmpty then None else Some strings
            | None -> None

        // sh:or — optional, RDF list of URI references
        let orShapes =
            match singleObjectOf g propNode orPred with
            | Some listHead ->
                let items = flattenRdfList g listHead

                let uris =
                    items
                    |> List.choose (function
                        | :? IUriNode as u -> Some u.Uri
                        | _ -> None)

                if uris.IsEmpty then None else Some uris
            | None -> None

        // sh:pattern — optional, string literal
        let pattern =
            match singleObjectOf g propNode patternPred with
            | Some(:? ILiteralNode as lit) -> Some lit.Value
            | _ -> None

        { Path = path
          Datatype = datatype
          MinCount = minCount
          MaxCount = maxCount
          NodeReference = nodeReference
          InValues = inValues
          OrShapes = orShapes
          Pattern = pattern
          MinInclusive = None
          MaxInclusive = None
          Description = None }

    /// Deserialize a single NodeShape URI node into a ShaclShape record.
    let private loadNodeShape (g: IGraph) (shapeNode: IUriNode) : ShaclShape =
        let closedPred = uriNode g shClosed :> INode
        let propertyPred = uriNode g shProperty :> INode

        // sh:closed
        let closed =
            match singleObjectOf g (shapeNode :> INode) closedPred with
            | Some(:? ILiteralNode as lit) -> lit.Value.Equals("true", StringComparison.OrdinalIgnoreCase)
            | _ -> false

        // sh:property
        let properties =
            objectsOf g (shapeNode :> INode) propertyPred
            |> Seq.map (loadPropertyShape g)
            |> Seq.toList

        { TargetType = None
          NodeShapeUri = shapeNode.Uri
          Properties = properties
          Closed = closed
          Description = None }

    /// Load all SHACL NodeShapes from an IGraph.
    /// Returns one ShaclShape per subject that has rdf:type sh:NodeShape.
    let loadFromGraph (graph: IGraph) : ShaclShape list =
        let rdfTypePred = uriNode graph rdfType :> INode
        let nodeShapeClass = uriNode graph shNodeShape :> INode

        graph.GetTriplesWithPredicateObject(rdfTypePred, nodeShapeClass)
        |> Seq.choose (fun t ->
            match t.Subject with
            | :? IUriNode as u -> Some(loadNodeShape graph u)
            | _ -> None)
        |> Seq.toList

    /// Load SHACL shapes from an embedded Turtle resource in the given assembly.
    /// The resource must be named "Frank.Semantic.shapes.shacl.ttl" (relative to manifest).
    /// Raises InvalidOperationException if the resource is not found.
    let loadFromAssembly (assembly: Assembly) : ShaclShape list =
        let resourceName = "Frank.Semantic.shapes.shacl.ttl"

        let stream = assembly.GetManifestResourceStream(resourceName)

        if isNull stream then
            let available = assembly.GetManifestResourceNames() |> String.concat ", "

            raise (
                InvalidOperationException(
                    sprintf
                        "Embedded resource '%s' not found in assembly '%s'. Available resources: %s"
                        resourceName
                        (assembly.GetName().Name)
                        available
                )
            )

        use s = stream
        let graph = new Graph()
        let parser = TurtleParser()
        use reader = new StreamReader(s)
        parser.Load(graph, reader)
        loadFromGraph graph
