namespace Frank.Validation

open System
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

    /// One case added per Task 5-13; the wildcard's scope is documented at each task that narrows it.
    /// Mutually recursive with propertyShapeStatements/shapeStatements from this task on, because
    /// Task 9's PropertyConstraint.Node/QualifiedValueShape cases call back into shapeStatements.
    let rec private constraintStatements (propNode: Node) (c: PropertyConstraint) : (Node * string * Value) list =
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
            let innerSubject, innerStmts = shapeStatements inner
            stmt propNode "sh:node" (Value.Node innerSubject) :: innerStmts
        | PropertyConstraint.QualifiedValueShape(inner, minC, maxC, disjoint) ->
            let innerSubject, innerStmts = shapeStatements inner

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
            let prefixLines =
                sc.Prefixes
                |> List.map (fun (name, uri) -> sprintf "PREFIX %s: <%s>" name uri)
                |> String.concat "\n"

            let fullQuery =
                if String.IsNullOrEmpty prefixLines then
                    sc.Query
                else
                    prefixLines + "\n" + sc.Query

            let bn = Node.blank ()

            stmt propNode "sh:sparql" (Value.Node bn)
            :: stmt bn "sh:select" (Value.Literal(Literal.String fullQuery))
            :: (sc.Message
                |> Option.map (fun m -> stmt bn "sh:message" (Value.Literal(Literal.String m)))
                |> Option.toList)

    and private propertyShapeStatements (spec: PropertyShapeSpec) : Node * (Node * string * Value) list =
        let bn = Node.blank ()
        let pathHead, pathStmts = pathNode spec.Path
        let constraintStmts = spec.Constraints |> List.collect (constraintStatements bn)

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

    and private shapeStatements (decl: ShapeDecl) : Node * (Node * string * Value) list =
        match decl with
        | ShapeDecl.RecordShape spec ->
            let subject =
                spec.Targets
                |> List.tryPick (function
                    | TargetSpec.Class uri -> Some(Node.Iri uri.AbsoluteUri)
                    | _ -> None)
                |> Option.defaultWith Node.blank

            let typeStmt = stmt subject RdfTypeIri (Value.Node(Node.Iri "sh:NodeShape"))
            let targetStmts = spec.Targets |> List.collect (targetStatements subject)

            let propertyStmts =
                spec.Properties
                |> List.collect (fun p ->
                    let bn, stmts = propertyShapeStatements p
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
            let items = NonEmptyList.toList members |> List.map shapeStatements
            let head, listStmts = rdfList (items |> List.map (fst >> Value.Node))
            let bn = Node.blank ()
            bn, (stmt bn "sh:and" (Value.Node head) :: (items |> List.collect snd)) @ listStmts
        | ShapeDecl.Or members ->
            let items = NonEmptyList.toList members |> List.map shapeStatements
            let head, listStmts = rdfList (items |> List.map (fst >> Value.Node))
            let bn = Node.blank ()
            bn, (stmt bn "sh:or" (Value.Node head) :: (items |> List.collect snd)) @ listStmts
        | ShapeDecl.Xone members ->
            let items = NonEmptyList.toList members |> List.map shapeStatements
            let head, listStmts = rdfList (items |> List.map (fst >> Value.Node))
            let bn = Node.blank ()
            bn, (stmt bn "sh:xone" (Value.Node head) :: (items |> List.collect snd)) @ listStmts
        | ShapeDecl.Not inner ->
            let innerSubject, innerStmts = shapeStatements inner
            let bn = Node.blank ()
            bn, stmt bn "sh:not" (Value.Node innerSubject) :: innerStmts
        | ShapeDecl.EnumShape(targetClassUri, cases) ->
            let subject = Node.Iri targetClassUri.AbsoluteUri
            let typeStmt = stmt subject RdfTypeIri (Value.Node(Node.Iri "sh:NodeShape"))
            let targetStmt = stmt subject "sh:targetClass" (Value.Node subject)

            let items =
                NonEmptyList.toList cases
                |> List.map (fun u -> Value.Node(Node.Iri u.AbsoluteUri))

            let listHead, listStmts = rdfList items
            subject, [ typeStmt; targetStmt; stmt subject "sh:in" (Value.Node listHead) ] @ listStmts

    let toDoc (shapes: ShapeDecl list) : Doc =
        let statements = shapes |> List.collect (shapeStatements >> snd)

        { Prefixes = shaclPrefixes
          Statements = statements }

    let toShapesGraph (shapes: ShapeDecl list) : VDS.RDF.Shacl.ShapesGraph =
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
        | _ -> Node.Iri(n.ToString())

    let private severityOf (n: VDS.RDF.INode) : Severity =
        match n.NodeType with
        | VDS.RDF.NodeType.Uri ->
            let uri = (n :?> VDS.RDF.IUriNode).Uri.AbsoluteUri

            if uri.EndsWith "Warning" then Severity.Warning
            elif uri.EndsWith "Info" then Severity.Info
            else Severity.Violation
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
        let report = shapesGraph.Validate(dataGraph)

        if report.Conforms then
            ValidationOutcome.Conforms
        else
            let violations =
                report.Results
                |> Seq.map (fun r ->
                    { FocusNode = nodeOf r.FocusNode
                      ResultPath = resultPathOf r.ResultPath
                      Severity = severityOf r.Severity
                      Message = if isNull (box r.Message) then "" else r.Message.Value
                      ConstraintComponent = uriOf r.SourceConstraintComponent
                      SourceShape = nodeOf r.SourceShape })
                |> List.ofSeq

            ValidationOutcome.Violates violations
