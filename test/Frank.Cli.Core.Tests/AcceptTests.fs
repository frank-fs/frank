module Frank.Cli.Core.Tests.AcceptTests

open Expecto
open Frank.Semantic
open Frank.Semantic.LockFile
open Frank.Cli.Core

let private emptyOracle: Accept.TermOracle =
    { Classes = Set.empty
      Properties = Set.empty
      Individuals = Set.empty
      CoveredBases = [] }

// ── Fixtures ──────────────────────────────────────────────────────────────────

let private emptyVocabs: Map<string, VocabularyEntry> = Map.empty

let private schemaVocabs: Map<string, VocabularyEntry> =
    Map.ofList
        [ "schema",
          { Uri = "https://schema.org/"
            FetchedAt = System.DateTimeOffset.UnixEpoch
            Hash = "stub" } ]

let private mkLock (mappings: Mapping list) : LockFile =
    { SchemaVersion = 1
      Generated = System.DateTimeOffset.UnixEpoch
      Vocabularies = emptyVocabs
      Mappings = mappings }

let private mkLockWithVocabs (mappings: Mapping list) : LockFile =
    { SchemaVersion = 1
      Generated = System.DateTimeOffset.UnixEpoch
      Vocabularies = schemaVocabs
      Mappings = mappings }

let private unresolvedOrderLine: Mapping =
    { FSharpType = "MyApp.OrderLine"
      Iri = None
      Confidence = 0.0
      Source = Convention
      Status = Unresolved
      Alternates = []
      Shape = MappingShape.Record [] }

let private unresolvedOrder: Mapping =
    { FSharpType = "MyApp.Order"
      Iri = None
      Confidence = 0.0
      Source = Convention
      Status = Unresolved
      Alternates = []
      Shape =
        MappingShape.Record
            [ { Name = "Total"
                Iri = None
                Confidence = 0.0
                Source = Convention
                Status = Unresolved } ] }

let private at1Json =
    """{"schemaVersion":1,"resolved":[{"fsharpType":"MyApp.OrderLine","iri":"schema:OrderItem","fields":[]}]}"""

let private at3Json =
    """{"schemaVersion":1,"resolved":[{"fsharpType":"MyApp.Order","iri":"schema:Order","fields":[]},{"fsharpType":"MyApp.Nonexistent","iri":"schema:Thing","fields":[]}]}"""

let private versionMismatchJson = """{"schemaVersion":99,"resolved":[]}"""

// ── Tests ─────────────────────────────────────────────────────────────────────

[<Tests>]
let acceptTests =
    testList
        "Accept"
        [ testCase "AT1: known type resolved — status Confirmed, source Llm, iri set"
          <| fun () ->
              let lock = mkLockWithVocabs [ unresolvedOrderLine ]

              match Accept.parseResolved at1Json with
              | Error e -> failtest $"parseResolved failed: {e}"
              | Ok doc ->
                  let updated, summary = Accept.apply lock doc Llm emptyOracle
                  Expect.equal summary.Merged 1 "Merged count"
                  Expect.equal summary.Rejected [] "no rejections"
                  Expect.equal summary.Unchanged 0 "unchanged count"

                  let m = updated.Mappings |> List.find (fun m -> m.FSharpType = "MyApp.OrderLine")
                  Expect.equal m.Iri (Some "schema:OrderItem") "iri"
                  Expect.equal m.Status Confirmed "status"
                  Expect.equal m.Source Llm "source"

          testCase "AT2: schema version 99 — Error with version message"
          <| fun () ->
              match Accept.parseResolved versionMismatchJson with
              | Ok _ -> failtest "expected Error for schema version 99"
              | Error msg -> Expect.stringContains msg "schema version 99 not supported" "error message"

          testCase "AT3: unknown type rejected, not appended"
          <| fun () ->
              let lock = mkLockWithVocabs [ unresolvedOrder ]

              match Accept.parseResolved at3Json with
              | Error e -> failtest $"parseResolved failed: {e}"
              | Ok doc ->
                  let updated, summary = Accept.apply lock doc Llm emptyOracle
                  Expect.equal summary.Merged 1 "Merged count"
                  let rejectedTypes = summary.Rejected |> List.map (fun r -> r.FSharpType)
                  Expect.equal rejectedTypes [ "MyApp.Nonexistent" ] "rejected types"
                  let rejectedReasons = summary.Rejected |> List.map (fun r -> r.Reason)
                  Expect.equal rejectedReasons [ "not in lock file" ] "rejected reasons"

                  let hasNonexistent =
                      updated.Mappings |> List.exists (fun m -> m.FSharpType = "MyApp.Nonexistent")

                  Expect.isFalse hasNonexistent "unknown type must not be appended"

          testCase "source manual — merged entry has Source=Manual"
          <| fun () ->
              let lock = mkLockWithVocabs [ unresolvedOrderLine ]

              match Accept.parseResolved at1Json with
              | Error e -> failtest $"parseResolved failed: {e}"
              | Ok doc ->
                  let updated, _ = Accept.apply lock doc Manual emptyOracle
                  let m = updated.Mappings |> List.find (fun m -> m.FSharpType = "MyApp.OrderLine")
                  Expect.equal m.Source Manual "source must be Manual"

          testCase "null-iri: known type with iri:null in resolved — rejected, lock unchanged"
          <| fun () ->
              let nullIriJson =
                  """{"schemaVersion":1,"resolved":[{"fsharpType":"MyApp.Order","iri":null,"fields":[]}]}"""

              let lock = mkLock [ unresolvedOrder ]

              match Accept.parseResolved nullIriJson with
              | Error e -> failtest $"parseResolved failed: {e}"
              | Ok doc ->
                  let updated, summary = Accept.apply lock doc Llm emptyOracle
                  Expect.equal summary.Merged 0 "nothing merged"
                  Expect.equal (summary.Rejected |> List.length) 1 "one rejection"
                  let rej = summary.Rejected |> List.head
                  Expect.equal rej.FSharpType "MyApp.Order" "rejected type"
                  Expect.stringContains rej.Reason "iri is required" "reason mentions iri"
                  let m = updated.Mappings |> List.find (fun m -> m.FSharpType = "MyApp.Order")
                  Expect.equal m.Status Unresolved "lock entry unchanged"

          testCase "field null-iri: type iri present, field iri null — field stays Unresolved"
          <| fun () ->
              let fieldNullIriJson =
                  """{"schemaVersion":1,"resolved":[{"fsharpType":"MyApp.Order","iri":"schema:Order","fields":[{"name":"Total","iri":null}]}]}"""

              let lock = mkLockWithVocabs [ unresolvedOrder ]

              match Accept.parseResolved fieldNullIriJson with
              | Error e -> failtest $"parseResolved failed: {e}"
              | Ok doc ->
                  let updated, summary = Accept.apply lock doc Llm emptyOracle
                  Expect.equal summary.Merged 1 "one merged"
                  Expect.isTrue (summary.FieldsUnresolved >= 1) "at least one field unresolved"
                  let m = updated.Mappings |> List.find (fun m -> m.FSharpType = "MyApp.Order")
                  Expect.equal m.Status Confirmed "type is Confirmed"

                  let totalField =
                      MappingShape.payloadFields m.Shape |> List.tryFind (fun f -> f.Name = "Total")

                  match totalField with
                  | None -> failtest "Total field missing"
                  | Some f -> Expect.equal f.Status Unresolved "Total field stays Unresolved"

          testCase "alreadyConfirmed: re-confirming an already-Confirmed entry"
          <| fun () ->
              let alreadyConfirmed: Mapping =
                  { FSharpType = "MyApp.OrderLine"
                    Iri = Some "schema:OrderItem"
                    Confidence = 1.0
                    Source = Llm
                    Status = Confirmed
                    Alternates = []
                    Shape = MappingShape.Record [] }

              let lock = mkLockWithVocabs [ alreadyConfirmed ]

              match Accept.parseResolved at1Json with
              | Error e -> failtest $"parseResolved failed: {e}"
              | Ok doc ->
                  let _updated, summary = Accept.apply lock doc Llm emptyOracle
                  Expect.equal summary.Merged 1 "Merged=1"
                  Expect.equal summary.AlreadyConfirmed 1 "AlreadyConfirmed=1"

          testCase "robustness: no schemaVersion — Error schemaVersion is required"
          <| fun () ->
              let json = """{"resolved":[]}"""

              match Accept.parseResolved json with
              | Ok _ -> failtest "expected Error"
              | Error msg -> Expect.stringContains msg "schemaVersion is required" "error message"

          testCase "robustness: root array — Error root must be a JSON object"
          <| fun () ->
              let json = """[]"""

              match Accept.parseResolved json with
              | Ok _ -> failtest "expected Error"
              | Error msg -> Expect.stringContains msg "root must be a JSON object" "error message"

          testCase "robustness: resolved is object not array — Error not exception"
          <| fun () ->
              let json = """{"schemaVersion":1,"resolved":{}}"""

              match Accept.parseResolved json with
              | Ok _ -> failtest "expected Error"
              | Error _ -> ()

          testCase "robustness: entry fields is string not array — Error not exception"
          <| fun () ->
              let json =
                  """{"schemaVersion":1,"resolved":[{"fsharpType":"MyApp.X","iri":"schema:X","fields":"x"}]}"""

              match Accept.parseResolved json with
              | Ok _ -> failtest "expected Error"
              | Error _ -> ()

          testCase "FIX2: absolute iri rejected at accept time — not silently merged"
          <| fun () ->
              let absoluteIriJson =
                  """{"schemaVersion":1,"resolved":[{"fsharpType":"MyApp.OrderLine","iri":"https://schema.org/MoveAction","fields":[]}]}"""

              let lock = mkLockWithVocabs [ unresolvedOrderLine ]

              match Accept.parseResolved absoluteIriJson with
              | Error e -> failtest $"parseResolved failed: {e}"
              | Ok doc ->
                  let updated, summary = Accept.apply lock doc Llm emptyOracle
                  Expect.equal summary.Merged 0 "absolute iri must not be merged"

                  let rej =
                      summary.Rejected |> List.tryFind (fun r -> r.FSharpType = "MyApp.OrderLine")

                  Expect.isSome rej "OrderLine must appear in Rejected"
                  Expect.stringContains rej.Value.Reason "https://schema.org/MoveAction" "reason mentions the iri"

                  let m = updated.Mappings |> List.tryFind (fun m -> m.FSharpType = "MyApp.OrderLine")

                  Expect.isSome m "lock entry must still exist"
                  Expect.equal m.Value.Status Unresolved "lock entry unchanged (not merged)"

          testCase "FIX2: valid CURIE iri merged correctly — not rejected"
          <| fun () ->
              let curieIriJson =
                  """{"schemaVersion":1,"resolved":[{"fsharpType":"MyApp.OrderLine","iri":"schema:MoveAction","fields":[]}]}"""

              let lock = mkLockWithVocabs [ unresolvedOrderLine ]

              match Accept.parseResolved curieIriJson with
              | Error e -> failtest $"parseResolved failed: {e}"
              | Ok doc ->
                  let updated, summary = Accept.apply lock doc Llm emptyOracle
                  Expect.equal summary.Merged 1 "valid CURIE must be merged"
                  Expect.equal summary.Rejected [] "no rejections for valid CURIE"
                  let m = updated.Mappings |> List.find (fun m -> m.FSharpType = "MyApp.OrderLine")
                  Expect.equal m.Status Confirmed "merged entry is Confirmed"
                  Expect.equal m.Iri (Some "schema:MoveAction") "iri stored as-is"

          testCase "json output: summaryToJson produces valid JSON with expected fields"
          <| fun () ->
              let summary: Accept.AcceptSummary =
                  { Merged = 2
                    Rejected =
                      [ { FSharpType = "MyApp.Ghost"
                          Reason = "not in lock file" } ]
                    Unchanged = 1
                    AlreadyConfirmed = 0
                    FieldsUnresolved = 3 }

              let json = Accept.summaryToJson summary
              let doc = System.Text.Json.JsonDocument.Parse(json)
              let root = doc.RootElement
              Expect.equal (root.GetProperty("merged").GetInt32()) 2 "merged"
              Expect.equal (root.GetProperty("unchanged").GetInt32()) 1 "unchanged"
              Expect.equal (root.GetProperty("alreadyConfirmed").GetInt32()) 0 "alreadyConfirmed"
              Expect.equal (root.GetProperty("fieldsUnresolved").GetInt32()) 3 "fieldsUnresolved"
              let rejected = root.GetProperty("rejected")
              Expect.equal (rejected.GetArrayLength()) 1 "rejected length"
              let r0 = rejected.[0]
              Expect.equal (r0.GetProperty("fsharpType").GetString()) "MyApp.Ghost" "fsharpType"
              Expect.equal (r0.GetProperty("reason").GetString()) "not in lock file" "reason"

          test "json output: summaryToJson round-trips reason containing quote and backslash" {
              let summary: Accept.AcceptSummary =
                  { Merged = 0
                    Rejected =
                      [ { FSharpType = "MyApp.Tricky"
                          Reason = "unresolvable iri \"schema:Foo\\bar\"" } ]
                    Unchanged = 0
                    AlreadyConfirmed = 0
                    FieldsUnresolved = 0 }

              let json = Accept.summaryToJson summary
              let doc = System.Text.Json.Nodes.JsonNode.Parse json
              let r0 = doc.["rejected"].[0]

              Expect.equal
                  (r0.["reason"].GetValue<string>())
                  "unresolvable iri \"schema:Foo\\bar\""
                  "round-trip preserves quotes and backslash"
          }

          test "accept a union investment produces a Union mapping with confirmed cases" {
              let unresolvedPayload =
                  [ { Name = "position"
                      Iri = None
                      Confidence = 0.0
                      Source = Convention
                      Status = Unresolved } ]

              let lock =
                  { SchemaVersion = 1
                    Generated = System.DateTimeOffset.Parse "2026-01-01T00:00:00Z"
                    Vocabularies =
                      Map.ofList
                          [ "schema",
                            { Uri = "https://schema.org/"
                              FetchedAt = System.DateTimeOffset.Parse "2026-01-01T00:00:00Z"
                              Hash = "h" } ]
                    Mappings =
                      [ { FSharpType = "App.Move"
                          Iri = Some "schema:MoveAction"
                          Confidence = 1.0
                          Source = Convention
                          Status = Confirmed
                          Alternates = []
                          Shape =
                            MappingShape.Union
                                [ { Name = "XMove"
                                    Iri = None
                                    Confidence = 0.0
                                    Source = Convention
                                    Status = Unresolved
                                    Payload = unresolvedPayload }
                                  { Name = "OMove"
                                    Iri = None
                                    Confidence = 0.0
                                    Source = Convention
                                    Status = Unresolved
                                    Payload = unresolvedPayload } ] } ] }

              let json =
                  """
                  { "schemaVersion": 1, "resolved": [
                    { "fsharpType": "App.Move", "iri": "schema:MoveAction",
                      "cases": [
                        { "name": "XMove", "iri": "schema:MoveAction", "payload": [ { "name": "position", "iri": "schema:position" } ] },
                        { "name": "OMove", "iri": "schema:MoveAction", "payload": [ { "name": "position", "iri": "schema:position" } ] } ] } ] }
                  """

              let doc = Expect.wantOk (Accept.parseResolved json) "parse"
              let updated, _ = Accept.apply lock doc Manual emptyOracle

              let move = updated.Mappings |> List.find (fun m -> m.FSharpType = "App.Move")

              match move.Shape with
              | MappingShape.Union cases ->
                  let x = cases |> List.find (fun c -> c.Name = "XMove")
                  Expect.equal x.Status Confirmed "XMove confirmed by investment"
                  Expect.equal x.Iri (Some "schema:MoveAction") "XMove iri"
                  let pos = x.Payload |> List.exactlyOne
                  Expect.equal pos.Status Confirmed "payload position confirmed"
                  Expect.equal pos.Iri (Some "schema:position") "payload iri"
              | _ -> failwith "expected Move to remain a Union after accept (not downcast to Record)"
          }

          test "back-compat: a resolved entry with fields (no cases) still builds a Record" {
              let lock =
                  { SchemaVersion = 1
                    Generated = System.DateTimeOffset.Parse "2026-01-01T00:00:00Z"
                    Vocabularies =
                      Map.ofList
                          [ "schema",
                            { Uri = "https://schema.org/"
                              FetchedAt = System.DateTimeOffset.Parse "2026-01-01T00:00:00Z"
                              Hash = "h" } ]
                    Mappings =
                      [ { FSharpType = "App.Game"
                          Iri = None
                          Confidence = 0.0
                          Source = Convention
                          Status = Unresolved
                          Alternates = []
                          Shape =
                            MappingShape.Record
                                [ { Name = "id"
                                    Iri = None
                                    Confidence = 0.0
                                    Source = Convention
                                    Status = Unresolved } ] } ] }

              let json =
                  """{ "schemaVersion": 1, "resolved": [ { "fsharpType": "App.Game", "iri": "schema:Game", "fields": [ { "name": "id", "iri": "schema:identifier" } ] } ] }"""

              let doc = Expect.wantOk (Accept.parseResolved json) "parse"
              let updated, _ = Accept.apply lock doc Manual emptyOracle

              let game = updated.Mappings |> List.find (fun m -> m.FSharpType = "App.Game")

              match game.Shape with
              | MappingShape.Record [ f ] -> Expect.equal f.Name "id" "record field preserved"
              | _ -> failwith "expected Record from a fields-based (no cases) resolved entry"
          }

          test "parseResolved preserves field order positionally" {
              let json =
                  """{ "schemaVersion": 1, "resolved": [
                        { "fsharpType": "App.T", "iri": "schema:Thing",
                          "fields": [ {"name":"alpha","iri":"schema:a"},
                                      {"name":"beta","iri":"schema:b"},
                                      {"name":"gamma","iri":"schema:c"} ] } ] }"""

              let doc = Expect.wantOk (Accept.parseResolved json) "parse"
              let entry = doc.Resolved |> List.exactlyOne

              match entry.Shape with
              | Accept.ResolvedShape.Record fields ->
                  Expect.equal
                      (fields |> List.map (fun f -> f.Name))
                      [ "alpha"; "beta"; "gamma" ]
                      "field order preserved"
              | Accept.ResolvedShape.Union _ -> failwith "expected ResolvedShape.Record"
          }

          test "a union case with an unresolvable iri is rejected" {
              let lock =
                  { SchemaVersion = 1
                    Generated = System.DateTimeOffset.Parse "2026-01-01T00:00:00Z"
                    Vocabularies =
                      Map.ofList
                          [ "schema",
                            { Uri = "https://schema.org/"
                              FetchedAt = System.DateTimeOffset.Parse "2026-01-01T00:00:00Z"
                              Hash = "h" } ]
                    Mappings =
                      [ { FSharpType = "App.Move"
                          Iri = Some "schema:MoveAction"
                          Confidence = 1.0
                          Source = Convention
                          Status = Confirmed
                          Alternates = []
                          Shape =
                            MappingShape.Union
                                [ { Name = "XMove"
                                    Iri = None
                                    Confidence = 0.0
                                    Source = Convention
                                    Status = Unresolved
                                    Payload = [] } ] } ] }

              let json =
                  """{ "schemaVersion": 1, "resolved": [ { "fsharpType": "App.Move", "iri": "schema:MoveAction", "cases": [ { "name": "XMove", "iri": "bogus:NotAPrefix", "payload": [] } ] } ] }"""

              let doc = Expect.wantOk (Accept.parseResolved json) "parse"
              let _, summary = Accept.apply lock doc Manual emptyOracle
              Expect.isNonEmpty summary.Rejected "unresolvable case iri must be rejected"

              let r = summary.Rejected |> List.find (fun r -> r.FSharpType = "App.Move")

              Expect.stringContains r.Reason "iri" "rejection reason mentions the iri problem"
          }

          // ── Term-existence oracle tests ────────────────────────────────────────

          test "term-existence: typo CURIE on type iri is rejected when namespace covered" {
              let oracle: Accept.TermOracle =
                  { Classes = Set.ofList [ "https://schema.org/Game" ]
                    Properties = Set.ofList [ "https://schema.org/identifier" ]
                    Individuals = Set.empty
                    CoveredBases = [ "https://schema.org/" ] }

              let lock =
                  { SchemaVersion = 1
                    Generated = System.DateTimeOffset.UnixEpoch
                    Vocabularies =
                      Map.ofList
                          [ "schema",
                            { Uri = "https://schema.org/"
                              FetchedAt = System.DateTimeOffset.UnixEpoch
                              Hash = "stub" } ]
                    Mappings =
                      [ { FSharpType = "App.Game"
                          Iri = None
                          Confidence = 0.0
                          Source = Convention
                          Status = Unresolved
                          Alternates = []
                          Shape = MappingShape.Record [] } ] }

              let json =
                  """{ "schemaVersion": 1, "resolved": [ { "fsharpType": "App.Game", "iri": "schema:WinActoin", "fields": [] } ] }"""

              let doc = Expect.wantOk (Accept.parseResolved json) "parse"
              let _, summary = Accept.apply lock doc Llm oracle
              Expect.equal summary.Merged 0 "typo iri must not be merged"

              let rej = summary.Rejected |> List.tryFind (fun r -> r.FSharpType = "App.Game")

              Expect.isSome rej "App.Game must be in Rejected"
              Expect.stringContains rej.Value.Reason "not found" "reason mentions not found"
          }

          test "term-existence: known CURIE on type iri is accepted when namespace covered" {
              let oracle: Accept.TermOracle =
                  { Classes = Set.ofList [ "https://schema.org/Game" ]
                    Properties = Set.ofList [ "https://schema.org/identifier" ]
                    Individuals = Set.empty
                    CoveredBases = [ "https://schema.org/" ] }

              let lock =
                  { SchemaVersion = 1
                    Generated = System.DateTimeOffset.UnixEpoch
                    Vocabularies =
                      Map.ofList
                          [ "schema",
                            { Uri = "https://schema.org/"
                              FetchedAt = System.DateTimeOffset.UnixEpoch
                              Hash = "stub" } ]
                    Mappings =
                      [ { FSharpType = "App.Game"
                          Iri = None
                          Confidence = 0.0
                          Source = Convention
                          Status = Unresolved
                          Alternates = []
                          Shape = MappingShape.Record [] } ] }

              let json =
                  """{ "schemaVersion": 1, "resolved": [ { "fsharpType": "App.Game", "iri": "schema:Game", "fields": [] } ] }"""

              let doc = Expect.wantOk (Accept.parseResolved json) "parse"
              let _, summary = Accept.apply lock doc Llm oracle
              Expect.equal summary.Merged 1 "known iri must be merged"
              Expect.equal summary.Rejected [] "no rejections for known iri"
          }

          test "term-existence: uncached namespace is not existence-checked (no false reject)" {
              let oracle: Accept.TermOracle =
                  { Classes = Set.ofList [ "https://schema.org/Game" ]
                    Properties = Set.empty
                    Individuals = Set.empty
                    CoveredBases = [ "https://schema.org/" ] }

              let lock =
                  { SchemaVersion = 1
                    Generated = System.DateTimeOffset.UnixEpoch
                    Vocabularies =
                      Map.ofList
                          [ "schema",
                            { Uri = "https://schema.org/"
                              FetchedAt = System.DateTimeOffset.UnixEpoch
                              Hash = "stub" }
                            "myns",
                            { Uri = "https://example.com/ns/"
                              FetchedAt = System.DateTimeOffset.UnixEpoch
                              Hash = "stub" } ]
                    Mappings =
                      [ { FSharpType = "App.Foo"
                          Iri = None
                          Confidence = 0.0
                          Source = Convention
                          Status = Unresolved
                          Alternates = []
                          Shape = MappingShape.Record [] } ] }

              let json =
                  """{ "schemaVersion": 1, "resolved": [ { "fsharpType": "App.Foo", "iri": "myns:AnythingAtAll", "fields": [] } ] }"""

              let doc = Expect.wantOk (Accept.parseResolved json) "parse"
              let _, summary = Accept.apply lock doc Llm oracle
              Expect.equal summary.Merged 1 "uncached namespace must not be existence-checked"
              Expect.equal summary.Rejected [] "no rejection for uncached namespace"
          }

          test "term-existence: empty oracle behaves like no check (back-compat)" {
              let lock =
                  { SchemaVersion = 1
                    Generated = System.DateTimeOffset.UnixEpoch
                    Vocabularies =
                      Map.ofList
                          [ "schema",
                            { Uri = "https://schema.org/"
                              FetchedAt = System.DateTimeOffset.UnixEpoch
                              Hash = "stub" } ]
                    Mappings =
                      [ { FSharpType = "App.Game"
                          Iri = None
                          Confidence = 0.0
                          Source = Convention
                          Status = Unresolved
                          Alternates = []
                          Shape = MappingShape.Record [] } ] }

              let json =
                  """{ "schemaVersion": 1, "resolved": [ { "fsharpType": "App.Game", "iri": "schema:WinActoin", "fields": [] } ] }"""

              let doc = Expect.wantOk (Accept.parseResolved json) "parse"
              let _, summary = Accept.apply lock doc Llm emptyOracle
              Expect.equal summary.Merged 1 "empty oracle: typo still merged (back-compat)"
          }

          test "term-existence: typo on field iri is caught (record field path)" {
              let oracle: Accept.TermOracle =
                  { Classes = Set.ofList [ "https://schema.org/Game" ]
                    Properties = Set.ofList [ "https://schema.org/identifier" ]
                    Individuals = Set.empty
                    CoveredBases = [ "https://schema.org/" ] }

              let lock =
                  { SchemaVersion = 1
                    Generated = System.DateTimeOffset.UnixEpoch
                    Vocabularies =
                      Map.ofList
                          [ "schema",
                            { Uri = "https://schema.org/"
                              FetchedAt = System.DateTimeOffset.UnixEpoch
                              Hash = "stub" } ]
                    Mappings =
                      [ { FSharpType = "App.Game"
                          Iri = None
                          Confidence = 0.0
                          Source = Convention
                          Status = Unresolved
                          Alternates = []
                          Shape =
                            MappingShape.Record
                                [ { Name = "Id"
                                    Iri = None
                                    Confidence = 0.0
                                    Source = Convention
                                    Status = Unresolved } ] } ] }

              let json =
                  """{ "schemaVersion": 1, "resolved": [ { "fsharpType": "App.Game", "iri": "schema:Game", "fields": [ { "name": "Id", "iri": "schema:identifyer" } ] } ] }"""

              let doc = Expect.wantOk (Accept.parseResolved json) "parse"
              let _, summary = Accept.apply lock doc Llm oracle
              Expect.equal summary.Merged 0 "field typo must cause rejection"

              let rej = summary.Rejected |> List.tryFind (fun r -> r.FSharpType = "App.Game")

              Expect.isSome rej "App.Game must be rejected for field typo"
              Expect.stringContains rej.Value.Reason "not found" "reason mentions not found"
          }

          test "term-existence: typo on union case iri is caught (union case path)" {
              let oracle: Accept.TermOracle =
                  { Classes = Set.ofList [ "https://schema.org/WinAction"; "https://schema.org/MoveAction" ]
                    Properties = Set.empty
                    Individuals = Set.empty
                    CoveredBases = [ "https://schema.org/" ] }

              let lock =
                  { SchemaVersion = 1
                    Generated = System.DateTimeOffset.UnixEpoch
                    Vocabularies =
                      Map.ofList
                          [ "schema",
                            { Uri = "https://schema.org/"
                              FetchedAt = System.DateTimeOffset.UnixEpoch
                              Hash = "stub" } ]
                    Mappings =
                      [ { FSharpType = "App.Result"
                          Iri = None
                          Confidence = 0.0
                          Source = Convention
                          Status = Unresolved
                          Alternates = []
                          Shape =
                            MappingShape.Union
                                [ { Name = "Won"
                                    Iri = None
                                    Confidence = 0.0
                                    Source = Convention
                                    Status = Unresolved
                                    Payload = [] } ] } ] }

              let json =
                  """{ "schemaVersion": 1, "resolved": [ { "fsharpType": "App.Result", "iri": "schema:MoveAction", "cases": [ { "name": "Won", "iri": "schema:WinActoin", "payload": [] } ] } ] }"""

              let doc = Expect.wantOk (Accept.parseResolved json) "parse"
              let _, summary = Accept.apply lock doc Llm oracle
              Expect.equal summary.Merged 0 "case iri typo must cause rejection"

              let rej = summary.Rejected |> List.tryFind (fun r -> r.FSharpType = "App.Result")

              Expect.isSome rej "App.Result must be rejected for case iri typo"
              Expect.stringContains rej.Value.Reason "not found" "reason mentions not found"
          }

          test "term-existence: typo on union case payload iri is caught" {
              let oracle: Accept.TermOracle =
                  { Classes = Set.ofList [ "https://schema.org/WinAction"; "https://schema.org/MoveAction" ]
                    Properties = Set.ofList [ "https://schema.org/position" ]
                    Individuals = Set.empty
                    CoveredBases = [ "https://schema.org/" ] }

              let lock =
                  { SchemaVersion = 1
                    Generated = System.DateTimeOffset.UnixEpoch
                    Vocabularies =
                      Map.ofList
                          [ "schema",
                            { Uri = "https://schema.org/"
                              FetchedAt = System.DateTimeOffset.UnixEpoch
                              Hash = "stub" } ]
                    Mappings =
                      [ { FSharpType = "App.Result"
                          Iri = None
                          Confidence = 0.0
                          Source = Convention
                          Status = Unresolved
                          Alternates = []
                          Shape =
                            MappingShape.Union
                                [ { Name = "Won"
                                    Iri = None
                                    Confidence = 0.0
                                    Source = Convention
                                    Status = Unresolved
                                    Payload =
                                      [ { Name = "pos"
                                          Iri = None
                                          Confidence = 0.0
                                          Source = Convention
                                          Status = Unresolved } ] } ] } ] }

              let json =
                  """{ "schemaVersion": 1, "resolved": [ { "fsharpType": "App.Result", "iri": "schema:MoveAction", "cases": [ { "name": "Won", "iri": "schema:WinAction", "payload": [ { "name": "pos", "iri": "schema:positionn" } ] } ] } ] }"""

              let doc = Expect.wantOk (Accept.parseResolved json) "parse"
              let _, summary = Accept.apply lock doc Llm oracle
              Expect.equal summary.Merged 0 "case payload typo must cause rejection"

              let rej = summary.Rejected |> List.tryFind (fun r -> r.FSharpType = "App.Result")

              Expect.isSome rej "App.Result must be rejected for payload typo"
          }

          // ── Category-aware oracle tests ────────────────────────────────────────

          // Shared oracle: Game+MoveAction = Classes; identifier = Properties; FailedActionStatus = Individuals
          // CoveredBases = ["https://schema.org/"]
          test "category: property used as TYPE iri → category error, not 'not found'" {
              let oracle: Accept.TermOracle =
                  { Classes = Set.ofList [ "https://schema.org/Game"; "https://schema.org/MoveAction" ]
                    Properties = Set.ofList [ "https://schema.org/identifier" ]
                    Individuals = Set.ofList [ "https://schema.org/FailedActionStatus" ]
                    CoveredBases = [ "https://schema.org/" ] }

              let lock =
                  { SchemaVersion = 1
                    Generated = System.DateTimeOffset.UnixEpoch
                    Vocabularies =
                      Map.ofList
                          [ "schema",
                            { Uri = "https://schema.org/"
                              FetchedAt = System.DateTimeOffset.UnixEpoch
                              Hash = "stub" } ]
                    Mappings =
                      [ { FSharpType = "App.Item"
                          Iri = None
                          Confidence = 0.0
                          Source = Convention
                          Status = Unresolved
                          Alternates = []
                          Shape = MappingShape.Record [] } ] }

              let json =
                  """{ "schemaVersion": 1, "resolved": [ { "fsharpType": "App.Item", "iri": "schema:identifier", "fields": [] } ] }"""

              let doc = Expect.wantOk (Accept.parseResolved json) "parse"
              let _, summary = Accept.apply lock doc Llm oracle
              Expect.equal summary.Merged 0 "property used as type must be rejected"

              let rej = summary.Rejected |> List.tryFind (fun r -> r.FSharpType = "App.Item")

              Expect.isSome rej "App.Item must be rejected for category mismatch"

              Expect.stringContains
                  rej.Value.Reason
                  "exists in the vocabulary but not as a"
                  "reason is a category error, not 'not found'"

              Expect.isFalse
                  (rej.Value.Reason.Contains "not found in vocabulary")
                  "must NOT say 'not found in vocabulary'"
          }

          test "category: class used as FIELD iri → category error" {
              let oracle: Accept.TermOracle =
                  { Classes = Set.ofList [ "https://schema.org/Game"; "https://schema.org/MoveAction" ]
                    Properties = Set.ofList [ "https://schema.org/identifier" ]
                    Individuals = Set.ofList [ "https://schema.org/FailedActionStatus" ]
                    CoveredBases = [ "https://schema.org/" ] }

              let lock =
                  { SchemaVersion = 1
                    Generated = System.DateTimeOffset.UnixEpoch
                    Vocabularies =
                      Map.ofList
                          [ "schema",
                            { Uri = "https://schema.org/"
                              FetchedAt = System.DateTimeOffset.UnixEpoch
                              Hash = "stub" } ]
                    Mappings =
                      [ { FSharpType = "App.Game"
                          Iri = None
                          Confidence = 0.0
                          Source = Convention
                          Status = Unresolved
                          Alternates = []
                          Shape =
                            MappingShape.Record
                                [ { Name = "f"
                                    Iri = None
                                    Confidence = 0.0
                                    Source = Convention
                                    Status = Unresolved } ] } ] }

              let json =
                  """{ "schemaVersion": 1, "resolved": [ { "fsharpType": "App.Game", "iri": "schema:Game", "fields": [ { "name": "f", "iri": "schema:Game" } ] } ] }"""

              let doc = Expect.wantOk (Accept.parseResolved json) "parse"
              let _, summary = Accept.apply lock doc Llm oracle
              Expect.equal summary.Merged 0 "class used as field must be rejected"

              let rej = summary.Rejected |> List.tryFind (fun r -> r.FSharpType = "App.Game")

              Expect.isSome rej "App.Game must be rejected for field category mismatch"
              Expect.stringContains rej.Value.Reason "exists in the vocabulary but not as a" "reason is category error"
          }

          test "category: class used as TYPE iri → OK" {
              let oracle: Accept.TermOracle =
                  { Classes = Set.ofList [ "https://schema.org/Game"; "https://schema.org/MoveAction" ]
                    Properties = Set.ofList [ "https://schema.org/identifier" ]
                    Individuals = Set.ofList [ "https://schema.org/FailedActionStatus" ]
                    CoveredBases = [ "https://schema.org/" ] }

              let lock =
                  { SchemaVersion = 1
                    Generated = System.DateTimeOffset.UnixEpoch
                    Vocabularies =
                      Map.ofList
                          [ "schema",
                            { Uri = "https://schema.org/"
                              FetchedAt = System.DateTimeOffset.UnixEpoch
                              Hash = "stub" } ]
                    Mappings =
                      [ { FSharpType = "App.Game"
                          Iri = None
                          Confidence = 0.0
                          Source = Convention
                          Status = Unresolved
                          Alternates = []
                          Shape = MappingShape.Record [] } ] }

              let json =
                  """{ "schemaVersion": 1, "resolved": [ { "fsharpType": "App.Game", "iri": "schema:Game", "fields": [] } ] }"""

              let doc = Expect.wantOk (Accept.parseResolved json) "parse"
              let _, summary = Accept.apply lock doc Llm oracle
              Expect.equal summary.Merged 1 "class as type iri must be accepted"
              Expect.equal summary.Rejected [] "no rejection for correct category"
          }

          test "category: property used as FIELD iri → OK" {
              let oracle: Accept.TermOracle =
                  { Classes = Set.ofList [ "https://schema.org/Game"; "https://schema.org/MoveAction" ]
                    Properties = Set.ofList [ "https://schema.org/identifier" ]
                    Individuals = Set.ofList [ "https://schema.org/FailedActionStatus" ]
                    CoveredBases = [ "https://schema.org/" ] }

              let lock =
                  { SchemaVersion = 1
                    Generated = System.DateTimeOffset.UnixEpoch
                    Vocabularies =
                      Map.ofList
                          [ "schema",
                            { Uri = "https://schema.org/"
                              FetchedAt = System.DateTimeOffset.UnixEpoch
                              Hash = "stub" } ]
                    Mappings =
                      [ { FSharpType = "App.Game"
                          Iri = None
                          Confidence = 0.0
                          Source = Convention
                          Status = Unresolved
                          Alternates = []
                          Shape =
                            MappingShape.Record
                                [ { Name = "id"
                                    Iri = None
                                    Confidence = 0.0
                                    Source = Convention
                                    Status = Unresolved } ] } ] }

              let json =
                  """{ "schemaVersion": 1, "resolved": [ { "fsharpType": "App.Game", "iri": "schema:Game", "fields": [ { "name": "id", "iri": "schema:identifier" } ] } ] }"""

              let doc = Expect.wantOk (Accept.parseResolved json) "parse"
              let _, summary = Accept.apply lock doc Llm oracle
              Expect.equal summary.Merged 1 "property as field iri must be accepted"
              Expect.equal summary.Rejected [] "no rejection for correct category"
          }

          test "category: individual used as CASE iri → OK (case allows Class∪Individual)" {
              let oracle: Accept.TermOracle =
                  { Classes = Set.ofList [ "https://schema.org/Game"; "https://schema.org/MoveAction" ]
                    Properties = Set.ofList [ "https://schema.org/identifier" ]
                    Individuals = Set.ofList [ "https://schema.org/FailedActionStatus" ]
                    CoveredBases = [ "https://schema.org/" ] }

              let lock =
                  { SchemaVersion = 1
                    Generated = System.DateTimeOffset.UnixEpoch
                    Vocabularies =
                      Map.ofList
                          [ "schema",
                            { Uri = "https://schema.org/"
                              FetchedAt = System.DateTimeOffset.UnixEpoch
                              Hash = "stub" } ]
                    Mappings =
                      [ { FSharpType = "App.Status"
                          Iri = None
                          Confidence = 0.0
                          Source = Convention
                          Status = Unresolved
                          Alternates = []
                          Shape =
                            MappingShape.Union
                                [ { Name = "Failed"
                                    Iri = None
                                    Confidence = 0.0
                                    Source = Convention
                                    Status = Unresolved
                                    Payload = [] } ] } ] }

              let json =
                  """{ "schemaVersion": 1, "resolved": [ { "fsharpType": "App.Status", "iri": "schema:MoveAction", "cases": [ { "name": "Failed", "iri": "schema:FailedActionStatus", "payload": [] } ] } ] }"""

              let doc = Expect.wantOk (Accept.parseResolved json) "parse"
              let _, summary = Accept.apply lock doc Llm oracle
              Expect.equal summary.Merged 1 "individual as case iri must be accepted (no false-reject)"
              Expect.equal summary.Rejected [] "no rejection for individual in case position"
          }

          test "category: class used as CASE iri → OK (case allows Class∪Individual)" {
              let oracle: Accept.TermOracle =
                  { Classes = Set.ofList [ "https://schema.org/Game"; "https://schema.org/MoveAction" ]
                    Properties = Set.ofList [ "https://schema.org/identifier" ]
                    Individuals = Set.ofList [ "https://schema.org/FailedActionStatus" ]
                    CoveredBases = [ "https://schema.org/" ] }

              let lock =
                  { SchemaVersion = 1
                    Generated = System.DateTimeOffset.UnixEpoch
                    Vocabularies =
                      Map.ofList
                          [ "schema",
                            { Uri = "https://schema.org/"
                              FetchedAt = System.DateTimeOffset.UnixEpoch
                              Hash = "stub" } ]
                    Mappings =
                      [ { FSharpType = "App.Move"
                          Iri = None
                          Confidence = 0.0
                          Source = Convention
                          Status = Unresolved
                          Alternates = []
                          Shape =
                            MappingShape.Union
                                [ { Name = "M"
                                    Iri = None
                                    Confidence = 0.0
                                    Source = Convention
                                    Status = Unresolved
                                    Payload = [] } ] } ] }

              let json =
                  """{ "schemaVersion": 1, "resolved": [ { "fsharpType": "App.Move", "iri": "schema:Game", "cases": [ { "name": "M", "iri": "schema:MoveAction", "payload": [] } ] } ] }"""

              let doc = Expect.wantOk (Accept.parseResolved json) "parse"
              let _, summary = Accept.apply lock doc Llm oracle
              Expect.equal summary.Merged 1 "class as case iri must be accepted"
              Expect.equal summary.Rejected [] "no rejection for class in case position"
          }

          test "category: typo TYPE iri (not in any set) → 'not found', not category error" {
              let oracle: Accept.TermOracle =
                  { Classes = Set.ofList [ "https://schema.org/Game"; "https://schema.org/MoveAction" ]
                    Properties = Set.ofList [ "https://schema.org/identifier" ]
                    Individuals = Set.ofList [ "https://schema.org/FailedActionStatus" ]
                    CoveredBases = [ "https://schema.org/" ] }

              let lock =
                  { SchemaVersion = 1
                    Generated = System.DateTimeOffset.UnixEpoch
                    Vocabularies =
                      Map.ofList
                          [ "schema",
                            { Uri = "https://schema.org/"
                              FetchedAt = System.DateTimeOffset.UnixEpoch
                              Hash = "stub" } ]
                    Mappings =
                      [ { FSharpType = "App.X"
                          Iri = None
                          Confidence = 0.0
                          Source = Convention
                          Status = Unresolved
                          Alternates = []
                          Shape = MappingShape.Record [] } ] }

              let json =
                  """{ "schemaVersion": 1, "resolved": [ { "fsharpType": "App.X", "iri": "schema:WinActoin", "fields": [] } ] }"""

              let doc = Expect.wantOk (Accept.parseResolved json) "parse"
              let _, summary = Accept.apply lock doc Llm oracle
              Expect.equal summary.Merged 0 "typo must be rejected"

              let rej = summary.Rejected |> List.tryFind (fun r -> r.FSharpType = "App.X")

              Expect.isSome rej "App.X must be rejected"
              Expect.stringContains rej.Value.Reason "not found in vocabulary" "typo gives 'not found' message"

              Expect.isFalse
                  (rej.Value.Reason.Contains "exists in the vocabulary")
                  "must NOT say 'exists in vocabulary'"
          }

          test "VocabFetcher.loadCachedGraph: absent cache returns None" {
              let cacheDir =
                  System.IO.Path.Combine(
                      "/private/tmp/claude-501/-Users-ryanr-Code-frank/8dcbe96d-0ab3-41b3-9ce3-5abb4ec9c623/scratchpad",
                      "vocab-test-empty"
                  )

              System.IO.Directory.CreateDirectory(cacheDir) |> ignore
              let result = VocabFetcher.loadCachedGraph cacheDir "no-such-vocab"
              Expect.isNone result "absent vocab returns None"
          }

          test "VocabFetcher.loadCachedGraph: present cache file returns Some (Ok graph)" {
              let cacheDir =
                  System.IO.Path.Combine(
                      "/private/tmp/claude-501/-Users-ryanr-Code-frank/8dcbe96d-0ab3-41b3-9ce3-5abb4ec9c623/scratchpad",
                      "vocab-test-present"
                  )

              System.IO.Directory.CreateDirectory(cacheDir) |> ignore

              let jsonLdBytes =
                  """{"@context":{"rdf":"http://www.w3.org/1999/02/22-rdf-syntax-ns#","rdfs":"http://www.w3.org/2000/01/rdf-schema#","schema":"https://schema.org/"},"@graph":[{"@id":"https://schema.org/Game","@type":"rdfs:Class"}]}"""
                  |> System.Text.Encoding.UTF8.GetBytes

              let hash = VocabFetcher.sha256Hex jsonLdBytes
              let fileName = $"testvocab.{hash}.jsonld"
              let filePath = System.IO.Path.Combine(cacheDir, fileName)
              System.IO.File.WriteAllBytes(filePath, jsonLdBytes)

              let result = VocabFetcher.loadCachedGraph cacheDir "testvocab"

              match result with
              | None -> failtest "expected Some result for present cache file"
              | Some(Error e) -> failtest $"expected Ok graph, got Error: {e}"
              | Some(Ok graph) -> Expect.isGreaterThan graph.Triples.Count 0 "graph has triples"
          } ]
