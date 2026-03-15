namespace Frank.Validation

open System
open VDS.RDF
open VDS.RDF.Shacl

/// Converts F# ShaclShape types into dotNetRdf ShapesGraph instances for SHACL validation.
module ShapeGraphBuilder =

    let private sh = "http://www.w3.org/ns/shacl#"
    let private xsd = "http://www.w3.org/2001/XMLSchema#"
    let private rdf = "http://www.w3.org/1999/02/22-rdf-syntax-ns#"

    /// The URI used as the focus node in data graphs. The shapes graph targets this
    /// URI via sh:targetNode so that SHACL constraints are applied to request data.
    [<Literal>]
    let FocusNodeUri = "urn:frank:validation:request"

    /// Create a URI node in the given graph.
    let private uriNode (g: IGraph) (uri: string) = g.CreateUriNode(UriFactory.Create(uri))

    /// Create a typed literal node for an integer value.
    let private intLiteral (g: IGraph) (value: int) =
        g.CreateLiteralNode(string value, UriFactory.Create(xsd + "integer"))

    /// Build an RDF list from a sequence of nodes using rdf:first/rdf:rest/rdf:nil.
    let private buildRdfList (g: IGraph) (items: INode list) : INode =
        let rdfFirst = uriNode g (rdf + "first")
        let rdfRest = uriNode g (rdf + "rest")
        let rdfNil = uriNode g (rdf + "nil") :> INode

        match items with
        | [] -> rdfNil
        | _ ->
            let nodes = items |> List.map (fun item -> g.CreateBlankNode(), item)

            nodes
            |> List.iteri (fun i (bnode, item) ->
                g.Assert(bnode, rdfFirst, item)

                let next =
                    if i < nodes.Length - 1 then
                        fst nodes.[i + 1] :> INode
                    else
                        rdfNil

                g.Assert(bnode, rdfRest, next))

            fst nodes.[0] :> INode

    /// Add property shape triples to the graph for a single PropertyShape.
    let private addPropertyShape (g: IGraph) (shapeNode: INode) (prop: PropertyShape) =
        let shProperty = uriNode g (sh + "property")
        let propNode = g.CreateBlankNode()
        g.Assert(shapeNode, shProperty, propNode)

        // sh:path
        let shPath = uriNode g (sh + "path")
        let pathUri = uriNode g (UriConventions.buildPropertyPathUri prop.Path)
        g.Assert(propNode, shPath, pathUri :> INode)

        // sh:datatype
        match prop.Datatype with
        | Some dt ->
            let shDatatype = uriNode g (sh + "datatype")
            let dtUri = uriNode g (TypeMapping.xsdUri(dt).ToString())
            g.Assert(propNode, shDatatype, dtUri :> INode)
        | None -> ()

        // sh:minCount
        if prop.MinCount > 0 then
            let shMinCount = uriNode g (sh + "minCount")
            g.Assert(propNode, shMinCount, intLiteral g prop.MinCount)

        // sh:maxCount
        match prop.MaxCount with
        | Some mc ->
            let shMaxCount = uriNode g (sh + "maxCount")
            g.Assert(propNode, shMaxCount, intLiteral g mc)
        | None -> ()

        // sh:node (nested record reference)
        match prop.NodeReference with
        | Some nodeUri ->
            let shNode = uriNode g (sh + "node")
            let nodeRef = uriNode g (nodeUri.ToString())
            g.Assert(propNode, shNode, nodeRef :> INode)
        | None -> ()

        // sh:in (simple DU values as RDF list)
        match prop.InValues with
        | Some values ->
            let shIn = uriNode g (sh + "in")

            let valueNodes = values |> List.map (fun v -> g.CreateLiteralNode(v) :> INode)

            let listNode = buildRdfList g valueNodes
            g.Assert(propNode, shIn, listNode)
        | None -> ()

        // sh:or (payload DU case references as RDF list)
        match prop.OrShapes with
        | Some uris ->
            let shOr = uriNode g (sh + "or")

            let shapeNodes = uris |> List.map (fun u -> uriNode g (u.ToString()) :> INode)

            let listNode = buildRdfList g shapeNodes
            g.Assert(propNode, shOr, listNode)
        | None -> ()

        // sh:pattern
        match prop.Pattern with
        | Some pattern ->
            let shPattern = uriNode g (sh + "pattern")
            g.Assert(propNode, shPattern, g.CreateLiteralNode(pattern))
        | None -> ()

    /// Build a dotNetRdf ShapesGraph from an F# ShaclShape.
    /// This is intended to be called once at startup and cached.
    let buildShapesGraph (shape: ShaclShape) : ShapesGraph =
        let g = new Graph()
        g.NamespaceMap.AddNamespace("sh", UriFactory.Create(sh))
        g.NamespaceMap.AddNamespace("xsd", UriFactory.Create(xsd))
        g.NamespaceMap.AddNamespace("rdf", UriFactory.Create(rdf))

        // Create NodeShape
        let shapeNode = g.CreateUriNode(shape.NodeShapeUri)
        let rdfType = uriNode g (rdf + "type")
        let nodeShapeType = uriNode g (sh + "NodeShape")
        g.Assert(shapeNode, rdfType, nodeShapeType :> INode)

        // sh:targetNode — target the well-known focus node URI used by DataGraphBuilder
        let shTargetNode = uriNode g (sh + "targetNode")
        let focusTarget = uriNode g FocusNodeUri
        g.Assert(shapeNode, shTargetNode, focusTarget :> INode)

        // sh:closed
        if shape.Closed then
            let shClosed = uriNode g (sh + "closed")

            g.Assert(shapeNode, shClosed, g.CreateLiteralNode("true", UriFactory.Create(xsd + "boolean")))

        // Add property shapes
        for prop in shape.Properties do
            addPropertyShape g (shapeNode :> INode) prop

        new ShapesGraph(g)
