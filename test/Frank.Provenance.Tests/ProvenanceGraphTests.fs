module Frank.Provenance.Tests.ProvenanceGraphTests

open System
open Expecto
open Frank.Semantic
open Frank.Provenance

let private rec0 dt =
    { Id = "urn:uuid:act-1"
      ResourceUri = "/orders/1"
      HttpMethod = "POST"
      StatusCode = 201
      DomainType = dt
      Agent =
        { Id = "urn:agent:alice"
          Label = Some "alice" }
      StartedAt = DateTimeOffset(2026, 6, 27, 0, 0, 0, TimeSpan.Zero)
      EndedAt = DateTimeOffset(2026, 6, 27, 0, 0, 1, TimeSpan.Zero) }

[<Tests>]
let tests =
    testList
        "ProvenanceGraph"
        [ test "typed Activity carries domain IRI + prov:Activity + Agent" {
              let g =
                  ProvenanceGraph.toJsonLd (rec0 (Some(ProvOClass.Activity, Uri "https://schema.org/OrderAction")))

              Expect.stringContains g "Activity" "prov:Activity present"
              Expect.stringContains g "https://schema.org/OrderAction" "domain IRI present"
              Expect.stringContains g "wasAssociatedWith" "agent association present"
          }
          test "untyped Activity omits any domain IRI but is still prov:Activity" {
              let g = ProvenanceGraph.toJsonLd (rec0 None)
              Expect.stringContains g "Activity" "still an Activity"
              Expect.isFalse (g.Contains "schema.org/OrderAction") "no domain IRI when DomainType None"
          } ]
