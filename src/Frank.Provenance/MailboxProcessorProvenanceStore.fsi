namespace Frank.Provenance

open System
open Microsoft.Extensions.Logging

/// The v1, in-memory IProvenanceStore: one dotNetRDF TripleStore holding one named graph per
/// appended record, queried via SPARQL over the whole store's union graph, with bounded eviction
/// of the oldest records once ProvenanceStoreConfig.MaxRecords is exceeded.
[<Sealed>]
type MailboxProcessorProvenanceStore =
    new: config: ProvenanceStoreConfig * logger: ILogger -> MailboxProcessorProvenanceStore

    interface IProvenanceStore
    interface IDisposable
