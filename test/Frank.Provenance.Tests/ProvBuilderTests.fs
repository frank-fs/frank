module Frank.Provenance.Tests.ProvBuilderTests

open System
open Expecto
open Frank.Rdf
open Frank.Provenance

[<Tests>]
let tests =
    testList
        "ProvBuilder"
        [ test "activity seeds a Description typed prov:Activity" {
              let d = activity (Node.Iri "https://example.org/a1") { () }

              Expect.equal
                  d.Statements
                  [ RdfTypeIri, Value.Node(Node.Iri "http://www.w3.org/ns/prov#Activity") ]
                  ""
          }

          test "entity seeds a Description typed prov:Entity" {
              let d = entity (Node.Iri "https://example.org/e1") { () }

              Expect.equal
                  d.Statements
                  [ RdfTypeIri, Value.Node(Node.Iri "http://www.w3.org/ns/prov#Entity") ]
                  ""
          }

          test "agent seeds a Description typed prov:Agent" {
              let d = agent (Node.Iri "https://example.org/ag1") { () }

              Expect.equal
                  d.Statements
                  [ RdfTypeIri, Value.Node(Node.Iri "http://www.w3.org/ns/prov#Agent") ]
                  ""
          }

          test "wasGeneratedBy adds a prov:wasGeneratedBy statement pointing at the given activity" {
              let d =
                  entity (Node.Iri "https://example.org/e1") { wasGeneratedBy (Node.Iri "https://example.org/a1") }

              Expect.equal
                  d.Statements.[1]
                  ("http://www.w3.org/ns/prov#wasGeneratedBy", Value.Node(Node.Iri "https://example.org/a1"))
                  "Second statement, after the rdf:type from entity"
          }

          test "wasAssociatedWith adds a prov:wasAssociatedWith statement pointing at the given agent" {
              let d =
                  activity (Node.Iri "https://example.org/a1") {
                      wasAssociatedWith (Node.Iri "https://example.org/ag1")
                  }

              Expect.equal
                  d.Statements.[1]
                  ("http://www.w3.org/ns/prov#wasAssociatedWith", Value.Node(Node.Iri "https://example.org/ag1"))
                  ""
          }

          test "used adds a prov:used statement pointing at the given entity" {
              let d =
                  activity (Node.Iri "https://example.org/a1") { used (Node.Iri "https://example.org/e1") }

              Expect.equal
                  d.Statements.[1]
                  ("http://www.w3.org/ns/prov#used", Value.Node(Node.Iri "https://example.org/e1"))
                  ""
          }

          test "startedAtTime and endedAtTime add DateTimeOffset-literal statements" {
              let t0 = DateTimeOffset(2026, 8, 3, 12, 0, 0, TimeSpan.Zero)
              let t1 = DateTimeOffset(2026, 8, 3, 12, 0, 1, TimeSpan.Zero)

              let d =
                  activity (Node.Iri "https://example.org/a1") {
                      startedAtTime t0
                      endedAtTime t1
                  }

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
                  entity (Node.Iri "https://example.org/e2") {
                      wasDerivedFrom (Node.Iri "https://example.org/e1")
                      specializationOf (Node.Iri "https://example.org/e1")
                  }

              Expect.equal
                  d.Statements.[1]
                  ("http://www.w3.org/ns/prov#wasDerivedFrom", Value.Node(Node.Iri "https://example.org/e1"))
                  ""

              Expect.equal
                  d.Statements.[2]
                  ("http://www.w3.org/ns/prov#specializationOf", Value.Node(Node.Iri "https://example.org/e1"))
                  ""
          }

          test "CE and |> combinators produce identical Descriptions" {
              let t0 = DateTimeOffset(2026, 8, 3, 12, 0, 0, TimeSpan.Zero)
              let t1 = DateTimeOffset(2026, 8, 3, 12, 0, 1, TimeSpan.Zero)
              let a = Node.Iri "https://example.org/a1"
              let ag = Node.Iri "https://example.org/ag1"
              let e = Node.Iri "https://example.org/e1"

              let viaCe =
                  activity a {
                      wasAssociatedWith ag
                      used e
                      startedAtTime t0
                      endedAtTime t1
                  }

              let viaPipe =
                  Prov.activity a
                  |> Prov.wasAssociatedWith ag
                  |> Prov.used e
                  |> Prov.startedAtTime t0
                  |> Prov.endedAtTime t1

              Expect.equal viaCe viaPipe "CE block produces the same Description as the equivalent |> chain"
          } ]
