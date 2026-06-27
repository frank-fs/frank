module Frank.Provenance.Tests.StoreTests

open System
open Expecto
open Microsoft.Extensions.Logging.Abstractions
open Frank.Semantic
open Frank.Provenance

let private mk id resource =
    { Id = id
      ResourceUri = resource
      HttpMethod = "POST"
      StatusCode = 201
      DomainType = None
      Agent = { Id = "urn:agent:a"; Label = None }
      StartedAt = DateTimeOffset.UnixEpoch
      EndedAt = DateTimeOffset.UnixEpoch }

[<Tests>]
let tests =
    testList
        "MailboxProcessorProvenanceStore"
        [ test "append then query by resource returns the record" {
              use store =
                  new MailboxProcessorProvenanceStore(ProvenanceStoreConfig.defaults, NullLogger.Instance)

              (store :> IProvenanceStore).Append(mk "a1" "/orders/1")
              let got = (store :> IProvenanceStore).QueryByResource "/orders/1"
              Expect.equal got.Length 1 "one record for the resource"
          }
          test "bounded eviction caps retained records" {
              let cfg =
                  { MaxRecords = 4
                    EvictionBatchSize = 2 }

              use store = new MailboxProcessorProvenanceStore(cfg, NullLogger.Instance)
              let s = store :> IProvenanceStore

              for i in 1..10 do
                  s.Append(mk (string i) "/r")

              Expect.isLessThanOrEqual (s.QueryByResource "/r").Length cfg.MaxRecords "never exceeds MaxRecords"
          } ]
