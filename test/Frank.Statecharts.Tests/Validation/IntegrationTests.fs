module Frank.Statecharts.Tests.Validation.IntegrationTests

open Expecto
open Frank.Statecharts.Validation
open Frank.Statecharts.Ast

// ─────────────────────────────────────────────
// Test Helpers
// ─────────────────────────────────────────────

/// Create a state element with the given identifier.
let makeState (id: string) : StateNode =
    { Identifier = Some id
      Label = None
      Kind = Regular
      Children = []
      Activities = None
      Position = None
      Annotations = [] }

/// Create a transition element with the given source, optional target, and optional event.
let makeTransition (source: string) (target: string option) (event: string option) : TransitionEdge =
    { Source = source
      Target = target
      Event = event
      Guard = None
      GuardHref = None
      Action = None
      Parameters = []
      Position = None
      Annotations = [] }

/// Build a StatechartDocument from states and transitions.
let makeDocument (states: StateNode list) (transitions: TransitionEdge list) : StatechartDocument =
    { Title = None
      InitialStateId = None
      Elements =
        (states |> List.map StateDecl) @ (transitions |> List.map TransitionElement)
      DataEntries = []
      Annotations = [] }

/// Build a FormatArtifact from a format tag and a document.
let makeArtifact (format: FormatTag) (doc: StatechartDocument) : FormatArtifact = { Format = format; Document = doc }

// ─────────────────────────────────────────────
// T046: Full pipeline integration test
// ─────────────────────────────────────────────

[<Tests>]
let fullPipelineTests =
    testList
        "Validation.Integration.FullPipeline"
        [
          // SC-002: Zero false positives on fully consistent 5-format artifact set
          test "Fully consistent 5-format tic-tac-toe artifact set produces zero failures" {
              let states =
                  [ makeState "idle"
                    makeState "playerX"
                    makeState "playerO"
                    makeState "gameOver" ]

              let transitions =
                  [ makeTransition "idle" (Some "playerX") (Some "start")
                    makeTransition "playerX" (Some "playerO") (Some "move")
                    makeTransition "playerO" (Some "playerX") (Some "move")
                    makeTransition "playerX" (Some "gameOver") (Some "win")
                    makeTransition "playerO" (Some "gameOver") (Some "win") ]

              let doc = makeDocument states transitions

              // Create 5 artifacts with identical documents
              let artifacts =
                  [ Wsd; Alps; Scxml; Smcat; XState ]
                  |> List.map (fun tag -> makeArtifact tag doc)

              let allRules = SelfConsistencyRules.rules @ CrossFormatRules.rules
              let report = Validator.validate allRules artifacts

              Expect.equal report.TotalFailures 0 (sprintf "Expected zero failures but got %d: %A" report.TotalFailures report.Failures)
          }

          // SC-001: Validator identifies all intentionally introduced mismatches (10+ distinct)
          test "10+ distinct cross-format inconsistencies detected" {
              let baseStates =
                  [ makeState "idle"
                    makeState "playerX"
                    makeState "playerO"
                    makeState "gameOver" ]

              let baseTransitions =
                  [ makeTransition "idle" (Some "playerX") (Some "start")
                    makeTransition "playerX" (Some "playerO") (Some "move")
                    makeTransition "playerO" (Some "playerX") (Some "move")
                    makeTransition "playerX" (Some "gameOver") (Some "win")
                    makeTransition "playerO" (Some "gameOver") (Some "win") ]

              let baseDoc = makeDocument baseStates baseTransitions

              // Wsd: missing state "gameOver" (removed from states, transitions still reference it)
              let wsdStates =
                  [ makeState "idle"; makeState "playerX"; makeState "playerO" ]

              let wsdDoc = makeDocument wsdStates baseTransitions

              // Alps: extra state "archived"
              let alpsStates = baseStates @ [ makeState "archived" ]
              let alpsDoc = makeDocument alpsStates baseTransitions

              // SCXML: extra state "review"
              let scxmlStates = baseStates @ [ makeState "review" ]
              let scxmlDoc = makeDocument scxmlStates baseTransitions

              // smcat: state "Idle" (casing mismatch with "idle")
              let smcatStates =
                  [ makeState "Idle"
                    makeState "playerX"
                    makeState "playerO"
                    makeState "gameOver" ]

              let smcatTransitions =
                  [ makeTransition "Idle" (Some "playerX") (Some "start")
                    makeTransition "playerX" (Some "playerO") (Some "move")
                    makeTransition "playerO" (Some "playerX") (Some "move")
                    makeTransition "playerX" (Some "gameOver") (Some "win")
                    makeTransition "playerO" (Some "gameOver") (Some "win") ]

              let smcatDoc = makeDocument smcatStates smcatTransitions

              // XState: missing event "start"
              let xstateTransitions =
                  [ makeTransition "idle" (Some "playerX") None // missing "start" event
                    makeTransition "playerX" (Some "playerO") (Some "move")
                    makeTransition "playerO" (Some "playerX") (Some "move")
                    makeTransition "playerX" (Some "gameOver") (Some "win")
                    makeTransition "playerO" (Some "gameOver") (Some "win") ]

              let xstateDoc = makeDocument baseStates xstateTransitions

              let artifacts =
                  [ makeArtifact Wsd wsdDoc
                    makeArtifact Alps alpsDoc
                    makeArtifact Scxml scxmlDoc
                    makeArtifact Smcat smcatDoc
                    makeArtifact XState xstateDoc ]

              let allRules = SelfConsistencyRules.rules @ CrossFormatRules.rules
              let report = Validator.validate allRules artifacts

              // Should detect at least 10 distinct failures:
              // - Wsd missing "gameOver" state -> orphan targets + cross-format mismatches
              // - Alps extra "archived" -> cross-format mismatches
              // - SCXML extra "review" -> cross-format mismatches
              // - smcat "Idle" vs "idle" -> cross-format casing mismatches
              // - XState missing "start" event -> cross-format event mismatches
              Expect.isGreaterThanOrEqual
                  report.TotalFailures
                  10
                  (sprintf "Expected at least 10 failures but got %d" report.TotalFailures)
          }

          // SC-006: New rule registration without modifying validator
          test "Custom rule can be registered without modifying validator module" {
              let customRule: ValidationRule =
                  { Name = "Custom: no duplicate events"
                    RequiredFormats = Set.empty
                    Check =
                      fun artifacts ->
                          let checks =
                              artifacts
                              |> List.collect (fun artifact ->
                                  let events =
                                      AstHelpers.allTransitions artifact.Document
                                      |> List.choose _.Event

                                  let duplicates =
                                      events
                                      |> List.groupBy id
                                      |> List.filter (fun (_, group) -> group.Length > 1)
                                      |> List.map fst

                                  if List.isEmpty duplicates then
                                      [ { Name = sprintf "No duplicate events (%A)" artifact.Format
                                          Status = Pass
                                          Reason = None } ]
                                  else
                                      duplicates
                                      |> List.map (fun dup ->
                                          { Name = sprintf "Duplicate event '%s' (%A)" dup artifact.Format
                                            Status = Fail
                                            Reason = Some(sprintf "Event '%s' appears multiple times" dup) }))

                          (checks, []) }

              let states = [ makeState "A"; makeState "B" ]

              let transitions =
                  [ makeTransition "A" (Some "B") (Some "go")
                    makeTransition "B" (Some "A") (Some "go") ]

              let doc = makeDocument states transitions
              let artifacts = [ makeArtifact Wsd doc ]

              // Register custom rule alongside existing rules
              let allRules = SelfConsistencyRules.rules @ CrossFormatRules.rules @ [ customRule ]
              let report = Validator.validate allRules artifacts

              // The custom rule should appear in the report
              let customChecks =
                  report.Checks
                  |> List.filter (fun c -> c.Name.StartsWith("Duplicate event") || c.Name.StartsWith("No duplicate"))

              Expect.isGreaterThan (List.length customChecks) 0 "Custom rule should produce at least one check"
          }

          // SC-005: Missing formats produce skipped checks (not false failures)
          test "Missing formats produce skipped checks not false failures" {
              let states = [ makeState "A"; makeState "B" ]
              let transitions = [ makeTransition "A" (Some "B") (Some "go") ]
              let doc = makeDocument states transitions

              // Only provide 2 out of 5 formats
              let artifacts = [ makeArtifact Wsd doc; makeArtifact Scxml doc ]

              let allRules = SelfConsistencyRules.rules @ CrossFormatRules.rules
              let report = Validator.validate allRules artifacts

              // Cross-format rules for missing format pairs should be skipped
              Expect.isGreaterThan report.TotalSkipped 0 "Some cross-format rules should be skipped"
              // No failures from skipped rules
              Expect.equal report.TotalFailures 0 "No failures should come from missing formats"
          } ]

// ─────────────────────────────────────────────
// T047: Performance benchmark test
// ─────────────────────────────────────────────

[<Tests>]
let performanceTests =
    testList
        "Validation.Integration.Performance"
        [
          // SC-003: < 1 second for 20 states, 50 transitions, 5 formats
          test "Performance: 20 states, 50 transitions, 5 formats under 1 second" {
              let states = [ for i in 1..20 -> makeState (sprintf "state%d" i) ]

              let transitions =
                  [ for i in 1..50 ->
                        let src = sprintf "state%d" ((i % 20) + 1)
                        let tgt = sprintf "state%d" (((i + 7) % 20) + 1)
                        makeTransition src (Some tgt) (Some(sprintf "event%d" i)) ]

              let doc = makeDocument states transitions

              let artifacts =
                  [ Wsd; Alps; Scxml; Smcat; XState ]
                  |> List.map (fun tag -> makeArtifact tag doc)

              let allRules = SelfConsistencyRules.rules @ CrossFormatRules.rules
              let sw = System.Diagnostics.Stopwatch.StartNew()
              let _report = Validator.validate allRules artifacts
              sw.Stop()

              Expect.isLessThan
                  sw.Elapsed.TotalSeconds
                  1.0
                  (sprintf "Validation took %.3f seconds, expected < 1.0" sw.Elapsed.TotalSeconds)
          } ]

// ─────────────────────────────────────────────
// T048: Diagnostic output quality verification
// ─────────────────────────────────────────────

[<Tests>]
let diagnosticQualityTests =
    testList
        "Validation.Integration.DiagnosticQuality"
        [
          // SC-004: Every failure contains formats, entity type, expected/actual, description
          test "All failures have complete diagnostic information" {
              // Create artifacts with intentional mismatches
              let wsdStates =
                  [ makeState "idle"; makeState "playerX"; makeState "playerO" ] // missing gameOver

              let wsdTransitions =
                  [ makeTransition "idle" (Some "playerX") (Some "start")
                    makeTransition "playerX" (Some "gameOver") (Some "win") ] // orphan target gameOver

              let wsdDoc = makeDocument wsdStates wsdTransitions

              let scxmlStates =
                  [ makeState "idle"
                    makeState "playerX"
                    makeState "playerO"
                    makeState "gameOver" ]

              let scxmlTransitions =
                  [ makeTransition "idle" (Some "playerX") (Some "start")
                    makeTransition "playerX" (Some "gameOver") (Some "win") ]

              let scxmlDoc = makeDocument scxmlStates scxmlTransitions

              let artifacts =
                  [ makeArtifact Wsd wsdDoc; makeArtifact Scxml scxmlDoc ]

              let allRules = SelfConsistencyRules.rules @ CrossFormatRules.rules
              let report = Validator.validate allRules artifacts

              Expect.isGreaterThan report.TotalFailures 0 "Should have some failures to check"

              for failure in report.Failures do
                  Expect.isNonEmpty failure.EntityType "EntityType should not be empty"
                  Expect.isNonEmpty failure.Expected "Expected should not be empty"
                  Expect.isNonEmpty failure.Actual "Actual should not be empty"
                  Expect.isNonEmpty failure.Description "Description should not be empty"
                  Expect.isNonEmpty failure.Formats
                      (sprintf "Formats should not be empty in failure: %s" failure.Description)
          }

          test "Failed checks have a Reason" {
              // Create artifacts with intentional mismatches
              let wsdStates =
                  [ makeState "idle"; makeState "playerX" ]

              let wsdTransitions =
                  [ makeTransition "idle" (Some "missing") (Some "go") ] // orphan target

              let wsdDoc = makeDocument wsdStates wsdTransitions
              let artifacts = [ makeArtifact Wsd wsdDoc ]

              let allRules = SelfConsistencyRules.rules
              let report = Validator.validate allRules artifacts

              let failedChecks =
                  report.Checks |> List.filter (fun c -> c.Status = Fail)

              Expect.isGreaterThan (List.length failedChecks) 0 "Should have some failed checks"

              for check in failedChecks do
                  Expect.isSome check.Reason "Failed checks should have a Reason"
          }

          test "Casing mismatch failures contain explicit casing note" {
              let wsdDoc =
                  makeDocument [ makeState "Active"; makeState "Done" ] [ makeTransition "Active" (Some "Done") (Some "finish") ]

              let scxmlDoc =
                  makeDocument [ makeState "active"; makeState "Done" ] [ makeTransition "active" (Some "Done") (Some "finish") ]

              let artifacts =
                  [ makeArtifact Wsd wsdDoc; makeArtifact Scxml scxmlDoc ]

              let allRules = CrossFormatRules.rules
              let report = Validator.validate allRules artifacts

              // Should have failures for casing mismatch
              Expect.isGreaterThan report.TotalFailures 0 "Should detect casing mismatch"

              // At least one failure should contain a casing note
              let casingFailures =
                  report.Failures
                  |> List.filter (fun f -> f.Description.Contains("casing differs"))

              Expect.isGreaterThan (List.length casingFailures) 0 "At least one failure should note casing difference"
          }

          test "Self-consistency: orphan target failure has correct entity type" {
              let states = [ makeState "A"; makeState "B" ]
              let transitions = [ makeTransition "A" (Some "C") (Some "go") ] // C doesn't exist
              let doc = makeDocument states transitions
              let artifacts = [ makeArtifact Wsd doc ]

              let report = Validator.validate [ SelfConsistencyRules.orphanTransitionTargets ] artifacts

              Expect.equal report.TotalFailures 1 "Should have 1 failure"
              Expect.equal report.Failures.[0].EntityType "transition target" "EntityType should be 'transition target'"
              Expect.isTrue (report.Failures.[0].Formats.Length > 0) "Formats should not be empty"
          } ]
