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

    let private propertyShapeStatements (spec: PropertyShapeSpec) : Node * (Node * string * Value) list =
        let bn = Node.blank ()
        let pathHead, pathStmts = pathNode spec.Path
        bn, (stmt bn "sh:path" (Value.Node pathHead) :: pathStmts)

    /// The one place a ShapeDecl becomes a subject node plus its own statements. RecordShape is fully
    /// handled here; EnumShape/And/Or/Not/Xone are added by Tasks 9-10 -- this wildcard is a real,
    /// defined interim behavior (no triples for those cases yet), not a stub, and it narrows task by
    /// task until Task 10 removes it and this becomes an exhaustive match.
    let rec private shapeStatements (decl: ShapeDecl) : Node * (Node * string * Value) list =
        match decl with
        | ShapeDecl.RecordShape spec ->
            // A RecordShape's subject is its own IRI when it has at least one TargetSpec.Class target
            // (the common, directly-dereferenceable case); otherwise a fresh blank node, since a shape
            // meant only to be nested via sh:node has no natural IRI of its own.
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
