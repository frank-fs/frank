namespace Frank.Rdf

[<AutoOpen>]
module Builder =
    [<Sealed>]
    type DescribeBuilder(subject: Node) =
        // Not `inline`: FS1113 -- the body captures `subject`, a private constructor field, which
        // isn't accessible enough for source-level inlining across assembly boundaries.
        member _.Yield(_) : Description = { Subject = subject; Statements = [] }
        member _.Zero() : Description = { Subject = subject; Statements = [] }
        member inline _.Run(d: Description) : Description = d

        [<CustomOperation("typ")>]
        member inline _.Typ(d: Description, curie: string) : Description =
            { d with Statements = d.Statements @ [ RdfTypeIri, Value.Node(Node.Iri curie) ] }

        [<CustomOperation("propertyString")>]
        member inline _.PropertyString(d: Description, predicate: string, value: string) : Description =
            { d with Statements = d.Statements @ [ predicate, Value.Literal(Literal.String value) ] }

        [<CustomOperation("propertyInt")>]
        member inline _.PropertyInt(d: Description, predicate: string, value: int) : Description =
            { d with Statements = d.Statements @ [ predicate, Value.Literal(Literal.Int value) ] }

        [<CustomOperation("propertyBool")>]
        member inline _.PropertyBool(d: Description, predicate: string, value: bool) : Description =
            { d with Statements = d.Statements @ [ predicate, Value.Literal(Literal.Bool value) ] }

        [<CustomOperation("propertyDateTime")>]
        member inline _.PropertyDateTime(d: Description, predicate: string, value: System.DateTimeOffset) : Description =
            { d with Statements = d.Statements @ [ predicate, Value.Literal(Literal.DateTime value) ] }

        [<CustomOperation("propertyLangString")>]
        member inline _.PropertyLangString(d: Description, predicate: string, value: string, language: string) : Description =
            { d with
                Statements = d.Statements @ [ predicate, Value.Literal(Literal.LangString(value, language)) ] }

        [<CustomOperation("propertyNode")>]
        member inline _.PropertyNode(d: Description, predicate: string, value: Node) : Description =
            { d with Statements = d.Statements @ [ predicate, Value.Node value ] }

    let describe subject = DescribeBuilder(subject)

    [<Sealed>]
    type RdfBuilder() =
        member inline _.Yield(_) : Doc = Doc.Empty
        member inline _.Run(doc: Doc) : Doc = doc

        [<CustomOperation("prefix")>]
        member inline _.Prefix(doc: Doc, name: string, uri: string) : Doc =
            { doc with
                Prefixes = doc.Prefixes @ [ name, uri ] }

        [<CustomOperation("about")>]
        member inline _.About(doc: Doc, d: Description) : Doc =
            { doc with
                Statements = doc.Statements @ (d.Statements |> List.map (fun (p, v) -> d.Subject, p, v)) }

        [<CustomOperation("triple")>]
        member inline _.Triple(doc: Doc, subject: Node, predicate: string, value: Value) : Doc =
            { doc with
                Statements = doc.Statements @ [ subject, predicate, value ] }

        [<CustomOperation("includeDoc")>]
        member inline _.IncludeDoc(doc: Doc, other: Doc) : Doc = Doc.merge doc other

    let rdf = RdfBuilder()
