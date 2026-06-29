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
      EndedAt = DateTimeOffset(2026, 6, 27, 0, 0, 1, TimeSpan.Zero)
      BodyAttributes = [] }

[<Tests>]
let tests =
    testList
        "ProvenanceGraph"
        [ test "typed Activity carries domain IRI + prov:Activity + Agent" {
              let g =
                  ProvenanceGraph.toJsonLd (rec0 (Some(ProvOClass.Activity, Uri "https://schema.org/OrderAction")))

              Expect.stringContains g "prov:Activity" "CURIE prov:Activity proves compaction"
              Expect.stringContains g "https://schema.org/OrderAction" "domain IRI stays full (no schema: prefix)"
              Expect.stringContains g "wasAssociatedWith" "agent association present"
          }
          test "untyped Activity omits any domain IRI but is still prov:Activity" {
              let g = ProvenanceGraph.toJsonLd (rec0 None)
              Expect.stringContains g "Activity" "still an Activity"
              Expect.isFalse (g.Contains "schema.org/OrderAction") "no domain IRI when DomainType None"
          }

          test "body attributes appear as IRI-keyed properties on activity node" {
              let r =
                  { rec0 (Some(ProvOClass.Activity, Uri "https://schema.org/OrderAction")) with
                      BodyAttributes =
                        [ "https://schema.org/agent", "alice"
                          "https://schema.org/object", "order-1" ] }

              let g = ProvenanceGraph.toJsonLd r
              Expect.stringContains g "schema.org/agent" "schema:agent IRI in body attrs"
              Expect.stringContains g "alice" "schema:agent value in body attrs"
              Expect.stringContains g "schema.org/object" "schema:object IRI in body attrs"
          } ]
