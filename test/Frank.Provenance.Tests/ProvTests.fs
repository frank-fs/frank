module Frank.Provenance.Tests.ProvTests

open System
open Expecto
open Frank.Rdf
open Frank.Provenance

[<Tests>]
let tests =
    testList
        "Prov"
        [ test "activity types the subject as prov:Activity" {
              let d = Prov.activity (Node.Iri "https://example.org/a1")

              Expect.equal
                  d.Statements
                  [ RdfTypeIri, Value.Node(Node.Iri "http://www.w3.org/ns/prov#Activity") ]
                  ""
          }

          test "entity types the subject as prov:Entity" {
              let d = Prov.entity (Node.Iri "https://example.org/e1")

              Expect.equal
                  d.Statements
                  [ RdfTypeIri, Value.Node(Node.Iri "http://www.w3.org/ns/prov#Entity") ]
                  ""
          }

          test "agent types the subject as prov:Agent" {
              let d = Prov.agent (Node.Iri "https://example.org/ag1")

              Expect.equal
                  d.Statements
                  [ RdfTypeIri, Value.Node(Node.Iri "http://www.w3.org/ns/prov#Agent") ]
                  ""
          }

          test "wasGeneratedBy adds a prov:wasGeneratedBy statement pointing at the given activity" {
              let d =
                  Prov.entity (Node.Iri "https://example.org/e1")
                  |> Prov.wasGeneratedBy (Node.Iri "https://example.org/a1")

              Expect.equal
                  d.Statements.[1]
                  ("http://www.w3.org/ns/prov#wasGeneratedBy", Value.Node(Node.Iri "https://example.org/a1"))
                  "Second statement, after the rdf:type from entity"
          }

          test "wasAssociatedWith adds a prov:wasAssociatedWith statement pointing at the given agent" {
              let d =
                  Prov.activity (Node.Iri "https://example.org/a1")
                  |> Prov.wasAssociatedWith (Node.Iri "https://example.org/ag1")

              Expect.equal
                  d.Statements.[1]
                  ("http://www.w3.org/ns/prov#wasAssociatedWith", Value.Node(Node.Iri "https://example.org/ag1"))
                  ""
          }

          test "used adds a prov:used statement pointing at the given entity" {
              let d =
                  Prov.activity (Node.Iri "https://example.org/a1") |> Prov.used (Node.Iri "https://example.org/e1")

              Expect.equal
                  d.Statements.[1]
                  ("http://www.w3.org/ns/prov#used", Value.Node(Node.Iri "https://example.org/e1"))
                  ""
          }

          test "startedAtTime and endedAtTime add DateTimeOffset-literal statements" {
              let t0 = DateTimeOffset(2026, 8, 2, 12, 0, 0, TimeSpan.Zero)
              let t1 = DateTimeOffset(2026, 8, 2, 12, 0, 1, TimeSpan.Zero)

              let d =
                  Prov.activity (Node.Iri "https://example.org/a1")
                  |> Prov.startedAtTime t0
                  |> Prov.endedAtTime t1

              Expect.equal
                  d.Statements.[1]
                  ("http://www.w3.org/ns/prov#startedAtTime", Value.Literal(Literal.DateTime t0))
                  ""

              Expect.equal
                  d.Statements.[2]
                  ("http://www.w3.org/ns/prov#endedAtTime", Value.Literal(Literal.DateTime t1))
                  ""
          }

          test "wasDerivedFrom and specializationOf add statements pointing at the given entity" {
              let d =
                  Prov.entity (Node.Iri "https://example.org/e2")
                  |> Prov.wasDerivedFrom (Node.Iri "https://example.org/e1")
                  |> Prov.specializationOf (Node.Iri "https://example.org/e1")

              Expect.equal
                  d.Statements.[1]
                  ("http://www.w3.org/ns/prov#wasDerivedFrom", Value.Node(Node.Iri "https://example.org/e1"))
                  ""

              Expect.equal
                  d.Statements.[2]
                  ("http://www.w3.org/ns/prov#specializationOf", Value.Node(Node.Iri "https://example.org/e1"))
                  ""
          }

          test "combinators compose freely, in order, onto one Description" {
              let t0 = DateTimeOffset(2026, 8, 2, 12, 0, 0, TimeSpan.Zero)
              let t1 = DateTimeOffset(2026, 8, 2, 12, 0, 1, TimeSpan.Zero)

              let d =
                  Prov.activity (Node.Iri "https://example.org/a1")
                  |> Prov.wasAssociatedWith (Node.Iri "https://example.org/ag1")
                  |> Prov.used (Node.Iri "https://example.org/e1")
                  |> Prov.startedAtTime t0
                  |> Prov.endedAtTime t1

              Expect.equal d.Statements.Length 5 "type + wasAssociatedWith + used + startedAtTime + endedAtTime"
              Expect.equal d.Subject (Node.Iri "https://example.org/a1") "Subject unchanged by combinators"
          } ]
