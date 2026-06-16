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
      Fields = [] }

let private at1Json =
    """{"schemaVersion":1,"resolved":[{"fsharpType":"MyApp.OrderLine","iri":"schema:OrderItem","fields":[]}]}"""

let private at3Json =
    """{"schemaVersion":1,"resolved":[{"fsharpType":"MyApp.Order","iri":"schema:Order","fields":[]},{"fsharpType":"MyApp.Nonexistent","iri":"schema:Thing","fields":[]}]}"""

let private versionMismatchJson =
    """{"schemaVersion":99,"resolved":[]}"""

let private missingIriJson =
    """{"schemaVersion":1,"resolved":[{"fsharpType":"MyApp.OrderLine","fields":[]}]}"""

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
                  Expect.equal summary.Rejected [ "MyApp.Nonexistent" ] "rejected types"

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

          testCase "structural: entry missing iri — Error"
          <| fun () ->
              match Accept.parseResolved missingIriJson with
              | Ok _ -> failtest "expected Error for missing iri"
              | Error msg -> Expect.stringContains msg "iri" "error message must mention iri" ]
