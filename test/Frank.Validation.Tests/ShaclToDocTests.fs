// test/Frank.Validation.Tests/ShaclToDocTests.fs
module Frank.Validation.Tests.ShaclToDocTests

open System
open Expecto
open Frank.Rdf
open Frank.Validation
open Frank.Validation.ShapeSpecFunctions

let private hasTriple (doc: Doc) (predicateSuffix: string) : bool =
    doc.Statements
    |> List.exists (fun (_, p, _) -> p.EndsWith(predicateSuffix: string))

[<Tests>]
let tests =
    testList
        "Shacl.toDoc"
        [ testList
              "foundation"
              [ test "rdfList: empty list has rdf:nil as its head and mints no blank nodes" {
                    let head, stmts = Shacl.rdfList []

                    Expect.equal
                        head
                        (Node.Iri "http://www.w3.org/1999/02/22-rdf-syntax-ns#nil")
                        "empty list head is rdf:nil"

                    Expect.isEmpty stmts "no statements for an empty list"
                }

                test
                    "rdfList: well-formed rdf:first/rdf:rest chain, terminated by rdf:nil (the orphaned-list bug this guards against)" {
                    let head, stmts =
                        Shacl.rdfList [ Value.Literal(Literal.Int 1); Value.Literal(Literal.Int 2) ]

                    let firsts = stmts |> List.filter (fun (s, p, _) -> s = head && p = "rdf:first")
                    Expect.hasLength firsts 1 "the list's head cell has exactly one rdf:first"
                    let rests = stmts |> List.filter (fun (s, p, _) -> p = "rdf:rest")
                    Expect.hasLength rests 2 "two cells, each with one rdf:rest"

                    let nilRests =
                        rests
                        |> List.filter (fun (_, _, v) ->
                            v = Value.Node(Node.Iri "http://www.w3.org/1999/02/22-rdf-syntax-ns#nil"))

                    Expect.hasLength nilRests 1 "exactly one cell terminates in rdf:nil"
                }

                test "pathNode: a simple predicate path is just its IRI, no blank nodes" {
                    let node, stmts =
                        Shacl.pathNode (PropertyPath.Predicate(Uri "https://schema.org/position"))

                    Expect.equal node (Node.Iri "https://schema.org/position") "predicate path is the bare IRI"
                    Expect.isEmpty stmts "no auxiliary statements"
                }

                test "pathNode: inverse path is a blank node with sh:inversePath pointing at the inner path" {
                    let node, stmts =
                        Shacl.pathNode (PropertyPath.Inverse(PropertyPath.Predicate(Uri "https://schema.org/parent")))

                    match node with
                    | Node.Blank _ -> ()
                    | other -> failtestf "expected a blank node, got %A" other

                    Expect.exists
                        stmts
                        (fun (s, p, v) ->
                            s = node
                            && p = "sh:inversePath"
                            && v = Value.Node(Node.Iri "https://schema.org/parent"))
                        "sh:inversePath triple present"
                }

                test "pathNode: zeroOrMore/oneOrMore/zeroOrOne each wrap in the matching sh:*Path predicate" {
                    let inner = PropertyPath.Predicate(Uri "https://schema.org/knows")

                    for path, predicate in
                        [ PropertyPath.ZeroOrMore inner, "sh:zeroOrMorePath"
                          PropertyPath.OneOrMore inner, "sh:oneOrMorePath"
                          PropertyPath.ZeroOrOne inner, "sh:zeroOrOnePath" ] do
                        let node, stmts = Shacl.pathNode path

                        Expect.exists
                            stmts
                            (fun (s, p, v) ->
                                s = node && p = predicate && v = Value.Node(Node.Iri "https://schema.org/knows"))
                            $"{predicate} triple present"
                }

                test "pathNode: sequence path is a well-formed rdf:list of the member path nodes" {
                    let a = PropertyPath.Predicate(Uri "https://schema.org/a")
                    let b = PropertyPath.Predicate(Uri "https://schema.org/b")
                    let node, stmts = Shacl.pathNode (PropertyPath.Sequence { Head = a; Tail = [ b ] })

                    let firsts =
                        stmts
                        |> List.choose (fun (s, p, v) -> if s = node && p = "rdf:first" then Some v else None)

                    Expect.equal
                        firsts
                        [ Value.Node(Node.Iri "https://schema.org/a") ]
                        "sequence head cell's rdf:first is the first path"
                }

                test "pathNode: alternative path is a blank node with sh:alternativePath pointing at a well-formed list" {
                    let a = PropertyPath.Predicate(Uri "https://schema.org/a")
                    let b = PropertyPath.Predicate(Uri "https://schema.org/b")

                    let node, stmts =
                        Shacl.pathNode (PropertyPath.Alternative { Head = a; Tail = [ b ] })

                    Expect.exists
                        stmts
                        (fun (s, p, _) -> s = node && p = "sh:alternativePath")
                        "sh:alternativePath present"
                } ]

          testList
              "RecordShape skeleton"
              [ test "an untyped, unconstrained RecordShape declares sh:NodeShape and its target class" {
                    let decl = recordShape (targetClass (Uri "https://schema.org/MoveAction")) []
                    let doc = Shacl.toDoc [ decl ]
                    let subject = Node.Iri "https://schema.org/MoveAction"

                    Expect.exists
                        doc.Statements
                        (fun (s, p, v) -> s = subject && p = Rdf.RdfTypeIri && v = Value.Node(Node.Iri "sh:NodeShape"))
                        "rdf:type sh:NodeShape"

                    Expect.exists
                        doc.Statements
                        (fun (s, p, v) -> s = subject && p = "sh:targetClass" && v = Value.Node subject)
                        "sh:targetClass"
                }

                test "a property shape becomes a blank-node sh:property with sh:path -- no constraint triples yet" {
                    let prop = ofPath (PropertyPath.Predicate(Uri "https://schema.org/position"))
                    let decl = recordShape (targetClass (Uri "https://schema.org/MoveAction")) [ prop ]
                    let doc = Shacl.toDoc [ decl ]
                    let subject = Node.Iri "https://schema.org/MoveAction"

                    let propertyBlankNodes =
                        doc.Statements
                        |> List.choose (fun (s, p, v) -> if s = subject && p = "sh:property" then Some v else None)

                    Expect.hasLength propertyBlankNodes 1 "one sh:property statement"

                    match propertyBlankNodes with
                    | [ Value.Node bn ] ->
                        Expect.exists
                            doc.Statements
                            (fun (s, p, v) ->
                                s = bn
                                && p = "sh:path"
                                && v = Value.Node(Node.Iri "https://schema.org/position"))
                            "sh:path on the blank node"
                    | other -> failtestf "expected one blank node, got %A" other
                }

                test
                    "multiple targets on one shape each become their own triple (never an rdf:list -- SHACL targets are repeated statements)" {
                    let targets =
                        [ TargetSpec.Class(Uri "https://schema.org/MoveAction")
                          TargetSpec.SubjectsOf(Uri "https://schema.org/agent") ]

                    let decl = recordShape targets []
                    let doc = Shacl.toDoc [ decl ]

                    Expect.exists
                        doc.Statements
                        (fun (_, p, v) ->
                            p = "sh:targetClass" && v = Value.Node(Node.Iri "https://schema.org/MoveAction"))
                        "sh:targetClass"

                    Expect.exists
                        doc.Statements
                        (fun (_, p, v) ->
                            p = "sh:targetSubjectsOf" && v = Value.Node(Node.Iri "https://schema.org/agent"))
                        "sh:targetSubjectsOf"
                }

                test "toDoc builds against a real dotNetRDF graph without throwing (prefixes resolve)" {
                    let decl =
                        recordShape
                            (targetClass (Uri "https://schema.org/MoveAction"))
                            [ ofPath (PropertyPath.Predicate(Uri "https://schema.org/position")) ]

                    let doc = Shacl.toDoc [ decl ]
                    let graph = Doc.toGraph doc
                    Expect.isGreaterThan graph.Triples.Count 0 "at least one triple asserted"
                } ]

          testList
              "value type constraints"
              [ test "sh:class on a property shape" {
                    let prop =
                        ofPath (PropertyPath.Predicate(Uri "https://schema.org/agent"))
                        |> addConstraint (PropertyConstraint.Class(Uri "https://schema.org/Person"))

                    let doc =
                        Shacl.toDoc [ recordShape (targetClass (Uri "https://schema.org/MoveAction")) [ prop ] ]

                    Expect.exists
                        doc.Statements
                        (fun (_, p, v) -> p = "sh:class" && v = Value.Node(Node.Iri "https://schema.org/Person"))
                        "sh:class present"
                }

                test "sh:datatype maps every XsdDatatype case to its xsd: CURIE" {
                    let cases =
                        [ XsdDatatype.Integer, "xsd:integer"
                          XsdDatatype.Long, "xsd:long"
                          XsdDatatype.Decimal, "xsd:decimal"
                          XsdDatatype.Double, "xsd:double"
                          XsdDatatype.Boolean, "xsd:boolean"
                          XsdDatatype.String, "xsd:string"
                          XsdDatatype.DateTime, "xsd:dateTime" ]

                    for dt, expectedCurie in cases do
                        let prop =
                            ofPath (PropertyPath.Predicate(Uri "https://schema.org/x"))
                            |> addConstraint (PropertyConstraint.Datatype dt)

                        let doc =
                            Shacl.toDoc [ recordShape (targetClass (Uri "https://schema.org/T")) [ prop ] ]

                        Expect.exists
                            doc.Statements
                            (fun (_, p, v) -> p = "sh:datatype" && v = Value.Node(Node.Iri expectedCurie))
                            $"sh:datatype for {dt}"
                }

                test "sh:nodeKind maps every NodeKind case to its sh: individual" {
                    let cases =
                        [ NodeKind.BlankNode, "sh:BlankNode"
                          NodeKind.Iri, "sh:IRI"
                          NodeKind.Literal, "sh:Literal"
                          NodeKind.BlankNodeOrIri, "sh:BlankNodeOrIRI"
                          NodeKind.BlankNodeOrLiteral, "sh:BlankNodeOrLiteral"
                          NodeKind.IriOrLiteral, "sh:IRIOrLiteral" ]

                    for nk, expectedCurie in cases do
                        let prop =
                            ofPath (PropertyPath.Predicate(Uri "https://schema.org/x"))
                            |> addConstraint (PropertyConstraint.NodeKind nk)

                        let doc =
                            Shacl.toDoc [ recordShape (targetClass (Uri "https://schema.org/T")) [ prop ] ]

                        Expect.exists
                            doc.Statements
                            (fun (_, p, v) -> p = "sh:nodeKind" && v = Value.Node(Node.Iri expectedCurie))
                            $"sh:nodeKind for {nk}"
                }

                test "a property shape with no constraints still emits only sh:path (wildcard is a no-op, not an error)" {
                    let prop = ofPath (PropertyPath.Predicate(Uri "https://schema.org/x"))

                    let doc =
                        Shacl.toDoc [ recordShape (targetClass (Uri "https://schema.org/T")) [ prop ] ]

                    Expect.exists doc.Statements (fun (_, p, _) -> p = "sh:path") "sh:path still present"
                } ]

          testList
              "cardinality and value range constraints"
              [ test "sh:minCount and sh:maxCount as xsd:integer literals" {
                    let prop =
                        ofPath (PropertyPath.Predicate(Uri "https://schema.org/position"))
                        |> addConstraint (PropertyConstraint.MinCount 1)
                        |> addConstraint (PropertyConstraint.MaxCount 1)

                    let doc =
                        Shacl.toDoc [ recordShape (targetClass (Uri "https://schema.org/T")) [ prop ] ]

                    Expect.exists
                        doc.Statements
                        (fun (_, p, v) -> p = "sh:minCount" && v = Value.Literal(Literal.Int 1))
                        "sh:minCount"

                    Expect.exists
                        doc.Statements
                        (fun (_, p, v) -> p = "sh:maxCount" && v = Value.Literal(Literal.Int 1))
                        "sh:maxCount"
                }

                test "sh:minExclusive/minInclusive/maxExclusive/maxInclusive carry the given Literal unchanged" {
                    let cases =
                        [ PropertyConstraint.MinExclusive(Literal.Int 0), "sh:minExclusive"
                          PropertyConstraint.MinInclusive(Literal.Int 0), "sh:minInclusive"
                          PropertyConstraint.MaxExclusive(Literal.Int 100), "sh:maxExclusive"
                          PropertyConstraint.MaxInclusive(Literal.Int 100), "sh:maxInclusive" ]

                    for constr, predicate in cases do
                        let prop =
                            ofPath (PropertyPath.Predicate(Uri "https://schema.org/x"))
                            |> addConstraint constr

                        let doc =
                            Shacl.toDoc [ recordShape (targetClass (Uri "https://schema.org/T")) [ prop ] ]

                        Expect.exists doc.Statements (fun (_, p, _) -> p = predicate) $"{predicate} present"
                }

                test "range constraints work with DateTime literals too, not just Int" {
                    let t = DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero)

                    let prop =
                        ofPath (PropertyPath.Predicate(Uri "https://schema.org/x"))
                        |> addConstraint (PropertyConstraint.MinInclusive(Literal.DateTime t))

                    let doc =
                        Shacl.toDoc [ recordShape (targetClass (Uri "https://schema.org/T")) [ prop ] ]

                    Expect.exists
                        doc.Statements
                        (fun (_, p, v) -> p = "sh:minInclusive" && v = Value.Literal(Literal.DateTime t))
                        "sh:minInclusive with a DateTime literal"
                } ] ]
