module Frank.Provenance.Tests.ProvenanceGraphTests

open System
open System.Text.Json
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
                        [ "https://schema.org/agent", Literal "alice"
                          "https://schema.org/object", Literal "order-1" ] }

              let g = ProvenanceGraph.toJsonLd r
              Expect.stringContains g "schema.org/agent" "schema:agent IRI in body attrs"
              Expect.stringContains g "alice" "schema:agent value in body attrs"
              Expect.stringContains g "schema.org/object" "schema:object IRI in body attrs"
          }

          test "class-ranged body attribute emits URI node not plain literal (AC4)" {
              let squareIri = "http://localhost/tictactoe#square"
              let valueIri = "http://localhost/tictactoe#TopLeft"

              let r =
                  { rec0 (Some(ProvOClass.Activity, Uri "https://schema.org/OrderAction")) with
                      BodyAttributes =
                        [ squareIri, IriNode valueIri
                          "https://schema.org/agent", Literal "alice" ] }

              let g = ProvenanceGraph.toJsonLd r
              Expect.stringContains g "tictactoe#TopLeft" "URI node IRI present in JSON-LD"
              Expect.stringContains g "alice" "literal value still present"
              Expect.isFalse (g.Contains "\"TopLeft\"") "TopLeft must not appear as standalone plain literal"
          }

          test "#16 listToJsonLd with extra context injects schema and ttt into @context" {
              // Use a record with a schema body attribute so compaction is observable.
              let r =
                  { rec0 None with
                      BodyAttributes = [ "https://schema.org/actionStatus", Literal "Active" ] }

              let extra =
                  [ "schema", "https://schema.org/"
                    "ttt", "http://localhost/tictactoe#" ]

              let json = ProvenanceGraph.listToJsonLd extra [ r ]
              let mutable schemaEl = Unchecked.defaultof<JsonElement>
              let mutable tttEl = Unchecked.defaultof<JsonElement>
              use doc = JsonDocument.Parse json
              let root = doc.RootElement

              let ctx =
                  match root.ValueKind with
                  | JsonValueKind.Object -> root.GetProperty("@context")
                  | _ -> Unchecked.defaultof<JsonElement>

              let ctxObj =
                  match ctx.ValueKind with
                  | JsonValueKind.Object -> ctx
                  | JsonValueKind.Array -> ctx.EnumerateArray() |> Seq.tryFind (fun e -> e.ValueKind = JsonValueKind.Object) |> Option.defaultWith (fun () -> Unchecked.defaultof<JsonElement>)
                  | _ -> Unchecked.defaultof<JsonElement>

              Expect.isTrue (ctxObj.TryGetProperty("schema", &schemaEl)) "@context has 'schema' prefix"
              Expect.isTrue (ctxObj.TryGetProperty("ttt", &tttEl)) "@context has 'ttt' prefix"
              Expect.equal (schemaEl.GetString()) "https://schema.org/" "schema value is schema.org/"
              // #16 real compaction: schema body attr must appear as compacted CURIE, not full IRI.
              Expect.stringContains json "schema:actionStatus" "schema:actionStatus must be compacted when schema in extraContext"

              Expect.isFalse
                  (json.Contains "\"https://schema.org/actionStatus\"")
                  "full schema.org property IRI must not appear as JSON key after compaction"
          } ]
