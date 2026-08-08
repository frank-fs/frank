namespace Frank.Provenance

open System
open Microsoft.Extensions.Logging

/// The v1, in-memory IProvenanceStore: one dotNetRDF TripleStore holding one named graph per
/// appended record, queried via SPARQL over the whole store's union graph, with bounded eviction
/// of the oldest records once ProvenanceStoreConfig.MaxRecords is exceeded.
[<Sealed>]
type MailboxProcessorProvenanceStore =
    /// journal is an opt-in durability hook (see IProvenanceJournal). When None (the default),
    /// behavior is unchanged from the in-memory-only v1: no recovery on construction, no snapshot
    /// calls, Append incurs no journal-write cost.
    new: config: ProvenanceStoreConfig * logger: ILogger * ?journal: IProvenanceJournal -> MailboxProcessorProvenanceStore

    interface IProvenanceStore
    interface IDisposable
