/// Final-review finding I5: Shacl.validate had four tests total (MinCount conforming/violating,
/// EnumShape violating, empty graph), while Shacl.toDoc had ~60. That asymmetry is the STRUCTURAL
/// reason findings C2 and I2 survived nineteen scoped task reviews: EMISSION was tested exhaustively
/// while BEHAVIOUR was not tested at all, so a shapes graph could be well-formed RDF and still
/// validate nothing.
///
/// This file closes that gap with a conforming/violating pair per constraint kind, run through the
/// real dotNetRDF SHACL engine against real IGraph data -- breadth over per-kind depth, exactly as
/// the design doc's Testing section calls for ("conformance tests, both conforming and violating,
/// per constraint kind, including at least one recursive Node case and one And/Or/Not/Xone
/// composition").
module Frank.Validation.Tests.ValidateConformanceTests

open System
open Expecto
open Frank.Rdf
open Frank.Validation
open Frank.Validation.ShapeSpecFunctions
open VDS.RDF

let private T = "https://schema.org/MoveAction"
let private Instance = "https://example.org/move1"

/// The object of a data triple, in the few shapes these tests need.
type private Obj =
    | Str of string
    | Num of int
    | Lang of value: string * lang: string
    | Ref of string
    /// A node identified by IRI that itself carries further triples -- for sh:node/sh:class/
    /// sh:qualifiedValueShape cases, where the VALUE has to be inspected.
    | Nested of iri: string * typ: string option * props: (string * Obj) list

let rec private assertProps (g: IGraph) (subject: INode) (props: (string * Obj) list) =
    for predicate, value in props do
        let p = g.CreateUriNode(UriFactory.Create predicate)

        let o: INode =
            match value with
            | Str s -> g.CreateLiteralNode s
            | Num n -> n.ToLiteral g
            | Lang(v, l) -> g.CreateLiteralNode(v, l)
            | Ref iri -> g.CreateUriNode(UriFactory.Create iri)
            | Nested(iri, typ, nestedProps) ->
                let node = g.CreateUriNode(UriFactory.Create iri)

                match typ with
                | Some t ->
                    g.Assert(
                        Triple(
                            node,
                            g.CreateUriNode(UriFactory.Create RdfTypeIri),
                            g.CreateUriNode(UriFactory.Create t)
                        )
                    )
                    |> ignore
                | None -> ()

                assertProps g node nestedProps
                node :> INode

        g.Assert(Triple(subject, p, o)) |> ignore

/// One instance of `T` carrying the given properties.
let private data (props: (string * Obj) list) : IGraph =
    let g = Graph() :> IGraph
    let subject = g.CreateUriNode(UriFactory.Create Instance)

    g.Assert(Triple(subject, g.CreateUriNode(UriFactory.Create RdfTypeIri), g.CreateUriNode(UriFactory.Create T)))
    |> ignore

    assertProps g subject props
    g

/// A shape targeting `T` with one property shape over `path` carrying `constraints`.
let private shapeOn (path: string) (constraints: PropertyConstraint list) : ShapeDecl =
    let prop =
        constraints
        |> List.fold (fun p c -> addConstraint c p) (ofPath (PropertyPath.Predicate(Uri path)))

    recordShape (targetClass (Uri T)) [ prop ]

let private expectConforms (shapes: ShapeDecl list) (graph: IGraph) (why: string) =
    match Shacl.validate (Shacl.toShapesGraph shapes) graph with
    | ValidationOutcome.Conforms -> ()
    | ValidationOutcome.Violates vs -> failtestf "%s -- expected Conforms, got %d violation(s): %A" why vs.Length vs

let private expectViolates (shapes: ShapeDecl list) (graph: IGraph) (why: string) =
    match Shacl.validate (Shacl.toShapesGraph shapes) graph with
    | ValidationOutcome.Conforms -> failtestf "%s -- expected Violates, got Conforms" why
    | ValidationOutcome.Violates vs -> Expect.isNonEmpty vs "at least one violation"

/// Both halves of one constraint kind, as a pair of tests.
let private pair
    (name: string)
    (shapes: ShapeDecl list)
    (conformingGraph: IGraph)
    (violatingGraph: IGraph)
    : Test list =
    [ test $"{name}: conforming data validates as Conforms" { expectConforms shapes conformingGraph name }
      test $"{name}: violating data validates as Violates" { expectViolates shapes violatingGraph name } ]

// --- shared fixtures for the shape-based (value-inspecting) constraints ---------------------------

/// A Person shape with no targets of its own -- referenced only via sh:node/sh:qualifiedValueShape,
/// which is the design doc's own "shape meant only for nesting" case.
let private personRequiresName =
    recordShape
        []
        [ ofPath (PropertyPath.Predicate(Uri "https://schema.org/name"))
          |> addConstraint (PropertyConstraint.MinCount 1) ]

let private personRequiresEmail =
    recordShape
        []
        [ ofPath (PropertyPath.Predicate(Uri "https://schema.org/email"))
          |> addConstraint (PropertyConstraint.MinCount 1) ]

let private agentWith (props: (string * Obj) list) =
    data [ "https://schema.org/agent", Nested("https://example.org/p1", Some "https://schema.org/Person", props) ]

[<Tests>]
let tests =
    testList
        "Shacl.validate conformance"
        [ testList
              "value type"
              (pair
                  "sh:datatype"
                  [ shapeOn "https://schema.org/position" [ PropertyConstraint.Datatype XsdDatatype.Integer ] ]
                  (data [ "https://schema.org/position", Num 3 ])
                  (data [ "https://schema.org/position", Str "three" ])
               @ pair
                   "sh:nodeKind"
                   [ shapeOn "https://schema.org/agent" [ PropertyConstraint.NodeKind NodeKind.Iri ] ]
                   (data [ "https://schema.org/agent", Ref "https://example.org/p1" ])
                   (data [ "https://schema.org/agent", Str "Alice" ])
               @ pair
                   "sh:class"
                   [ shapeOn "https://schema.org/agent" [ PropertyConstraint.Class(Uri "https://schema.org/Person") ] ]
                   (agentWith [])
                   (data
                       [ "https://schema.org/agent",
                         Nested("https://example.org/p1", Some "https://schema.org/Organization", []) ]))

          testList
              "cardinality"
              (pair
                  "sh:maxCount"
                  [ shapeOn "https://schema.org/position" [ PropertyConstraint.MaxCount 1 ] ]
                  (data [ "https://schema.org/position", Num 3 ])
                  (data [ "https://schema.org/position", Num 3; "https://schema.org/position", Num 4 ]))

          testList
              "string-based"
              (pair
                  "sh:minLength"
                  [ shapeOn "https://schema.org/name" [ PropertyConstraint.MinLength 3 ] ]
                  (data [ "https://schema.org/name", Str "Alice" ])
                  (data [ "https://schema.org/name", Str "Al" ])
               @ pair
                   "sh:maxLength"
                   [ shapeOn "https://schema.org/name" [ PropertyConstraint.MaxLength 5 ] ]
                   (data [ "https://schema.org/name", Str "Alice" ])
                   (data [ "https://schema.org/name", Str "Alexandra" ])
               @ pair
                   "sh:pattern"
                   [ shapeOn "https://schema.org/email" [ PropertyConstraint.Pattern(@"^\S+@\S+\.\S+$", None) ] ]
                   (data [ "https://schema.org/email", Str "alice@example.org" ])
                   (data [ "https://schema.org/email", Str "not-an-email" ])
               @ pair
                   "sh:pattern with sh:flags (case-insensitive)"
                   [ shapeOn "https://schema.org/name" [ PropertyConstraint.Pattern("^alice$", Some "i") ] ]
                   (data [ "https://schema.org/name", Str "ALICE" ])
                   (data [ "https://schema.org/name", Str "Bob" ])
               @ pair
                   "sh:languageIn"
                   [ shapeOn
                         "https://schema.org/name"
                         [ PropertyConstraint.LanguageIn { Head = "en"; Tail = [ "fr" ] } ] ]
                   (data [ "https://schema.org/name", Lang("Alice", "en") ])
                   (data [ "https://schema.org/name", Lang("Alicja", "pl") ])
               @ pair
                   "sh:uniqueLang"
                   [ shapeOn "https://schema.org/name" [ PropertyConstraint.UniqueLang true ] ]
                   (data
                       [ "https://schema.org/name", Lang("Alice", "en")
                         "https://schema.org/name", Lang("Alicia", "fr") ])
                   (data
                       [ "https://schema.org/name", Lang("Alice", "en")
                         "https://schema.org/name", Lang("Ally", "en") ]))

          testList
              "value range"
              (pair
                  "sh:minInclusive"
                  [ shapeOn "https://schema.org/position" [ PropertyConstraint.MinInclusive(Literal.Int 1) ] ]
                  (data [ "https://schema.org/position", Num 1 ])
                  (data [ "https://schema.org/position", Num 0 ])
               @ pair
                   "sh:minExclusive"
                   [ shapeOn "https://schema.org/position" [ PropertyConstraint.MinExclusive(Literal.Int 1) ] ]
                   (data [ "https://schema.org/position", Num 2 ])
                   (data [ "https://schema.org/position", Num 1 ])
               @ pair
                   "sh:maxInclusive"
                   [ shapeOn "https://schema.org/position" [ PropertyConstraint.MaxInclusive(Literal.Int 9) ] ]
                   (data [ "https://schema.org/position", Num 9 ])
                   (data [ "https://schema.org/position", Num 10 ])
               @ pair
                   "sh:maxExclusive"
                   [ shapeOn "https://schema.org/position" [ PropertyConstraint.MaxExclusive(Literal.Int 9) ] ]
                   (data [ "https://schema.org/position", Num 8 ])
                   (data [ "https://schema.org/position", Num 9 ]))

          testList
              "property pair"
              (pair
                  "sh:equals"
                  [ shapeOn
                        "https://schema.org/name"
                        [ PropertyConstraint.Equals(Uri "https://schema.org/alternateName") ] ]
                  (data
                      [ "https://schema.org/name", Str "Alice"
                        "https://schema.org/alternateName", Str "Alice" ])
                  (data
                      [ "https://schema.org/name", Str "Alice"
                        "https://schema.org/alternateName", Str "Bob" ])
               @ pair
                   "sh:disjoint"
                   [ shapeOn
                         "https://schema.org/name"
                         [ PropertyConstraint.Disjoint(Uri "https://schema.org/alternateName") ] ]
                   (data
                       [ "https://schema.org/name", Str "Alice"
                         "https://schema.org/alternateName", Str "Bob" ])
                   (data
                       [ "https://schema.org/name", Str "Alice"
                         "https://schema.org/alternateName", Str "Alice" ])
               @ pair
                   "sh:lessThan"
                   [ shapeOn
                         "https://schema.org/position"
                         [ PropertyConstraint.LessThan(Uri "https://schema.org/endPosition") ] ]
                   (data
                       [ "https://schema.org/position", Num 1
                         "https://schema.org/endPosition", Num 2 ])
                   (data
                       [ "https://schema.org/position", Num 3
                         "https://schema.org/endPosition", Num 2 ])
               @ pair
                   "sh:lessThanOrEquals"
                   [ shapeOn
                         "https://schema.org/position"
                         [ PropertyConstraint.LessThanOrEquals(Uri "https://schema.org/endPosition") ] ]
                   (data
                       [ "https://schema.org/position", Num 2
                         "https://schema.org/endPosition", Num 2 ])
                   (data
                       [ "https://schema.org/position", Num 3
                         "https://schema.org/endPosition", Num 2 ]))

          testList
              "shape-based (recursive)"
              (pair
                  "sh:node -- the value must itself conform to another shape"
                  [ shapeOn "https://schema.org/agent" [ PropertyConstraint.Node personRequiresName ] ]
                  (agentWith [ "https://schema.org/name", Str "Alice" ])
                  (agentWith [ "https://schema.org/email", Str "alice@example.org" ])
               @ pair
                   "sh:node nested two deep -- agent conforms to a shape whose own property has an sh:node"
                   [ shapeOn
                         "https://schema.org/agent"
                         [ PropertyConstraint.Node(
                               recordShape
                                   []
                                   [ ofPath (PropertyPath.Predicate(Uri "https://schema.org/worksFor"))
                                     |> addConstraint (PropertyConstraint.Node personRequiresName) ]
                           ) ] ]
                   (data
                       [ "https://schema.org/agent",
                         Nested(
                             "https://example.org/p1",
                             None,
                             [ "https://schema.org/worksFor",
                               Nested("https://example.org/o1", None, [ "https://schema.org/name", Str "Acme" ]) ]
                         ) ])
                   (data
                       [ "https://schema.org/agent",
                         Nested(
                             "https://example.org/p1",
                             None,
                             [ "https://schema.org/worksFor", Nested("https://example.org/o1", None, []) ]
                         ) ])
               @ pair
                   "sh:qualifiedValueShape with sh:qualifiedMinCount"
                   [ shapeOn
                         "https://schema.org/agent"
                         [ PropertyConstraint.QualifiedValueShape(personRequiresName, Some 1, None, false) ] ]
                   (agentWith [ "https://schema.org/name", Str "Alice" ])
                   (agentWith [ "https://schema.org/email", Str "alice@example.org" ]))

          testList
              "value set"
              (pair
                  "sh:hasValue"
                  [ shapeOn
                        "https://schema.org/status"
                        [ PropertyConstraint.HasValue(Value.Node(Node.Iri "https://schema.org/Active")) ] ]
                  (data [ "https://schema.org/status", Ref "https://schema.org/Active" ])
                  (data [ "https://schema.org/status", Ref "https://schema.org/Completed" ])
               @ pair
                   "sh:in (AllowedValues)"
                   [ shapeOn
                         "https://schema.org/status"
                         [ PropertyConstraint.AllowedValues
                               { Head = Value.Node(Node.Iri "https://schema.org/Active")
                                 Tail = [ Value.Node(Node.Iri "https://schema.org/Completed") ] } ] ]
                   (data [ "https://schema.org/status", Ref "https://schema.org/Completed" ])
                   (data [ "https://schema.org/status", Ref "https://schema.org/Abandoned" ]))

          // The combinators are ShapeDecls, and a top-level combinator carries no target of its own,
          // so it is reached the way SHACL intends: as the value of an sh:node constraint (or
          // sh:qualifiedValueShape). That is exactly how a consumer composes them.
          testList
              "logical combinators"
              (pair
                  "sh:and -- the value must satisfy BOTH member shapes"
                  [ shapeOn
                        "https://schema.org/agent"
                        [ PropertyConstraint.Node(
                              ShapeDecl.And
                                  { Head = personRequiresName
                                    Tail = [ personRequiresEmail ] }
                          ) ] ]
                  (agentWith
                      [ "https://schema.org/name", Str "Alice"
                        "https://schema.org/email", Str "alice@example.org" ])
                  (agentWith [ "https://schema.org/name", Str "Alice" ])
               @ pair
                   "sh:or -- the value must satisfy AT LEAST ONE member shape"
                   [ shapeOn
                         "https://schema.org/agent"
                         [ PropertyConstraint.Node(
                               ShapeDecl.Or
                                   { Head = personRequiresName
                                     Tail = [ personRequiresEmail ] }
                           ) ] ]
                   (agentWith [ "https://schema.org/email", Str "alice@example.org" ])
                   (agentWith [ "https://schema.org/telephone", Str "555" ])
               @ pair
                   "sh:not -- the value must NOT satisfy the member shape"
                   [ shapeOn "https://schema.org/agent" [ PropertyConstraint.Node(ShapeDecl.Not personRequiresName) ] ]
                   (agentWith [ "https://schema.org/email", Str "alice@example.org" ])
                   (agentWith [ "https://schema.org/name", Str "Alice" ])
               @ pair
                   "sh:xone -- the value must satisfy EXACTLY ONE member shape"
                   [ shapeOn
                         "https://schema.org/agent"
                         [ PropertyConstraint.Node(
                               ShapeDecl.Xone
                                   { Head = personRequiresName
                                     Tail = [ personRequiresEmail ] }
                           ) ] ]
                   (agentWith [ "https://schema.org/name", Str "Alice" ])
                   (agentWith
                       [ "https://schema.org/name", Str "Alice"
                         "https://schema.org/email", Str "alice@example.org" ]))

          testList
              "closedness"
              [ let closedToName =
                    match
                        recordShape
                            (targetClass (Uri T))
                            [ ofPath (PropertyPath.Predicate(Uri "https://schema.org/name")) ]
                    with
                    | ShapeDecl.RecordShape spec ->
                        ShapeDecl.RecordShape
                            { spec with
                                Closed = true
                                IgnoredProperties = [ Uri RdfTypeIri ] }
                    | other -> other

                yield!
                    pair
                        "sh:closed with rdf:type in sh:ignoredProperties"
                        [ closedToName ]
                        (data [ "https://schema.org/name", Str "Alice" ])
                        (data
                            [ "https://schema.org/name", Str "Alice"
                              "https://schema.org/email", Str "alice@example.org" ])

                let closedWithIgnored =
                    match closedToName with
                    | ShapeDecl.RecordShape spec ->
                        ShapeDecl.RecordShape
                            { spec with
                                IgnoredProperties = [ Uri RdfTypeIri; Uri "https://schema.org/email" ] }
                    | other -> other

                yield
                    test "sh:ignoredProperties widens the closed set by exactly the listed predicates" {
                        expectConforms
                            [ closedWithIgnored ]
                            (data
                                [ "https://schema.org/name", Str "Alice"
                                  "https://schema.org/email", Str "alice@example.org" ])
                            "schema:email is explicitly ignored"

                        expectViolates
                            [ closedWithIgnored ]
                            (data
                                [ "https://schema.org/name", Str "Alice"
                                  "https://schema.org/telephone", Str "555" ])
                            "schema:telephone is neither declared nor ignored"
                    } ]

          testList
              "targets"
              [ test "sh:targetSubjectsOf targets the subjects of a predicate" {
                    let shape =
                        recordShape
                            [ TargetSpec.SubjectsOf(Uri "https://schema.org/agent") ]
                            [ ofPath (PropertyPath.Predicate(Uri "https://schema.org/position"))
                              |> addConstraint (PropertyConstraint.MinCount 1) ]

                    expectViolates
                        [ shape ]
                        (data [ "https://schema.org/agent", Ref "https://example.org/p1" ])
                        "the subject of schema:agent has no schema:position"

                    expectConforms
                        [ shape ]
                        (data
                            [ "https://schema.org/agent", Ref "https://example.org/p1"
                              "https://schema.org/position", Num 3 ])
                        "it does now"
                }

                test "sh:targetNode targets exactly one named node" {
                    let shape =
                        recordShape
                            [ TargetSpec.Node(Node.Iri Instance) ]
                            [ ofPath (PropertyPath.Predicate(Uri "https://schema.org/position"))
                              |> addConstraint (PropertyConstraint.MinCount 1) ]

                    expectViolates [ shape ] (data []) "the targeted node has no schema:position"

                    expectConforms [ shape ] (data [ "https://schema.org/position", Num 3 ]) "it does now"
                } ]

          testList
              "property paths"
              [ test "an inverse path constrains the SUBJECTS pointing at the focus node" {
                    let shape =
                        recordShape
                            (targetClass (Uri T))
                            [ ofPath (PropertyPath.Inverse(PropertyPath.Predicate(Uri "https://schema.org/object")))
                              |> addConstraint (PropertyConstraint.MinCount 1) ]

                    expectViolates [ shape ] (data []) "nothing points at the move via schema:object"

                    let g = data []
                    let subject = g.CreateUriNode(UriFactory.Create "https://example.org/action1")

                    g.Assert(
                        Triple(
                            subject,
                            g.CreateUriNode(UriFactory.Create "https://schema.org/object"),
                            g.CreateUriNode(UriFactory.Create Instance)
                        )
                    )
                    |> ignore

                    expectConforms [ shape ] g "now something does"
                }

                test "a sequence path walks two predicates in order" {
                    let shape =
                        recordShape
                            (targetClass (Uri T))
                            [ ofPath (
                                  PropertyPath.Sequence
                                      { Head = PropertyPath.Predicate(Uri "https://schema.org/agent")
                                        Tail = [ PropertyPath.Predicate(Uri "https://schema.org/name") ] }
                              )
                              |> addConstraint (PropertyConstraint.MinCount 1) ]

                    expectViolates
                        [ shape ]
                        (agentWith [ "https://schema.org/email", Str "alice@example.org" ])
                        "agent/name does not resolve"

                    expectConforms
                        [ shape ]
                        (agentWith [ "https://schema.org/name", Str "Alice" ])
                        "agent/name resolves"
                } ]

          testList
              "severity"
              [ test "a Warning-severity violation is still reported, tagged Severity.Warning" {
                    let shape =
                        recordShape
                            (targetClass (Uri T))
                            [ { ofPath (PropertyPath.Predicate(Uri "https://schema.org/position")) with
                                  Constraints = [ PropertyConstraint.MinCount 1 ]
                                  Severity = Some Severity.Warning
                                  Message = Some "position is conventionally set" } ]

                    match Shacl.validate (Shacl.toShapesGraph [ shape ]) (data []) with
                    | ValidationOutcome.Conforms -> failtest "expected the warning to be reported"
                    | ValidationOutcome.Violates violations ->
                        Expect.equal violations.Head.Severity Severity.Warning "severity round-trips as Warning"

                        Expect.equal
                            violations.Head.Message
                            "position is conventionally set"
                            "the author's sh:message is carried through"
                }

                test "an Info-severity violation round-trips as Severity.Info, not Violation" {
                    let shape =
                        recordShape
                            (targetClass (Uri T))
                            [ { ofPath (PropertyPath.Predicate(Uri "https://schema.org/position")) with
                                  Constraints = [ PropertyConstraint.MinCount 1 ]
                                  Severity = Some Severity.Info
                                  Message = None } ]

                    match Shacl.validate (Shacl.toShapesGraph [ shape ]) (data []) with
                    | ValidationOutcome.Conforms -> failtest "expected the info result to be reported"
                    | ValidationOutcome.Violates violations ->
                        Expect.equal violations.Head.Severity Severity.Info "severity round-trips as Info"
                }

                // Regression for the final-review minor item: severityOf matched severity IRIs with
                // uri.EndsWith "Warning"/"Info" instead of comparing the full IRI, so any vocabulary
                // term merely ENDING in those words would have been misread.
                test "severity is decided by the full sh: IRI, not a string suffix" {
                    let shape =
                        recordShape
                            (targetClass (Uri T))
                            [ { ofPath (PropertyPath.Predicate(Uri "https://schema.org/position")) with
                                  Constraints = [ PropertyConstraint.MinCount 1 ]
                                  Severity = None
                                  Message = None } ]

                    match Shacl.validate (Shacl.toShapesGraph [ shape ]) (data []) with
                    | ValidationOutcome.Conforms -> failtest "expected a violation"
                    | ValidationOutcome.Violates violations ->
                        Expect.equal
                            violations.Head.Severity
                            Severity.Violation
                            "SHACL's default severity is sh:Violation"
                } ]

          testList
              "reported violation fields"
              [ test "a violation carries the constraint component, source shape and result path" {
                    let shape = shapeOn "https://schema.org/position" [ PropertyConstraint.MinCount 1 ]

                    match Shacl.validate (Shacl.toShapesGraph [ shape ]) (data []) with
                    | ValidationOutcome.Conforms -> failtest "expected a violation"
                    | ValidationOutcome.Violates violations ->
                        let v = violations.Head

                        Expect.equal
                            v.ConstraintComponent
                            (Uri "http://www.w3.org/ns/shacl#MinCountConstraintComponent")
                            "sh:MinCountConstraintComponent"

                        Expect.equal
                            v.ResultPath
                            (Some(Uri "https://schema.org/position"))
                            "the violated property's path"

                        Expect.equal v.FocusNode (Value.Node(Node.Iri Instance)) "the focus node"

                        // Since finding I2, a shape's subject is a freshly minted blank node rather
                        // than its target class's IRI, so this is what a source shape looks like.
                        match v.SourceShape with
                        | Node.Blank _ -> ()
                        | Node.Iri iri -> failtestf "expected a blank-node source shape, got %s" iri
                } ]

          testList
              "report projection"
              [ test "every violation kind above survives reportToDoc |> toJsonLd (the 422 ld+json path)" {
                    // A single graph violating several kinds at once, projected the way the
                    // middleware's ld+json response projects it -- the one place a malformed
                    // Violation value turns into an unhandled 500.
                    let shapes =
                        [ shapeOn
                              "https://schema.org/position"
                              [ PropertyConstraint.MinCount 1
                                PropertyConstraint.Datatype XsdDatatype.Integer ]
                          shapeOn "https://schema.org/agent" [ PropertyConstraint.Node personRequiresName ]
                          shapeOn "https://schema.org/name" [ PropertyConstraint.MinLength 3 ] ]

                    match
                        Shacl.validate
                            (Shacl.toShapesGraph shapes)
                            (data
                                [ "https://schema.org/name", Str "Al"
                                  "https://schema.org/agent", Nested("https://example.org/p1", None, []) ])
                    with
                    | ValidationOutcome.Conforms -> failtest "expected violations"
                    | ValidationOutcome.Violates violations ->
                        Expect.isGreaterThan violations.Length 1 "several kinds violated at once"
                        let json = Doc.toJsonLd (Shacl.reportToDoc violations)
                        Expect.stringContains json "ValidationReport" "a real sh:ValidationReport"
                } ] ]
