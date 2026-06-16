module Frank.Cli.Core.Tests.AcceptTests

open Expecto
open Frank.Semantic
open Frank.Semantic.LockFile
open Frank.Cli.Core

// ── Fixtures ──────────────────────────────────────────────────────────────────

let private emptyVocabs : Map<string, VocabularyEntry> = Map.empty

let private mkLock (mappings: Mapping list) : LockFile =
    { SchemaVersion = 1
      Generated = System.DateTimeOffset.UnixEpoch
      Vocabularies = emptyVocabs
      Mappings = mappings }

let private unresolvedOrderLine : Mapping =
    { FSharpType = "MyApp.OrderLine"
      Iri = None
      Confidence = 0.0
      Source = Convention
      Status = Unresolved
      Alternates = []
      Fields = [] }

let private unresolvedOrder : Mapping =
    { FSharpType = "MyApp.Order"
      Iri = None
      Confidence = 0.0
      Source = Convention
      Status = Unresolved
      Alternates = []
      Fields =
        [ { Name = "Total"
            Iri = None
            Confidence = 0.0
            Source = Convention
            Status = Unresolved } ] }

let private at1Json =
    """{"schemaVersion":1,"resolved":[{"fsharpType":"MyApp.OrderLine","iri":"schema:OrderItem","fields":[]}]}"""

let private at3Json =
    """{"schemaVersion":1,"resolved":[{"fsharpType":"MyApp.Order","iri":"schema:Order","fields":[]},{"fsharpType":"MyApp.Nonexistent","iri":"schema:Thing","fields":[]}]}"""

let private versionMismatchJson =
    """{"schemaVersion":99,"resolved":[]}"""

// ── Tests ─────────────────────────────────────────────────────────────────────

[<Tests>]
let acceptTests =
    testList
        "Accept"
        [ testCase "AT1: known type resolved — status Confirmed, source Llm, iri set"
          <| fun () ->
              let lock = mkLock [ unresolvedOrderLine ]

              match Accept.parseResolved at1Json with
              | Error e -> failtest $"parseResolved failed: {e}"
              | Ok doc ->
                  let updated, summary = Accept.apply lock doc Llm
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
              let lock = mkLock [ unresolvedOrder ]

              match Accept.parseResolved at3Json with
              | Error e -> failtest $"parseResolved failed: {e}"
              | Ok doc ->
                  let updated, summary = Accept.apply lock doc Llm
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
              let lock = mkLock [ unresolvedOrderLine ]

              match Accept.parseResolved at1Json with
              | Error e -> failtest $"parseResolved failed: {e}"
              | Ok doc ->
                  let updated, _ = Accept.apply lock doc Manual
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
                  let updated, summary = Accept.apply lock doc Llm
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

              let lock = mkLock [ unresolvedOrder ]

              match Accept.parseResolved fieldNullIriJson with
              | Error e -> failtest $"parseResolved failed: {e}"
              | Ok doc ->
                  let updated, summary = Accept.apply lock doc Llm
                  Expect.equal summary.Merged 1 "one merged"
                  Expect.isTrue (summary.FieldsUnresolved >= 1) "at least one field unresolved"
                  let m = updated.Mappings |> List.find (fun m -> m.FSharpType = "MyApp.Order")
                  Expect.equal m.Status Confirmed "type is Confirmed"

                  let totalField = m.Fields |> List.tryFind (fun f -> f.Name = "Total")

                  match totalField with
                  | None -> failtest "Total field missing"
                  | Some f -> Expect.equal f.Status Unresolved "Total field stays Unresolved"

          testCase "alreadyConfirmed: re-confirming an already-Confirmed entry"
          <| fun () ->
              let alreadyConfirmed : Mapping =
                  { FSharpType = "MyApp.OrderLine"
                    Iri = Some "schema:OrderItem"
                    Confidence = 1.0
                    Source = Llm
                    Status = Confirmed
                    Alternates = []
                    Fields = [] }

              let lock = mkLock [ alreadyConfirmed ]

              match Accept.parseResolved at1Json with
              | Error e -> failtest $"parseResolved failed: {e}"
              | Ok doc ->
                  let _updated, summary = Accept.apply lock doc Llm
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

          testCase "json output: summaryToJson produces valid JSON with expected fields"
          <| fun () ->
              let summary : Accept.AcceptSummary =
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
              Expect.equal (r0.GetProperty("reason").GetString()) "not in lock file" "reason" ]
