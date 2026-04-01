module Frank.Statecharts.Tests.Validation.SelfConsistencyTests

open Expecto
open Frank.Statecharts.Validation
open Frank.Statecharts.Ast

// ─────────────────────────────────────────────
// Test Helpers
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
      GuardHref = None
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

let runRule (rule: ValidationRule) (artifacts: FormatArtifact list) =
    rule.Check artifacts |> fst

// ─────────────────────────────────────────────
// T035: Orphan transition targets
// ─────────────────────────────────────────────

[<Tests>]
let orphanTransitionTargetTests =
    testList
        "Validation.SelfConsistencyRules.OrphanTransitionTargets"
        [ test "all targets valid - no orphans" {
              let doc =
                  makeDocument
                      [ "idle"; "active"; "done" ]
                      [ ("idle", Some "active", Some "start")
                        ("active", Some "done", Some "finish") ]

              let artifact = makeArtifact Scxml doc
              let checks = runRule SelfConsistencyRules.orphanTransitionTargets [ artifact ]

              Expect.equal (List.length checks) 1 "Should produce one check"
              Expect.equal checks.[0].Status Pass "All targets valid should pass"
              Expect.isNone checks.[0].Reason "Pass should have no reason"
          }

          test "orphan target 'review' reported as failure" {
              let doc =
                  makeDocument
                      [ "idle"; "active" ]
                      [ ("active", Some "review", Some "submit") ]

              let artifact = makeArtifact Scxml doc
              let checks = runRule SelfConsistencyRules.orphanTransitionTargets [ artifact ]

              let failChecks = checks |> List.filter (fun c -> c.Status = Fail)
              Expect.isGreaterThan (List.length failChecks) 0 "Should have at least one failure"

              let failCheck = failChecks.[0]
              Expect.stringContains failCheck.Name "review" "Failure should identify orphan target 'review'"
              Expect.isSome failCheck.Reason "Failure should have a reason"
              Expect.stringContains failCheck.Reason.Value "review" "Reason should mention 'review'"
          }

          test "internal transition (None target) does not trigger orphan failure" {
              let doc =
                  makeDocument
                      [ "idle"; "active" ]
                      [ ("idle", None, Some "tick") ]

              let artifact = makeArtifact Scxml doc
              let checks = runRule SelfConsistencyRules.orphanTransitionTargets [ artifact ]

              let failChecks = checks |> List.filter (fun c -> c.Status = Fail)
              Expect.isEmpty failChecks "None targets should not produce orphan failures"
          }

          test "multiple orphan targets both reported" {
              let doc =
                  makeDocument
                      [ "idle" ]
                      [ ("idle", Some "missing1", Some "go1")
                        ("idle", Some "missing2", Some "go2") ]

              let artifact = makeArtifact Scxml doc
              let checks = runRule SelfConsistencyRules.orphanTransitionTargets [ artifact ]

              let failChecks = checks |> List.filter (fun c -> c.Status = Fail)
              Expect.equal (List.length failChecks) 2 "Both orphan targets should be reported"

              let failNames = failChecks |> List.map (fun c -> c.Name) |> String.concat " "
              Expect.stringContains failNames "missing1" "Should report missing1"
              Expect.stringContains failNames "missing2" "Should report missing2"
          } ]

// ─────────────────────────────────────────────
// T036: Duplicate state identifiers
// ─────────────────────────────────────────────

[<Tests>]
let duplicateStateIdentifierTests =
    testList
        "Validation.SelfConsistencyRules.DuplicateStateIdentifiers"
        [ test "no duplicates passes" {
              let doc = makeDocument [ "idle"; "active"; "done" ] []
              let artifact = makeArtifact Scxml doc
              let checks = runRule SelfConsistencyRules.duplicateStateIdentifiers [ artifact ]

              Expect.equal (List.length checks) 1 "Should produce one check"
              Expect.equal checks.[0].Status Pass "No duplicates should pass"
          }

          test "one duplicate 'idle' reported as failure" {
              let doc = makeDocument [ "idle"; "active"; "idle" ] []
              let artifact = makeArtifact Scxml doc
              let checks = runRule SelfConsistencyRules.duplicateStateIdentifiers [ artifact ]

              let failChecks = checks |> List.filter (fun c -> c.Status = Fail)
              Expect.equal (List.length failChecks) 1 "Should report one duplicate"
              Expect.stringContains failChecks.[0].Name "idle" "Should identify 'idle' as duplicate"
              Expect.isSome failChecks.[0].Reason "Failure should have a reason"
              Expect.stringContains failChecks.[0].Reason.Value "idle" "Reason should mention 'idle'"
          }

          test "multiple duplicates both reported" {
              let doc = makeDocument [ "a"; "b"; "a"; "b"; "c" ] []
              let artifact = makeArtifact Scxml doc
              let checks = runRule SelfConsistencyRules.duplicateStateIdentifiers [ artifact ]

              let failChecks = checks |> List.filter (fun c -> c.Status = Fail)
              Expect.equal (List.length failChecks) 2 "Both 'a' and 'b' should be reported"

              let failNames = failChecks |> List.map (fun c -> c.Name) |> String.concat " "
              Expect.stringContains failNames "a" "Should report 'a'"
              Expect.stringContains failNames "b" "Should report 'b'"
          }

          test "triple duplicate reported once not multiple times" {
              let doc = makeDocument [ "x"; "x"; "x" ] []
              let artifact = makeArtifact Scxml doc
              let checks = runRule SelfConsistencyRules.duplicateStateIdentifiers [ artifact ]

              let failChecks = checks |> List.filter (fun c -> c.Status = Fail)
              Expect.equal (List.length failChecks) 1 "Triple duplicate should be reported once"
              Expect.stringContains failChecks.[0].Name "x" "Should identify 'x' as duplicate"
          } ]

// ─────────────────────────────────────────────
// T037: Required AST fields
// ─────────────────────────────────────────────

[<Tests>]
let requiredAstFieldsTests =
    testList
        "Validation.SelfConsistencyRules.RequiredAstFields"
        [ test "all fields populated passes" {
              let doc =
                  makeDocument
                      [ "idle"; "active" ]
                      [ ("idle", Some "active", Some "start") ]

              let artifact = makeArtifact Scxml doc
              let checks = runRule SelfConsistencyRules.requiredAstFields [ artifact ]

              Expect.equal (List.length checks) 1 "Should produce one check"
              Expect.equal checks.[0].Status Pass "All fields populated should pass"
          }

          test "empty state identifier reports failure" {
              let doc =
                  { Title = None
                    InitialStateId = None
                    Elements =
                      [ StateDecl (makeState "")
                        StateDecl (makeState "valid") ]
                    DataEntries = []
                    Annotations = [] }

              let artifact = makeArtifact Scxml doc
              let checks = runRule SelfConsistencyRules.requiredAstFields [ artifact ]

              let failChecks = checks |> List.filter (fun c -> c.Status = Fail)
              Expect.isGreaterThan (List.length failChecks) 0 "Should report empty state identifier"
              Expect.stringContains failChecks.[0].Name "Empty state identifier" "Should identify empty state ID"
          }

          test "whitespace-only state identifier reports failure" {
              let doc =
                  { Title = None
                    InitialStateId = None
                    Elements =
                      [ StateDecl (makeState "  ")
                        StateDecl (makeState "valid") ]
                    DataEntries = []
                    Annotations = [] }

              let artifact = makeArtifact Scxml doc
              let checks = runRule SelfConsistencyRules.requiredAstFields [ artifact ]

              let failChecks = checks |> List.filter (fun c -> c.Status = Fail)
              Expect.isGreaterThan (List.length failChecks) 0 "Should report whitespace-only state identifier"
          }

          test "empty transition source reports failure" {
              let doc =
                  { Title = None
                    InitialStateId = None
                    Elements =
                      [ StateDecl (makeState "idle")
                        TransitionElement (makeTransition "" (Some "idle") (Some "go")) ]
                    DataEntries = []
                    Annotations = [] }

              let artifact = makeArtifact Scxml doc
              let checks = runRule SelfConsistencyRules.requiredAstFields [ artifact ]

              let failChecks = checks |> List.filter (fun c -> c.Status = Fail)
              Expect.isGreaterThan (List.length failChecks) 0 "Should report empty transition source"
              Expect.stringContains failChecks.[0].Name "Empty transition source" "Should identify empty source"
          }

          test "multiple missing fields both reported" {
              let doc =
                  { Title = None
                    InitialStateId = None
                    Elements =
                      [ StateDecl (makeState "")
                        TransitionElement (makeTransition "" (Some "target") (Some "go")) ]
                    DataEntries = []
                    Annotations = [] }

              let artifact = makeArtifact Scxml doc
              let checks = runRule SelfConsistencyRules.requiredAstFields [ artifact ]

              let failChecks = checks |> List.filter (fun c -> c.Status = Fail)
              Expect.equal (List.length failChecks) 2 "Both empty state ID and empty source should be reported"

              let failNames = failChecks |> List.map (fun c -> c.Name) |> String.concat " "
              Expect.stringContains failNames "state identifier" "Should report empty state identifier"
              Expect.stringContains failNames "transition source" "Should report empty transition source"
          } ]

// ─────────────────────────────────────────────
// T038: Isolated states and empty statechart
// ─────────────────────────────────────────────

[<Tests>]
let isolatedStatesTests =
    testList
        "Validation.SelfConsistencyRules.IsolatedStates"
        [ test "isolated state gets Pass with reason (warning, not failure)" {
              let doc =
                  makeDocument
                      [ "idle"; "active"; "orphan" ]
                      [ ("idle", Some "active", Some "start")
                        ("active", Some "idle", Some "reset") ]

              let artifact = makeArtifact Smcat doc
              let checks = runRule SelfConsistencyRules.isolatedStates [ artifact ]

              // Find the check for the isolated 'orphan' state
              let orphanChecks =
                  checks
                  |> List.filter (fun c -> c.Name.Contains("orphan"))

              Expect.isGreaterThan (List.length orphanChecks) 0 "Should report isolated 'orphan' state"

              let orphanCheck = orphanChecks.[0]
              // CRITICAL: Isolated states are warnings, so status must be Pass, NOT Fail
              Expect.equal orphanCheck.Status Pass "Isolated state should have Pass status (warning)"
              Expect.isSome orphanCheck.Reason "Warning should have a reason explaining the isolation"
              Expect.stringContains orphanCheck.Reason.Value "orphan" "Reason should mention the isolated state"
          }

          test "no isolated states when all connected by transitions" {
              let doc =
                  makeDocument
                      [ "idle"; "active"; "done" ]
                      [ ("idle", Some "active", Some "start")
                        ("active", Some "done", Some "finish")
                        ("done", Some "idle", Some "reset") ]

              let artifact = makeArtifact Smcat doc
              let checks = runRule SelfConsistencyRules.isolatedStates [ artifact ]

              Expect.equal (List.length checks) 1 "Should produce one check"
              Expect.equal checks.[0].Status Pass "No isolated states should pass"
              Expect.isNone checks.[0].Reason "No warning when no isolated states"
          }

          test "circular transitions A->B->C->A: all states connected, no infinite loop" {
              let doc =
                  makeDocument
                      [ "A"; "B"; "C" ]
                      [ ("A", Some "B", Some "go1")
                        ("B", Some "C", Some "go2")
                        ("C", Some "A", Some "go3") ]

              let artifact = makeArtifact Scxml doc
              let checks = runRule SelfConsistencyRules.isolatedStates [ artifact ]

              // All states are connected (each appears as source or target)
              Expect.equal (List.length checks) 1 "Should produce one check"
              Expect.equal checks.[0].Status Pass "All states connected should pass"
              Expect.isNone checks.[0].Reason "No warnings for fully connected graph"
          } ]

[<Tests>]
let emptyStatechartTests =
    testList
        "Validation.SelfConsistencyRules.EmptyStatechart"
        [ test "empty statechart gets Pass with warning reason" {
              let doc =
                  { Title = None
                    InitialStateId = None
                    Elements = []
                    DataEntries = []
                    Annotations = [] }

              let artifact = makeArtifact Wsd doc
              let checks = runRule SelfConsistencyRules.emptyStatechart [ artifact ]

              Expect.equal (List.length checks) 1 "Should produce one check"
              Expect.equal checks.[0].Status Pass "Empty statechart should pass (warning, not failure)"
              Expect.isSome checks.[0].Reason "Should have warning reason about empty state machine"
              Expect.stringContains checks.[0].Reason.Value "no states" "Reason should mention no states"
          }

          test "non-empty statechart passes without warning" {
              let doc =
                  makeDocument
                      [ "idle" ]
                      [ ("idle", Some "idle", Some "tick") ]

              let artifact = makeArtifact Wsd doc
              let checks = runRule SelfConsistencyRules.emptyStatechart [ artifact ]

              Expect.equal (List.length checks) 1 "Should produce one check"
              Expect.equal checks.[0].Status Pass "Non-empty statechart should pass"
              Expect.isNone checks.[0].Reason "No warning for non-empty statechart"
          } ]

// ─────────────────────────────────────────────
// Integration: Run all self-consistency rules via Validator
// ─────────────────────────────────────────────

[<Tests>]
let selfConsistencyIntegrationTests =
    testList
        "Validation.SelfConsistencyRules.Integration"
        [ test "valid SCXML artifact passes all self-consistency checks" {
              let doc =
                  makeDocument
                      [ "idle"; "active"; "done" ]
                      [ ("idle", Some "active", Some "start")
                        ("active", Some "done", Some "finish") ]

              let artifact = makeArtifact Scxml doc
              let report = Validator.validate SelfConsistencyRules.rules [ artifact ]

              Expect.equal report.TotalFailures 0 "Valid artifact should have no failures"

              let failChecks =
                  report.Checks |> List.filter (fun c -> c.Status = Fail)

              Expect.isEmpty failChecks "No checks should fail for valid artifact"
          }

          test "Unicode state names handled correctly in self-consistency rules" {
              let doc =
                  makeDocument
                      [ "\u00e9tat_initial"; "\u00e9tat_actif"; "\u00e9tat_final" ]
                      [ ("\u00e9tat_initial", Some "\u00e9tat_actif", Some "d\u00e9marrer")
                        ("\u00e9tat_actif", Some "\u00e9tat_final", Some "terminer") ]

              let artifact = makeArtifact Scxml doc
              let report = Validator.validate SelfConsistencyRules.rules [ artifact ]

              Expect.equal report.TotalFailures 0 "Unicode state names should pass all checks"
          } ]
