module Frank.Provenance.Tests.ProvenanceJournalTests

open System
open System.IO
open Expecto
open VDS.RDF
open Frank.Rdf
open Frank.Provenance

let private tempDir () : string =
    let dir = Path.Combine(Path.GetTempPath(), "frank-provenance-tests", Guid.NewGuid().ToString())
    Directory.CreateDirectory dir |> ignore
    dir

let private graphFor (activityIri: string) : IGraph =
    let record: ProvenanceRecord =
        { Activity = Node.Iri activityIri
          Resource = Node.Iri "https://example.org/games/1"
          Agent = Node.Iri "https://example.org/users/42"
          StartedAt = DateTimeOffset(2026, 8, 8, 12, 0, 0, TimeSpan.Zero)
          EndedAt = DateTimeOffset(2026, 8, 8, 12, 0, 1, TimeSpan.Zero)
          ActivityType = None
          Properties = [] }

    let content = record |> ProvenanceRecord.toDoc |> Doc.toGraph
    let named = new Graph(Uri activityIri)
    named.Merge(content :> IGraph)
    named :> IGraph

[<Tests>]
let tests =
    testList
        "FileProvenanceJournal"
        [ test "Append then Recover on a fresh instance returns the appended graph" {
              let dir = tempDir ()
              let writer = FileProvenanceJournal(dir, "actor-1")
              (writer :> IProvenanceJournal).Append(graphFor "https://example.org/activities/1")
              writer.Flush()

              let reader = FileProvenanceJournal(dir, "actor-1") :> IProvenanceJournal
              let recovered = reader.Recover() |> List.ofSeq

              Expect.equal recovered.Length 1 "One graph recovered"
              Expect.isGreaterThan recovered.[0].Triples.Count 0 "Recovered graph has triples"
          }

          test "Multiple appends before any snapshot all survive recovery" {
              let dir = tempDir ()
              let writer = FileProvenanceJournal(dir, "actor-2")
              let journal = writer :> IProvenanceJournal
              journal.Append(graphFor "https://example.org/activities/1")
              journal.Append(graphFor "https://example.org/activities/2")
              journal.Append(graphFor "https://example.org/activities/3")
              writer.Flush()

              let reader = FileProvenanceJournal(dir, "actor-2") :> IProvenanceJournal
              let recovered = reader.Recover() |> List.ofSeq

              Expect.equal recovered.Length 3 "All three appended graphs recovered"
          }

          test "Snapshot compacts current graphs; later appends layer on top without duplicating" {
              let dir = tempDir ()
              let writer = FileProvenanceJournal(dir, "actor-3")
              let journal = writer :> IProvenanceJournal
              let g1 = graphFor "https://example.org/activities/1"
              let g2 = graphFor "https://example.org/activities/2"
              journal.Append(g1)
              journal.Append(g2)
              writer.Flush()

              journal.Snapshot([ g1; g2 ])
              writer.Flush()

              journal.Append(graphFor "https://example.org/activities/3")
              writer.Flush()

              Expect.isTrue (File.Exists(Path.Combine(dir, "actor-3.snapshot.1.nq"))) "Snapshot file written"

              let journalSegments =
                  Directory.GetFiles(dir, "actor-3.journal.*.nq")

              Expect.equal journalSegments.Length 3 "All three journal segments remain on disk (never deleted)"

              let reader = FileProvenanceJournal(dir, "actor-3") :> IProvenanceJournal
              let recovered = reader.Recover() |> List.ofSeq

              Expect.equal recovered.Length 3 "Snapshot's two graphs plus the one post-snapshot append"
          }

          test "A fresh actor with no manifest recovers an empty graph set" {
              let dir = tempDir ()
              let journal = FileProvenanceJournal(dir, "actor-never-appended") :> IProvenanceJournal

              Expect.isEmpty (journal.Recover() |> List.ofSeq) "Nothing to recover"
          }

          test "Recover raises when the manifest references a missing segment file" {
              let dir = tempDir ()
              let writer = FileProvenanceJournal(dir, "actor-4")
              (writer :> IProvenanceJournal).Append(graphFor "https://example.org/activities/1")
              writer.Flush()

              File.Delete(Path.Combine(dir, "actor-4.journal.1.nq"))

              let reader = FileProvenanceJournal(dir, "actor-4") :> IProvenanceJournal
              Expect.throws (fun () -> reader.Recover() |> Seq.iter ignore |> ignore) "Missing referenced file is corruption, not a recoverable gap"
          }

          test "A failed segment write is absorbed; the journal keeps serving later messages" {
              let dir = tempDir ()

              // Pre-create a *directory* where the first segment file belongs. StreamWriter against a
              // path that is actually a directory throws (UnauthorizedAccessException/IOException),
              // which is the same shape of failure as a full disk or a permission denial -- and before
              // the loop caught it, that exception killed the MailboxProcessor's receive loop for good,
              // so every later Append/Snapshot posted into a mailbox nobody would ever read again.
              let obstruction = Path.Combine(dir, "actor-5.journal.1.nq")
              Directory.CreateDirectory obstruction |> ignore

              let writer = FileProvenanceJournal(dir, "actor-5")
              let journal = writer :> IProvenanceJournal

              journal.Append(graphFor "https://example.org/activities/doomed")
              writer.Flush() // Must return rather than hang, even though the message ahead of it failed.

              // The failure left the manifest at its last known-good state, so NextSegmentSeq is still 1
              // -- clearing the obstruction makes the very next append reuse that sequence number and
              // succeed, which is exactly the transient-failure (disk freed up) recovery case.
              Directory.Delete obstruction

              journal.Append(graphFor "https://example.org/activities/after-failure")
              writer.Flush()

              let reader = FileProvenanceJournal(dir, "actor-5") :> IProvenanceJournal
              let recovered = reader.Recover() |> List.ofSeq

              Expect.equal recovered.Length 1 "The post-failure append was written and is recoverable"
              Expect.isGreaterThan recovered.[0].Triples.Count 0 "Recovered graph has triples"
          } ]
