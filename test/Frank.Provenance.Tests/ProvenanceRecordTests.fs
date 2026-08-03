module Frank.Provenance.Tests.ProvenanceRecordTests

open System
open System.IO
open Expecto
open VDS.RDF
open VDS.RDF.Parsing
open Frank.Rdf
open Frank.Provenance

let private sampleRecord () : ProvenanceRecord =
    { Activity = Node.Iri "https://example.org/activities/1"
      Resource = Node.Iri "https://example.org/games/1"
      Agent = Node.Iri "https://example.org/users/42"
      StartedAt = DateTimeOffset(2026, 8, 2, 12, 0, 0, TimeSpan.Zero)
      EndedAt = DateTimeOffset(2026, 8, 2, 12, 0, 1, TimeSpan.Zero)
      ActivityType = None
      Properties = [] }

[<Tests>]
let tests =
    testList
        "ProvenanceRecord"
        [ test "toDoc types the Activity node as prov:Activity" {
              let graph = sampleRecord () |> ProvenanceRecord.toDoc |> Doc.toGraph
              let activityNode = graph.CreateUriNode(Uri "https://example.org/activities/1")
              let typeNode = graph.CreateUriNode(Uri RdfTypeIri)
              let activityClassNode = graph.CreateUriNode(Uri(ProvClass.toIri ProvClass.Activity))

              Expect.isGreaterThan
                  (graph.GetTriplesWithSubjectPredicate(activityNode, typeNode)
                   |> Seq.filter (fun t -> t.Object = activityClassNode)
                   |> Seq.length)
                  0
                  "Activity node is typed prov:Activity"
          }

          test "toDoc types the Resource node as prov:Entity and connects it via wasGeneratedBy" {
              let graph = sampleRecord () |> ProvenanceRecord.toDoc |> Doc.toGraph
              let resourceNode = graph.CreateUriNode(Uri "https://example.org/games/1")
              let activityNode = graph.CreateUriNode(Uri "https://example.org/activities/1")
              let typeNode = graph.CreateUriNode(Uri RdfTypeIri)
              let entityClassNode = graph.CreateUriNode(Uri(ProvClass.toIri ProvClass.Entity))
              let wasGeneratedByNode = graph.CreateUriNode(Uri(ProvRelation.toIri ProvRelation.WasGeneratedBy))

              Expect.isGreaterThan
                  (graph.GetTriplesWithSubjectPredicate(resourceNode, typeNode)
                   |> Seq.filter (fun t -> t.Object = entityClassNode)
                   |> Seq.length)
                  0
                  "Resource node is typed prov:Entity"

              Expect.isGreaterThan
                  (graph.GetTriplesWithSubjectPredicate(resourceNode, wasGeneratedByNode)
                   |> Seq.filter (fun t -> t.Object = activityNode)
                   |> Seq.length)
                  0
                  "Resource prov:wasGeneratedBy Activity"
          }

          test "toDoc types the Agent node as prov:Agent and connects it via wasAssociatedWith" {
              let graph = sampleRecord () |> ProvenanceRecord.toDoc |> Doc.toGraph
              let agentNode = graph.CreateUriNode(Uri "https://example.org/users/42")
              let activityNode = graph.CreateUriNode(Uri "https://example.org/activities/1")
              let typeNode = graph.CreateUriNode(Uri RdfTypeIri)
              let agentClassNode = graph.CreateUriNode(Uri(ProvClass.toIri ProvClass.Agent))
              let wasAssociatedWithNode = graph.CreateUriNode(Uri(ProvRelation.toIri ProvRelation.WasAssociatedWith))

              Expect.isGreaterThan
                  (graph.GetTriplesWithSubjectPredicate(agentNode, typeNode)
                   |> Seq.filter (fun t -> t.Object = agentClassNode)
                   |> Seq.length)
                  0
                  "Agent node is typed prov:Agent"

              Expect.isGreaterThan
                  (graph.GetTriplesWithSubjectPredicate(activityNode, wasAssociatedWithNode)
                   |> Seq.filter (fun t -> t.Object = agentNode)
                   |> Seq.length)
                  0
                  "Activity prov:wasAssociatedWith Agent"
          }

          test "toDoc asserts startedAtTime and endedAtTime on the Activity" {
              let graph = sampleRecord () |> ProvenanceRecord.toDoc |> Doc.toGraph
              let activityNode = graph.CreateUriNode(Uri "https://example.org/activities/1")
              let startedNode = graph.CreateUriNode(Uri(ProvRelation.toIri ProvRelation.StartedAtTime))
              let endedNode = graph.CreateUriNode(Uri(ProvRelation.toIri ProvRelation.EndedAtTime))

              Expect.equal (graph.GetTriplesWithSubjectPredicate(activityNode, startedNode) |> Seq.length) 1 ""
              Expect.equal (graph.GetTriplesWithSubjectPredicate(activityNode, endedNode) |> Seq.length) 1 ""
          }

          test "toDoc adds an extra rdf:type for ActivityType, alongside prov:Activity, when Some" {
              let record =
                  { sampleRecord () with
                      ActivityType = Some(Uri "https://schema.org/OrderAction") }

              let graph = record |> ProvenanceRecord.toDoc |> Doc.toGraph
              let activityNode = graph.CreateUriNode(Uri "https://example.org/activities/1")
              let typeNode = graph.CreateUriNode(Uri RdfTypeIri)
              let domainTypeNode = graph.CreateUriNode(Uri "https://schema.org/OrderAction")
              let provActivityNode = graph.CreateUriNode(Uri(ProvClass.toIri ProvClass.Activity))

              Expect.isGreaterThan
                  (graph.GetTriplesWithSubjectPredicate(activityNode, typeNode)
                   |> Seq.filter (fun t -> t.Object = domainTypeNode)
                   |> Seq.length)
                  0
                  "Domain type asserted"

              Expect.isGreaterThan
                  (graph.GetTriplesWithSubjectPredicate(activityNode, typeNode)
                   |> Seq.filter (fun t -> t.Object = provActivityNode)
                   |> Seq.length)
                  0
                  "prov:Activity still asserted alongside the domain type"
          }

          test "toDoc omits any extra rdf:type when ActivityType is None" {
              let graph = sampleRecord () |> ProvenanceRecord.toDoc |> Doc.toGraph
              let activityNode = graph.CreateUriNode(Uri "https://example.org/activities/1")
              let typeNode = graph.CreateUriNode(Uri RdfTypeIri)

              Expect.equal (graph.GetTriplesWithSubjectPredicate(activityNode, typeNode) |> Seq.length) 1 "Only prov:Activity"
          }

          test "toDoc attaches Properties to the Activity node as-is" {
              let record =
                  { sampleRecord () with
                      Properties = [ "https://schema.org/cellIndex", Value.Literal(Literal.Int 4) ] }

              let graph = record |> ProvenanceRecord.toDoc |> Doc.toGraph
              let activityNode = graph.CreateUriNode(Uri "https://example.org/activities/1")
              let cellIndexNode = graph.CreateUriNode(Uri "https://schema.org/cellIndex")

              Expect.equal (graph.GetTriplesWithSubjectPredicate(activityNode, cellIndexNode) |> Seq.length) 1 ""
          }

          test "toDoc round-trips through JSON-LD to an isomorphic graph" {
              // Same pattern as Frank.Rdf's own RoundTripTests.fs: serialize, parse the JSON-LD back
              // into a graph with dotNetRDF's own reader, assert isomorphism. Stronger than asserting
              // against a hand-written expected string.
              let record =
                  { sampleRecord () with
                      ActivityType = Some(Uri "https://schema.org/OrderAction")
                      Properties = [ "https://schema.org/cellIndex", Value.Literal(Literal.Int 4) ] }

              let doc = ProvenanceRecord.toDoc record
              let originalGraph = Doc.toGraph doc :> IGraph

              let store = TripleStore()
              use reader = new StringReader(Doc.toJsonLd doc)
              JsonLdParser().Load(store, reader)
              let parsedGraph = store.Graphs |> Seq.exactlyOne

              Expect.isTrue (originalGraph.Equals(parsedGraph)) "Isomorphic after round-trip"
          } ]
