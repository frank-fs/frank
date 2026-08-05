namespace Frank.Validation

open System
open System.Collections.Generic
open Frank.Rdf

module Shacl =
    [<Literal>]
    let private RdfNs = "http://www.w3.org/1999/02/22-rdf-syntax-ns#"

    let private shaclPrefixes =
        [ "sh", "http://www.w3.org/ns/shacl#"
          "xsd", "http://www.w3.org/2001/XMLSchema#"
          "rdf", RdfNs ]

    let private stmt (s: Node) (p: string) (v: Value) : Node * string * Value = s, p, v

    let rec internal rdfList (items: Value list) : Node * (Node * string * Value) list =
        match items with
        | [] -> Node.Iri(RdfNs + "nil"), []
        | item :: rest ->
            let cell = Node.blank ()
            let restHead, restStmts = rdfList rest

            let stmts =
                [ stmt cell "rdf:first" item; stmt cell "rdf:rest" (Value.Node restHead) ]
                @ restStmts

            cell, stmts

    let rec internal pathNode (path: PropertyPath) : Node * (Node * string * Value) list =
        let wrap (predicate: string) (inner: PropertyPath) =
            let bn = Node.blank ()
            let innerNode, innerStmts = pathNode inner
            bn, stmt bn predicate (Value.Node innerNode) :: innerStmts

        match path with
        | PropertyPath.Predicate uri -> Node.Iri uri.AbsoluteUri, []
        | PropertyPath.Inverse inner -> wrap "sh:inversePath" inner
        | PropertyPath.ZeroOrMore inner -> wrap "sh:zeroOrMorePath" inner
        | PropertyPath.OneOrMore inner -> wrap "sh:oneOrMorePath" inner
        | PropertyPath.ZeroOrOne inner -> wrap "sh:zeroOrOnePath" inner
        | PropertyPath.Sequence paths ->
            let members = NonEmptyList.toList paths |> List.map pathNode
            let listHead, listStmts = rdfList (members |> List.map (fst >> Value.Node))
            listHead, (members |> List.collect snd) @ listStmts
        | PropertyPath.Alternative paths ->
            let members = NonEmptyList.toList paths |> List.map pathNode
            let listHead, listStmts = rdfList (members |> List.map (fst >> Value.Node))
            let bn = Node.blank ()

            bn,
            (stmt bn "sh:alternativePath" (Value.Node listHead)
             :: (members |> List.collect snd))
            @ listStmts

    let private targetStatements (subject: Node) (target: TargetSpec) : (Node * string * Value) list =
        match target with
        | TargetSpec.Class uri -> [ stmt subject "sh:targetClass" (Value.Node(Node.Iri uri.AbsoluteUri)) ]
        | TargetSpec.Node node -> [ stmt subject "sh:targetNode" (Value.Node node) ]
        | TargetSpec.SubjectsOf uri -> [ stmt subject "sh:targetSubjectsOf" (Value.Node(Node.Iri uri.AbsoluteUri)) ]
        | TargetSpec.ObjectsOf uri -> [ stmt subject "sh:targetObjectsOf" (Value.Node(Node.Iri uri.AbsoluteUri)) ]

    let private xsdCurie (dt: XsdDatatype) : string =
        match dt with
        | XsdDatatype.Integer -> "xsd:integer"
        | XsdDatatype.Long -> "xsd:long"
        | XsdDatatype.Decimal -> "xsd:decimal"
        | XsdDatatype.Double -> "xsd:double"
        | XsdDatatype.Boolean -> "xsd:boolean"
        | XsdDatatype.String -> "xsd:string"
        | XsdDatatype.DateTime -> "xsd:dateTime"

    let private nodeKindCurie (nk: NodeKind) : string =
        match nk with
        | NodeKind.BlankNode -> "sh:BlankNode"
        | NodeKind.Iri -> "sh:IRI"
        | NodeKind.Literal -> "sh:Literal"
        | NodeKind.BlankNodeOrIri -> "sh:BlankNodeOrIRI"
        | NodeKind.BlankNodeOrLiteral -> "sh:BlankNodeOrLiteral"
        | NodeKind.IriOrLiteral -> "sh:IRIOrLiteral"

    let private severityCurie (s: Severity) : string =
        match s with
        | Severity.Violation -> "sh:Violation"
        | Severity.Warning -> "sh:Warning"
        | Severity.Info -> "sh:Info"

    /// The query text exactly as it is emitted into the shapes graph: the constraint's declared
    /// prefixes rendered as PREFIX lines, then the author's query. Parsing anything else (finding I1)
    /// would validate text that never reaches dotNetRDF's SHACL engine.
    let private fullSparqlText (sc: SparqlConstraint) : string =
        let prefixLines =
            sc.Prefixes
            |> List.map (fun (name, uri) -> sprintf "PREFIX %s: <%s>" name uri)
            |> String.concat "\n"

        if String.IsNullOrEmpty prefixLines then
            sc.Query
        else
            prefixLines + "\n" + sc.Query

    /// Parses a constraint's emitted query text with dotNetRDF's own SPARQL parser -- the same
    /// grammar the SHACL engine will use at request time. A fresh parser per call: SparqlQueryParser
    /// carries mutable per-parse state and is not documented thread-safe, and toShapesGraph is
    /// reachable concurrently.
    let internal parseSparqlConstraint (sc: SparqlConstraint) : Result<VDS.RDF.Query.SparqlQuery, string> =
        try
            Ok(VDS.RDF.Parsing.SparqlQueryParser().ParseFromString(fullSparqlText sc))
        with ex ->
            Error ex.Message

    /// True for every SELECT-shaped SparqlQueryType (Select, SelectAll, SelectDistinct,
    /// SelectAllDistinct, SelectReduced, SelectAllReduced) -- the only form sh:sparql accepts.
    let private isSelectForm (queryType: VDS.RDF.Query.SparqlQueryType) : bool =
        match queryType with
        | VDS.RDF.Query.SparqlQueryType.Select
        | VDS.RDF.Query.SparqlQueryType.SelectAll
        | VDS.RDF.Query.SparqlQueryType.SelectDistinct
        | VDS.RDF.Query.SparqlQueryType.SelectAllDistinct
        | VDS.RDF.Query.SparqlQueryType.SelectReduced
        | VDS.RDF.Query.SparqlQueryType.SelectAllReduced -> true
        | _ -> false

    /// One case added per Task 5-13; the wildcard's scope is documented at each task that narrows it.
    /// Mutually recursive with propertyShapeStatements/shapeStatements from this task on, because
    /// Task 9's PropertyConstraint.Node/QualifiedValueShape cases call back into shapeStatements.
    ///
    /// `memo` maps a ShapeDecl value already emitted (in this toDoc call) to the subject node it was
    /// emitted under -- see shapeStatements below for why this exists. Threaded through here purely
    /// because Node/QualifiedValueShape recurse into shapeStatements and must share the same table.
    let rec private constraintStatements
        (memo: Dictionary<ShapeDecl, Node>)
        (propNode: Node)
        (c: PropertyConstraint)
        : (Node * string * Value) list =
        match c with
        | PropertyConstraint.Class uri -> [ stmt propNode "sh:class" (Value.Node(Node.Iri uri.AbsoluteUri)) ]
        | PropertyConstraint.Datatype dt -> [ stmt propNode "sh:datatype" (Value.Node(Node.Iri(xsdCurie dt))) ]
        | PropertyConstraint.NodeKind nk -> [ stmt propNode "sh:nodeKind" (Value.Node(Node.Iri(nodeKindCurie nk))) ]
        | PropertyConstraint.MinCount n -> [ stmt propNode "sh:minCount" (Value.Literal(Literal.Int n)) ]
        | PropertyConstraint.MaxCount n -> [ stmt propNode "sh:maxCount" (Value.Literal(Literal.Int n)) ]
        | PropertyConstraint.MinExclusive lit -> [ stmt propNode "sh:minExclusive" (Value.Literal lit) ]
        | PropertyConstraint.MinInclusive lit -> [ stmt propNode "sh:minInclusive" (Value.Literal lit) ]
        | PropertyConstraint.MaxExclusive lit -> [ stmt propNode "sh:maxExclusive" (Value.Literal lit) ]
        | PropertyConstraint.MaxInclusive lit -> [ stmt propNode "sh:maxInclusive" (Value.Literal lit) ]
        | PropertyConstraint.MinLength n -> [ stmt propNode "sh:minLength" (Value.Literal(Literal.Int n)) ]
        | PropertyConstraint.MaxLength n -> [ stmt propNode "sh:maxLength" (Value.Literal(Literal.Int n)) ]
        | PropertyConstraint.Pattern(pattern, flags) ->
            stmt propNode "sh:pattern" (Value.Literal(Literal.String pattern))
            :: (flags
                |> Option.map (fun f -> stmt propNode "sh:flags" (Value.Literal(Literal.String f)))
                |> Option.toList)
        | PropertyConstraint.LanguageIn tags ->
            let items = NonEmptyList.toList tags |> List.map (Literal.String >> Value.Literal)
            let head, listStmts = rdfList items
            stmt propNode "sh:languageIn" (Value.Node head) :: listStmts
        | PropertyConstraint.UniqueLang b -> [ stmt propNode "sh:uniqueLang" (Value.Literal(Literal.Bool b)) ]
        | PropertyConstraint.Equals uri -> [ stmt propNode "sh:equals" (Value.Node(Node.Iri uri.AbsoluteUri)) ]
        | PropertyConstraint.Disjoint uri -> [ stmt propNode "sh:disjoint" (Value.Node(Node.Iri uri.AbsoluteUri)) ]
        | PropertyConstraint.LessThan uri -> [ stmt propNode "sh:lessThan" (Value.Node(Node.Iri uri.AbsoluteUri)) ]
        | PropertyConstraint.LessThanOrEquals uri ->
            [ stmt propNode "sh:lessThanOrEquals" (Value.Node(Node.Iri uri.AbsoluteUri)) ]
        | PropertyConstraint.Node inner ->
            let innerSubject, innerStmts = shapeStatements memo inner
            stmt propNode "sh:node" (Value.Node innerSubject) :: innerStmts
        | PropertyConstraint.QualifiedValueShape(inner, minC, maxC, disjoint) ->
            let innerSubject, innerStmts = shapeStatements memo inner

            [ stmt propNode "sh:qualifiedValueShape" (Value.Node innerSubject) ]
            @ (minC
               |> Option.map (fun n -> stmt propNode "sh:qualifiedMinCount" (Value.Literal(Literal.Int n)))
               |> Option.toList)
            @ (maxC
               |> Option.map (fun n -> stmt propNode "sh:qualifiedMaxCount" (Value.Literal(Literal.Int n)))
               |> Option.toList)
            @ [ stmt propNode "sh:qualifiedValueShapesDisjoint" (Value.Literal(Literal.Bool disjoint)) ]
            @ innerStmts
        | PropertyConstraint.HasValue value -> [ stmt propNode "sh:hasValue" value ]
        | PropertyConstraint.AllowedValues values ->
            let items = NonEmptyList.toList values
            let head, listStmts = rdfList items
            stmt propNode "sh:in" (Value.Node head) :: listStmts
        | PropertyConstraint.Sparql sc ->
            let bn = Node.blank ()

            // Always sh:select, never sh:ask -- see toShapesGraph's build-time form check for the
            // full reasoning (SHACL's sh:sparql is SELECT-based by definition; dotNetRDF's
            // Constraint.cs maps sh:sparql to its Select validator unconditionally).
            stmt propNode "sh:sparql" (Value.Node bn)
            :: stmt bn "sh:select" (Value.Literal(Literal.String(fullSparqlText sc)))
            :: (sc.Message
                |> Option.map (fun m -> stmt bn "sh:message" (Value.Literal(Literal.String m)))
                |> Option.toList)

    and private propertyShapeStatements
        (memo: Dictionary<ShapeDecl, Node>)
        (spec: PropertyShapeSpec)
        : Node * (Node * string * Value) list =
        let bn = Node.blank ()
        let pathHead, pathStmts = pathNode spec.Path

        let constraintStmts =
            spec.Constraints |> List.collect (constraintStatements memo bn)

        let severityStmt =
            spec.Severity
            |> Option.map (fun s -> stmt bn "sh:severity" (Value.Node(Node.Iri(severityCurie s))))
            |> Option.toList

        let messageStmt =
            spec.Message
            |> Option.map (fun m -> stmt bn "sh:message" (Value.Literal(Literal.String m)))
            |> Option.toList

        bn,
        (stmt bn "sh:path" (Value.Node pathHead) :: pathStmts)
        @ constraintStmts
        @ severityStmt
        @ messageStmt

    /// `memo` maps a ShapeDecl value already emitted earlier in this toDoc call to the subject node
    /// it was emitted under. Without this, a shape referenced BOTH as a top-level toDoc list entry
    /// AND nested via another shape's sh:node/sh:qualifiedValueShape/And/Or/Xone/Not constraint --
    /// the "validates standalone AND nests" pattern the design doc itself recommends -- got emitted
    /// once per reference site, each with its own freshly-minted sh:property blank nodes. Simple
    /// literal/IRI triples on the shared subject collapsed harmlessly via RDF set semantics, but the
    /// duplicate sh:property blank nodes did not (each is unique by construction), so dotNetRDF's
    /// SHACL engine saw two distinct-but-content-identical property shapes and raised every
    /// violation on that shape twice.
    ///
    /// The memo is keyed by REFERENCE identity (HashIdentity.Reference below), not structural
    /// equality -- deliberately, after an earlier structural-equality version of this fix (fix round
    /// 1) turned out to be a worse bug than the one it fixed. System.Uri.Equals/GetHashCode ignore
    /// the URI fragment, so under F#'s generic structural equality two shapes differing only after a
    /// `#` (e.g. targetClass "http://www.w3.org/ns/prov#Agent" vs "...#Activity" -- the DOMINANT IRI
    /// convention for hand-authored RDF vocabularies: prov#, rdf#, rdfs#, owl#, skos#, sh#, and
    /// virtually every ontology#Term namespace, including this very repo's own Frank.Provenance)
    /// compared equal and collided in the Dictionary, silently dropping the second shape's triples
    /// entirely (or misrouting a nested sh:node reference to the wrong shape) with no exception, no
    /// warning. Reference identity has no such false-collapse risk: it only memo-hits when the exact
    /// same ShapeDecl VALUE (the same `let`-bound object, as personShape is in both the sample and
    /// this file's round-1 regression test) is reached a second time, which is precisely -- and only
    /// -- the "validates standalone AND nests" pattern this fix targets. Two independently-
    /// constructed shapes that happen to be structurally similar (or IRI-fragment-hash-colliding) are
    /// never memoized together and each gets its own correct emission, exactly as before this whole
    /// fix existed. NOTE: this means two independently-built-but-content-identical ShapeDecl values
    /// are NOT deduplicated -- only genuinely shared (same-reference) shapes are. That is
    /// deliberately the narrower, safer guarantee; broadening it to structural-equality-with-correct-
    /// IRI-comparison (a custom IEqualityComparer<ShapeDecl> comparing Uri.AbsoluteUri strings rather
    /// than Uri.Equals) would need its own design/test pass and isn't required by any task's spec.
    ///
    /// Memoized BEFORE recursing into a shape's own children (properties/members), which would also
    /// stop a self-referential shape graph from looping -- not reachable today since ShapeDecl is an
    /// ordinary immutable tree with no way to construct a true cycle, but cheap insurance regardless.
    and private shapeStatements
        (memo: Dictionary<ShapeDecl, Node>)
        (decl: ShapeDecl)
        : Node * (Node * string * Value) list =
        match memo.TryGetValue decl with
        | true, subject -> subject, []
        | false, _ ->
            match decl with
            | ShapeDecl.RecordShape spec ->
                // ALWAYS a fresh subject -- never derived from a TargetSpec.Class (final-review
                // finding I2). Deriving it made shape identity a function of the TARGET rather than
                // of the shape, so two structurally different, independently constructed ShapeDecls
                // over the same class landed on one subject and merged: duplicate violations at
                // best, and at worst a closed shape whose allowed-property set silently grew by the
                // other shape's paths, quietly accepting data it was written to reject. Two shapes
                // over one class is ordinary, legal SHACL -- dotNetRDF runs both against every
                // class-matched node, which is exactly what the author asked for. sh:targetClass is
                // now just a triple pointing FROM the shape TO the class, the same way
                // sh:targetNode/sh:targetSubjectsOf/sh:targetObjectsOf always worked.
                //
                // Shape identity is therefore purely the ShapeDecl VALUE, via the reference-identity
                // memo below -- the "validates standalone AND nests" sharing pattern still emits
                // once, because that really is one object reached twice.
                let subject = Node.blank ()

                memo.[decl] <- subject

                let typeStmt = stmt subject RdfTypeIri (Value.Node(Node.Iri "sh:NodeShape"))
                let targetStmts = spec.Targets |> List.collect (targetStatements subject)

                let propertyStmts =
                    spec.Properties
                    |> List.collect (fun p ->
                        let bn, stmts = propertyShapeStatements memo p
                        stmt subject "sh:property" (Value.Node bn) :: stmts)

                let closedStmts =
                    if spec.Closed then
                        let ignoredValues =
                            spec.IgnoredProperties |> List.map (fun u -> Value.Node(Node.Iri u.AbsoluteUri))

                        let ignoredHead, ignoredListStmts = rdfList ignoredValues

                        stmt subject "sh:closed" (Value.Literal(Literal.Bool true))
                        :: stmt subject "sh:ignoredProperties" (Value.Node ignoredHead)
                        :: ignoredListStmts
                    else
                        []

                let severityStmt =
                    spec.Severity
                    |> Option.map (fun s -> stmt subject "sh:severity" (Value.Node(Node.Iri(severityCurie s))))
                    |> Option.toList

                let messageStmt =
                    spec.Message
                    |> Option.map (fun m -> stmt subject "sh:message" (Value.Literal(Literal.String m)))
                    |> Option.toList

                subject,
                typeStmt :: targetStmts
                @ propertyStmts
                @ closedStmts
                @ severityStmt
                @ messageStmt
            | ShapeDecl.And members ->
                let bn = Node.blank ()
                memo.[decl] <- bn
                let items = NonEmptyList.toList members |> List.map (shapeStatements memo)
                let head, listStmts = rdfList (items |> List.map (fst >> Value.Node))
                bn, (stmt bn "sh:and" (Value.Node head) :: (items |> List.collect snd)) @ listStmts
            | ShapeDecl.Or members ->
                let bn = Node.blank ()
                memo.[decl] <- bn
                let items = NonEmptyList.toList members |> List.map (shapeStatements memo)
                let head, listStmts = rdfList (items |> List.map (fst >> Value.Node))
                bn, (stmt bn "sh:or" (Value.Node head) :: (items |> List.collect snd)) @ listStmts
            | ShapeDecl.Xone members ->
                let bn = Node.blank ()
                memo.[decl] <- bn
                let items = NonEmptyList.toList members |> List.map (shapeStatements memo)
                let head, listStmts = rdfList (items |> List.map (fst >> Value.Node))
                bn, (stmt bn "sh:xone" (Value.Node head) :: (items |> List.collect snd)) @ listStmts
            | ShapeDecl.Not inner ->
                let bn = Node.blank ()
                memo.[decl] <- bn
                let innerSubject, innerStmts = shapeStatements memo inner
                bn, stmt bn "sh:not" (Value.Node innerSubject) :: innerStmts
            | ShapeDecl.EnumShape(targetClassUri, cases) ->
                // Fresh subject, same reasoning as RecordShape above (finding I2): two EnumShapes
                // over one class used to collide onto the class IRI and have their sh:in lists
                // merged onto a single subject -- two sh:in values on one shape, which SHACL does
                // not define.
                let subject = Node.blank ()
                memo.[decl] <- subject
                let typeStmt = stmt subject RdfTypeIri (Value.Node(Node.Iri "sh:NodeShape"))

                let targetStmt =
                    stmt subject "sh:targetClass" (Value.Node(Node.Iri targetClassUri.AbsoluteUri))

                let items =
                    NonEmptyList.toList cases
                    |> List.map (fun u -> Value.Node(Node.Iri u.AbsoluteUri))

                let listHead, listStmts = rdfList items
                subject, [ typeStmt; targetStmt; stmt subject "sh:in" (Value.Node listHead) ] @ listStmts

    let toDoc (shapes: ShapeDecl list) : Doc =
        // Fresh per call -- dedup only applies within a single toDoc invocation's shape list, never
        // across separate calls. Reference identity, not structural equality -- see the doc comment
        // on shapeStatements for why (structural equality silently collapsed shapes that differ only
        // by IRI fragment, e.g. prov#Agent vs prov#Activity).
        let memo = Dictionary<ShapeDecl, Node>(HashIdentity.Reference)
        let statements = shapes |> List.collect (shapeStatements memo >> snd)

        { Prefixes = shaclPrefixes
          Statements = statements }

    /// Every sh:sparql constraint reachable from a shape, including through nested
    /// sh:node/sh:qualifiedValueShape references and the logical combinators.
    let rec private sparqlConstraintsOf (decl: ShapeDecl) : SparqlConstraint list =
        match decl with
        | ShapeDecl.RecordShape spec ->
            spec.Properties
            |> List.collect (fun p -> p.Constraints |> List.collect sparqlConstraintsOfConstraint)
        | ShapeDecl.EnumShape _ -> []
        | ShapeDecl.And members
        | ShapeDecl.Or members
        | ShapeDecl.Xone members -> NonEmptyList.toList members |> List.collect sparqlConstraintsOf
        | ShapeDecl.Not inner -> sparqlConstraintsOf inner

    and private sparqlConstraintsOfConstraint (c: PropertyConstraint) : SparqlConstraint list =
        match c with
        | PropertyConstraint.Sparql sc -> [ sc ]
        | PropertyConstraint.Node inner -> sparqlConstraintsOf inner
        | PropertyConstraint.QualifiedValueShape(inner, _, _, _) -> sparqlConstraintsOf inner
        | _ -> []

    /// Two build-time gates over every reachable sh:sparql constraint, both closing "the shape is
    /// silently wrong and you find out never" holes the final review found:
    ///
    /// 1. (finding I1, and the design doc's error-handling table) The query must PARSE. "A Sparql
    ///    constraint's query failing to parse is raised at toShapesGraph build time
    ///    (shape-authoring time), never deferred to request-validation time -- a malformed
    ///    author-supplied query is a shape bug, not a per-request condition." Before this, a typo'd
    ///    query produced an RdfParseException on EVERY subsequent request to the guarded resource.
    ///
    /// 2. (finding C2) The query must be a SELECT. SHACL's sh:sparql is SELECT-based BY DEFINITION:
    ///    a SPARQL-based constraint is a SHACL instance of sh:SPARQLConstraint, which has exactly one
    ///    sh:select whose value is a valid SPARQL SELECT query (SHACL §5.2); sh:ask belongs to
    ///    sh:SPARQLAskValidator, reachable only through sh:validator on a custom
    ///    sh:ConstraintComponent (SHACL §6.2.3.2) -- a construct this package doesn't emit.
    ///    dotNetRDF agrees unconditionally: Constraints/Constraint.cs dispatches
    ///    `case INode t when t.Equals(Vocabulary.Sparql): return new Select(shape, value)`, and its
    ///    Select validator raises `A sh:SPARQLSelectValidator must have exactly one sh:select
    ///    property` if handed sh:ask instead.
    ///
    ///    So an ASK-shaped query cannot be honoured, only rejected. Left alone it was WORSE than
    ///    rejected: emitted under sh:select, an ASK executes to a SparqlResultSet with no bindings,
    ///    which the Select validator reads as "no results, therefore conforming" -- the constraint
    ///    silently never fires and non-conforming data passes. The review reported this as "always
    ///    emitted as sh:select" and asked for sh:ask emission; the spec and the engine both make that
    ///    impossible, so the silent bypass is closed at the other end instead -- loudly, at
    ///    shape-authoring time, with a message naming the SELECT rewrite.
    let toShapesGraph (shapes: ShapeDecl list) : VDS.RDF.Shacl.ShapesGraph =
        for sc in shapes |> List.collect sparqlConstraintsOf do
            match parseSparqlConstraint sc with
            | Error message ->
                invalidOp (
                    "Frank.Validation: a sh:sparql constraint's query does not parse as SPARQL, so the shapes "
                    + "graph cannot be built. Fix the query text -- deferring this to request time would fail "
                    + "every request to the resource it guards.\nParser error: "
                    + message
                    + "\nQuery:\n"
                    + fullSparqlText sc
                )
            | Ok query when not (isSelectForm query.QueryType) ->
                invalidOp (
                    "Frank.Validation: a sh:sparql constraint's query is a "
                    + string query.QueryType
                    + " query, but SHACL's sh:sparql accepts only SELECT queries (SHACL "
                    + "§5.2 -- sh:ask belongs to sh:SPARQLAskValidator inside a custom constraint "
                    + "component, which this package does not emit). Rewrite it as a SELECT returning one row "
                    + "per violation: an `ASK { P }` that must hold becomes "
                    + "`SELECT $this WHERE { FILTER NOT EXISTS { P } }`.\nQuery:\n"
                    + fullSparqlText sc
                )
            | Ok _ -> ()

        new VDS.RDF.Shacl.ShapesGraph(Doc.toGraph (toDoc shapes))

    // NOTE on the helpers below: dotNetRdf.Shacl's Path/Shape wrapper types (e.g.
    // VDS.RDF.Shacl.Paths.Predicate, VDS.RDF.Shacl.Shapes.Property) derive from WrapperNode, which
    // structurally implements EVERY node marker interface (IUriNode, IBlankNode, ILiteralNode, ...)
    // regardless of what kind of node is actually wrapped. That means `:? IUriNode` always matches
    // for these types -- even when the wrapped node is a blank node -- and calling `.Uri` on the
    // resulting (mis)match throws InvalidCastException at runtime instead of the type test failing.
    // Verified live against dotNetRdf.Shacl 3.5.1 (see task-13-report.md): every node-kind dispatch
    // below switches on the real `.NodeType` enum first and only then downcasts, rather than
    // pattern-matching on interface type as the original sketch assumed.
    let private nodeOf (n: VDS.RDF.INode) : Node =
        match n.NodeType with
        | VDS.RDF.NodeType.Uri -> Node.Iri (n :?> VDS.RDF.IUriNode).Uri.AbsoluteUri
        | VDS.RDF.NodeType.Blank -> Node.Blank (n :?> VDS.RDF.IBlankNode).InternalID
        // Only reachable for a node kind that can never legally appear where this is used
        // (Violation.SourceShape, always an IRI or blank node). A urn: placeholder, NOT
        // `Node.Iri (n.ToString())` -- the latter fabricates an IRI Frank.Rdf's resolveIri rejects,
        // turning a would-be degenerate report into an unhandled 500 (final-review finding C1).
        | _ -> Node.Iri "urn:frank:validation:unknown-node"

    [<Literal>]
    let private XsdNs = "http://www.w3.org/2001/XMLSchema#"

    /// Maps a dotNetRDF literal node back onto Frank.Rdf's Literal. Frank.Rdf.Literal has no
    /// Decimal/Double/Float/Short/... cases, so any datatype outside the five it does model comes
    /// back as Literal.String carrying the lexical form -- a disclosed narrowing, documented on
    /// Violation.FocusNode in Validation.fsi rather than silently swallowed.
    let private literalOf (n: VDS.RDF.ILiteralNode) : Literal =
        if not (String.IsNullOrEmpty n.Language) then
            Literal.LangString(n.Value, n.Language)
        else
            match n.DataType with
            | null -> Literal.String n.Value
            | dt ->
                match dt.AbsoluteUri with
                | u when u = XsdNs + "integer" || u = XsdNs + "int" || u = XsdNs + "long" ->
                    match Int32.TryParse n.Value with
                    | true, i -> Literal.Int i
                    | _ -> Literal.String n.Value
                | u when u = XsdNs + "boolean" ->
                    match Boolean.TryParse n.Value with
                    | true, b -> Literal.Bool b
                    | _ -> Literal.String n.Value
                | u when u = XsdNs + "dateTime" ->
                    match DateTimeOffset.TryParse(n.Value, Globalization.CultureInfo.InvariantCulture) with
                    | true, dt -> Literal.DateTime dt
                    | _ -> Literal.String n.Value
                | _ -> Literal.String n.Value

    /// A SHACL focus node is NOT always an IRI or blank node -- sh:targetObjectsOf targets the
    /// objects of a predicate, which are routinely literals. Mapping one onto Node.Iri (n.ToString())
    /// fabricated an IRI like `Alice^^http://www.w3.org/2001/XMLSchema#string`, which then raised out
    /// of Frank.Rdf's resolveIri the moment reportToDoc's output was serialized for the 422
    /// application/ld+json response (final-review finding C1).
    let private valueOf (n: VDS.RDF.INode) : Value =
        match n.NodeType with
        | VDS.RDF.NodeType.Uri -> Value.Node(Node.Iri (n :?> VDS.RDF.IUriNode).Uri.AbsoluteUri)
        | VDS.RDF.NodeType.Blank -> Value.Node(Node.Blank (n :?> VDS.RDF.IBlankNode).InternalID)
        | VDS.RDF.NodeType.Literal -> Value.Literal(literalOf (n :?> VDS.RDF.ILiteralNode))
        | _ -> Value.Literal(Literal.String(n.ToString()))

    [<Literal>]
    let private ShaclNs = "http://www.w3.org/ns/shacl#"

    /// Exact IRI comparison, not `uri.EndsWith "Warning"`/`"Info"` (final-review minor item): a
    /// suffix test would misread any severity term from another vocabulary that merely ends in those
    /// words, and silently downgrade or upgrade a result's severity. Anything that is not one of
    /// SHACL's own three terms falls back to sh:Violation, SHACL's own default.
    let private severityOf (n: VDS.RDF.INode) : Severity =
        match n.NodeType with
        | VDS.RDF.NodeType.Uri ->
            match (n :?> VDS.RDF.IUriNode).Uri.AbsoluteUri with
            | uri when uri = ShaclNs + "Warning" -> Severity.Warning
            | uri when uri = ShaclNs + "Info" -> Severity.Info
            | _ -> Severity.Violation
        | _ -> Severity.Violation

    let private uriOf (n: VDS.RDF.INode) : Uri =
        match n.NodeType with
        | VDS.RDF.NodeType.Uri -> Uri (n :?> VDS.RDF.IUriNode).Uri.AbsoluteUri
        | _ -> Uri "urn:frank:validation:unknown-constraint-component"

    /// Result.ResultPath is a nullable VDS.RDF.Shacl.Path (null for node-shape-level violations that
    /// have no sh:path, e.g. sh:in on the shape itself). Some uri for the common simple-predicate
    /// case; None for a null path or a complex (non-IRI) path structure -- see Violation.ResultPath's
    /// doc comment in Validation.fsi for the disclosed round-trip simplification.
    let private resultPathOf (path: VDS.RDF.Shacl.Path) : Uri option =
        match box path with
        | null -> None
        | _ ->
            match path.NodeType with
            | VDS.RDF.NodeType.Uri -> Some(Uri (box path :?> VDS.RDF.IUriNode).Uri.AbsoluteUri)
            | _ -> None

    /// A typed wrapper over VDS.RDF.Shacl.Validation.Report -- never exposes the raw dotNetRDF
    /// Result type to callers.
    let validate (shapesGraph: VDS.RDF.Shacl.ShapesGraph) (dataGraph: VDS.RDF.IGraph) : ValidationOutcome =
        // NOTE on concurrency (final-review finding I7): one ShapesGraph shared across concurrent
        // Validate calls was suspected of being the cause of a measured multi-second stall on the
        // first parallel burst. It is not -- a shared instance, a fresh ShapesGraph wrapper per call,
        // a cloned shapes graph per call and a rebuilt toShapesGraph per call all measured the same,
        // and the stall turned out to be .NET ThreadPool thread-injection latency in the benchmark's
        // own Task.Run fan-out. Sharing is therefore left as-is (and covered by a concurrency
        // correctness test); see test/Frank.Validation.Tests/ValidationConcurrencyTests.fs for the
        // full measurements. The middleware's exception boundary catches an RdfQueryTimeoutException
        // from any source regardless.
        let report = shapesGraph.Validate(dataGraph)

        if report.Conforms then
            ValidationOutcome.Conforms
        else
            let violations =
                report.Results
                |> Seq.map (fun r ->
                    { FocusNode = valueOf r.FocusNode
                      ResultPath = resultPathOf r.ResultPath
                      Severity = severityOf r.Severity
                      Message = if isNull (box r.Message) then "" else r.Message.Value
                      ConstraintComponent = uriOf r.SourceConstraintComponent
                      SourceShape = nodeOf r.SourceShape })
                |> List.ofSeq

            ValidationOutcome.Violates violations

    let reportToDoc (violations: Violation list) : Doc =
        let reportNode = Node.blank ()

        let resultStatements =
            violations
            |> List.collect (fun v ->
                let resultNode = Node.blank ()

                let pathStmt =
                    v.ResultPath
                    |> Option.map (fun u -> stmt resultNode "sh:resultPath" (Value.Node(Node.Iri u.AbsoluteUri)))
                    |> Option.toList

                [ stmt reportNode "sh:result" (Value.Node resultNode)
                  stmt resultNode RdfTypeIri (Value.Node(Node.Iri "sh:ValidationResult"))
                  stmt resultNode "sh:focusNode" v.FocusNode
                  stmt resultNode "sh:resultSeverity" (Value.Node(Node.Iri(severityCurie v.Severity)))
                  stmt resultNode "sh:resultMessage" (Value.Literal(Literal.String v.Message))
                  stmt
                      resultNode
                      "sh:sourceConstraintComponent"
                      (Value.Node(Node.Iri v.ConstraintComponent.AbsoluteUri))
                  stmt resultNode "sh:sourceShape" (Value.Node v.SourceShape) ]
                @ pathStmt)

        { Prefixes = shaclPrefixes
          Statements =
            stmt reportNode RdfTypeIri (Value.Node(Node.Iri "sh:ValidationReport"))
            :: stmt reportNode "sh:conforms" (Value.Literal(Literal.Bool(List.isEmpty violations)))
            :: resultStatements }
