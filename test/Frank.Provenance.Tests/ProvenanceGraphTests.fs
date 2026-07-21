module Frank.Provenance.Tests.ProvenanceGraphTests

open System
open System.Text.Json
open Expecto
open VDS.RDF
open Frank.Semantic
open Frank.Provenance

/// #424: counts how many times the graph's Triples collection is accessed, so tests can
/// prove a scan happens exactly once instead of asserting on wall-clock timing.
type private CountingGraph() =
    inherit Graph()
    let mutable triplesAccessCount = 0
    member _.TriplesAccessCount = triplesAccessCount

    override this.Triples =
        triplesAccessCount <- triplesAccessCount + 1
        base.Triples

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
                      BodyAttributes = [ squareIri, IriNode valueIri; "https://schema.org/agent", Literal "alice" ] }

              let g = ProvenanceGraph.toJsonLd r
              Expect.stringContains g "tictactoe#TopLeft" "URI node IRI present in JSON-LD"
              Expect.stringContains g "alice" "literal value still present"
              Expect.isFalse (g.Contains "\"TopLeft\"") "TopLeft must not appear as standalone plain literal"
          }

          test "#16 listToJsonLd with extra context injects schema and ttt into @context when both are used" {
              // Use a record with schema AND ttt body attributes so both extraContext
              // prefixes are actually used in the graph (post-hollow-decoration-fix
              // behavior: extraContext is filtered like declaredPrefixes/compactGraph).
              let r =
                  { rec0 None with
                      BodyAttributes =
                          [ "https://schema.org/actionStatus", Literal "Active"
                            "http://localhost/tictactoe#square", IriNode "http://localhost/tictactoe#TopLeft" ] }

              let extra =
                  [ "schema", "https://schema.org/"; "ttt", "http://localhost/tictactoe#" ]

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
                  | JsonValueKind.Array ->
                      ctx.EnumerateArray()
                      |> Seq.tryFind (fun e -> e.ValueKind = JsonValueKind.Object)
                      |> Option.defaultWith (fun () -> Unchecked.defaultof<JsonElement>)
                  | _ -> Unchecked.defaultof<JsonElement>

              Expect.isTrue (ctxObj.TryGetProperty("schema", &schemaEl)) "@context has 'schema' prefix"
              Expect.isTrue (ctxObj.TryGetProperty("ttt", &tttEl)) "@context has 'ttt' prefix"
              Expect.equal (schemaEl.GetString()) "https://schema.org/" "schema value is schema.org/"
              // #16 real compaction: schema body attr must appear as compacted CURIE, not full IRI.
              Expect.stringContains
                  json
                  "schema:actionStatus"
                  "schema:actionStatus must be compacted when schema in extraContext"

              Expect.isFalse
                  (json.Contains "\"https://schema.org/actionStatus\"")
                  "full schema.org property IRI must not appear as JSON key after compaction"
          }

          test
              "#424 AC1: usedContextEntries scans the graph's triples exactly once for combined prov+declared filtering" {
              let g = new CountingGraph()
              let rdfType = g.CreateUriNode(Uri "http://www.w3.org/1999/02/22-rdf-syntax-ns#type")
              let activity = g.CreateUriNode(Uri "urn:uuid:act-1")
              let provActivity = g.CreateUriNode(Uri(ProvVocabulary.Namespace + "Activity"))
              g.Assert(Triple(activity, rdfType, provActivity)) |> ignore
              let schemaAgent = g.CreateUriNode(Uri "https://schema.org/agent")
              let aliceLit = g.CreateLiteralNode "alice"
              g.Assert(Triple(activity, schemaAgent, aliceLit)) |> ignore

              let entries =
                  ProvenanceGraph.usedContextEntries [ "schema", "https://schema.org/" ] g

              Expect.equal
                  g.TriplesAccessCount
                  1
                  "graphUriNodes-equivalent triple walk must run exactly once, not once per prefix set"

              Expect.contains entries ("prov", ProvVocabulary.Namespace) "prov prefix present (used via rdf:type)"
              Expect.contains entries ("schema", "https://schema.org/") "schema prefix present (used via schema:agent)"
          }

          test "#424: compactGraph's declaredPrefixes are filtered to only those used in the graph" {
              let g = new Graph() :> IGraph
              let activity = g.CreateUriNode(Uri "urn:uuid:act-1")
              let schemaAgent = g.CreateUriNode(Uri "https://schema.org/agent")
              let aliceLit = g.CreateLiteralNode "alice"
              g.Assert(Triple(activity, schemaAgent, aliceLit)) |> ignore

              let json =
                  ProvenanceGraph.compactGraph
                      [ "schema", "https://schema.org/"
                        "wikidata", "http://www.wikidata.org/entity/" ]
                      g

              Expect.stringContains json "schema:agent" "used schema prefix compacts schema:agent"
              Expect.isFalse (json.Contains "wikidata") "unused wikidata prefix must be filtered out"
          }

          test
              "extraContext entries unused in the graph are filtered out (converges compact with compactGraph's #424 discipline)" {
              // Body attribute only uses schema -- ttt is declared in extraContext but never
              // used in the produced graph, so it must be filtered out just like an unused
              // declaredPrefixes entry is in compactGraph (#424).
              let r =
                  { rec0 None with
                      BodyAttributes = [ "https://schema.org/actionStatus", Literal "Active" ] }

              let extra =
                  [ "schema", "https://schema.org/"; "ttt", "http://localhost/tictactoe#" ]

              let json = ProvenanceGraph.toJsonLdWith extra r

              Expect.stringContains json "schema:actionStatus" "used schema prefix still compacts"

              Expect.isFalse
                  (json.Contains "ttt")
                  "unused ttt extraContext prefix must be filtered out, matching compactGraph's discipline"
          } ]
