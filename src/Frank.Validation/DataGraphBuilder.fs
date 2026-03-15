namespace Frank.Validation

open System
open System.Text.Json
open Microsoft.AspNetCore.Http
open VDS.RDF

/// Converts request data (JSON body or query parameters) into dotNetRdf IGraph
/// instances for validation against a SHACL shapes graph.
module DataGraphBuilder =

    let private xsd = "http://www.w3.org/2001/XMLSchema#"

    /// Convert a JSON value to an RDF literal node, using the property's expected XSD datatype
    /// for type-appropriate literals.
    let private jsonValueToNode (g: IGraph) (prop: PropertyShape) (value: JsonElement) : INode =
        match prop.Datatype with
        | Some XsdBoolean ->
            match value.ValueKind with
            | JsonValueKind.True -> g.CreateLiteralNode("true", UriFactory.Create(xsd + "boolean")) :> INode
            | JsonValueKind.False -> g.CreateLiteralNode("false", UriFactory.Create(xsd + "boolean")) :> INode
            | _ -> g.CreateLiteralNode(value.GetRawText(), UriFactory.Create(xsd + "boolean")) :> INode
        | Some XsdInteger -> g.CreateLiteralNode(value.GetRawText(), UriFactory.Create(xsd + "integer")) :> INode
        | Some XsdLong -> g.CreateLiteralNode(value.GetRawText(), UriFactory.Create(xsd + "long")) :> INode
        | Some XsdDouble -> g.CreateLiteralNode(value.GetRawText(), UriFactory.Create(xsd + "double")) :> INode
        | Some XsdDecimal -> g.CreateLiteralNode(value.GetRawText(), UriFactory.Create(xsd + "decimal")) :> INode
        | Some dt ->
            let dtUri = TypeMapping.xsdUri dt
            g.CreateLiteralNode(value.GetString(), dtUri) :> INode
        | None ->
            // No datatype: treat as plain string literal
            g.CreateLiteralNode(value.ToString()) :> INode

    /// Convert a string value to an RDF literal node, coercing to the expected XSD datatype.
    /// Used for query parameter values which arrive as strings.
    let private stringValueToNode (g: IGraph) (prop: PropertyShape) (value: string) : INode =
        match prop.Datatype with
        | Some dt ->
            let dtUri = TypeMapping.xsdUri dt
            g.CreateLiteralNode(value, dtUri) :> INode
        | None -> g.CreateLiteralNode(value) :> INode

    /// Build a data graph from a JSON body, using the shape's property paths
    /// to map JSON properties to RDF predicates.
    /// The returned graph should be disposed after validation via `use` binding.
    let buildFromJsonBody (shape: ShaclShape) (json: JsonElement) : IGraph =
        let g = new Graph()
        let focusNode = g.CreateUriNode(UriFactory.Create(ShapeGraphBuilder.FocusNodeUri))

        for prop in shape.Properties do
            match json.TryGetProperty(prop.Path) with
            | true, value ->
                match value.ValueKind with
                | JsonValueKind.Null -> () // Missing/null: minCount validation will catch this
                | JsonValueKind.Array ->
                    // Array values: create multiple triples for the same predicate
                    let predicate =
                        g.CreateUriNode(UriFactory.Create(UriConventions.buildPropertyPathUri prop.Path))

                    for i in 0 .. value.GetArrayLength() - 1 do
                        let elem = value.[i]
                        let obj = jsonValueToNode g prop elem
                        g.Assert(focusNode, predicate, obj)
                | _ ->
                    let predicate =
                        g.CreateUriNode(UriFactory.Create(UriConventions.buildPropertyPathUri prop.Path))

                    let obj = jsonValueToNode g prop value
                    g.Assert(focusNode, predicate, obj)
            | false, _ -> () // Missing property: minCount validation will catch this

        g :> IGraph

    /// Build a data graph from query string parameters.
    /// The returned graph should be disposed after validation via `use` binding.
    let buildFromQueryParams (shape: ShaclShape) (query: IQueryCollection) : IGraph =
        let g = new Graph()
        let focusNode = g.CreateUriNode(UriFactory.Create(ShapeGraphBuilder.FocusNodeUri))

        for prop in shape.Properties do
            match query.TryGetValue(prop.Path) with
            | true, values ->
                let predicate =
                    g.CreateUriNode(UriFactory.Create(UriConventions.buildPropertyPathUri prop.Path))

                for v in values do
                    let obj = stringValueToNode g prop (v.ToString())
                    g.Assert(focusNode, predicate, obj)
            | false, _ -> ()

        g :> IGraph
