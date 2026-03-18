module Frank.Statecharts.Tests.Validation.NearMatchTests

open Expecto
open Frank.Statecharts.Validation
open Frank.Statecharts.Ast

// ─────────────────────────────────────────────
// Test Helpers (mirrors CrossFormatTests helpers)
// ─────────────────────────────────────────────

let makeState id =
    { Identifier = Some id
      Label = None
      Kind = Regular
      Children = []
      Activities = None
      Position = None
      Annotations = [] }

let makeTransition source target event =
    { Source = source
      Target = target
      Event = event
      Guard = None
      Action = None
      Parameters = []
      Position = None
      Annotations = [] }

let makeDocument (states: string list) (transitions: (string * string option * string option) list) : StatechartDocument =
    { Title = None
      InitialStateId = None
      Elements =
        (states |> List.map (fun s -> StateDecl (makeState s)))
        @ (transitions |> List.map (fun (s, t, e) -> TransitionElement (makeTransition s t e)))
      DataEntries = []
      Annotations = [] }

let makeArtifact format doc : FormatArtifact =
    { Format = format; Document = doc }

// ─────────────────────────────────────────────
// Near-match rule: state identifier detection
// ─────────────────────────────────────────────

[<Tests>]
let nearMatchStateTests =
    testList
        "Validation.CrossFormatRules.NearMatch.States"
        [ test "'start' vs 'startOnboarding' across two formats produces near-match failure" {
              // "start" and "startOnboarding" share a long common prefix — Jaro-Winkler should exceed 0.8
              let docWsd = makeDocument [ "start"; "end" ] []
              let docScxml = makeDocument [ "startOnboarding"; "end" ] []
              let artWsd = makeArtifact Wsd docWsd
              let artScxml = makeArtifact Scxml docScxml

              let (checks, failures) = CrossFormatRules.nearMatchRule.Check [ artWsd; artScxml ]

              let failChecks = checks |> List.filter (fun c -> c.Status = Fail)
              Expect.isGreaterThan (List.length failChecks) 0 "Should detect near-match between 'start' and 'startOnboarding'"

              let allReasons = failChecks |> List.choose (fun c -> c.Reason) |> String.concat " "
              Expect.stringContains allReasons "start" "Reason should mention 'start'"
              Expect.stringContains allReasons "startOnboarding" "Reason should mention 'startOnboarding'"
              Expect.stringContains allReasons "similarity" "Reason should include similarity score"
              Expect.isNonEmpty failures "Failures list should not be empty"
          }

          test "'Idle' vs 'idle' casing produces near-match failure (very high similarity)" {
              // Identical except for casing — Jaro-Winkler will be very close to 1.0
              let docWsd = makeDocument [ "Idle"; "active" ] []
              let docScxml = makeDocument [ "idle"; "active" ] []
              let artWsd = makeArtifact Wsd docWsd
              let artScxml = makeArtifact Scxml docScxml

              let (checks, failures) = CrossFormatRules.nearMatchRule.Check [ artWsd; artScxml ]

              let failChecks = checks |> List.filter (fun c -> c.Status = Fail)
              Expect.isGreaterThan (List.length failChecks) 0 "Should detect near-match between 'Idle' and 'idle'"

              let allReasons = failChecks |> List.choose (fun c -> c.Reason) |> String.concat " "
              Expect.stringContains allReasons "Idle" "Reason should mention 'Idle'"
              Expect.stringContains allReasons "idle" "Reason should mention 'idle'"
              Expect.isNonEmpty failures "Failures list should not be empty"
          }

          test "'login' vs 'shutdown' are too different — no near-match" {
              let docWsd = makeDocument [ "login"; "active" ] []
              let docScxml = makeDocument [ "shutdown"; "active" ] []
              let artWsd = makeArtifact Wsd docWsd
              let artScxml = makeArtifact Scxml docScxml

              let (checks, failures) = CrossFormatRules.nearMatchRule.Check [ artWsd; artScxml ]

              let failChecks = checks |> List.filter (fun c -> c.Status = Fail)
              // "login" vs "shutdown" should be below the 0.8 threshold
              let loginShutdownFails =
                  failChecks
                  |> List.filter (fun c ->
                      let name = c.Name
                      name.Contains("login") && name.Contains("shutdown"))

              Expect.isEmpty loginShutdownFails "Should not flag 'login' vs 'shutdown' as a near-match"
          }

          test "identical state names across formats do not trigger near-match warning" {
              // Exact matches are excluded — only unmatched identifiers are checked
              let doc = makeDocument [ "idle"; "active"; "done" ] []
              let artWsd = makeArtifact Wsd doc
              let artScxml = makeArtifact Scxml doc

              let (checks, failures) = CrossFormatRules.nearMatchRule.Check [ artWsd; artScxml ]

              let failChecks = checks |> List.filter (fun c -> c.Status = Fail)
              Expect.isEmpty failChecks "Identical state names should not trigger near-match"
              Expect.isEmpty failures "No failures for identical states"

              let passChecks = checks |> List.filter (fun c -> c.Status = Pass)
              Expect.isNonEmpty passChecks "Should have a passing check"
          }

          test "multiple near-matches all reported" {
              // Mix of similar and dissimilar pairs:
              //   "Idle"/"idle" and "Active"/"active" are similar enough (Jaro-Winkler > 0.8)
              //   "Done"/"done" — 4 chars, first chars 'D'/'d' differ, window=1
              //     matches: 'o','n','e' (3 of 4) => Jaro=(3/4+3/4+1)=0.833 > 0.8 — also fires
              let docWsd = makeDocument [ "Idle"; "Active"; "Done" ] []
              let docScxml = makeDocument [ "idle"; "active"; "done" ] []
              let artWsd = makeArtifact Wsd docWsd
              let artScxml = makeArtifact Scxml docScxml

              let (checks, failures) = CrossFormatRules.nearMatchRule.Check [ artWsd; artScxml ]

              let failChecks = checks |> List.filter (fun c -> c.Status = Fail)
              // All three casing mismatches should exceed the 0.8 Jaro-Winkler threshold
              Expect.isGreaterThanOrEqual (List.length failChecks) 3 "Should report all near-match pairs"
              Expect.isGreaterThanOrEqual (List.length failures) 3 "Failures list should contain all near-matches"
          }

          test "failure contains both format identifiers" {
              let docWsd = makeDocument [ "Idle" ] []
              let docScxml = makeDocument [ "idle" ] []
              let artWsd = makeArtifact Wsd docWsd
              let artScxml = makeArtifact Scxml docScxml

              let (checks, failures) = CrossFormatRules.nearMatchRule.Check [ artWsd; artScxml ]

              let failChecks = checks |> List.filter (fun c -> c.Status = Fail)
              Expect.isGreaterThan (List.length failChecks) 0 "Should have at least one failure"

              Expect.isNonEmpty failures "Failures list should not be empty"
              let failure = failures.[0]
              Expect.isTrue
                  (failure.Formats |> List.exists (fun f -> f = Wsd))
                  "Failure should reference Wsd format"
              Expect.isTrue
                  (failure.Formats |> List.exists (fun f -> f = Scxml))
                  "Failure should reference Scxml format"
              Expect.equal failure.EntityType "state" "EntityType should be 'state'"
          } ]

// ─────────────────────────────────────────────
// Near-match rule: event name detection
// ─────────────────────────────────────────────

[<Tests>]
let nearMatchEventTests =
    testList
        "Validation.CrossFormatRules.NearMatch.Events"
        [ test "near-match event names across formats are detected" {
              // "submit" vs "submitForm" — long common prefix, high Jaro-Winkler
              let docWsd =
                  makeDocument
                      [ "idle"; "active" ]
                      [ ("idle", Some "active", Some "submit") ]

              let docScxml =
                  makeDocument
                      [ "idle"; "active" ]
                      [ ("idle", Some "active", Some "submitForm") ]

              let artWsd = makeArtifact Wsd docWsd
              let artScxml = makeArtifact Scxml docScxml

              let (checks, failures) = CrossFormatRules.nearMatchRule.Check [ artWsd; artScxml ]

              let failChecks = checks |> List.filter (fun c -> c.Status = Fail)
              Expect.isGreaterThan (List.length failChecks) 0 "Should detect near-match for 'submit' vs 'submitForm'"

              let allReasons = failChecks |> List.choose (fun c -> c.Reason) |> String.concat " "
              Expect.stringContains allReasons "submit" "Reason should mention 'submit'"
              Expect.stringContains allReasons "similarity" "Reason should include similarity score"
          }

          test "near-match event failure has EntityType 'event'" {
              let docWsd =
                  makeDocument
                      [ "idle"; "active" ]
                      [ ("idle", Some "active", Some "Click") ]

              let docScxml =
                  makeDocument
                      [ "idle"; "active" ]
                      [ ("idle", Some "active", Some "click") ]

              let artWsd = makeArtifact Wsd docWsd
              let artScxml = makeArtifact Scxml docScxml

              let (_, failures) = CrossFormatRules.nearMatchRule.Check [ artWsd; artScxml ]

              let eventFailures = failures |> List.filter (fun f -> f.EntityType = "event")
              Expect.isGreaterThan (List.length eventFailures) 0 "Should have failures with EntityType 'event'"
          }

          test "identical event names do not trigger near-match" {
              let doc =
                  makeDocument
                      [ "idle"; "active" ]
                      [ ("idle", Some "active", Some "start")
                        ("active", Some "idle", Some "reset") ]

              let artWsd = makeArtifact Wsd doc
              let artScxml = makeArtifact Scxml doc

              let (checks, failures) = CrossFormatRules.nearMatchRule.Check [ artWsd; artScxml ]

              let eventFails =
                  failures |> List.filter (fun f -> f.EntityType = "event")

              Expect.isEmpty eventFails "Identical events should not produce near-match failures"
          } ]

// ─────────────────────────────────────────────
// Near-match rule: edge cases
// ─────────────────────────────────────────────

[<Tests>]
let nearMatchEdgeCaseTests =
    testList
        "Validation.CrossFormatRules.NearMatch.EdgeCases"
        [ test "empty artifact list produces a single passing check" {
              let (checks, failures) = CrossFormatRules.nearMatchRule.Check []

              Expect.equal (List.length checks) 1 "Should produce exactly one check"
              Expect.equal checks.[0].Status Pass "Check should be Pass"
              Expect.isEmpty failures "No failures for empty artifacts"
          }

          test "single artifact produces a single passing check" {
              let doc = makeDocument [ "idle"; "active" ] []
              let artifact = makeArtifact Wsd doc

              let (checks, failures) = CrossFormatRules.nearMatchRule.Check [ artifact ]

              Expect.equal (List.length checks) 1 "Should produce exactly one check"
              Expect.equal checks.[0].Status Pass "Check should be Pass with no pairs to compare"
              Expect.isEmpty failures "No failures for single artifact"
          }

          test "three artifacts with near-matches detected across all pairs" {
              // 3 artifacts: Wsd has "Idle", Scxml has "idle", XState has "IDLE"
              // "Idle"/"idle" and "Idle"/"IDLE" and "idle"/"IDLE" should all exceed threshold
              let docWsd = makeDocument [ "Idle" ] []
              let docScxml = makeDocument [ "idle" ] []
              let docXState = makeDocument [ "IDLE" ] []
              let artWsd = makeArtifact Wsd docWsd
              let artScxml = makeArtifact Scxml docScxml
              let artXState = makeArtifact XState docXState

              let (checks, failures) = CrossFormatRules.nearMatchRule.Check [ artWsd; artScxml; artXState ]

              let failChecks = checks |> List.filter (fun c -> c.Status = Fail)
              Expect.isGreaterThan (List.length failChecks) 0 "Should detect near-matches across all three pairs"
              Expect.isGreaterThan (List.length failures) 0 "Should have failures"
          }

          test "near-match rule name is 'cross-format-near-match'" {
              Expect.equal CrossFormatRules.nearMatchRule.Name "cross-format-near-match" "Rule name should match"
          }

          test "near-match rule has empty RequiredFormats (universal)" {
              Expect.equal CrossFormatRules.nearMatchRule.RequiredFormats Set.empty "Rule should be universal"
          }

          test "nearMatchThreshold is 0.8" {
              Expect.equal CrossFormatRules.nearMatchThreshold 0.8 "Threshold should be 0.8"
          } ]
