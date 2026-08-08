namespace Frank.Provenance

open Microsoft.Extensions.Logging
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
/// `{actorId}.manifest.json` pointer file. Segment and snapshot files are never overwritten or
/// deleted -- see docs/superpowers/specs/2026-08-08-frank-provenance-persistence-design.md for why.
/// (The manifest itself is replaced on every write, atomically, since it is the pointer into them.)
[<Sealed>]
type FileProvenanceJournal =
    /// `logger` is optional because Append/Snapshot are fire-and-forget: a write failure is this
    /// journal's own concern to absorb, never something it propagates back to the store, so there
    /// is no other channel by which a caller could learn a write failed. Omitting it substitutes
    /// NullLogger, which means write failures are swallowed *silently* -- durability stops while
    /// the store keeps reporting success. That's the accepted v1 default (it keeps the two-argument
    /// construction working and matches the fire-and-forget contract), but supply a logger in
    /// production if you want any signal at all that the journal has stopped persisting.
    new: baseDirectory: string * actorId: string * ?logger: ILogger -> FileProvenanceJournal

    interface IProvenanceJournal

    /// Test-only synchronization barrier: blocks until every Append/Snapshot posted before this call
    /// has been durably written and the manifest updated. Production code never needs this --
    /// Recover only ever runs before a store starts serving traffic -- but a test that Appends/
    /// Snapshots then immediately constructs a fresh instance to Recover needs to know the
    /// fire-and-forget writes actually landed first.
    member internal Flush: unit -> unit
