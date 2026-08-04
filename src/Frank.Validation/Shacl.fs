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
        | _ -> []

    and private propertyShapeStatements (spec: PropertyShapeSpec) : Node * (Node * string * Value) list =
        let bn = Node.blank ()
        let pathHead, pathStmts = pathNode spec.Path
        let constraintStmts = spec.Constraints |> List.collect (constraintStatements bn)
        bn, (stmt bn "sh:path" (Value.Node pathHead) :: pathStmts) @ constraintStmts

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

            subject, typeStmt :: targetStmts @ propertyStmts
        | _ -> Node.blank (), []

    let toDoc (shapes: ShapeDecl list) : Doc =
        let statements = shapes |> List.collect (shapeStatements >> snd)

        { Prefixes = shaclPrefixes
          Statements = statements }
