module Frank.Provenance.Tests.MailboxProcessorProvenanceStoreTests

open System
open System.IO
open Expecto
open Microsoft.Extensions.Logging.Abstractions
open VDS.RDF
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

let private recordEndingAt
    (activityIri: string)
    (resourceIri: string)
    (agentIri: string)
    (endedAt: DateTimeOffset)
    (activityType: string)
    : ProvenanceRecord =
    { Activity = Node.Iri activityIri
      Resource = Node.Iri resourceIri
      Agent = Node.Iri agentIri
      StartedAt = endedAt.AddSeconds(-1.0)
      EndedAt = endedAt
      ActivityType = Some(Uri activityType)
      Properties = [] }

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

          test "Latest returns only the most-recently-ended activity's data, regardless of append order" {
              let store = newStore ProvenanceStoreConfig.defaults
              let resourceIri = "https://example.org/games/latest-1"
              let baseTime = DateTimeOffset.UtcNow

              // Appended in reverse chronological order (newest first) so this only passes if Latest
              // genuinely orders by endedAtTime rather than by append/insertion order.
              store.Append(
                  recordEndingAt
                      "https://example.org/activities/latest-1-newer"
                      resourceIri
                      "https://example.org/users/1"
                      (baseTime.AddMinutes(5.0))
                      "https://tictactoe.example/states/closed"
              )

              store.Append(
                  recordEndingAt
                      "https://example.org/activities/latest-1-older"
                      resourceIri
                      "https://example.org/users/1"
                      baseTime
                      "https://tictactoe.example/states/open"
              )

              match store.Query(ProvenanceQuery.Latest resourceIri) with
              | SparqlQueryResult.Graph g ->
                  let newerActivity = g.CreateUriNode(Uri "https://example.org/activities/latest-1-newer")
                  let olderActivity = g.CreateUriNode(Uri "https://example.org/activities/latest-1-older")

                  Expect.isGreaterThan
                      (g.GetTriplesWithSubject(newerActivity) |> Seq.length)
                      0
                      "The more-recently-ended activity's triples are present"

                  Expect.equal
                      (g.GetTriplesWithSubject(olderActivity) |> Seq.length)
                      0
                      "The older activity's triples are excluded"
              | SparqlQueryResult.Bindings _ -> failwith "Expected a graph, Latest is a CONSTRUCT query"
          }

          test "Latest for a resource with only one recorded activity returns that activity" {
              let store = newStore ProvenanceStoreConfig.defaults
              let resourceIri = "https://example.org/games/latest-2"

              store.Append(
                  recordEndingAt
                      "https://example.org/activities/latest-2-only"
                      resourceIri
                      "https://example.org/users/1"
                      DateTimeOffset.UtcNow
                      "https://tictactoe.example/states/open"
              )

              match store.Query(ProvenanceQuery.Latest resourceIri) with
              | SparqlQueryResult.Graph g -> Expect.isGreaterThan g.Triples.Count 0 "The only recorded activity comes back"
              | SparqlQueryResult.Bindings _ -> failwith "Expected a graph"
          }

          test "Latest for an unknown resource returns an empty graph, not an error" {
              let store = newStore ProvenanceStoreConfig.defaults

              match store.Query(ProvenanceQuery.Latest "https://example.org/games/does-not-exist") with
              | SparqlQueryResult.Graph g -> Expect.equal g.Triples.Count 0 "Nothing recorded for this resource"
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
              let config =
                  { MaxRecords = 2
                    EvictionBatchSize = 1
                    SnapshotEvery = 100 }
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
              let config =
                  { MaxRecords = 1
                    EvictionBatchSize = 100
                    SnapshotEvery = 100 }
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
          }

          test "With no journal attached, behavior is unchanged from v1" {
              let store = new MailboxProcessorProvenanceStore(ProvenanceStoreConfig.defaults, NullLogger.Instance)

              (store :> IProvenanceStore).Append(
                  record "https://example.org/activities/parity" "https://example.org/games/1" "https://example.org/users/42"
              )

              match (store :> IProvenanceStore).Query(ProvenanceQuery.ByResource "https://example.org/games/1") with
              | SparqlQueryResult.Graph g -> Expect.isGreaterThan g.Triples.Count 0 "Append/Query work with no journal"
              | SparqlQueryResult.Bindings _ -> failwith "Expected a graph"

              (store :> IDisposable).Dispose()
          }

          test "A store constructed with a journal recovers prior appends after restart" {
              let dir = Path.Combine(Path.GetTempPath(), "frank-provenance-tests", Guid.NewGuid().ToString())
              Directory.CreateDirectory dir |> ignore

              let firstJournal = FileProvenanceJournal(dir, "recovery-actor")
              let firstStore =
                  new MailboxProcessorProvenanceStore(
                      ProvenanceStoreConfig.defaults,
                      NullLogger.Instance,
                      (firstJournal :> IProvenanceJournal)
                  )

              (firstStore :> IProvenanceStore).Append(
                  record "https://example.org/activities/r1" "https://example.org/games/r" "https://example.org/users/r"
              )

              // Query is a PostAndReply -- it can only complete after the mailbox has already
              // processed the Append message ahead of it in the queue, so this doubles as the
              // barrier that guarantees the Append (and its journal.Append post) has been handled.
              (firstStore :> IProvenanceStore).Query(ProvenanceQuery.ByResource "https://example.org/games/r")
              |> ignore

              firstJournal.Flush()
              (firstStore :> IDisposable).Dispose()

              let secondJournal = FileProvenanceJournal(dir, "recovery-actor")
              let secondStore =
                  new MailboxProcessorProvenanceStore(
                      ProvenanceStoreConfig.defaults,
                      NullLogger.Instance,
                      (secondJournal :> IProvenanceJournal)
                  )

              match (secondStore :> IProvenanceStore).Query(ProvenanceQuery.ByResource "https://example.org/games/r") with
              | SparqlQueryResult.Graph g -> Expect.isGreaterThan g.Triples.Count 0 "Recovered record is queryable after restart"
              | SparqlQueryResult.Bindings _ -> failwith "Expected a graph"

              (secondStore :> IDisposable).Dispose()
          }

          test "A journal whose Append throws doesn't kill the mailbox loop" {
              let failingJournal =
                  { new IProvenanceJournal with
                      member _.Append(_: IGraph) = failwith "simulated journal failure"
                      member _.Snapshot(_: IGraph seq) = ()
                      member _.Recover() = Seq.empty }

              let store =
                  new MailboxProcessorProvenanceStore(ProvenanceStoreConfig.defaults, NullLogger.Instance, failingJournal)

              // The journal.Append call happens inside the same try/with as the rest of Append's body
              // in MailboxProcessorProvenanceStore.fs, so a throwing journal is caught there, logged,
              // and the record is dropped for this Append -- but the mailbox loop itself survives.
              (store :> IProvenanceStore).Append(
                  record "https://example.org/activities/fail1" "https://example.org/games/f" "https://example.org/users/f"
              )

              (store :> IProvenanceStore).Append(
                  record "https://example.org/activities/fail2" "https://example.org/games/f2" "https://example.org/users/f"
              )

              // The second Append still reaches the store, proving the loop is alive -- even though
              // the first one's record was dropped because the journal failure aborted its whole
              // try-block before store.Add for fail1 completed... actually store.Add happens BEFORE
              // journal.Append, so fail1's graph IS in the store; the journal failure only drops
              // fail1's *durability*, not its live-query visibility. Assert on fail2's resource,
              // which has no such ambiguity either way.
              match (store :> IProvenanceStore).Query(ProvenanceQuery.ByResource "https://example.org/games/f2") with
              | SparqlQueryResult.Graph g -> Expect.isGreaterThan g.Triples.Count 0 "Second Append succeeded; mailbox survived the journal failure"
              | SparqlQueryResult.Bindings _ -> failwith "Expected a graph"

              (store :> IDisposable).Dispose()
          } ]
