namespace Frank.Provenance

open System

type ProvAgent = { Id: string; Label: string option }

type ProvenanceRecord =
    { Id: string
      ResourceUri: string
      HttpMethod: string
      StatusCode: int
      DomainType: (Frank.Semantic.ProvOClass * Uri) option
      Agent: ProvAgent
      StartedAt: DateTimeOffset
      EndedAt: DateTimeOffset }

type ProvenanceStoreConfig =
    { MaxRecords: int
      EvictionBatchSize: int }

module ProvenanceStoreConfig =
    let defaults =
        { MaxRecords = 10_000
          EvictionBatchSize = 100 }

type ProvenanceConfig =
    { ProvClasses: Map<string, Frank.Semantic.ProvOClass * Uri option>
      KnownNamespaces: string[]
      StoreConfig: ProvenanceStoreConfig }
