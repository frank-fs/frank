# Frank.Provenance Persistence Hook Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Give `MailboxProcessorProvenanceStore` an opt-in, event-sourced + periodic-snapshot durability hook (`IProvenanceJournal`), with a default N-Quads file-backed implementation, per frank-fs/frank#486 and `docs/superpowers/specs/2026-08-08-frank-provenance-persistence-design.md`.

**Architecture:** `IProvenanceJournal` is a pure-mechanics interface (`Append`/`Snapshot`/`Recover`) constructor-injected into `MailboxProcessorProvenanceStore` as `?journal: IProvenanceJournal` — no decorator, no parallel `IProvenanceStore`. The store owns snapshot-trigger policy (`ProvenanceStoreConfig.SnapshotEvery`); the journal only knows how to persist what it's handed. The default implementation, `FileProvenanceJournal`, writes immutable, versioned N-Quads segment/snapshot files tracked by a JSON manifest pointer — modeled on Delta Lake/Iceberg-style table formats (nothing overwritten or deleted).

**Tech Stack:** F# 8.0+, multi-targeting `net8.0;net9.0;net10.0`, dotNetRdf.Core 3.5.1 (`VDS.RDF.Writing.NQuadsWriter`, `VDS.RDF.Parsing.NQuadsParser` — already a transitive dependency, no new NuGet package), `System.Text.Json` (BCL, no new package) for the manifest, Expecto (existing test framework, run via `dotnet test`).

## Global Constraints

- Every `.fs` module gets a matching `.fsi`, added directly above it in `<Compile>` order in the `.fsproj`.
- `dotNetRdf.Core` stays pinned at exactly `3.5.1` (see `Frank.Provenance.fsproj`'s `NU1902` comment — do not touch the pin or the `NoWarn`).
- No new NuGet package references — N-Quads via dotNetRdf.Core (already referenced), manifest JSON via `System.Text.Json` (BCL).
- Multi-target build must succeed on all three TFMs (`net8.0;net9.0;net10.0`), not just `net10.0`.
- Run tests with: `dotnet test test/Frank.Provenance.Tests/Frank.Provenance.Tests.fsproj`

---

## File Structure

- **Create** `src/Frank.Provenance/ProvenanceJournal.fsi` / `.fs` — `IProvenanceJournal` interface + `FileProvenanceJournal` default implementation. New file because it's a distinct responsibility (persistence mechanics) from both the query store (`ProvenanceStore.fs`) and the mailbox actor (`MailboxProcessorProvenanceStore.fs`).
- **Modify** `src/Frank.Provenance/ProvenanceStore.fsi` / `.fs` — add `SnapshotEvery` to `ProvenanceStoreConfig` and its default.
- **Modify** `src/Frank.Provenance/MailboxProcessorProvenanceStore.fsi` / `.fs` — optional `?journal` constructor param, recovery-on-construct, `Append`/`Snapshot` calls in the mailbox loop.
- **Modify** `src/Frank.Provenance/Frank.Provenance.fsproj` — add the new `Compile` entries, positioned after `ProvenanceStore.fs` and before `MailboxProcessorProvenanceStore.fsi` (the store depends on `IProvenanceJournal`).
- **Modify** `src/Frank.Provenance/README.md` — document the hook.
- **Create** `test/Frank.Provenance.Tests/ProvenanceJournalTests.fs`.
- **Modify** `test/Frank.Provenance.Tests/MailboxProcessorProvenanceStoreTests.fs` — journal integration tests.
- **Modify** `test/Frank.Provenance.Tests/Frank.Provenance.Tests.fsproj` — add the new test `Compile` entry, before `MailboxProcessorProvenanceStoreTests.fs`.

---

### Task 1: `IProvenanceJournal` + `FileProvenanceJournal`

**Files:**
- Create: `src/Frank.Provenance/ProvenanceJournal.fsi`
- Create: `src/Frank.Provenance/ProvenanceJournal.fs`
- Modify: `src/Frank.Provenance/Frank.Provenance.fsproj`
- Create: `test/Frank.Provenance.Tests/ProvenanceJournalTests.fs`
- Modify: `test/Frank.Provenance.Tests/Frank.Provenance.Tests.fsproj`

**Interfaces:**
- Consumes: `Frank.Provenance.ProvenanceRecord`, `Frank.Provenance.ProvenanceRecord.toDoc` and `Frank.Rdf.Doc.toGraph` (both already exist) — only in the test file, to build sample `IGraph` values.
- Produces:
  - `type IProvenanceJournal = abstract Append: graph: IGraph -> unit; abstract Snapshot: graphs: IGraph seq -> unit; abstract Recover: unit -> IGraph seq` — consumed by Task 3.
  - `type FileProvenanceJournal = new: baseDirectory: string * actorId: string -> FileProvenanceJournal`, implementing `IProvenanceJournal`, plus `member internal Flush: unit -> unit` (test-only synchronization barrier) — consumed by Task 3's integration tests.

- [ ] **Step 1: Add the new files to the fsproj**

Edit `src/Frank.Provenance/Frank.Provenance.fsproj`, in the existing `<ItemGroup>` with the `<Compile>` entries:

```xml
    <Compile Include="ProvenanceStore.fsi" />
    <Compile Include="ProvenanceStore.fs" />
    <Compile Include="ProvenanceJournal.fsi" />
    <Compile Include="ProvenanceJournal.fs" />
    <Compile Include="MailboxProcessorProvenanceStore.fsi" />
    <Compile Include="MailboxProcessorProvenanceStore.fs" />
```

Edit `test/Frank.Provenance.Tests/Frank.Provenance.Tests.fsproj`:

```xml
    <Compile Include="ProvenanceQueryTests.fs" />
    <Compile Include="ProvenanceJournalTests.fs" />
    <Compile Include="MailboxProcessorProvenanceStoreTests.fs" />
    <Compile Include="Program.fs" />
```

- [ ] **Step 2: Write `ProvenanceJournal.fsi`**

```fsharp
namespace Frank.Provenance

open VDS.RDF

/// Pure persistence mechanics for MailboxProcessorProvenanceStore's opt-in durability hook. Trigger
/// policy (when to snapshot) lives in the store (ProvenanceStoreConfig.SnapshotEvery) -- this
/// contract only knows how to durably persist what it's handed and how to recover it.
type IProvenanceJournal =
    /// Durably logs one named graph. Non-blocking: implementations own whatever background
    /// mechanism they need so this returns immediately.
    abstract Append: graph: IGraph -> unit

    /// Compacts the given graphs -- the store's full current state at the moment of the call -- into
    /// a new snapshot. Non-blocking, same as Append.
    abstract Snapshot: graphs: IGraph seq -> unit

    /// Reads whatever this journal has durably persisted and returns the merged graph set to replay
    /// into a fresh store. Called once, synchronously, before a store starts serving Append/Query.
    abstract Recover: unit -> IGraph seq

/// File-backed IProvenanceJournal: immutable, versioned N-Quads segment/snapshot files under
/// baseDirectory, named `{actorId}.journal.{seq}.nq` / `{actorId}.snapshot.{seq}.nq`, tracked by an
/// `{actorId}.manifest.json` pointer file. Nothing is ever overwritten or deleted -- see
/// docs/superpowers/specs/2026-08-08-frank-provenance-persistence-design.md for why.
[<Sealed>]
type FileProvenanceJournal =
    new: baseDirectory: string * actorId: string -> FileProvenanceJournal
    interface IProvenanceJournal

    /// Test-only synchronization barrier: blocks until every Append/Snapshot posted before this call
    /// has been durably written and the manifest updated. Production code never needs this --
    /// Recover only ever runs before a store starts serving traffic -- but a test that Appends/
    /// Snapshots then immediately constructs a fresh instance to Recover needs to know the
    /// fire-and-forget writes actually landed first.
    member internal Flush: unit -> unit
```

- [ ] **Step 3: Write the failing round-trip test**

Create `test/Frank.Provenance.Tests/ProvenanceJournalTests.fs`:

```fsharp
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
          } ]
```

- [ ] **Step 4: Run it, verify it fails**

Run: `dotnet test test/Frank.Provenance.Tests/Frank.Provenance.Tests.fsproj --filter "FullyQualifiedName~ProvenanceJournalTests"`
Expected: build FAILS — `IProvenanceJournal`/`FileProvenanceJournal` don't exist in `ProvenanceJournal.fs` yet (only the `.fsi` exists).

- [ ] **Step 5: Implement `FileProvenanceJournal` (Append + Recover)**

Create `src/Frank.Provenance/ProvenanceJournal.fs`:

```fsharp
namespace Frank.Provenance

open System
open System.IO
open System.Text.Json
open VDS.RDF
open VDS.RDF.Parsing
open VDS.RDF.Writing

type IProvenanceJournal =
    abstract Append: graph: IGraph -> unit
    abstract Snapshot: graphs: IGraph seq -> unit
    abstract Recover: unit -> IGraph seq

// Not marked `private`/`internal`: System.Text.Json's reflection-based deserializer needs this
// record's generated constructor to be public. Omitting Manifest from ProvenanceJournal.fsi already
// makes it inaccessible outside this module -- adding `private` here would additionally make the
// constructor non-public and break JSON deserialization for no encapsulation benefit.
type Manifest =
    { LatestSnapshot: int
      NextSnapshotSeq: int
      JournalSegmentsSince: int[]
      NextSegmentSeq: int }

module Manifest =
    let empty =
        { LatestSnapshot = 0
          NextSnapshotSeq = 1
          JournalSegmentsSince = [||]
          NextSegmentSeq = 1 }

    let private jsonOptions =
        JsonSerializerOptions(PropertyNamingPolicy = JsonNamingPolicy.CamelCase)

    let load (path: string) : Manifest =
        if File.Exists path then
            JsonSerializer.Deserialize<Manifest>(File.ReadAllText path, jsonOptions)
        else
            empty

    let save (path: string) (manifest: Manifest) : unit =
        File.WriteAllText(path, JsonSerializer.Serialize(manifest, jsonOptions))

type JournalMessage =
    | AppendSegment of IGraph
    | TakeSnapshot of IGraph list
    | Flush of AsyncReplyChannel<unit>

[<Sealed>]
type FileProvenanceJournal(baseDirectory: string, actorId: string) =
    do Directory.CreateDirectory(baseDirectory) |> ignore

    let manifestPath = Path.Combine(baseDirectory, sprintf "%s.manifest.json" actorId)
    let snapshotPath (seqNum: int) = Path.Combine(baseDirectory, sprintf "%s.snapshot.%d.nq" actorId seqNum)
    let segmentPath (seqNum: int) = Path.Combine(baseDirectory, sprintf "%s.journal.%d.nq" actorId seqNum)

    let writeGraphs (path: string) (graphs: IGraph seq) : unit =
        let store = new TripleStore()

        for g in graphs do
            store.Add(g, true) |> ignore

        use writer = new StreamWriter(path)
        NQuadsWriter().Save(store, writer, true)

    let readGraphs (path: string) : IGraph list =
        let store = new TripleStore()
        NQuadsParser().Load(store, path)
        [ for g in store.Graphs -> g ]

    let agent =
        MailboxProcessor<JournalMessage>.Start(fun inbox ->
            let rec loop (manifest: Manifest) =
                async {
                    let! msg = inbox.Receive()

                    match msg with
                    | AppendSegment graph ->
                        let seqNum = manifest.NextSegmentSeq
                        writeGraphs (segmentPath seqNum) [ graph ]

                        let updated =
                            { manifest with
                                JournalSegmentsSince = Array.append manifest.JournalSegmentsSince [| seqNum |]
                                NextSegmentSeq = seqNum + 1 }

                        Manifest.save manifestPath updated
                        return! loop updated

                    | TakeSnapshot graphs ->
                        let seqNum = manifest.NextSnapshotSeq
                        writeGraphs (snapshotPath seqNum) graphs

                        let updated =
                            { manifest with
                                LatestSnapshot = seqNum
                                NextSnapshotSeq = seqNum + 1
                                JournalSegmentsSince = [||] }

                        Manifest.save manifestPath updated
                        return! loop updated

                    | Flush reply ->
                        reply.Reply(())
                        return! loop manifest
                }

            loop (Manifest.load manifestPath))

    interface IProvenanceJournal with
        member _.Append(graph: IGraph) = agent.Post(AppendSegment graph)
        member _.Snapshot(graphs: IGraph seq) = agent.Post(TakeSnapshot(List.ofSeq graphs))

        member _.Recover() : IGraph seq =
            let manifest = Manifest.load manifestPath

            let snapshotGraphs =
                if manifest.LatestSnapshot > 0 then
                    readGraphs (snapshotPath manifest.LatestSnapshot)
                else
                    []

            let segmentGraphs =
                manifest.JournalSegmentsSince
                |> Array.toList
                |> List.collect (fun seqNum -> readGraphs (segmentPath seqNum))

            snapshotGraphs @ segmentGraphs

    member internal _.Flush() : unit = agent.PostAndReply Flush
```

- [ ] **Step 6: Run it, verify it passes**

Run: `dotnet test test/Frank.Provenance.Tests/Frank.Provenance.Tests.fsproj --filter "FullyQualifiedName~ProvenanceJournalTests"`
Expected: PASS.

- [ ] **Step 7: Write the failing multi-append test**

Add inside the `testList` in `ProvenanceJournalTests.fs`, after the first test:

```fsharp
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
```

This should already pass without further implementation changes (Step 5 handles arbitrary append counts) — run it to confirm rather than assume:

Run: `dotnet test test/Frank.Provenance.Tests/Frank.Provenance.Tests.fsproj --filter "FullyQualifiedName~ProvenanceJournalTests"`
Expected: PASS. If it fails, the `NextSegmentSeq`/`JournalSegmentsSince` bookkeeping in Step 5 has a bug — fix there, not by special-casing this test.

- [ ] **Step 8: Write the failing snapshot test**

```fsharp
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
```

- [ ] **Step 9: Run it, verify it fails**

Run: `dotnet test test/Frank.Provenance.Tests/Frank.Provenance.Tests.fsproj --filter "FullyQualifiedName~ProvenanceJournalTests"`
Expected: FAIL on the `recovered.Length` assertion — before Step 5's `Snapshot` handling resets `JournalSegmentsSince`, `NextSnapshotSeq`/`LatestSnapshot` wiring must be exercised for the first time by this test. If Step 5 was implemented exactly as written above, this should actually already PASS (the snapshot-reset logic was written in Step 5, not deferred) — run it to confirm; if it fails, the bug is almost certainly `LatestSnapshot`/`NextSnapshotSeq` not being threaded through the `TakeSnapshot` match arm correctly.

- [ ] **Step 10: Fix `ProvenanceJournal.fs` if Step 9 failed, then re-run to confirm PASS**

Run: `dotnet test test/Frank.Provenance.Tests/Frank.Provenance.Tests.fsproj --filter "FullyQualifiedName~ProvenanceJournalTests"`
Expected: PASS.

- [ ] **Step 11: Write the fresh-actor and missing-file edge case tests**

```fsharp
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
```

- [ ] **Step 12: Run it, verify it passes**

Run: `dotnet test test/Frank.Provenance.Tests/Frank.Provenance.Tests.fsproj --filter "FullyQualifiedName~ProvenanceJournalTests"`
Expected: PASS. Both should pass without further implementation changes — the first because `Manifest.load` already returns `Manifest.empty` for a nonexistent manifest file, the second because `VDS.RDF.Parsing.NQuadsParser.Load(ITripleStore, string)` already throws `FileNotFoundException` internally when given a path that doesn't exist. If either fails, that assumption was wrong — add the minimal explicit check needed (e.g. `if not (File.Exists path) then failwithf ...` in `readGraphs`) and re-run.

- [ ] **Step 13: Commit**

```bash
git add src/Frank.Provenance/ProvenanceJournal.fsi src/Frank.Provenance/ProvenanceJournal.fs src/Frank.Provenance/Frank.Provenance.fsproj test/Frank.Provenance.Tests/ProvenanceJournalTests.fs test/Frank.Provenance.Tests/Frank.Provenance.Tests.fsproj
git commit -m "feat(frank-provenance): add IProvenanceJournal and FileProvenanceJournal"
```

---

### Task 2: `ProvenanceStoreConfig.SnapshotEvery`

**Files:**
- Modify: `src/Frank.Provenance/ProvenanceStore.fsi`
- Modify: `src/Frank.Provenance/ProvenanceStore.fs`
- Modify: `test/Frank.Provenance.Tests/ProvenanceQueryTests.fs` (or wherever `ProvenanceStoreConfig.defaults` is currently asserted — see Step 1)

**Interfaces:**
- Consumes: nothing new.
- Produces: `ProvenanceStoreConfig.SnapshotEvery: int` and `ProvenanceStoreConfig.defaults` including it — consumed by Task 3.

- [ ] **Step 1: Check for an existing test asserting `ProvenanceStoreConfig.defaults`**

Run: `grep -rn "ProvenanceStoreConfig.defaults" test/Frank.Provenance.Tests/`

If a test asserts the exact record value (e.g. `Expect.equal ProvenanceStoreConfig.defaults { MaxRecords = 1000; EvictionBatchSize = 100 } "..."`), it will fail to compile once `SnapshotEvery` is added to the type — note the file/line, it needs updating in Step 4.

- [ ] **Step 2: Add the field to `ProvenanceStore.fsi`**

In `src/Frank.Provenance/ProvenanceStore.fsi`, change:

```fsharp
type ProvenanceStoreConfig =
    { /// The number of records to retain before the store starts evicting the oldest ones. A value
      /// <= 0 does not stop the store from accepting appends -- it just means eviction kicks in on
      /// (almost) every append, subject to the "never evict the newest record" clamp below.
      MaxRecords: int
      /// The number of oldest records to evict at once, once MaxRecords is exceeded. Clamped so it can
      /// never evict the record just appended, even when configured >= MaxRecords.
      EvictionBatchSize: int }
```

to:

```fsharp
type ProvenanceStoreConfig =
    { /// The number of records to retain before the store starts evicting the oldest ones. A value
      /// <= 0 does not stop the store from accepting appends -- it just means eviction kicks in on
      /// (almost) every append, subject to the "never evict the newest record" clamp below.
      MaxRecords: int
      /// The number of oldest records to evict at once, once MaxRecords is exceeded. Clamped so it can
      /// never evict the record just appended, even when configured >= MaxRecords.
      EvictionBatchSize: int
      /// Number of Append calls between snapshots, when a journal is attached (see
      /// MailboxProcessorProvenanceStore). Ignored entirely when no journal is present. Values <= 0
      /// are clamped to 1 (snapshot on every Append) rather than raising or dividing by zero. }
      SnapshotEvery: int }
```

- [ ] **Step 3: Update `ProvenanceStore.fs`'s `defaults`**

Find and edit:

```fsharp
    let defaults = { MaxRecords = 1000; EvictionBatchSize = 100 }
```

to:

```fsharp
    let defaults =
        { MaxRecords = 1000
          EvictionBatchSize = 100
          SnapshotEvery = 100 }
```

- [ ] **Step 4: Fix any test that constructs `ProvenanceStoreConfig` by full record literal without `SnapshotEvery`**

If Step 1 found any `{ MaxRecords = ...; EvictionBatchSize = ... }` literals (not using `ProvenanceStoreConfig.defaults with ...`), add `SnapshotEvery = 100` (or another explicit value if the test's intent calls for it) to each. Search broadly, not just the file found in Step 1:

Run: `grep -rn "MaxRecords =" test/Frank.Provenance.Tests/`

Update every match that constructs a full record literal (not `{ ProvenanceStoreConfig.defaults with ... }`, which doesn't need changes).

- [ ] **Step 5: Build and run the full test suite to confirm nothing else broke**

Run: `dotnet build src/Frank.Provenance/Frank.Provenance.fsproj -f net10.0`
Expected: builds clean.

Run: `dotnet test test/Frank.Provenance.Tests/Frank.Provenance.Tests.fsproj`
Expected: all existing tests PASS (this is a config field addition, not a behavior change yet — nothing should regress).

- [ ] **Step 6: Commit**

```bash
git add src/Frank.Provenance/ProvenanceStore.fsi src/Frank.Provenance/ProvenanceStore.fs test/Frank.Provenance.Tests/
git commit -m "feat(frank-provenance): add ProvenanceStoreConfig.SnapshotEvery"
```

---

### Task 3: Wire `IProvenanceJournal` into `MailboxProcessorProvenanceStore`

**Files:**
- Modify: `src/Frank.Provenance/MailboxProcessorProvenanceStore.fsi`
- Modify: `src/Frank.Provenance/MailboxProcessorProvenanceStore.fs`
- Modify: `test/Frank.Provenance.Tests/MailboxProcessorProvenanceStoreTests.fs`

**Interfaces:**
- Consumes: `IProvenanceJournal` (Task 1), `FileProvenanceJournal` + its internal `Flush` (Task 1, test-only), `ProvenanceStoreConfig.SnapshotEvery` (Task 2).
- Produces: `MailboxProcessorProvenanceStore(config, logger, ?journal)` — the only public surface change; existing 2-arg call sites keep compiling unchanged since `journal` is optional.

- [ ] **Step 1: Update `MailboxProcessorProvenanceStore.fsi`**

Change:

```fsharp
[<Sealed>]
type MailboxProcessorProvenanceStore =
    new: config: ProvenanceStoreConfig * logger: ILogger -> MailboxProcessorProvenanceStore

    interface IProvenanceStore
    interface IDisposable
```

to:

```fsharp
[<Sealed>]
type MailboxProcessorProvenanceStore =
    /// journal is an opt-in durability hook (see IProvenanceJournal). When None (the default),
    /// behavior is unchanged from the in-memory-only v1: no recovery on construction, no snapshot
    /// calls, Append incurs no journal-write cost.
    new: config: ProvenanceStoreConfig * logger: ILogger * ?journal: IProvenanceJournal -> MailboxProcessorProvenanceStore

    interface IProvenanceStore
    interface IDisposable
```

- [ ] **Step 2: Write the failing no-journal-parity test**

This locks in that adding the optional parameter doesn't change v1 behavior. In `test/Frank.Provenance.Tests/MailboxProcessorProvenanceStoreTests.fs`, add to the `testList`:

```fsharp
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
```

- [ ] **Step 3: Run it, verify it fails to build**

Run: `dotnet test test/Frank.Provenance.Tests/Frank.Provenance.Tests.fsproj --filter "FullyQualifiedName~MailboxProcessorProvenanceStore"`
Expected: build FAILS — `.fsi` now declares the 3-arg (with optional 3rd) constructor but `.fs` still only implements 2 args, a signature mismatch.

- [ ] **Step 4: Implement the wiring in `MailboxProcessorProvenanceStore.fs`**

Replace the whole file with:

```fsharp
namespace Frank.Provenance

open System
open Microsoft.Extensions.Logging
open VDS.RDF
open VDS.RDF.Query
open VDS.RDF.Query.Datasets
open Frank.Rdf

type private StoreMessage =
    | Append of ProvenanceRecord
    | Query of ProvenanceQuery * AsyncReplyChannel<SparqlQueryResult>

[<Sealed>]
type MailboxProcessorProvenanceStore(config: ProvenanceStoreConfig, logger: ILogger, ?journal: IProvenanceJournal) =
    let store = new TripleStore()
    let snapshotEvery = max 1 config.SnapshotEvery

    let graphNameFor (record: ProvenanceRecord) : Uri =
        match record.Activity with
        | Node.Iri s -> Uri s
        | Node.Blank id -> Uri(sprintf "urn:frank:provenance:%s" id)

    // A recovered graph's Name came from graphNameFor's own output at the time it was first
    // appended (see graphNameFor above), so it's always an IUriNode -- the urn:frank:provenance:...
    // fallback in graphNameFor still produces a URI, never a blank node. The Guid fallback below only
    // guards a journal implementation that hands back a graph built some other way.
    let graphUriOf (g: IGraph) : Uri =
        match g.Name with
        | :? IUriNode as un -> un.Uri
        | _ -> Uri(sprintf "urn:frank:provenance:recovered:%s" (Guid.NewGuid().ToString()))

    let runQuery (query: ProvenanceQuery) : SparqlQueryResult =
        let sparqlQuery = toSparqlQuery query
        let dataset = new InMemoryDataset(store, true)
        let processor = new LeviathanQueryProcessor(dataset)

        match processor.ProcessQuery(sparqlQuery) with
        | :? SparqlResultSet as rs -> SparqlQueryResult.Bindings rs
        | :? IGraph as g -> SparqlQueryResult.Graph g
        | other -> failwithf "Frank.Provenance: unexpected SPARQL result shape %A" other

    let recoveredEntries =
        let recovered =
            journal |> Option.map (fun j -> j.Recover() |> List.ofSeq) |> Option.defaultValue []

        for g in recovered do
            store.Add(g, true) |> ignore

        recovered |> List.map (fun g -> g.Name, graphUriOf g)

    let agent =
        MailboxProcessor<StoreMessage>.Start(fun inbox ->
            let rec loop (entries: (IRefNode * Uri) list) (appendCount: int) =
                async {
                    let! msg = inbox.Receive()

                    match msg with
                    | Append record ->
                        // A malformed record (a relative-IRI Activity, an invalid prefix/IRI Doc.toGraph
                        // rejects, ...) must not kill the mailbox loop: an unhandled exception here stops
                        // MailboxProcessor's Receive loop for good, so every subsequent Append silently
                        // vanishes into a dead mailbox and every subsequent Query (PostAndReply, infinite
                        // timeout) blocks its caller forever. Catch, log, and keep the previous, known-good
                        // entries state -- the bad record is dropped, not retried.
                        try
                            let graphName = graphNameFor record
                            // dotNetRDF names a graph via its constructor (IRefNode/Uri), not via a mutable
                            // property: setting Graph.BaseUri does NOT set Graph.Name, so a graph built that
                            // way is added as the store's unnamed default graph, not a named graph. Build the
                            // record's content unnamed (via Doc.toGraph), then merge it into a freshly
                            // constructed, properly named graph before adding it to the store.
                            let content = record |> ProvenanceRecord.toDoc |> Doc.toGraph
                            let namedGraph = new Graph(graphName)
                            namedGraph.Merge(content :> IGraph)
                            store.Add(namedGraph :> IGraph, true) |> ignore
                            logger.LogDebug("Appended provenance record for activity {GraphName}", graphName)

                            let appendCount = appendCount + 1

                            match journal with
                            | Some j ->
                                j.Append(namedGraph :> IGraph)

                                if appendCount % snapshotEvery = 0 then
                                    j.Snapshot(seq { for g in store.Graphs -> g })
                            | None -> ()

                            let updated = entries @ [ namedGraph.Name, graphName ]

                            let retained =
                                if updated.Length > config.MaxRecords then
                                    // Clamp so eviction can never remove the record just appended above,
                                    // regardless of how config.MaxRecords/EvictionBatchSize are configured
                                    // (e.g. MaxRecords <= 0, or EvictionBatchSize >= MaxRecords): always
                                    // leave at least the newest entry behind.
                                    let evictCount =
                                        [ config.EvictionBatchSize; updated.Length - 1 ] |> List.min |> max 0

                                    for evictedName, evictedUri in updated |> List.truncate evictCount do
                                        store.Remove(evictedName) |> ignore
                                        logger.LogDebug("Evicted provenance record {GraphName}", evictedUri)

                                    updated |> List.skip evictCount
                                else
                                    updated

                            return! loop retained appendCount
                        with ex ->
                            logger.LogError(ex, "Failed to append a provenance record; dropping it and continuing")
                            return! loop entries appendCount

                    | Query(query, reply) ->
                        // Same story as Append, but the caller is blocked in PostAndReply waiting on
                        // `reply` -- so on failure we must still Reply (with an empty graph) rather than
                        // let the exception propagate, or that caller hangs forever with no log line to
                        // explain it.
                        let result =
                            try
                                runQuery query
                            with ex ->
                                logger.LogError(ex, "Failed to run provenance query {Query}; returning an empty graph", query)
                                SparqlQueryResult.Graph(new Graph() :> IGraph)

                        reply.Reply(result)
                        return! loop entries appendCount
                }

            loop recoveredEntries 0)

    interface IProvenanceStore with
        member _.Append(record: ProvenanceRecord) = agent.Post(Append record)
        member _.Query(query: ProvenanceQuery) = agent.PostAndReply(fun reply -> Query(query, reply))

    interface IDisposable with
        member _.Dispose() = (agent :> IDisposable).Dispose()
```

Note what changed from v1: `snapshotEvery` (clamped), `graphUriOf`, `recoveredEntries` (computed before `agent` — runs `journal.Recover()` synchronously at construction, merges into `store`), the `loop` signature gaining `appendCount`, the journal calls inside the `Append` case, and `loop recoveredEntries 0` as the initial call instead of `loop []`. Everything else — eviction, error handling, `Query` — is untouched.

- [ ] **Step 5: Run it, verify it passes**

Run: `dotnet test test/Frank.Provenance.Tests/Frank.Provenance.Tests.fsproj --filter "FullyQualifiedName~MailboxProcessorProvenanceStore"`
Expected: PASS, including all pre-existing tests in this file (they construct with the 2-arg form, which still works since `journal` is optional).

- [ ] **Step 6: Write the failing full-recovery integration test**

Add to the `testList`:

```fsharp
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
```

Add `open System.IO` to the top of `MailboxProcessorProvenanceStoreTests.fs` if not already present.

- [ ] **Step 7: Run it, verify it passes**

Run: `dotnet test test/Frank.Provenance.Tests/Frank.Provenance.Tests.fsproj --filter "FullyQualifiedName~MailboxProcessorProvenanceStore"`
Expected: PASS.

- [ ] **Step 8: Write the failing journal-failure-isolation test**

```fsharp
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
          }
```

- [ ] **Step 9: Run it, verify it passes**

Run: `dotnet test test/Frank.Provenance.Tests/Frank.Provenance.Tests.fsproj --filter "FullyQualifiedName~MailboxProcessorProvenanceStore"`
Expected: PASS. If it hangs or fails, check that `journal.Append`/`journal.Snapshot` calls in Step 4 are inside the existing `try ... with ex -> logger.LogError(...)` block (they must be — the whole point of Task 3's design is that this is the same "don't kill the mailbox" `try` that already wraps graph construction).

- [ ] **Step 10: Full-suite regression run**

Run: `dotnet build --property WarningLevel=0` on the whole solution to confirm all three TFMs still build:

```bash
dotnet build src/Frank.Provenance/Frank.Provenance.fsproj -f net8.0
dotnet build src/Frank.Provenance/Frank.Provenance.fsproj -f net9.0
dotnet build src/Frank.Provenance/Frank.Provenance.fsproj -f net10.0
dotnet test test/Frank.Provenance.Tests/Frank.Provenance.Tests.fsproj
```

Expected: all four commands succeed, full test suite green.

- [ ] **Step 11: Commit**

```bash
git add src/Frank.Provenance/MailboxProcessorProvenanceStore.fsi src/Frank.Provenance/MailboxProcessorProvenanceStore.fs test/Frank.Provenance.Tests/MailboxProcessorProvenanceStoreTests.fs
git commit -m "feat(frank-provenance): wire IProvenanceJournal into MailboxProcessorProvenanceStore"
```

---

### Task 4: Document the hook

**Files:**
- Modify: `src/Frank.Provenance/README.md`

**Interfaces:**
- Consumes: `FileProvenanceJournal`, `IProvenanceJournal`, `ProvenanceStoreConfig.SnapshotEvery` (all from Tasks 1-3).
- Produces: nothing consumed by later tasks — this is the last task.

- [ ] **Step 1: Read the current README's Quick Start section**

Run: `grep -n "^##" src/Frank.Provenance/README.md`

Find where `MailboxProcessorProvenanceStore` construction is shown (the `## Quick Start` section, per the existing header list) to match its existing code-sample style exactly.

- [ ] **Step 2: Add a "Durability" section**

Insert a new `## Durability` section in `src/Frank.Provenance/README.md`, immediately after `## Quick Start` and before `## Scope`:

```markdown
## Durability

`MailboxProcessorProvenanceStore` is in-memory by default -- everything it holds is lost on process
restart. Attach a journal to make it durable:

```fsharp
open Frank.Provenance

let journal = FileProvenanceJournal("/var/data/provenance", "leaderboard-actor")

let store =
    new MailboxProcessorProvenanceStore(
        ProvenanceStoreConfig.defaults,
        logger,
        journal
    )
```

With a journal attached, every `Append` is durably logged (fire-and-forget -- it doesn't block the
caller), and every `ProvenanceStoreConfig.SnapshotEvery` appends (100 by default) the current state is
compacted into a snapshot. On construction, the store replays the latest snapshot plus any journal
entries recorded since it, so a freshly-started process with the same `(baseDirectory, actorId)` picks
up where the last one left off.

`FileProvenanceJournal` writes N-Quads (`{actorId}.journal.{seq}.nq` / `{actorId}.snapshot.{seq}.nq`)
tracked by an `{actorId}.manifest.json` pointer file -- immutable and versioned, nothing is overwritten
or deleted. `IProvenanceJournal` is a small interface (`Append`/`Snapshot`/`Recover`); a different
durability backend can implement it without changing `MailboxProcessorProvenanceStore` at all.

Omit the third constructor argument (or pass `None`) for the original in-memory-only behavior --
zero cost, zero files written.
```

- [ ] **Step 3: Verify the README's code samples still match reality**

Run: `dotnet build src/Frank.Provenance/Frank.Provenance.fsproj -f net10.0`
Expected: builds clean (this doesn't compile the README, but confirms the API shapes referenced in Step 2's snippet — `FileProvenanceJournal`'s constructor, `MailboxProcessorProvenanceStore`'s 3-arg constructor — actually exist as written).

- [ ] **Step 4: Commit**

```bash
git add src/Frank.Provenance/README.md
git commit -m "docs(frank-provenance): document the persistence extensibility hook"
```

---

## Self-Review Notes

- **Spec coverage**: `IProvenanceJournal` contract (Task 1) ✓, constructor-injected attachment not decorator (Task 3, Step 4) ✓, store-owned `SnapshotEvery` trigger (Task 2 + Task 3 Step 4) ✓, N-Quads via existing dotNetRdf.Core, no new package (Task 1 Step 5) ✓, manifest + immutable versioned segments, nothing deleted (Task 1 Step 5, asserted in Task 1 Step 8's test) ✓, recovery on construction (Task 3 Step 4's `recoveredEntries`) ✓, fire-and-forget `Append` (unchanged — `agent.Post`, never `PostAndReply`) ✓, journal-failure isolation (Task 3 Step 8) ✓, `SnapshotEvery <= 0` clamp (Task 3 Step 4's `max 1 config.SnapshotEvery`) ✓, missing-manifest-reference raises (Task 1 Step 11) ✓, no-journal parity (Task 3 Step 2) ✓, README documentation (Task 4) ✓. Aggregation layer and segment retention/GC are explicit spec non-goals — no task for either, correctly.
- **Placeholder scan**: no TBD/TODO markers; every step has real, complete code.
- **Type consistency**: `IProvenanceJournal.Append: graph: IGraph -> unit` used identically in Task 1's implementation and Task 3's `j.Append(namedGraph :> IGraph)`/test double. `Snapshot: graphs: IGraph seq -> unit` matches `seq { for g in store.Graphs -> g }` at the Task 3 call site and the test double's `member _.Snapshot(_: IGraph seq) = ()`. `Recover: unit -> IGraph seq` matches `j.Recover() |> List.ofSeq` in Task 3. `FileProvenanceJournal(baseDirectory: string, actorId: string)` used consistently across Task 1 and Task 3's tests. `ProvenanceStoreConfig.SnapshotEvery: int` matches `config.SnapshotEvery` in Task 3.
