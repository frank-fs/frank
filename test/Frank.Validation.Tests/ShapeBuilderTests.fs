module Frank.Validation.Tests.ShapeBuilderTests

open System
open Expecto
open Frank.Rdf
open Frank.Validation
open Frank.Validation.ShapeSpecFunctions

[<Tests>]
let tests =
    testList
        "ShapeBuilder"
        [ testList
              "property { }"
              [ test "an empty block equals ofPath directly (Yield/Zero return initial unchanged)" {
                    let path = PropertyPath.Predicate(Uri "https://schema.org/x")
                    let viaCe = property path { () }
                    Expect.equal viaCe (ofPath path) "empty CE block == ofPath"
                }

                test "datatype/minCount/maxCount produce the same PropertyShapeSpec as addConstraint chains" {
                    let path = PropertyPath.Predicate(Uri "https://schema.org/position")

                    let viaCe =
                        property path {
                            datatype XsdDatatype.Integer
                            minCount 1
                            maxCount 1
                        }

                    let viaFunctions =
                        ofPath path
                        |> addConstraint (PropertyConstraint.Datatype XsdDatatype.Integer)
                        |> addConstraint (PropertyConstraint.MinCount 1)
                        |> addConstraint (PropertyConstraint.MaxCount 1)

                    Expect.equal viaCe viaFunctions "CE sugar == plain functions, same result"
                }

                test "every constraint operation is reachable and produces the matching PropertyConstraint case" {
                    let path = PropertyPath.Predicate(Uri "https://schema.org/x")
                    let inner = recordShape [] []

                    let viaCe =
                        property path {
                            ofClass (Uri "https://schema.org/Person")
                            nodeKind NodeKind.Iri
                            minLength 1
                            maxLength 10
                            minExclusive (Literal.Int 0)
                            minInclusive (Literal.Int 0)
                            maxExclusive (Literal.Int 100)
                            maxInclusive (Literal.Int 100)
                            pattern @"^\d+$"
                            uniqueLang true
                            equalsPath (Uri "https://schema.org/a")
                            disjoint (Uri "https://schema.org/b")
                            lessThan (Uri "https://schema.org/c")
                            lessThanOrEquals (Uri "https://schema.org/d")
                            node inner
                            hasValue (Value.Node(Node.Iri "https://schema.org/v"))
                            severity Severity.Warning
                            message "careful"
                        }

                    Expect.hasLength
                        viaCe.Constraints
                        16
                        "sixteen constraint operations above (severity/message aren't constraints)"

                    Expect.equal viaCe.Severity (Some Severity.Warning) "severity set"
                    Expect.equal viaCe.Message (Some "careful") "message set"
                }

                test "patternWithFlags sets both sh:pattern and sh:flags via one Pattern(pattern, Some flags) case" {
                    let viaCe =
                        property (PropertyPath.Predicate(Uri "https://schema.org/x")) { patternWithFlags @"^\d+$" "i" }

                    Expect.equal
                        viaCe.Constraints
                        [ PropertyConstraint.Pattern(@"^\d+$", Some "i") ]
                        "pattern with flags"
                }

                test "languageIn and allowedValues take a NonEmptyList directly" {
                    let tags = NonEmptyList.ofList [ "en"; "fr" ] |> Option.get
                    let values = NonEmptyList.ofList [ Value.Literal(Literal.String "a") ] |> Option.get

                    let viaCe =
                        property (PropertyPath.Predicate(Uri "https://schema.org/x")) {
                            languageIn tags
                            allowedValues values
                        }

                    Expect.equal
                        viaCe.Constraints
                        [ PropertyConstraint.LanguageIn tags; PropertyConstraint.AllowedValues values ]
                        "both present, in order"
                }

                test "qualifiedValueShape and sparqlConstraint reach their PropertyConstraint cases" {
                    let inner = recordShape [] []

                    let sc: SparqlConstraint =
                        { Query = "ASK { }"
                          Message = None
                          Prefixes = [] }

                    let viaCe =
                        property (PropertyPath.Predicate(Uri "https://schema.org/x")) {
                            qualifiedValueShape inner (Some 1) (Some 2) true
                            sparqlConstraint sc
                        }

                    Expect.equal
                        viaCe.Constraints
                        [ PropertyConstraint.QualifiedValueShape(inner, Some 1, Some 2, true)
                          PropertyConstraint.Sparql sc ]
                        "both present, in order"
                } ]

          testList
              "shape { }"
              [ test "an empty block equals recordShape targets [] directly" {
                    let targets = targetClass (Uri "https://schema.org/T")

                    Expect.equal
                        (shape targets { () })
                        (recordShape targets [])
                        "empty CE block == recordShape targets []"
                }

                test "properties [ ... ] appends to the shape's property list" {
                    let p1 =
                        ofPath (PropertyPath.Predicate(Uri "https://schema.org/a"))
                        |> addConstraint (PropertyConstraint.MinCount 1)

                    let p2 =
                        ofPath (PropertyPath.Predicate(Uri "https://schema.org/b"))
                        |> addConstraint (PropertyConstraint.MinCount 1)

                    let viaCe =
                        shape (targetClass (Uri "https://schema.org/T")) { properties [ p1; p2 ] }

                    match viaCe with
                    | ShapeDecl.RecordShape n ->
                        Expect.equal n.Properties [ p1; p2 ] "both properties present, in order"
                    | other -> failtestf "expected RecordShape, got %A" other
                }

                test "closed sets Closed=true and the given IgnoredProperties" {
                    let viaCe =
                        shape (targetClass (Uri "https://schema.org/T")) { closed [ Uri "https://schema.org/extra" ] }

                    match viaCe with
                    | ShapeDecl.RecordShape n ->
                        Expect.isTrue n.Closed "closed"
                        Expect.equal n.IgnoredProperties [ Uri "https://schema.org/extra" ] "ignored properties"
                    | other -> failtestf "expected RecordShape, got %A" other
                }

                test "severity/message set NodeShapeSpec.Severity/Message" {
                    let viaCe =
                        shape (targetClass (Uri "https://schema.org/T")) {
                            severity Severity.Warning
                            message "heads up"
                        }

                    match viaCe with
                    | ShapeDecl.RecordShape n ->
                        Expect.equal n.Severity (Some Severity.Warning) "severity"
                        Expect.equal n.Message (Some "heads up") "message"
                    | other -> failtestf "expected RecordShape, got %A" other
                }

                test
                    "properties/closed/severity/message compose in one block, matching the design doc's personShape example" {
                    let personShape =
                        shape (targetClass (Uri "https://schema.org/Person")) {
                            properties
                                [ property (PropertyPath.Predicate(Uri "https://schema.org/email")) {
                                      datatype XsdDatatype.String
                                      pattern @"^\S+@\S+\.\S+$"
                                      minCount 1
                                  }
                                  property (PropertyPath.Predicate(Uri "https://schema.org/birthDate")) {
                                      datatype XsdDatatype.DateTime
                                      maxCount 1
                                  } ]

                            closed []
                        }

                    match personShape with
                    | ShapeDecl.RecordShape n ->
                        Expect.hasLength n.Properties 2 "two property shapes"
                        Expect.isTrue n.Closed "closed"
                    | other -> failtestf "expected RecordShape, got %A" other
                }

                test "shape{ } composes with property{ }'s recursive `node` operation" {
                    let personShape =
                        shape (targetClass (Uri "https://schema.org/Person")) { properties [] }

                    let moveShape =
                        shape (targetClass (Uri "https://schema.org/MoveAction")) {
                            properties
                                [ property (PropertyPath.Predicate(Uri "https://schema.org/agent")) {
                                      node personShape
                                      minCount 1
                                  } ]
                        }

                    match moveShape with
                    | ShapeDecl.RecordShape n ->
                        match n.Properties.Head.Constraints with
                        | [ PropertyConstraint.Node inner; PropertyConstraint.MinCount 1 ] ->
                            Expect.equal inner personShape "the nested shape is exactly personShape"
                        | other -> failtestf "unexpected constraints: %A" other
                    | other -> failtestf "expected RecordShape, got %A" other
                } ] ]
