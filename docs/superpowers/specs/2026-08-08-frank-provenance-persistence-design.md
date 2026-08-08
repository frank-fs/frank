# Frank.Provenance: persistence extensibility hook

**Date**: 2026-08-08
**Branch**: `worktree-persistence` (not yet created)
**Status**: Draft — awaiting review

## Context

[Frank.Provenance's design](2026-08-02-frank-provenance-design.md) (frank-fs/frank#483) ships v1 with `MailboxProcessorProvenanceStore`: in-memory, bounded eviction, no durability — an intentional, scoped-down choice, `MailboxProcessor` treated as proof-of-concept-appropriate for the package's current scope (`[[project_actor_model_trajectory]]`: Akka.NET/Orleans/Proto.Actor is the stated long-term trajectory). frank-fs/frank#486 tracked giving it a durable option, framed as two competing directions: a parallel `SqliteProvenanceStore` implementing `IProvenanceStore` directly, or a persistence extensibility hook on the existing store.

This design picks the hook direction and works out its shape. See `provenance-persistence-decision.md` (private brainstorm doc, 2026-08-08) for the format rationale (N-Quads over SQLite/Parquet/DuckDB) that fed into it.

### Why the hook, not a parallel store

`MailboxProcessorProvenanceStore` is already the only `IProvenanceStore` implementation — there is no existing "default" to preserve alongside a new one; a second implementation would just be two stores to maintain. Other actor frameworks already ship their own persistence extensibility as part of the actor's own definition rather than as an alternate implementation swapped in for the live one — Akka.NET's `IPersistentActor` (event journal + periodic snapshot, same actor handles both live state and persistence) and Orleans' `IStorageProvider` (state read/write hooks on the grain). The hook follows that shape: `MailboxProcessorProvenanceStore` stays the one live-query implementation; persistence becomes an opt-in constructor dependency, not a fork.

### Reference specifications

| Spec | Version | Media type |
|---|---|---|
| N-Quads | [W3C Recommendation](https://www.w3.org/TR/n-quads/) | `application/n-quads` |
| PROV-O | inherited from [Frank.Provenance](2026-08-02-frank-provenance-design.md) | — |

## Goals

1. Give `MailboxProcessorProvenanceStore` an opt-in durability path: appended records survive process restart when a journal is attached, with zero behavior change and zero cost when one isn't.
2. Event-sourced journal + periodic snapshot, matching Akka.NET's `IPersistentActor` shape: every `Append` durably logged (fire-and-forget, non-blocking), full state snapshotted periodically so recovery only replays what's since the last snapshot.
3. N-Quads as the on-disk format for both journal entries and snapshots — native RDF serialization, no impedance mismatch with the in-memory `TripleStore`.
4. Recovery on startup: load the latest snapshot, replay journal entries recorded after it, resume serving queries.
5. A pluggable `IProvenanceJournal` contract, so the default file-backed implementation isn't the only possible one, without committing to what else might implement it.

## Non-goals

- **A parallel `IProvenanceStore` implementation** (e.g. `SqliteProvenanceStore`). Rejected direction — see *Why the hook, not a parallel store*.
- **Aggregation layer** (central repository pulling N-Quads from actors for cross-actor analytics/leaderboards). Decoupled concern, future issue — this design covers only per-actor durability.
- **Retention/garbage collection of superseded journal segments.** Segments folded into a snapshot aren't deleted by this design (see *File layout*); a GC pass is a separate, later concern, consistent with provenance being append-only/audit-trail by nature.
- **SQL or SPARQL-over-disk query layer.** Queries continue to run in-memory via `LeviathanQueryProcessor`/`InMemoryDataset`, unchanged from v1 — the journal is durability-only, never in the query path.
- **Transactional/synchronous durability guarantees.** `Append` is fire-and-forget; a crash between an `Append` returning and its journal write landing loses that one record. Acceptable for the same reason the original decision doc rejected SQLite's transaction overhead for an append-only workload.

## The design

### `IProvenanceJournal`

```fsharp
type IProvenanceJournal =
    /// Durably logs one named graph. Fire-and-forget from the caller's perspective -- the store
    /// posts this and continues; failure is the journal implementation's own concern to log/retry,
    /// never propagated back into the store's mailbox loop.
    abstract Append: graph: IGraph -> unit

    /// Compacts the given graphs (the store's full current state at the moment of the call) into a
    /// new snapshot, and folds any journal segments made redundant by it. The store decides *when*
    /// to call this (see Snapshot trigger); the journal decides *how* to persist it.
    abstract Snapshot: graphs: IGraph seq -> unit

    /// Reads the current manifest, loads the latest snapshot plus every journal segment recorded
    /// since it, and returns the merged graph set. Called once, at store construction, before the
    /// mailbox starts serving Append/Query.
    abstract Recover: unit -> IGraph seq
```

Pure mechanics — no trigger policy lives here. This keeps the journal swappable (a future non-file-backed implementation only has to satisfy this contract) without the store caring how persistence happens underneath it.

### Attachment: constructor-injected, not a decorator

```fsharp
type MailboxProcessorProvenanceStore(config: ProvenanceStoreConfig, logger: ILogger, ?journal: IProvenanceJournal) =
    ...
```

Same shape as the existing `config`/`logger` constructor dependencies — not a new layer of indirection. A wrapping `IProvenanceStore` decorator was considered and rejected: it would add an actual extra dispatch layer between caller and store, and recovery would have to replay through the store's own `Append` (re-deriving graph names, re-running `graphNameFor`) rather than loading graphs directly. Constructor injection lets recovery merge graphs straight into the store's internal `TripleStore` once, before the mailbox loop starts.

When `journal` is `None`, behavior is byte-for-byte what v1 ships today.

### Mailbox loop changes

On `Append` (after the existing named-graph construction, `MailboxProcessorProvenanceStore.fs:54-57`):

```fsharp
journal |> Option.iter (fun j -> j.Append(namedGraph))

let appendCount = appendCount + 1
if appendCount % config.SnapshotEvery = 0 then
    journal |> Option.iter (fun j -> j.Snapshot(store.Graphs))
```

Both calls are posts into the journal's own fire-and-forget path (`IProvenanceJournal.Append`/`Snapshot` don't block the mailbox loop waiting on disk I/O) — the journal implementation owns whatever async/background mechanism it needs internally.

At construction, before `MailboxProcessor.Start`:

```fsharp
let recovered = journal |> Option.map (fun j -> j.Recover()) |> Option.defaultValue Seq.empty
for g in recovered do store.Add(g, true) |> ignore
```

### Snapshot trigger: store-owned policy

`ProvenanceStoreConfig` gains one field:

```fsharp
type ProvenanceStoreConfig =
    { MaxRecords: int
      EvictionBatchSize: int
      /// Number of Append calls between snapshots, when a journal is attached. Ignored when no
      /// journal is present. A journal-swallowing implementation could still choose to snapshot on
      /// its own schedule internally, but the default (file-backed) implementation defers entirely
      /// to this count -- see Snapshot trigger.
      SnapshotEvery: int }

module ProvenanceStoreConfig =
    let defaults = { MaxRecords = 1000; EvictionBatchSize = 100; SnapshotEvery = 100 }
```

`SnapshotEvery = 100` matches `EvictionBatchSize`'s existing scale and gives ~10 snapshots' worth of headroom before `MaxRecords`-driven eviction starts mattering. Count-based, not time-based — no timer dependency added to the mailbox loop.

The journal never decides *when* to snapshot; it only knows how, given a graph set handed to it. This mirrors the earlier lock: trigger policy is the actor's (store's) concern, not the hook's.

### File layout: immutable segments + manifest

Modeled on modern table-format lakes (Delta Lake, Iceberg, DuckLake): immutable, versioned data files, never overwritten in place, plus a small manifest that names which files are current. Rejected alternatives:

- **Single growing/truncated journal file** — simplest, but truncating on snapshot silently discards the pre-consolidation record of what happened, which sits uneasily with provenance being an audit trail.
- **Versioned files with no manifest** — avoids truncation but pushes "which files are current" logic into filename parsing/globbing at every recovery.

Default `IProvenanceJournal` implementation, per actor (`{actorId}` = the store's identity, however that's threaded in — out of scope here, assumed already available from the surrounding actor infrastructure):

```
{actorId}.manifest.json          -- { latestSnapshot: int; journalSegmentsSince: int list }
{actorId}.snapshot.{seq}.nq      -- one per Snapshot call, never overwritten
{actorId}.journal.{seq}.nq       -- one per... (see below)
```

Open sub-question this design leaves to implementation, not policy: whether each `Append` gets its own journal segment file (`seq` increments per-record) or records batch into a rolling segment that closes and rotates on some size/count threshold. Either is a valid `IProvenanceJournal.Append` implementation; the interface doesn't care. Start with per-record segments (simplest correct thing) and revisit if file-count overhead proves to matter.

`Snapshot(graphs)`:
1. Writes `{actorId}.snapshot.{nextSeq}.nq` (all graphs, full dump).
2. Writes a new manifest: `latestSnapshot = nextSeq`, `journalSegmentsSince = []` (nothing appended yet since this snapshot).
3. Segments superseded by the new snapshot are left on disk — not deleted. Retention/GC is a non-goal (see above).

`Append(graph)`, when a journal is attached:
1. Writes the next journal segment file.
2. Updates the manifest's `journalSegmentsSince` to include it.

`Recover()`:
1. Reads the manifest. No manifest (fresh actor) → empty.
2. Parses `{actorId}.snapshot.{latestSnapshot}.nq` into graphs.
3. Parses each `{actorId}.journal.{seq}.nq` in `journalSegmentsSince`, in order, into graphs.
4. Returns the concatenation.

N-Quads read/write uses dotNetRDF's existing `VDS.RDF.Writing.NQuadsWriter`/`VDS.RDF.Parsing.NQuadsParser` (already available transitively via `dotNetRdf.Core`, the same package `Frank.Rdf`/`Frank.Provenance` already depend on — no new NuGet reference).

## Error handling and edge cases

| Situation | Behaviour |
|---|---|
| No journal attached | Store behaves exactly as v1 — no durability, no cost. |
| Journal's `Append` throws/fails internally | Journal's own concern to log and continue; must never propagate into the mailbox loop (same "don't kill the mailbox" discipline as the existing malformed-record handling, `MailboxProcessorProvenanceStore.fs:41-46`). |
| Crash between `Append` returning and its journal write landing | That one record is lost on recovery — accepted, fire-and-forget is the explicit trade-off (see Non-goals). |
| Manifest present but a referenced segment/snapshot file missing | Journal's `Recover` responsibility; default implementation raises — a missing file it was told exists is corruption, not a recoverable gap, and recovery happening once at startup (not mid-flight) means failing loudly here is safe. |
| `SnapshotEvery <= 0` | Same defensive-clamp discipline as `MaxRecords`/`EvictionBatchSize` — never divide/mod by a non-positive value; treat as "snapshot every Append" at minimum, never crash the loop. |
| Fresh actor, no manifest yet | `Recover()` returns empty; store starts as if no journal were attached, until the first `Append`/`Snapshot`. |

## Testing

Mirrors `Frank.Provenance`'s existing test pattern (`test/Frank.Provenance.Tests`):

- **Round-trip**: append N records with a journal attached, construct a fresh store pointed at the same manifest, assert recovered graphs are isomorphic to what was appended (same pattern as Frank.Rdf's JSON-LD round-trip tests).
- **Snapshot + replay**: append past `SnapshotEvery`, assert a snapshot file was written and the manifest's `journalSegmentsSince` reset; append more, recover, assert both snapshot and post-snapshot segments contribute.
- **No-journal parity**: construct with `journal = None`, assert identical `Append`/`Query` behavior to v1 (no manifest/segment files written anywhere).
- **Fire-and-forget**: `Append` returns before the journal write is guaranteed durable (assert non-blocking, not assert timing).
- **Journal failure isolation**: a journal whose `Append` throws doesn't kill the mailbox loop — subsequent `Append`/`Query` calls still work (same style as the existing malformed-record test).
- **Corrupt/missing manifest reference**: `Recover` raises when a referenced file is absent.

## Future work (separate)

- **Aggregation layer** — central repository pulling N-Quads from actors for cross-actor analytics/leaderboards. Push vs. pull and frequency undecided; explicitly out of scope here.
- **Retention/GC** of superseded journal segments and old snapshots.
- **Journal segment batching** — whether per-record files prove to be a real overhead at higher append volumes than this package's demo scope, and if so, rolling-segment rotation.
- **Non-file-backed `IProvenanceJournal` implementations** — the interface doesn't assume file-backed; not building a second one until there's a real need.

## Sources

- [Frank.Provenance design](2026-08-02-frank-provenance-design.md) — the store this hook attaches to.
- frank-fs/frank#486 — durable store issue this design resolves.
- W3C N-Quads: https://www.w3.org/TR/n-quads/
- `provenance-persistence-decision.md` (private brainstorm doc, 2026-08-08) — format rationale (N-Quads vs. SQLite/Parquet/DuckDB).
- `[[project_actor_model_trajectory]]` — MailboxProcessor's stated proof-of-concept framing.
