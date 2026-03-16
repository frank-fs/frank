module Frank.Statecharts.Tests.Validation.CrossFormatTests

open Expecto
open Frank.Statecharts.Validation
open Frank.Statecharts.Ast

// ─────────────────────────────────────────────
// Test Helpers
// ─────────────────────────────────────────────

let makeState id =
    { Identifier = id
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

/// Create consistent artifacts for multiple formats with the same states and transitions.
let makeConsistentArtifacts
    (formats: FormatTag list)
    (states: string list)
    (transitions: (string * string option * string option) list)
    : FormatArtifact list =
    let doc = makeDocument states transitions
    formats |> List.map (fun fmt -> makeArtifact fmt doc)

// ─────────────────────────────────────────────
// T039: State name agreement
// ─────────────────────────────────────────────

[<Tests>]
let stateNameAgreementTests =
    testList
        "Validation.CrossFormatRules.StateNameAgreement"
        [ test "matching state names pass" {
              let doc = makeDocument [ "idle"; "active"; "done" ] []
              let artScxml = makeArtifact Scxml doc
              let artXState = makeArtifact XState doc
              let rule = CrossFormatRules.stateNameAgreement Scxml XState

              let (checks, _) = rule.Check [ artScxml; artXState ]

              Expect.equal (List.length checks) 1 "Should produce one check"
              Expect.equal checks.[0].Status Pass "Matching states should pass"
          }

          test "missing state in format B reported as failure" {
              let docScxml = makeDocument [ "idle"; "active"; "gameOver" ] []
              let docXState = makeDocument [ "idle"; "active" ] []
              let artScxml = makeArtifact Scxml docScxml
              let artXState = makeArtifact XState docXState
              let rule = CrossFormatRules.stateNameAgreement Scxml XState

              let (checks, _) = rule.Check [ artScxml; artXState ]

              let failChecks = checks |> List.filter (fun c -> c.Status = Fail)
              Expect.isGreaterThan (List.length failChecks) 0 "Should report missing state"

              let failReasons = failChecks |> List.map (fun c -> c.Reason |> Option.defaultValue "") |> String.concat " "
              Expect.stringContains failReasons "gameOver" "Should mention missing state 'gameOver'"
              Expect.stringContains failReasons "XState" "Should mention XState as the format missing the state"
          }

          test "missing state in format A reported as failure" {
              let docScxml = makeDocument [ "idle"; "active" ] []
              let docXState = makeDocument [ "idle"; "active"; "maintenance" ] []
              let artScxml = makeArtifact Scxml docScxml
              let artXState = makeArtifact XState docXState
              let rule = CrossFormatRules.stateNameAgreement Scxml XState

              let (checks, _) = rule.Check [ artScxml; artXState ]

              let failChecks = checks |> List.filter (fun c -> c.Status = Fail)
              Expect.isGreaterThan (List.length failChecks) 0 "Should report missing state"

              let failReasons = failChecks |> List.map (fun c -> c.Reason |> Option.defaultValue "") |> String.concat " "
              Expect.stringContains failReasons "maintenance" "Should mention missing state 'maintenance'"
              Expect.stringContains failReasons "Scxml" "Should mention Scxml as the format missing the state"
          }

          test "symmetric reporting: both formats have unique states" {
              let docScxml = makeDocument [ "idle"; "active"; "gameOver" ] []
              let docXState = makeDocument [ "idle"; "active"; "maintenance" ] []
              let artScxml = makeArtifact Scxml docScxml
              let artXState = makeArtifact XState docXState
              let rule = CrossFormatRules.stateNameAgreement Scxml XState

              let (checks, _) = rule.Check [ artScxml; artXState ]

              let failChecks = checks |> List.filter (fun c -> c.Status = Fail)
              Expect.equal (List.length failChecks) 2 "Should report both directions"

              let allReasons = failChecks |> List.map (fun c -> c.Reason |> Option.defaultValue "") |> String.concat " "
              Expect.stringContains allReasons "gameOver" "Should report gameOver"
              Expect.stringContains allReasons "maintenance" "Should report maintenance"
          }

          test "failure contains correct format names" {
              let docScxml = makeDocument [ "idle"; "active"; "done" ] []
              let docXState = makeDocument [ "idle"; "active" ] []
              let artScxml = makeArtifact Scxml docScxml
              let artXState = makeArtifact XState docXState
              let rule = CrossFormatRules.stateNameAgreement Scxml XState

              let (checks, _) = rule.Check [ artScxml; artXState ]

              let failChecks = checks |> List.filter (fun c -> c.Status = Fail)
              Expect.isGreaterThan (List.length failChecks) 0 "Should have at least one failure"

              let reason = failChecks.[0].Reason |> Option.defaultValue ""
              Expect.stringContains reason "Scxml" "Reason should identify SCXML"
              Expect.stringContains reason "XState" "Reason should identify XState"
          } ]

// ─────────────────────────────────────────────
// T040: Event name agreement
// ─────────────────────────────────────────────

[<Tests>]
let eventNameAgreementTests =
    testList
        "Validation.CrossFormatRules.EventNameAgreement"
        [ test "matching events pass" {
              let docAlps =
                  makeDocument
                      [ "idle"; "active" ]
                      [ ("idle", Some "active", Some "submitMove")
                        ("active", Some "idle", Some "reset") ]

              let docXState =
                  makeDocument
                      [ "idle"; "active" ]
                      [ ("idle", Some "active", Some "submitMove")
                        ("active", Some "idle", Some "reset") ]

              let artAlps = makeArtifact Alps docAlps
              let artXState = makeArtifact XState docXState
              let rule = CrossFormatRules.eventNameAgreement Alps XState

              let (checks, _) = rule.Check [ artAlps; artXState ]

              Expect.equal (List.length checks) 1 "Should produce one check"
              Expect.equal checks.[0].Status Pass "Matching events should pass"
          }

          test "missing event 'submitMove' reported with format info" {
              let docAlps =
                  makeDocument
                      [ "idle"; "active" ]
                      [ ("idle", Some "active", Some "reset") ]

              let docXState =
                  makeDocument
                      [ "idle"; "active" ]
                      [ ("idle", Some "active", Some "submitMove")
                        ("active", Some "idle", Some "reset") ]

              let artAlps = makeArtifact Alps docAlps
              let artXState = makeArtifact XState docXState
              let rule = CrossFormatRules.eventNameAgreement Alps XState

              let (checks, _) = rule.Check [ artAlps; artXState ]

              let failChecks = checks |> List.filter (fun c -> c.Status = Fail)
              Expect.isGreaterThan (List.length failChecks) 0 "Should report missing event"

              let reason = failChecks.[0].Reason |> Option.defaultValue ""
              Expect.stringContains reason "submitMove" "Should identify missing event name"
              // Verify format identification
              Expect.stringContains reason "Alps" "Should mention Alps"
              Expect.stringContains reason "XState" "Should mention XState"
          }

          test "empty events in one format: missing events reported" {
              let docAlps = makeDocument [ "idle" ] []  // no transitions = no events

              let docXState =
                  makeDocument
                      [ "idle"; "active" ]
                      [ ("idle", Some "active", Some "start") ]

              let artAlps = makeArtifact Alps docAlps
              let artXState = makeArtifact XState docXState
              let rule = CrossFormatRules.eventNameAgreement Alps XState

              let (checks, _) = rule.Check [ artAlps; artXState ]

              let failChecks = checks |> List.filter (fun c -> c.Status = Fail)
              Expect.isGreaterThan (List.length failChecks) 0 "Should report events missing from empty format"

              let reason = failChecks.[0].Reason |> Option.defaultValue ""
              Expect.stringContains reason "start" "Should mention the missing event 'start'"
          }

          test "both have no events passes" {
              let docAlps = makeDocument [ "idle" ] []
              let docXState = makeDocument [ "idle" ] []
              let artAlps = makeArtifact Alps docAlps
              let artXState = makeArtifact XState docXState
              let rule = CrossFormatRules.eventNameAgreement Alps XState

              let (checks, _) = rule.Check [ artAlps; artXState ]

              Expect.equal (List.length checks) 1 "Should produce one check"
              Expect.equal checks.[0].Status Pass "Both empty event sets should pass"
          } ]

// ─────────────────────────────────────────────
// T041: Casing mismatch detection
// ─────────────────────────────────────────────

[<Tests>]
let casingMismatchTests =
    testList
        "Validation.CrossFormatRules.CasingMismatch"
        [ test "casing mismatch 'Active' vs 'active' reported with casing note" {
              let docScxml = makeDocument [ "idle"; "Active" ] []
              let docXState = makeDocument [ "idle"; "active" ] []
              let artScxml = makeArtifact Scxml docScxml
              let artXState = makeArtifact XState docXState
              let rule = CrossFormatRules.stateNameAgreement Scxml XState

              let (checks, _) = rule.Check [ artScxml; artXState ]

              let failChecks = checks |> List.filter (fun c -> c.Status = Fail)
              Expect.isGreaterThan (List.length failChecks) 0 "Casing mismatch should be reported as failure"

              // CRITICAL: The failure description must explicitly mention casing
              let allReasons =
                  failChecks
                  |> List.choose (fun c -> c.Reason)
                  |> String.concat " "

              Expect.stringContains allReasons "casing" "Failure should explicitly mention casing difference"
          }

          test "exact match not flagged with casing note" {
              let docScxml = makeDocument [ "idle"; "Active" ] []
              let docXState = makeDocument [ "idle"; "Active" ] []
              let artScxml = makeArtifact Scxml docScxml
              let artXState = makeArtifact XState docXState
              let rule = CrossFormatRules.stateNameAgreement Scxml XState

              let (checks, _) = rule.Check [ artScxml; artXState ]

              Expect.equal (List.length checks) 1 "Should produce one check"
              Expect.equal checks.[0].Status Pass "Exact match should pass"
          }

          test "multiple casing mismatches both flagged" {
              let docScxml = makeDocument [ "idle"; "Active"; "Done" ] []
              let docXState = makeDocument [ "idle"; "active"; "done" ] []
              let artScxml = makeArtifact Scxml docScxml
              let artXState = makeArtifact XState docXState
              let rule = CrossFormatRules.stateNameAgreement Scxml XState

              let (checks, _) = rule.Check [ artScxml; artXState ]

              let failChecks = checks |> List.filter (fun c -> c.Status = Fail)
              // Should report Active/active and Done/done mismatches in both directions
              Expect.isGreaterThan (List.length failChecks) 2 "Should report multiple casing mismatches"

              let allReasons =
                  failChecks
                  |> List.choose (fun c -> c.Reason)
                  |> String.concat " "

              // Each casing mismatch should have a casing note
              let casingNoteCount =
                  failChecks
                  |> List.choose (fun c -> c.Reason)
                  |> List.filter (fun r -> r.Contains("casing"))
                  |> List.length

              Expect.isGreaterThan casingNoteCount 0 "At least some failures should have casing notes"
          }

          test "event casing mismatch detected" {
              let docScxml =
                  makeDocument
                      [ "idle"; "active" ]
                      [ ("idle", Some "active", Some "Submit") ]

              let docXState =
                  makeDocument
                      [ "idle"; "active" ]
                      [ ("idle", Some "active", Some "submit") ]

              let artScxml = makeArtifact Scxml docScxml
              let artXState = makeArtifact XState docXState
              let rule = CrossFormatRules.eventNameAgreement Scxml XState

              let (checks, _) = rule.Check [ artScxml; artXState ]

              let failChecks = checks |> List.filter (fun c -> c.Status = Fail)
              Expect.isGreaterThan (List.length failChecks) 0 "Event casing mismatch should be reported"

              let allReasons =
                  failChecks
                  |> List.choose (fun c -> c.Reason)
                  |> String.concat " "

              Expect.stringContains allReasons "casing" "Event casing mismatch should mention casing"
          } ]

// ─────────────────────────────────────────────
// T042: Multi-format validation
// ─────────────────────────────────────────────

[<Tests>]
let multiFormatTests =
    testList
        "Validation.CrossFormatRules.MultiFormat"
        [ test "all 5 formats consistent: all checks pass, zero skipped" {
              let states = [ "idle"; "active"; "done" ]

              let transitions =
                  [ ("idle", Some "active", Some "start")
                    ("active", Some "done", Some "finish") ]

              let artifacts =
                  makeConsistentArtifacts [ Wsd; Alps; Scxml; Smcat; XState ] states transitions

              let report = Validator.validate CrossFormatRules.rules artifacts

              Expect.equal report.TotalFailures 0 "All consistent formats should have no failures"
              Expect.equal report.TotalSkipped 0 "All formats present, no rules should be skipped"
              // 30 rules (10 pairs x 3 check types), each producing 1 pass check
              Expect.equal report.TotalChecks 30 "All 30 cross-format rules should execute"
          }

          test "3 of 5 formats: applicable rules run, others skipped" {
              let states = [ "idle"; "active"; "done" ]

              let transitions =
                  [ ("idle", Some "active", Some "start")
                    ("active", Some "done", Some "finish") ]

              let artifacts =
                  makeConsistentArtifacts [ Scxml; XState; Smcat ] states transitions

              let report = Validator.validate CrossFormatRules.rules artifacts

              Expect.equal report.TotalFailures 0 "Consistent formats should have no failures"

              // 10 pairs total, 3 of which involve only Scxml/XState/Smcat:
              //   Scxml-XState, Scxml-Smcat, Smcat-XState
              // That's 3 pairs x 3 check types = 9 executed
              // The other 7 pairs involve Wsd or Alps, so 7 x 3 = 21 skipped
              Expect.equal report.TotalChecks 9 "9 rules should execute (3 pairs x 3 check types)"
              Expect.equal report.TotalSkipped 21 "21 rules should be skipped (7 pairs x 3 check types)"
          }

          test "partial mismatch: SCXML-XState agree, smcat has extra 'maintenance'" {
              let docConsistent = makeDocument [ "idle"; "active" ] []
              let docSmcat = makeDocument [ "idle"; "active"; "maintenance" ] []

              let artifacts =
                  [ makeArtifact Scxml docConsistent
                    makeArtifact XState docConsistent
                    makeArtifact Smcat docSmcat ]

              let report = Validator.validate CrossFormatRules.rules artifacts

              // Scxml-XState state name agreement should pass (same states)
              let scxmlXstateStateChecks =
                  report.Checks
                  |> List.filter (fun c ->
                      c.Name.Contains("Scxml") && c.Name.Contains("XState") && c.Name.Contains("state name"))

              let scxmlXstatePasses =
                  scxmlXstateStateChecks
                  |> List.filter (fun c -> c.Status = Pass)

              Expect.isGreaterThan (List.length scxmlXstatePasses) 0 "Scxml-XState state name agreement should pass"

              // Smcat-Scxml and Smcat-XState should have failures for 'maintenance'
              let smcatFailChecks =
                  report.Checks
                  |> List.filter (fun c ->
                      c.Name.Contains("maintenance") && c.Status = Fail)

              Expect.isGreaterThan (List.length smcatFailChecks) 0 "Should report 'maintenance' failures for smcat pairs"
          }

          test "no artifacts: all cross-format rules skipped" {
              let report = Validator.validate CrossFormatRules.rules []

              Expect.equal report.TotalChecks 0 "No checks should execute"
              Expect.equal report.TotalSkipped 30 "All 30 rules should be skipped"
              Expect.equal report.TotalFailures 0 "No failures"
          }

          test "single artifact: all cross-format rules skipped" {
              let doc = makeDocument [ "idle"; "active" ] []
              let artifacts = [ makeArtifact Scxml doc ]

              let report = Validator.validate CrossFormatRules.rules artifacts

              Expect.equal report.TotalChecks 0 "No cross-format checks should execute with single artifact"
              Expect.equal report.TotalSkipped 30 "All 30 cross-format rules should be skipped"

              // But self-consistency rules should still run
              let selfReport = Validator.validate SelfConsistencyRules.rules artifacts
              Expect.isGreaterThan selfReport.TotalChecks 0 "Self-consistency rules should still run"
          } ]
