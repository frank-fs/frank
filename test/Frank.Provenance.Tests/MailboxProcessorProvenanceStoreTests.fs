module Frank.Provenance.Tests.MailboxProcessorProvenanceStoreTests

open System
open Expecto
open Microsoft.Extensions.Logging.Abstractions
open Frank.Rdf
open Frank.Provenance

let private record (activityIri: string) (resourceIri: string) (agentIri: string) : ProvenanceRecord =
    let now = DateTimeOffset.UtcNow

    { Activity = Node.Iri activityIri
      Resource = Node.Iri resourceIri
      Agent = Node.Iri agentIri
      StartedAt = now
      EndedAt = now.AddSeconds(1.0)
      ActivityType = None
      Properties = [] }

let private newStore (config: ProvenanceStoreConfig) : IProvenanceStore =
    new MailboxProcessorProvenanceStore(config, NullLogger.Instance) :> IProvenanceStore

[<Tests>]
let tests =
    testList
        "MailboxProcessorProvenanceStore"
        [ test "ByResource finds an activity generated-by the given resource" {
              let store = newStore ProvenanceStoreConfig.defaults

              store.Append(
                  record "https://example.org/activities/1" "https://example.org/games/1" "https://example.org/users/42"
              )

              match store.Query(ProvenanceQuery.ByResource "https://example.org/games/1") with
              | SparqlQueryResult.Graph g -> Expect.isGreaterThan g.Triples.Count 0 "Some triples came back"
              | SparqlQueryResult.Bindings _ -> failwith "Expected a graph, ByResource is a CONSTRUCT query"
          }

          test "ByResource for an unknown resource returns an empty graph, not an error" {
              let store = newStore ProvenanceStoreConfig.defaults

              match store.Query(ProvenanceQuery.ByResource "https://example.org/games/does-not-exist") with
              | SparqlQueryResult.Graph g -> Expect.equal g.Triples.Count 0 "Nothing recorded for this resource"
              | SparqlQueryResult.Bindings _ -> failwith "Expected a graph"
          }

          test "ByResource for one resource never returns another resource's activity data" {
              let store = newStore ProvenanceStoreConfig.defaults

              store.Append(
                  record
                      "https://example.org/activities/x1"
                      "https://example.org/games/x"
                      "https://example.org/users/x"
              )

              store.Append(
                  record
                      "https://example.org/activities/y1"
                      "https://example.org/games/y"
                      "https://example.org/users/y"
              )

              match store.Query(ProvenanceQuery.ByResource "https://example.org/games/x") with
              | SparqlQueryResult.Graph g ->
                  let activityYNode = g.CreateUriNode(Uri "https://example.org/activities/y1")
                  Expect.equal (g.GetTriplesWithSubject(activityYNode) |> Seq.length) 0 "No cross-contamination from games/y"
              | SparqlQueryResult.Bindings _ -> failwith "Expected a graph"
          }

          test "ByAgent for one agent never returns another agent's activity data" {
              let store = newStore ProvenanceStoreConfig.defaults

              store.Append(
                  record
                      "https://example.org/activities/x2"
                      "https://example.org/games/x2"
                      "https://example.org/users/x2"
              )

              store.Append(
                  record
                      "https://example.org/activities/y2"
                      "https://example.org/games/y2"
                      "https://example.org/users/y2"
              )

              match store.Query(ProvenanceQuery.ByAgent "https://example.org/users/x2") with
              | SparqlQueryResult.Graph g ->
                  let activityYNode = g.CreateUriNode(Uri "https://example.org/activities/y2")
                  Expect.equal (g.GetTriplesWithSubject(activityYNode) |> Seq.length) 0 "No cross-contamination from users/y2"
              | SparqlQueryResult.Bindings _ -> failwith "Expected a graph"
          }

          test "ByAgent finds an activity associated with the given agent" {
              let store = newStore ProvenanceStoreConfig.defaults

              store.Append(
                  record "https://example.org/activities/2" "https://example.org/games/2" "https://example.org/users/7"
              )

              match store.Query(ProvenanceQuery.ByAgent "https://example.org/users/7") with
              | SparqlQueryResult.Graph g -> Expect.isGreaterThan g.Triples.Count 0 ""
              | SparqlQueryResult.Bindings _ -> failwith "Expected a graph"
          }

          test "ByActivityId finds the named activity by its own id" {
              let store = newStore ProvenanceStoreConfig.defaults

              store.Append(
                  record "https://example.org/activities/3" "https://example.org/games/3" "https://example.org/users/9"
              )

              match store.Query(ProvenanceQuery.ByActivityId "https://example.org/activities/3") with
              | SparqlQueryResult.Graph g -> Expect.isGreaterThan g.Triples.Count 0 ""
              | SparqlQueryResult.Bindings _ -> failwith "Expected a graph"
          }

          test "eviction removes the oldest records once MaxRecords is exceeded" {
              let config = { MaxRecords = 2; EvictionBatchSize = 1 }
              let store = newStore config

              store.Append(
                  record "https://example.org/activities/a" "https://example.org/games/a" "https://example.org/users/a"
              )

              store.Append(
                  record "https://example.org/activities/b" "https://example.org/games/b" "https://example.org/users/b"
              )

              store.Append(
                  record "https://example.org/activities/c" "https://example.org/games/c" "https://example.org/users/c"
              )

              match store.Query(ProvenanceQuery.ByActivityId "https://example.org/activities/a") with
              | SparqlQueryResult.Graph g -> Expect.equal g.Triples.Count 0 "Oldest record evicted"
              | SparqlQueryResult.Bindings _ -> failwith "Expected a graph"

              // ByActivityId compiles to a DESCRIBE query, and dotNetRDF's default Concise Bounded
              // Description only returns the Activity's own outbound triples -- so the assertion above
              // would pass identically even if eviction only removed the Activity's triples and left
              // the same record's Resource/Agent triples orphaned in the store. ByResource additionally
              // pulls in the resource's own outbound triples (including wasGeneratedBy), proving the
              // whole record's named graph -- not just the Activity's outbound edges -- was removed.
              match store.Query(ProvenanceQuery.ByResource "https://example.org/games/a") with
              | SparqlQueryResult.Graph g -> Expect.equal g.Triples.Count 0 "Oldest record's resource data also evicted"
              | SparqlQueryResult.Bindings _ -> failwith "Expected a graph"

              match store.Query(ProvenanceQuery.ByActivityId "https://example.org/activities/c") with
              | SparqlQueryResult.Graph g -> Expect.isGreaterThan g.Triples.Count 0 "Newest record still present"
              | SparqlQueryResult.Bindings _ -> failwith "Expected a graph"
          }

          test "a pathological EvictionBatchSize never evicts the record just appended" {
              // EvictionBatchSize (100) far exceeds MaxRecords (1): without the min-with-(length - 1)
              // clamp in the eviction path, evictCount would equal updated.Length and wipe out every
              // record in the store -- including the one just appended in this very Append call.
              let config = { MaxRecords = 1; EvictionBatchSize = 100 }
              let store = newStore config

              store.Append(
                  record
                      "https://example.org/activities/pathological"
                      "https://example.org/games/pathological"
                      "https://example.org/users/pathological"
              )

              match store.Query(ProvenanceQuery.ByActivityId "https://example.org/activities/pathological") with
              | SparqlQueryResult.Graph g ->
                  Expect.isGreaterThan g.Triples.Count 0 "The just-appended record is never evicted in the same Append call"
              | SparqlQueryResult.Bindings _ -> failwith "Expected a graph"
          }

          test "Append/Query from multiple threads never throws (mailbox serializes access)" {
              let store = newStore ProvenanceStoreConfig.defaults

              let work =
                  [ 0..19 ]
                  |> List.map (fun i ->
                      System.Threading.Tasks.Task.Run(fun () ->
                          store.Append(
                              record
                                  (sprintf "https://example.org/activities/thread-%d" i)
                                  "https://example.org/games/concurrent"
                                  "https://example.org/users/concurrent"
                          )

                          store.Query(ProvenanceQuery.ByResource "https://example.org/games/concurrent") |> ignore))
                  |> Array.ofList

              System.Threading.Tasks.Task.WaitAll(work)
          }

          test "a malformed record does not kill the mailbox -- subsequent Append/Query still succeed" {
              let store = newStore ProvenanceStoreConfig.defaults

              // "not-a-uri" is a relative reference, not an absolute IRI: System.Uri(string) defaults to
              // UriKind.Absolute and throws UriFormatException when graphNameFor tries to turn this
              // Activity into a graph name. Before the mailbox loop caught exceptions, this would kill
              // the loop -- silently dropping every later Append and hanging every later Query forever.
              store.Append(
                  record "not-a-uri" "https://example.org/games/broken" "https://example.org/users/broken"
              )

              // Run the recovery Append/Query on a background thread and bound the wait: if the mailbox
              // died above, PostAndReply blocks forever (MailboxProcessor.DefaultTimeout is
              // Timeout.Infinite), and without a bound this test would hang the whole run instead of
              // failing fast.
              let task =
                  System.Threading.Tasks.Task.Run(fun () ->
                      store.Append(
                          record
                              "https://example.org/activities/recovery"
                              "https://example.org/games/recovery"
                              "https://example.org/users/recovery"
                      )

                      store.Query(ProvenanceQuery.ByActivityId "https://example.org/activities/recovery"))

              let completedInTime = task.Wait(TimeSpan.FromSeconds(5.0))
              Expect.isTrue completedInTime "Query completed within the bound instead of hanging on a dead mailbox"

              match task.Result with
              | SparqlQueryResult.Graph g ->
                  Expect.isGreaterThan g.Triples.Count 0 "The recovery record was actually appended and is queryable"
              | SparqlQueryResult.Bindings _ -> failwith "Expected a graph"
          } ]
