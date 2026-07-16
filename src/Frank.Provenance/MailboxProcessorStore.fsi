namespace Frank.Provenance

open System
open Microsoft.Extensions.Logging

type MailboxProcessorProvenanceStore =
    new: config: ProvenanceStoreConfig * logger: ILogger -> MailboxProcessorProvenanceStore

    interface IProvenanceStore
    interface IDisposable
