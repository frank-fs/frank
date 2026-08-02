namespace Frank.Rdf

open System

[<AutoOpen>]
module Rdf =
    let internal resolveIri (prefixes: (string * string) list) (s: string) : string =
        match s.IndexOf ':' with
        | -1 -> failwithf "Frank.Rdf: '%s' is neither an absolute IRI nor a CURIE (no ':')" s
        | i ->
            let prefix = s.Substring(0, i)

            match prefixes |> List.tryFind (fun (p, _) -> p = prefix) with
            | Some(_, ns) -> ns + s.Substring(i + 1)
            | None ->
                if Uri.IsWellFormedUriString(s, UriKind.Absolute) then
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
