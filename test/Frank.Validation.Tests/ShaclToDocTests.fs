// test/Frank.Validation.Tests/ShaclToDocTests.fs
module Frank.Validation.Tests.ShaclToDocTests

open System
open Expecto
open Frank.Rdf
open Frank.Validation
open Frank.Validation.ShapeSpecFunctions

/// Expecto's Expect.throwsT returns unit, and these tests assert on the raised message (the whole
/// point of finding I1/C2 is that the failure is DESCRIPTIVE), so capture it here instead.
let private captureInvalidOp (f: unit -> unit) : InvalidOperationException =
    let mutable captured = None

    try
        f ()
    with :? InvalidOperationException as ex ->
        captured <- Some ex

    match captured with
    | Some ex -> ex
    | None -> failtest "expected an InvalidOperationException, but none was raised"

/// A shape's RDF subject is a freshly minted blank node, NOT its target class's IRI (final-review
/// finding I2), so tests locate a shape by the target triple that points AT the class.
let private subjectOfTarget (doc: Doc) (predicate: string) (targetIri: string) : Node =
    doc.Statements
    |> List.tryPick (fun (s, p, v) ->
        if p = predicate && v = Value.Node(Node.Iri targetIri) then
            Some s
        else
            None)
    |> Option.defaultWith (fun () -> failtestf "no %s statement pointing at %s" predicate targetIri)

let private subjectOfTargetClass (doc: Doc) (classIri: string) : Node =
    subjectOfTarget doc "sh:targetClass" classIri

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

          // Final-review finding I2. A RecordShape's RDF subject used to be DERIVED from its first
          // TargetSpec.Class, so two structurally different, independently constructed shapes over
          // the same class collided onto one subject and merged. Task 19's reference-identity memo
          // does not catch this -- it only dedupes the SAME object reached twice. The merge was
          // silently dangerous: a closed shape's allowed-property set grew by the other shape's
          // paths. Shape subjects are now always freshly minted, and sh:targetClass is an ordinary
          // triple pointing FROM the shape TO the class, exactly like the other three TargetSpec
          // cases already did.
          testList
              "two shapes over one target class"
              [ test "two independently-constructed shapes with the same targetClass get DISTINCT subjects" {
                    let withName =
                        recordShape
                            (targetClass (Uri "https://schema.org/Person"))
                            [ ofPath (PropertyPath.Predicate(Uri "https://schema.org/name"))
                              |> addConstraint (PropertyConstraint.MinCount 1) ]

                    let withEmail =
                        recordShape
                            (targetClass (Uri "https://schema.org/Person"))
                            [ ofPath (PropertyPath.Predicate(Uri "https://schema.org/email"))
                              |> addConstraint (PropertyConstraint.MinCount 1) ]

                    let doc = Shacl.toDoc [ withName; withEmail ]

                    let shapeSubjects =
                        doc.Statements
                        |> List.filter (fun (_, p, v) ->
                            p = "sh:targetClass" && v = Value.Node(Node.Iri "https://schema.org/Person"))
                        |> List.map (fun (s, _, _) -> s)

                    Expect.hasLength shapeSubjects 2 "two sh:targetClass triples, one per shape"

                    Expect.isTrue
                        (shapeSubjects |> List.distinct |> List.length = 2)
                        "and they sit on two DISTINCT subjects, so neither shape's constraints leak into the other"

                    for subject in shapeSubjects do
                        let props =
                            doc.Statements
                            |> List.filter (fun (s, p, _) -> s = subject && p = "sh:property")

                        Expect.hasLength props 1 "each shape keeps exactly its own one property shape"
                }

                test "a shape's subject is a blank node, never its target class's IRI" {
                    let doc =
                        Shacl.toDoc [ recordShape (targetClass (Uri "https://schema.org/Person")) [] ]

                    let subject = subjectOfTargetClass doc "https://schema.org/Person"

                    match subject with
                    | Node.Blank _ -> ()
                    | Node.Iri iri ->
                        failtestf
                            "shape subject is the class IRI (%s) -- that is what makes two shapes over one class collide"
                            iri

                    Expect.all
                        doc.Statements
                        (fun (s, _, _) -> s <> Node.Iri "https://schema.org/Person")
                        "nothing is asserted with the target class itself as subject"
                }

                test "an EnumShape's subject is a blank node too, with sh:targetClass pointing at the class" {
                    let doc =
                        Shacl.toDoc
                            [ enumShape
                                  (Uri "https://schema.org/GameStatusType")
                                  (Uri "https://schema.org/Active")
                                  [ Uri "https://schema.org/Completed" ] ]

                    let subject = subjectOfTargetClass doc "https://schema.org/GameStatusType"

                    match subject with
                    | Node.Blank _ -> ()
                    | Node.Iri iri -> failtestf "EnumShape subject is the class IRI (%s)" iri

                    Expect.exists
                        doc.Statements
                        (fun (s, p, _) -> s = subject && p = "sh:in")
                        "sh:in hangs off the shape's own subject"
                }

                test "two EnumShapes over one class keep their sh:in lists apart" {
                    let a =
                        enumShape (Uri "https://schema.org/Status") (Uri "https://schema.org/Active") []

                    let b =
                        enumShape (Uri "https://schema.org/Status") (Uri "https://schema.org/Done") []

                    let doc = Shacl.toDoc [ a; b ]

                    let ins = doc.Statements |> List.filter (fun (_, p, _) -> p = "sh:in")

                    Expect.hasLength ins 2 "two sh:in statements"

                    Expect.isTrue
                        (ins |> List.map (fun (s, _, _) -> s) |> List.distinct |> List.length = 2)
                        "on two distinct subjects -- one shape must not see the other's allowed values"
                } ]

          testList
              "RecordShape skeleton"
              [ test "an untyped, unconstrained RecordShape declares sh:NodeShape and its target class" {
                    let decl = recordShape (targetClass (Uri "https://schema.org/MoveAction")) []
                    let doc = Shacl.toDoc [ decl ]
                    // The subject is a freshly minted blank node, not the class IRI -- finding I2.
                    let subject = subjectOfTargetClass doc "https://schema.org/MoveAction"

                    Expect.exists
                        doc.Statements
                        (fun (s, p, v) -> s = subject && p = RdfTypeIri && v = Value.Node(Node.Iri "sh:NodeShape"))
                        "rdf:type sh:NodeShape"

                    Expect.exists
                        doc.Statements
                        (fun (s, p, v) ->
                            s = subject
                            && p = "sh:targetClass"
                            && v = Value.Node(Node.Iri "https://schema.org/MoveAction"))
                        "sh:targetClass points FROM the shape TO the class"
                }

                test "a property shape becomes a blank-node sh:property with sh:path -- no constraint triples yet" {
                    let prop = ofPath (PropertyPath.Predicate(Uri "https://schema.org/position"))
                    let decl = recordShape (targetClass (Uri "https://schema.org/MoveAction")) [ prop ]
                    let doc = Shacl.toDoc [ decl ]
                    let subject = subjectOfTargetClass doc "https://schema.org/MoveAction"

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
                } ]

          testList
              "string-based constraints"
              [ test "sh:minLength and sh:maxLength" {
                    let prop =
                        ofPath (PropertyPath.Predicate(Uri "https://schema.org/name"))
                        |> addConstraint (PropertyConstraint.MinLength 1)
                        |> addConstraint (PropertyConstraint.MaxLength 200)

                    let doc =
                        Shacl.toDoc [ recordShape (targetClass (Uri "https://schema.org/T")) [ prop ] ]

                    Expect.exists
                        doc.Statements
                        (fun (_, p, v) -> p = "sh:minLength" && v = Value.Literal(Literal.Int 1))
                        "sh:minLength"

                    Expect.exists
                        doc.Statements
                        (fun (_, p, v) -> p = "sh:maxLength" && v = Value.Literal(Literal.Int 200))
                        "sh:maxLength"
                }

                test "sh:pattern without flags omits sh:flags entirely" {
                    let prop =
                        ofPath (PropertyPath.Predicate(Uri "https://schema.org/email"))
                        |> addConstraint (PropertyConstraint.Pattern(@"^\S+@\S+$", None))

                    let doc =
                        Shacl.toDoc [ recordShape (targetClass (Uri "https://schema.org/T")) [ prop ] ]

                    Expect.exists
                        doc.Statements
                        (fun (_, p, v) -> p = "sh:pattern" && v = Value.Literal(Literal.String @"^\S+@\S+$"))
                        "sh:pattern"

                    Expect.all doc.Statements (fun (_, p, _) -> p <> "sh:flags") "no sh:flags when None"
                }

                test "sh:pattern with Some flags also emits sh:flags" {
                    let prop =
                        ofPath (PropertyPath.Predicate(Uri "https://schema.org/email"))
                        |> addConstraint (PropertyConstraint.Pattern(@"^\S+$", Some "i"))

                    let doc =
                        Shacl.toDoc [ recordShape (targetClass (Uri "https://schema.org/T")) [ prop ] ]

                    Expect.exists
                        doc.Statements
                        (fun (_, p, v) -> p = "sh:flags" && v = Value.Literal(Literal.String "i"))
                        "sh:flags present"
                }

                test "sh:languageIn is a well-formed rdf:list of string literals" {
                    let tags = NonEmptyList.ofList [ "en"; "fr" ] |> Option.get

                    let prop =
                        ofPath (PropertyPath.Predicate(Uri "https://schema.org/name"))
                        |> addConstraint (PropertyConstraint.LanguageIn tags)

                    let doc =
                        Shacl.toDoc [ recordShape (targetClass (Uri "https://schema.org/T")) [ prop ] ]

                    Expect.exists doc.Statements (fun (_, p, _) -> p = "sh:languageIn") "sh:languageIn present"

                    let listHead =
                        doc.Statements
                        |> List.pick (fun (_, p, v) -> if p = "sh:languageIn" then Some v else None)

                    match listHead with
                    | Value.Node headNode ->
                        Expect.exists
                            doc.Statements
                            (fun (s, p, _) -> s = headNode && p = "rdf:first")
                            "list head has rdf:first"
                    | other -> failtestf "expected a node, got %A" other
                }

                test "sh:uniqueLang as a boolean literal" {
                    let prop =
                        ofPath (PropertyPath.Predicate(Uri "https://schema.org/name"))
                        |> addConstraint (PropertyConstraint.UniqueLang true)

                    let doc =
                        Shacl.toDoc [ recordShape (targetClass (Uri "https://schema.org/T")) [ prop ] ]

                    Expect.exists
                        doc.Statements
                        (fun (_, p, v) -> p = "sh:uniqueLang" && v = Value.Literal(Literal.Bool true))
                        "sh:uniqueLang"
                } ]

          testList
              "property pair constraints"
              [ test "sh:equals, sh:disjoint, sh:lessThan, sh:lessThanOrEquals each point at the given property IRI" {
                    let cases =
                        [ PropertyConstraint.Equals(Uri "https://schema.org/a"), "sh:equals"
                          PropertyConstraint.Disjoint(Uri "https://schema.org/b"), "sh:disjoint"
                          PropertyConstraint.LessThan(Uri "https://schema.org/c"), "sh:lessThan"
                          PropertyConstraint.LessThanOrEquals(Uri "https://schema.org/d"), "sh:lessThanOrEquals" ]

                    for constr, predicate in cases do
                        let prop =
                            ofPath (PropertyPath.Predicate(Uri "https://schema.org/x"))
                            |> addConstraint constr

                        let doc =
                            Shacl.toDoc [ recordShape (targetClass (Uri "https://schema.org/T")) [ prop ] ]

                        Expect.exists doc.Statements (fun (_, p, _) -> p = predicate) $"{predicate} present"
                } ]

          testList
              "recursive shape-based constraints and logical combinators"
              [ test "sh:node embeds the referenced shape's own subject and statements" {
                    let personShape =
                        recordShape
                            (targetClass (Uri "https://schema.org/Person"))
                            [ ofPath (PropertyPath.Predicate(Uri "https://schema.org/email"))
                              |> addConstraint (PropertyConstraint.MinCount 1) ]

                    let agentProp =
                        ofPath (PropertyPath.Predicate(Uri "https://schema.org/agent"))
                        |> addConstraint (PropertyConstraint.Node personShape)

                    let doc =
                        Shacl.toDoc [ recordShape (targetClass (Uri "https://schema.org/MoveAction")) [ agentProp ] ]

                    let personSubject = subjectOfTargetClass doc "https://schema.org/Person"

                    Expect.exists
                        doc.Statements
                        (fun (_, p, v) -> p = "sh:node" && v = Value.Node personSubject)
                        "sh:node points at Person's own shape subject"

                    Expect.exists
                        doc.Statements
                        (fun (s, p, _) -> s = personSubject && p = RdfTypeIri)
                        "Person's own sh:NodeShape triples are present too"
                }

                test
                    "a shape referenced both in toDoc's top-level list AND nested via sh:node is emitted exactly once (no duplicate sh:property blank nodes)" {
                    // The exact "shared shape" pattern the design doc recommends and
                    // Frank.Validation.Sample uses: personShape validates standalone (as a top-level
                    // list entry) AND nests inside moveShape's agent property via sh:node.
                    let personShape =
                        recordShape
                            (targetClass (Uri "https://schema.org/Person"))
                            [ ofPath (PropertyPath.Predicate(Uri "https://schema.org/name"))
                              |> addConstraint (PropertyConstraint.MinCount 1) ]

                    let agentProp =
                        ofPath (PropertyPath.Predicate(Uri "https://schema.org/agent"))
                        |> addConstraint (PropertyConstraint.Node personShape)

                    let moveShape =
                        recordShape (targetClass (Uri "https://schema.org/MoveAction")) [ agentProp ]

                    let doc = Shacl.toDoc [ moveShape; personShape ]
                    let personSubject = subjectOfTargetClass doc "https://schema.org/Person"

                    let personPropertyStatements =
                        doc.Statements
                        |> List.filter (fun (s, p, _) -> s = personSubject && p = "sh:property")

                    Expect.hasLength
                        personPropertyStatements
                        1
                        "Person has exactly one sh:property (for schema:name) -- emitted once, not once per reference site"
                }

                test
                    "two independently-constructed shapes differing only by IRI fragment (e.g. prov#Agent vs prov#Activity) both appear fully -- not silently dropped or hash-collided" {
                    // System.Uri.Equals/GetHashCode ignore the URI fragment, so a memo keyed by naive
                    // structural equality treats two shapes as equal whenever every field EXCEPT the
                    // fragment portion of a targetClass Uri matches -- exactly what both shapes below
                    // do (both require schema:name, differing only in whether their target class ends
                    // in #Agent or #Activity). Hash-fragment IRIs (prov#, rdf#, rdfs#, owl#, skos#,
                    // sh#, ...) are the dominant convention for hand-authored RDF vocabularies --
                    // including this repo's own Frank.Provenance -- so this is not an exotic edge case.
                    let agentShape =
                        recordShape
                            (targetClass (Uri "http://www.w3.org/ns/prov#Agent"))
                            [ ofPath (PropertyPath.Predicate(Uri "https://schema.org/name"))
                              |> addConstraint (PropertyConstraint.MinCount 1) ]

                    let activityShape =
                        recordShape
                            (targetClass (Uri "http://www.w3.org/ns/prov#Activity"))
                            [ ofPath (PropertyPath.Predicate(Uri "https://schema.org/name"))
                              |> addConstraint (PropertyConstraint.MinCount 1) ]

                    let doc = Shacl.toDoc [ agentShape; activityShape ]

                    let agentSubject = subjectOfTargetClass doc "http://www.w3.org/ns/prov#Agent"

                    let activitySubject = subjectOfTargetClass doc "http://www.w3.org/ns/prov#Activity"

                    Expect.notEqual
                        agentSubject
                        activitySubject
                        "Activity's shape is its OWN subject -- NOT silently dropped by a fragment hash collision"

                    let agentPropertyStatements =
                        doc.Statements
                        |> List.filter (fun (s, p, _) -> s = agentSubject && p = "sh:property")

                    let activityPropertyStatements =
                        doc.Statements
                        |> List.filter (fun (s, p, _) -> s = activitySubject && p = "sh:property")

                    Expect.hasLength
                        agentPropertyStatements
                        1
                        "Agent has its own sh:property (schema:name) -- not silently dropped"

                    Expect.hasLength
                        activityPropertyStatements
                        1
                        "Activity has its own sh:property (schema:name) -- not silently dropped or merged into Agent's"

                    // Both shapes constrain the same predicate (schema:name), so the meaningful check
                    // here isn't "which path" but "did each shape get its OWN sh:property blank node"
                    // -- a hash collision would either drop Activity's property entirely (asserted
                    // above already) or, worse, have both shapes point at the very same blank node.
                    match agentPropertyStatements, activityPropertyStatements with
                    | [ (_, _, Value.Node agentPropBn) ], [ (_, _, Value.Node activityPropBn) ] ->
                        Expect.notEqual
                            agentPropBn
                            activityPropBn
                            "Agent's and Activity's sh:property blank nodes are distinct -- each shape's constraint is its own, not shared/misrouted"
                    | other -> failtestf "expected exactly one sh:property statement per shape, got %A" other
                }

                test
                    "sh:qualifiedValueShape carries the shape plus qualifiedMinCount/qualifiedMaxCount/qualifiedValueShapesDisjoint" {
                    let inner = recordShape [] []

                    let prop =
                        ofPath (PropertyPath.Predicate(Uri "https://schema.org/x"))
                        |> addConstraint (PropertyConstraint.QualifiedValueShape(inner, Some 1, Some 2, true))

                    let doc =
                        Shacl.toDoc [ recordShape (targetClass (Uri "https://schema.org/T")) [ prop ] ]

                    Expect.exists
                        doc.Statements
                        (fun (_, p, _) -> p = "sh:qualifiedValueShape")
                        "sh:qualifiedValueShape present"

                    Expect.exists
                        doc.Statements
                        (fun (_, p, v) -> p = "sh:qualifiedMinCount" && v = Value.Literal(Literal.Int 1))
                        "sh:qualifiedMinCount"

                    Expect.exists
                        doc.Statements
                        (fun (_, p, v) -> p = "sh:qualifiedMaxCount" && v = Value.Literal(Literal.Int 2))
                        "sh:qualifiedMaxCount"

                    Expect.exists
                        doc.Statements
                        (fun (_, p, v) -> p = "sh:qualifiedValueShapesDisjoint" && v = Value.Literal(Literal.Bool true))
                        "sh:qualifiedValueShapesDisjoint"
                }

                test "sh:qualifiedMinCount/MaxCount are omitted when None, not emitted as absent literals" {
                    let inner = recordShape [] []

                    let prop =
                        ofPath (PropertyPath.Predicate(Uri "https://schema.org/x"))
                        |> addConstraint (PropertyConstraint.QualifiedValueShape(inner, None, None, false))

                    let doc =
                        Shacl.toDoc [ recordShape (targetClass (Uri "https://schema.org/T")) [ prop ] ]

                    Expect.all
                        doc.Statements
                        (fun (_, p, _) -> p <> "sh:qualifiedMinCount")
                        "no sh:qualifiedMinCount when None"

                    Expect.all
                        doc.Statements
                        (fun (_, p, _) -> p <> "sh:qualifiedMaxCount")
                        "no sh:qualifiedMaxCount when None"
                }

                test "And/Or/Xone are well-formed rdf:lists of member shape nodes; Not is a single shape reference" {
                    let a = recordShape (targetClass (Uri "https://schema.org/A")) []
                    let b = recordShape (targetClass (Uri "https://schema.org/B")) []

                    let andDoc = Shacl.toDoc [ ShapeDecl.And { Head = a; Tail = [ b ] } ]
                    let orDoc = Shacl.toDoc [ ShapeDecl.Or { Head = a; Tail = [ b ] } ]
                    let xoneDoc = Shacl.toDoc [ ShapeDecl.Xone { Head = a; Tail = [ b ] } ]
                    let notDoc = Shacl.toDoc [ ShapeDecl.Not a ]

                    // Check And predicate and rdf:list structure
                    Expect.exists andDoc.Statements (fun (_, p, _) -> p = "sh:and") "sh:and present"

                    let andFirsts = andDoc.Statements |> List.filter (fun (_, p, _) -> p = "rdf:first")

                    Expect.isGreaterThanOrEqual
                        andFirsts.Length
                        2
                        "And combinator list has at least 2 rdf:first cells (A and B shapes)"

                    let andRests = andDoc.Statements |> List.filter (fun (_, p, _) -> p = "rdf:rest")
                    Expect.isGreaterThanOrEqual andRests.Length 2 "And combinator list has at least 2 rdf:rest cells"

                    let andNilRests =
                        andRests
                        |> List.filter (fun (_, _, v) ->
                            v = Value.Node(Node.Iri "http://www.w3.org/1999/02/22-rdf-syntax-ns#nil"))

                    Expect.isGreaterThanOrEqual andNilRests.Length 1 "And combinator list terminates in rdf:nil"

                    Expect.exists
                        andDoc.Statements
                        (fun (s, p, _) -> s = subjectOfTargetClass andDoc "https://schema.org/A" && p = RdfTypeIri)
                        "Shape A's own rdf:type sh:NodeShape triple is present"

                    Expect.exists
                        andDoc.Statements
                        (fun (s, p, _) -> s = subjectOfTargetClass andDoc "https://schema.org/B" && p = RdfTypeIri)
                        "Shape B's own rdf:type sh:NodeShape triple is present"

                    // Check Or predicate and rdf:list structure
                    Expect.exists orDoc.Statements (fun (_, p, _) -> p = "sh:or") "sh:or present"

                    let orFirsts = orDoc.Statements |> List.filter (fun (_, p, _) -> p = "rdf:first")
                    Expect.isGreaterThanOrEqual orFirsts.Length 2 "Or combinator list has at least 2 rdf:first cells"

                    let orRests = orDoc.Statements |> List.filter (fun (_, p, _) -> p = "rdf:rest")
                    Expect.isGreaterThanOrEqual orRests.Length 2 "Or combinator list has at least 2 rdf:rest cells"

                    Expect.exists
                        orDoc.Statements
                        (fun (s, p, _) -> s = subjectOfTargetClass orDoc "https://schema.org/A" && p = RdfTypeIri)
                        "Shape A's own rdf:type is present in Or"

                    Expect.exists
                        orDoc.Statements
                        (fun (s, p, _) -> s = subjectOfTargetClass orDoc "https://schema.org/B" && p = RdfTypeIri)
                        "Shape B's own rdf:type is present in Or"

                    // Check Xone predicate and rdf:list structure
                    Expect.exists xoneDoc.Statements (fun (_, p, _) -> p = "sh:xone") "sh:xone present"

                    let xoneFirsts =
                        xoneDoc.Statements |> List.filter (fun (_, p, _) -> p = "rdf:first")

                    Expect.isGreaterThanOrEqual
                        xoneFirsts.Length
                        2
                        "Xone combinator list has at least 2 rdf:first cells"

                    let xoneRests = xoneDoc.Statements |> List.filter (fun (_, p, _) -> p = "rdf:rest")
                    Expect.isGreaterThanOrEqual xoneRests.Length 2 "Xone combinator list has at least 2 rdf:rest cells"

                    Expect.exists
                        xoneDoc.Statements
                        (fun (s, p, _) -> s = subjectOfTargetClass xoneDoc "https://schema.org/A" && p = RdfTypeIri)
                        "Shape A's own rdf:type is present in Xone"

                    Expect.exists
                        xoneDoc.Statements
                        (fun (s, p, _) -> s = subjectOfTargetClass xoneDoc "https://schema.org/B" && p = RdfTypeIri)
                        "Shape B's own rdf:type is present in Xone"

                    // Check Not predicate (single reference, no list)
                    Expect.exists
                        notDoc.Statements
                        (fun (_, p, v) ->
                            p = "sh:not"
                            && v = Value.Node(subjectOfTargetClass notDoc "https://schema.org/A"))
                        "sh:not points directly at the negated shape"

                    Expect.exists
                        notDoc.Statements
                        (fun (s, p, _) -> s = subjectOfTargetClass notDoc "https://schema.org/A" && p = RdfTypeIri)
                        "Shape A's own rdf:type is present in Not"
                } ]

          testList
              "EnumShape, sh:hasValue, sh:in"
              [ test "EnumShape emits sh:targetClass and a well-formed sh:in list of the case IRIs" {
                    let decl =
                        enumShape
                            (Uri "https://schema.org/GameStatusType")
                            (Uri "https://schema.org/Active")
                            [ Uri "https://schema.org/Completed" ]

                    let doc = Shacl.toDoc [ decl ]
                    let subject = subjectOfTargetClass doc "https://schema.org/GameStatusType"

                    Expect.exists
                        doc.Statements
                        (fun (s, p, v) ->
                            s = subject
                            && p = "sh:targetClass"
                            && v = Value.Node(Node.Iri "https://schema.org/GameStatusType"))
                        "sh:targetClass points FROM the shape TO the class"

                    Expect.exists doc.Statements (fun (s, p, _) -> s = subject && p = "sh:in") "sh:in present"
                }

                test "sh:hasValue carries the given Value (node or literal) unchanged" {
                    let prop =
                        ofPath (PropertyPath.Predicate(Uri "https://schema.org/status"))
                        |> addConstraint (PropertyConstraint.HasValue(Value.Node(Node.Iri "https://schema.org/Active")))

                    let doc =
                        Shacl.toDoc [ recordShape (targetClass (Uri "https://schema.org/T")) [ prop ] ]

                    Expect.exists
                        doc.Statements
                        (fun (_, p, v) -> p = "sh:hasValue" && v = Value.Node(Node.Iri "https://schema.org/Active"))
                        "sh:hasValue"
                }

                test "sh:in (AllowedValues) on a property shape is a well-formed rdf:list, mixing nodes and literals" {
                    let values =
                        NonEmptyList.ofList
                            [ Value.Literal(Literal.String "a")
                              Value.Node(Node.Iri "https://schema.org/b") ]
                        |> Option.get

                    let prop =
                        ofPath (PropertyPath.Predicate(Uri "https://schema.org/x"))
                        |> addConstraint (PropertyConstraint.AllowedValues values)

                    let doc =
                        Shacl.toDoc [ recordShape (targetClass (Uri "https://schema.org/T")) [ prop ] ]

                    Expect.exists doc.Statements (fun (_, p, _) -> p = "sh:in") "sh:in present"
                }

                // No test for "constraintStatements is now exhaustive except Sparql" -- that guarantee is
                // compiler-checked (FS0025 -> error under this repo's TreatWarningsAsErrors) the moment the
                // wildcard narrows to just Sparql; a runtime assertion would add nothing. Sparql itself is
                // exercised in Task 11's tests.
                ]

          testList
              "sh:sparql"
              [ test "sh:sparql is a blank node carrying sh:select with the author's query text" {
                    let sc =
                        { Query = "SELECT $this WHERE { $this <https://schema.org/position> ?p . FILTER (?p < 0) }"
                          Message = None
                          Prefixes = [] }

                    let prop =
                        ofPath (PropertyPath.Predicate(Uri "https://schema.org/position"))
                        |> addConstraint (PropertyConstraint.Sparql sc)

                    let doc =
                        Shacl.toDoc [ recordShape (targetClass (Uri "https://schema.org/T")) [ prop ] ]

                    Expect.exists doc.Statements (fun (_, p, _) -> p = "sh:sparql") "sh:sparql present"

                    Expect.exists
                        doc.Statements
                        (fun (_, p, v) ->
                            p = "sh:select"
                            && (match v with
                                | Value.Literal(Literal.String s) -> s = sc.Query
                                | _ -> false))
                        "sh:select carries the query text exactly (no prefix lines prepended)"

                    Expect.all doc.Statements (fun (_, p, _) -> p <> "sh:message") "no sh:message when Message = None"
                }

                test "declared prefixes are prepended to the query text as PREFIX lines" {
                    let sc =
                        { Query = "SELECT $this WHERE { $this a schema:Person }"
                          Message = None
                          Prefixes = [ "schema", "https://schema.org/" ] }

                    let prop =
                        ofPath (PropertyPath.Predicate(Uri "https://schema.org/x"))
                        |> addConstraint (PropertyConstraint.Sparql sc)

                    let doc =
                        Shacl.toDoc [ recordShape (targetClass (Uri "https://schema.org/T")) [ prop ] ]

                    Expect.exists
                        doc.Statements
                        (fun (_, p, v) ->
                            p = "sh:select"
                            && (match v with
                                | Value.Literal(Literal.String s) -> s.Contains "PREFIX schema: <https://schema.org/>"
                                | _ -> false))
                        "PREFIX line prepended"
                }

                // Final-review finding C2. The review reported "documented as ASK, always emitted as
                // sh:select" and asked for sh:ask emission. Verified against both the spec and the
                // engine, that is not implementable: SHACL's sh:sparql is SELECT-based by definition
                // (5.2 -- sh:ask belongs to sh:SPARQLAskValidator under a custom constraint
                // component, 6.2.3.2), and dotNetRDF's Constraints/Constraint.cs dispatches
                // `Vocabulary.Sparql -> new Select(shape, value)` unconditionally, its Select
                // validator raising "A sh:SPARQLSelectValidator must have exactly one sh:select
                // property" when handed sh:ask. So the SILENT BYPASS the finding is really about
                // (an ASK executes to zero bindings, which the Select validator reads as
                // "conforming") is closed at the other end: rejected loudly, at shape-build time.
                test "an ASK-form query is rejected at toShapesGraph build time, not silently ignored" {
                    let sc =
                        { Query = "ASK { $this <https://schema.org/position> ?p . FILTER (?p > 0) }"
                          Message = None
                          Prefixes = [] }

                    let prop =
                        ofPath (PropertyPath.Predicate(Uri "https://schema.org/position"))
                        |> addConstraint (PropertyConstraint.Sparql sc)

                    let shapes = [ recordShape (targetClass (Uri "https://schema.org/T")) [ prop ] ]

                    let ex = captureInvalidOp (fun () -> Shacl.toShapesGraph shapes |> ignore)

                    Expect.stringContains ex.Message "only SELECT queries" "the message says what is wrong"

                    Expect.stringContains
                        ex.Message
                        "FILTER NOT EXISTS"
                        "the message shows the SELECT rewrite, so the author is not left guessing"
                }

                test "toDoc itself stays total -- an ASK query still projects (as sh:select), it just cannot build" {
                    let sc =
                        { Query = "ASK { $this <https://schema.org/position> ?p }"
                          Message = None
                          Prefixes = [] }

                    let prop =
                        ofPath (PropertyPath.Predicate(Uri "https://schema.org/position"))
                        |> addConstraint (PropertyConstraint.Sparql sc)

                    let doc =
                        Shacl.toDoc [ recordShape (targetClass (Uri "https://schema.org/T")) [ prop ] ]

                    Expect.exists doc.Statements (fun (_, p, _) -> p = "sh:select") "toDoc does not raise"
                    Expect.all doc.Statements (fun (_, p, _) -> p <> "sh:ask") "sh:ask is never emitted"
                }

                // Final-review finding I1: the design doc's error-handling table says a Sparql
                // constraint's query failing to parse is "raised at toShapesGraph build time (shape-
                // authoring time), never deferred to request-validation time". It was not checked at
                // all, so a typo produced an unhandled RdfParseException on every request instead.
                test "a malformed SPARQL query raises at toShapesGraph time, not at validate time" {
                    let sc =
                        { Query = "SELECT $this WHERE { <<< not sparql"
                          Message = None
                          Prefixes = [] }

                    let prop =
                        ofPath (PropertyPath.Predicate(Uri "https://schema.org/position"))
                        |> addConstraint (PropertyConstraint.Sparql sc)

                    let shapes = [ recordShape (targetClass (Uri "https://schema.org/T")) [ prop ] ]

                    // toDoc stays total: only the ShapesGraph build rejects it.
                    Shacl.toDoc shapes |> ignore

                    let ex = captureInvalidOp (fun () -> Shacl.toShapesGraph shapes |> ignore)

                    Expect.stringContains ex.Message "does not parse as SPARQL" "descriptive message"
                }

                test "a malformed SPARQL query nested behind sh:node and a combinator is still caught" {
                    let sc =
                        { Query = "SELECT $this WHERE { }}}}"
                          Message = None
                          Prefixes = [] }

                    let inner =
                        recordShape
                            []
                            [ ofPath (PropertyPath.Predicate(Uri "https://schema.org/x"))
                              |> addConstraint (PropertyConstraint.Sparql sc) ]

                    let outer =
                        recordShape
                            (targetClass (Uri "https://schema.org/T"))
                            [ ofPath (PropertyPath.Predicate(Uri "https://schema.org/agent"))
                              |> addConstraint (PropertyConstraint.Node inner) ]

                    let combined =
                        ShapeDecl.And
                            { Head = outer
                              Tail = [ recordShape (targetClass (Uri "https://schema.org/U")) [] ] }

                    captureInvalidOp (fun () -> Shacl.toShapesGraph [ combined ] |> ignore)
                    |> ignore
                }

                test "a prefix declared on the constraint is part of what gets parsed, not just what gets emitted" {
                    // Without the PREFIX line prepended this query does not parse at all -- proof
                    // that toShapesGraph validates the text it actually emits.
                    let sc =
                        { Query = "SELECT $this WHERE { $this a schema:Person }"
                          Message = None
                          Prefixes = [ "schema", "https://schema.org/" ] }

                    let prop =
                        ofPath (PropertyPath.Predicate(Uri "https://schema.org/x"))
                        |> addConstraint (PropertyConstraint.Sparql sc)

                    Shacl.toShapesGraph [ recordShape (targetClass (Uri "https://schema.org/T")) [ prop ] ]
                    |> ignore

                    let brokenShapes =
                        [ recordShape
                              (targetClass (Uri "https://schema.org/T"))
                              [ ofPath (PropertyPath.Predicate(Uri "https://schema.org/x"))
                                |> addConstraint (PropertyConstraint.Sparql { sc with Prefixes = [] }) ] ]

                    captureInvalidOp (fun () -> Shacl.toShapesGraph brokenShapes |> ignore)
                    |> ignore
                }

                test "an author message on the sh:sparql constraint becomes sh:message on the same blank node" {
                    let sc =
                        { Query = "SELECT $this WHERE { FILTER (false) }"
                          Message = Some "always fails"
                          Prefixes = [] }

                    let prop =
                        ofPath (PropertyPath.Predicate(Uri "https://schema.org/x"))
                        |> addConstraint (PropertyConstraint.Sparql sc)

                    let doc =
                        Shacl.toDoc [ recordShape (targetClass (Uri "https://schema.org/T")) [ prop ] ]

                    Expect.exists
                        doc.Statements
                        (fun (_, p, v) -> p = "sh:message" && v = Value.Literal(Literal.String "always fails"))
                        "sh:message present"
                } ]

          testList
              "closed, severity, message, toShapesGraph"
              [ test "sh:closed true plus sh:ignoredProperties as a well-formed rdf:list, when Closed is set" {
                    let decl =
                        recordShape (targetClass (Uri "https://schema.org/T")) []
                        |> function
                            | ShapeDecl.RecordShape n ->
                                ShapeDecl.RecordShape
                                    { n with
                                        Closed = true
                                        IgnoredProperties = [ Uri "https://schema.org/extra" ] }
                            | other -> other

                    let doc = Shacl.toDoc [ decl ]

                    Expect.exists
                        doc.Statements
                        (fun (_, p, v) -> p = "sh:closed" && v = Value.Literal(Literal.Bool true))
                        "sh:closed"

                    Expect.exists
                        doc.Statements
                        (fun (_, p, _) -> p = "sh:ignoredProperties")
                        "sh:ignoredProperties present"
                }

                test "sh:closed false emits no sh:closed triple at all (SHACL's own default, nothing to assert)" {
                    let doc = Shacl.toDoc [ recordShape (targetClass (Uri "https://schema.org/T")) [] ]

                    Expect.all doc.Statements (fun (_, p, _) -> p <> "sh:closed") "no sh:closed when not closed"
                }

                test "NodeShapeSpec.Severity/Message become sh:severity/sh:message on the shape's own subject" {
                    let decl =
                        recordShape (targetClass (Uri "https://schema.org/T")) []
                        |> function
                            | ShapeDecl.RecordShape n ->
                                ShapeDecl.RecordShape
                                    { n with
                                        Severity = Some Severity.Warning
                                        Message = Some "be careful" }
                            | other -> other

                    let doc = Shacl.toDoc [ decl ]

                    Expect.exists
                        doc.Statements
                        (fun (_, p, v) -> p = "sh:severity" && v = Value.Node(Node.Iri "sh:Warning"))
                        "sh:severity"

                    Expect.exists
                        doc.Statements
                        (fun (_, p, v) -> p = "sh:message" && v = Value.Literal(Literal.String "be careful"))
                        "sh:message"
                }

                test
                    "PropertyShapeSpec.Severity/Message become sh:severity/sh:message on that property's own blank node" {
                    let prop =
                        { ofPath (PropertyPath.Predicate(Uri "https://schema.org/x")) with
                            Severity = Some Severity.Info
                            Message = Some "informational" }

                    let doc =
                        Shacl.toDoc [ recordShape (targetClass (Uri "https://schema.org/T")) [ prop ] ]

                    Expect.exists
                        doc.Statements
                        (fun (_, p, v) -> p = "sh:severity" && v = Value.Node(Node.Iri "sh:Info"))
                        "sh:severity on property shape"

                    Expect.exists
                        doc.Statements
                        (fun (_, p, v) -> p = "sh:message" && v = Value.Literal(Literal.String "informational"))
                        "sh:message on property shape"
                }

                test "toShapesGraph builds a real dotNetRDF ShapesGraph from a ShapeDecl list" {
                    let decl =
                        recordShape
                            (targetClass (Uri "https://schema.org/MoveAction"))
                            [ ofPath (PropertyPath.Predicate(Uri "https://schema.org/position"))
                              |> addConstraint (PropertyConstraint.Datatype XsdDatatype.Integer) ]

                    let sg = Shacl.toShapesGraph [ decl ]
                    Expect.isNotNull (box sg) "ShapesGraph constructed without throwing"
                } ] ]
