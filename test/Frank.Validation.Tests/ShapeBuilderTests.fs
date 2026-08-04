module Frank.Validation.Tests.ShapeBuilderTests

open System
open Expecto
open Frank.Rdf
open Frank.Validation
open Frank.Validation.ShapeSpecFunctions

[<Tests>]
let tests =
    testList
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

              Expect.equal viaCe.Constraints [ PropertyConstraint.Pattern(@"^\d+$", Some "i") ] "pattern with flags"
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
