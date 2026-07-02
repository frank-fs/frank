namespace Frank.Provenance

open System

type ProvAgent = { Id: string; Label: string option }

type BodyAttributeValue =
    | Literal of string
    | IriNode of string

type ProvenanceRecord =
    { Id: string
      ResourceUri: string
      HttpMethod: string
      StatusCode: int
      DomainType: (Frank.Semantic.ProvOClass * Uri) option
      Agent: ProvAgent
      StartedAt: DateTimeOffset
      EndedAt: DateTimeOffset
      BodyAttributes: (string * BodyAttributeValue) list }

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
      PropertyClassRanges: Map<string, string>
      DeclaredPrefixes: (string * string) list
      StoreConfig: ProvenanceStoreConfig
      MaxBodyBytes: int64 }

module ProvenanceConfig =
    let defaultMaxBodyBytes: int64 = Frank.RequestBodyBuffer.defaultMaxBodyBytes
