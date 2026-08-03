namespace Frank.Rdf

open System

[<AutoOpen>]
module Rdf =
    let private nonHierarchicalAbsoluteSchemes = [ "urn:"; "mailto:"; "tel:" ]

    let internal resolveIri (prefixes: (string * string) list) (s: string) : string =
        match s.IndexOf ':' with
        | -1 -> failwithf "Frank.Rdf: '%s' is neither an absolute IRI nor a CURIE (no ':')" s
        | i ->
            let prefix = s.Substring(0, i)

            match prefixes |> List.tryFind (fun (p, _) -> p = prefix) with
            | Some(_, ns) -> ns + s.Substring(i + 1)
            | None ->
                let looksAbsolute =
                    s.Contains "://"
                    || nonHierarchicalAbsoluteSchemes |> List.exists s.StartsWith

                if looksAbsolute && Uri.IsWellFormedUriString(s, UriKind.Absolute) then
                    s
                else
                    failwithf "Frank.Rdf: undeclared prefix '%s' in '%s'" prefix s

    let internal validatePrefixes (prefixes: (string * string) list) : unit =
        prefixes
        |> List.groupBy fst
        |> List.iter (fun (prefix, entries) ->
            let uris = entries |> List.map snd |> List.distinct

            if uris.Length > 1 then
                failwithf "Frank.Rdf: prefix '%s' declared with conflicting URIs: %s" prefix (String.concat ", " uris))

    let RdfTypeIri = "http://www.w3.org/1999/02/22-rdf-syntax-ns#type"

    [<Sealed>]
    type DescribeBuilder(subject: Node) =
        member _.Yield(_) : Description = { Subject = subject; Statements = [] }
        member _.Zero() : Description = { Subject = subject; Statements = [] }
        member _.Run(d: Description) : Description = d

        [<CustomOperation("typ")>]
        member _.Typ(d: Description, curie: string) : Description =
            { d with Statements = d.Statements @ [ RdfTypeIri, Value.Node(Node.Iri curie) ] }

        [<CustomOperation("propertyString")>]
        member _.PropertyString(d: Description, predicate: string, value: string) : Description =
            { d with Statements = d.Statements @ [ predicate, Value.Literal(Literal.String value) ] }

        [<CustomOperation("propertyInt")>]
        member _.PropertyInt(d: Description, predicate: string, value: int) : Description =
            { d with Statements = d.Statements @ [ predicate, Value.Literal(Literal.Int value) ] }

        [<CustomOperation("propertyBool")>]
        member _.PropertyBool(d: Description, predicate: string, value: bool) : Description =
            { d with Statements = d.Statements @ [ predicate, Value.Literal(Literal.Bool value) ] }

        [<CustomOperation("propertyDateTime")>]
        member _.PropertyDateTime(d: Description, predicate: string, value: System.DateTimeOffset) : Description =
            { d with Statements = d.Statements @ [ predicate, Value.Literal(Literal.DateTime value) ] }

        [<CustomOperation("propertyNode")>]
        member _.PropertyNode(d: Description, predicate: string, value: Node) : Description =
            { d with Statements = d.Statements @ [ predicate, Value.Node value ] }

    let describe subject = DescribeBuilder(subject)

    module Doc =
        open VDS.RDF
        open VDS.RDF.Writing

        let private toGraphNode (graph: Graph) (prefixes: (string * string) list) (node: Node) : INode =
            match node with
            | Node.Iri s -> graph.CreateUriNode(Uri(resolveIri prefixes s)) :> INode
            | Node.Blank id -> graph.CreateBlankNode(id) :> INode

        let private toLiteralNode (graph: Graph) (literal: Literal) : INode =
            match literal with
            | Literal.String s -> graph.CreateLiteralNode(s) :> INode
            | Literal.Int i -> i.ToLiteral(graph)
            | Literal.Bool b -> b.ToLiteral(graph)
            | Literal.DateTime dt -> dt.ToLiteral(graph)

        let private toObjectNode (graph: Graph) (prefixes: (string * string) list) (value: Value) : INode =
            match value with
            | Value.Node n -> toGraphNode graph prefixes n
            | Value.Literal l -> toLiteralNode graph l

        let toGraph (doc: Doc) : Graph =
            validatePrefixes doc.Prefixes

            let graph = new Graph()

            for prefixName, uri in doc.Prefixes do
                graph.NamespaceMap.AddNamespace(prefixName, Uri(uri))

            for subject, predicate, value in doc.Statements do
                let s = toGraphNode graph doc.Prefixes subject
                let p = graph.CreateUriNode(Uri(resolveIri doc.Prefixes predicate))
                let o = toObjectNode graph doc.Prefixes value
                graph.Assert(Triple(s, p, o)) |> ignore

            graph

        let writeJsonLd (doc: Doc) (writer: System.IO.TextWriter) : unit =
            let graph = toGraph doc
            let store = new TripleStore()
            store.Add(graph) |> ignore
            (new JsonLdWriter()).Save(store, writer, true)

        let toJsonLd (doc: Doc) : string =
            use writer = new System.IO.StringWriter()
            writeJsonLd doc writer
            writer.ToString()

        let merge (a: Doc) (b: Doc) : Doc =
            { Prefixes = a.Prefixes @ b.Prefixes
              Statements = a.Statements @ b.Statements }

    [<Sealed>]
    type RdfBuilder() =
        member _.Yield(_) : Doc = Doc.Empty
        member _.Run(doc: Doc) : Doc = doc

        [<CustomOperation("prefix")>]
        member _.Prefix(doc: Doc, name: string, uri: string) : Doc =
            { doc with
                Prefixes = doc.Prefixes @ [ name, uri ] }

        [<CustomOperation("about")>]
        member _.About(doc: Doc, d: Description) : Doc =
            { doc with
                Statements = doc.Statements @ (d.Statements |> List.map (fun (p, v) -> d.Subject, p, v)) }

        [<CustomOperation("triple")>]
        member _.Triple(doc: Doc, subject: Node, predicate: string, value: Value) : Doc =
            { doc with
                Statements = doc.Statements @ [ subject, predicate, value ] }

        [<CustomOperation("includeDoc")>]
        member _.IncludeDoc(doc: Doc, other: Doc) : Doc = Doc.merge doc other

    let rdf = RdfBuilder()
