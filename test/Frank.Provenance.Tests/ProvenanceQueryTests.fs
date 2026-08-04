module Frank.Provenance.Tests.ProvenanceQueryTests

open Expecto
open Frank.Provenance

[<Tests>]
let tests =
    testList
        "ProvenanceQuery -> SparqlQuery"
        [ test "ByResource produces a query naming the resource IRI, resolvable by a real SPARQL parser" {
              let query = toSparqlQuery (ProvenanceQuery.ByResource "https://example.org/games/1")
              Expect.stringContains (query.ToString()) "https://example.org/games/1" ""
          }

          test "ByAgent produces a query naming the agent IRI" {
              let query = toSparqlQuery (ProvenanceQuery.ByAgent "https://example.org/users/42")
              Expect.stringContains (query.ToString()) "https://example.org/users/42" ""
          }

          test "ByActivityId produces a query naming the activity IRI" {
              let query = toSparqlQuery (ProvenanceQuery.ByActivityId "https://example.org/activities/1")
              Expect.stringContains (query.ToString()) "https://example.org/activities/1" ""
          }

          test "Latest produces a query naming the resource IRI" {
              let query = toSparqlQuery (ProvenanceQuery.Latest "https://example.org/games/1")
              Expect.stringContains (query.ToString()) "https://example.org/games/1" ""
          }

          test "ProvenanceStoreConfig.defaults has a positive MaxRecords and EvictionBatchSize" {
              Expect.isGreaterThan ProvenanceStoreConfig.defaults.MaxRecords 0 ""
              Expect.isGreaterThan ProvenanceStoreConfig.defaults.EvictionBatchSize 0 ""
          } ]
