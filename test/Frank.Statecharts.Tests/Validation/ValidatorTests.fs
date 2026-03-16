module Frank.Statecharts.Tests.Validation.ValidatorTests

open Expecto
open Frank.Statecharts.Validation
open Frank.Statecharts.Ast

// ─────────────────────────────────────────────
// Test Helpers
// ─────────────────────────────────────────────

/// Create a minimal empty StatechartDocument.
let emptyDocument: StatechartDocument =
    { Title = None
      InitialStateId = None
      Elements = []
      DataEntries = []
      Annotations = [] }

/// Create a StatechartDocument with given state identifiers and transitions.
let makeDocument (stateIds: string list) (transitions: (string * string option * string option) list) : StatechartDocument =
    let stateElements =
        stateIds
        |> List.map (fun id ->
            StateDecl
                { Identifier = id
                  Label = None
                  Kind = Regular
                  Children = []
                  Activities = None
                  Position = None
                  Annotations = [] })

    let transitionElements =
        transitions
        |> List.map (fun (source, target, event) ->
            TransitionElement
                { Source = source
                  Target = target
                  Event = event
                  Guard = None
                  Action = None
                  Parameters = []
                  Position = None
                  Annotations = [] })

    { Title = None
      InitialStateId = None
      Elements = stateElements @ transitionElements
      DataEntries = []
      Annotations = [] }

/// Create a FormatArtifact with a given format and document.
let makeArtifact (format: FormatTag) (doc: StatechartDocument) : FormatArtifact =
    { Format = format; Document = doc }

/// Create a simple passing rule.
let passingRule (name: string) : ValidationRule =
    { Name = name
      RequiredFormats = Set.empty
      Check =
        fun _ ->
            [ { Name = name
                Status = Pass
                Reason = None } ] }

/// Create a simple failing rule.
let failingRule (name: string) : ValidationRule =
    { Name = name
      RequiredFormats = Set.empty
      Check =
        fun _ ->
            [ { Name = name
                Status = Fail
                Reason = Some "Test failure" } ] }

/// Create a rule that requires specific formats.
let formatRequiringRule (name: string) (formats: FormatTag Set) : ValidationRule =
    { Name = name
      RequiredFormats = formats
      Check =
        fun _ ->
            [ { Name = name
                Status = Pass
                Reason = None } ] }

// ─────────────────────────────────────────────
// T030: Orchestrator execution tests
// ─────────────────────────────────────────────

[<Tests>]
let orchestratorExecutionTests =
    testList
        "Validation.Validator.Execution"
        [ test "single passing rule produces correct report" {
              let rules = [ passingRule "test rule" ]
              let artifacts = [ makeArtifact Wsd emptyDocument ]
              let report = Validator.validate rules artifacts

              Expect.equal report.TotalChecks 1 "TotalChecks should be 1"
              Expect.equal report.TotalFailures 0 "TotalFailures should be 0"
              Expect.equal report.TotalSkipped 0 "TotalSkipped should be 0"
              Expect.equal (List.length report.Checks) 1 "Should have 1 check"
              Expect.equal report.Checks.[0].Status Pass "Check should pass"
          }

          test "single failing rule produces correct report" {
              let rules = [ failingRule "fail rule" ]
              let artifacts = [ makeArtifact Wsd emptyDocument ]
              let report = Validator.validate rules artifacts

              Expect.equal report.TotalChecks 1 "TotalChecks should be 1"
              Expect.equal report.TotalFailures 1 "TotalFailures should be 1"
              Expect.equal report.TotalSkipped 0 "TotalSkipped should be 0"
              Expect.equal (List.length report.Failures) 1 "Should have 1 failure"
          }

          test "multiple rules aggregated correctly" {
              let rules = [ passingRule "pass rule"; failingRule "fail rule" ]
              let artifacts = [ makeArtifact Wsd emptyDocument ]
              let report = Validator.validate rules artifacts

              Expect.equal report.TotalChecks 2 "TotalChecks should be 2"
              Expect.equal report.TotalFailures 1 "TotalFailures should be 1"
              Expect.equal report.TotalSkipped 0 "TotalSkipped should be 0"
          }

          test "rule returning multiple checks" {
              let multiRule: ValidationRule =
                  { Name = "multi check rule"
                    RequiredFormats = Set.empty
                    Check =
                      fun _ ->
                          [ { Name = "check 1"
                              Status = Pass
                              Reason = None }
                            { Name = "check 2"
                              Status = Pass
                              Reason = None }
                            { Name = "check 3"
                              Status = Fail
                              Reason = Some "found issue" } ] }

              let artifacts = [ makeArtifact Wsd emptyDocument ]
              let report = Validator.validate [ multiRule ] artifacts

              Expect.equal report.TotalChecks 3 "TotalChecks should be 3 (2 pass + 1 fail)"
              Expect.equal report.TotalFailures 1 "TotalFailures should be 1"
              Expect.equal (List.length report.Checks) 3 "Should have 3 checks"
          }

          test "empty rule list produces empty report" {
              let artifacts = [ makeArtifact Wsd emptyDocument ]
              let report = Validator.validate [] artifacts

              Expect.equal report.TotalChecks 0 "TotalChecks should be 0"
              Expect.equal report.TotalSkipped 0 "TotalSkipped should be 0"
              Expect.equal report.TotalFailures 0 "TotalFailures should be 0"
              Expect.isEmpty report.Checks "Checks should be empty"
              Expect.isEmpty report.Failures "Failures should be empty"
          }

          test "rule receives all artifacts" {
              let mutable receivedArtifacts = []

              let inspectingRule: ValidationRule =
                  { Name = "inspect rule"
                    RequiredFormats = Set.empty
                    Check =
                      fun artifacts ->
                          receivedArtifacts <- artifacts

                          [ { Name = "ok"
                              Status = Pass
                              Reason = None } ] }

              let doc1 = makeDocument [ "A" ] []
              let doc2 = makeDocument [ "B" ] []
              let artifacts = [ makeArtifact Wsd doc1; makeArtifact Scxml doc2 ]

              let _ = Validator.validate [ inspectingRule ] artifacts

              Expect.equal (List.length receivedArtifacts) 2 "Rule should receive all artifacts"
              Expect.equal receivedArtifacts.[0].Format Wsd "First artifact should be Wsd"
              Expect.equal receivedArtifacts.[1].Format Scxml "Second artifact should be Scxml"
          } ]

// ─────────────────────────────────────────────
// T031: Orchestrator skip tests
// ─────────────────────────────────────────────

[<Tests>]
let orchestratorSkipTests =
    testList
        "Validation.Validator.Skip"
        [ test "rule with missing format is skipped" {
              let rule = formatRequiringRule "cross-format" (set [ Scxml; XState ])
              let artifacts = [ makeArtifact Scxml emptyDocument ]
              let report = Validator.validate [ rule ] artifacts

              Expect.equal report.TotalSkipped 1 "TotalSkipped should be 1"
              Expect.equal report.TotalChecks 0 "TotalChecks should be 0 (skip not counted)"
              Expect.equal (List.length report.Checks) 1 "Should have 1 check entry (the skip)"
              Expect.equal report.Checks.[0].Status Skip "Check should be Skip"
              Expect.isSome report.Checks.[0].Reason "Skip should have a reason"

              let reason = report.Checks.[0].Reason.Value
              Expect.stringContains reason "XState" "Reason should mention missing format"
          }

          test "rule with all formats present executes normally" {
              let rule = formatRequiringRule "cross-format" (set [ Scxml; XState ])

              let artifacts =
                  [ makeArtifact Scxml emptyDocument
                    makeArtifact XState emptyDocument ]

              let report = Validator.validate [ rule ] artifacts

              Expect.equal report.TotalSkipped 0 "TotalSkipped should be 0"
              Expect.equal report.TotalChecks 1 "TotalChecks should be 1"
              Expect.equal report.Checks.[0].Status Pass "Check should pass"
          }

          test "universal rule (empty RequiredFormats) always executes" {
              let rule =
                  { Name = "universal"
                    RequiredFormats = Set.empty
                    Check =
                      fun _ ->
                          [ { Name = "universal check"
                              Status = Pass
                              Reason = None } ] }

              // Even with only one artifact type, universal rule runs
              let artifacts = [ makeArtifact Wsd emptyDocument ]
              let report = Validator.validate [ rule ] artifacts

              Expect.equal report.TotalSkipped 0 "Universal rule should not be skipped"
              Expect.equal report.TotalChecks 1 "Should execute normally"
          }

          test "universal rule executes with no artifacts" {
              let rule =
                  { Name = "universal"
                    RequiredFormats = Set.empty
                    Check =
                      fun _ ->
                          [ { Name = "universal check"
                              Status = Pass
                              Reason = None } ] }

              let report = Validator.validate [ rule ] []

              Expect.equal report.TotalSkipped 0 "Universal rule should not be skipped even with empty artifacts"
              Expect.equal report.TotalChecks 1 "Should execute"
          }

          test "mixed applicable and non-applicable rules" {
              let universalRule = passingRule "universal"

              let scxmlRule = formatRequiringRule "scxml-only" (set [ Scxml ])

              let crossRule = formatRequiringRule "cross-format" (set [ Scxml; XState ])

              let rules = [ universalRule; scxmlRule; crossRule ]
              let artifacts = [ makeArtifact Scxml emptyDocument ]
              let report = Validator.validate rules artifacts

              Expect.equal report.TotalChecks 2 "2 rules executed (universal + scxml-only)"
              Expect.equal report.TotalSkipped 1 "1 rule skipped (cross-format)"
          }

          test "skip reason lists all missing formats" {
              let rule = formatRequiringRule "needs-3" (set [ Scxml; XState; Alps ])
              let artifacts = [ makeArtifact Scxml emptyDocument ]
              let report = Validator.validate [ rule ] artifacts

              let reason = report.Checks.[0].Reason.Value
              Expect.stringContains reason "XState" "Should mention XState"
              Expect.stringContains reason "Alps" "Should mention Alps"
          } ]

// ─────────────────────────────────────────────
// T032: Exception handling tests (FR-013)
// ─────────────────────────────────────────────

[<Tests>]
let exceptionHandlingTests =
    testList
        "Validation.Validator.ExceptionHandling"
        [ test "rule that throws InvalidOperationException is caught" {
              let throwingRule: ValidationRule =
                  { Name = "throwing rule"
                    RequiredFormats = Set.empty
                    Check = fun _ -> raise (System.InvalidOperationException("test error")) }

              let artifacts = [ makeArtifact Wsd emptyDocument ]
              let report = Validator.validate [ throwingRule ] artifacts

              Expect.equal report.TotalChecks 1 "TotalChecks should be 1 (the fail)"
              Expect.equal report.TotalFailures 1 "TotalFailures should be 1"
              Expect.equal (List.length report.Checks) 1 "Should have 1 check"
              Expect.equal report.Checks.[0].Status Fail "Check status should be Fail"

              // Verify the failure contains rule name and error message
              Expect.equal (List.length report.Failures) 1 "Should have 1 failure"
              let failure = report.Failures.[0]
              Expect.stringContains failure.Description "throwing rule" "Failure should mention rule name"
              Expect.stringContains failure.Description "test error" "Failure should mention error message"
          }

          test "rule that throws does not prevent other rules from running" {
              let throwingRule: ValidationRule =
                  { Name = "throwing rule"
                    RequiredFormats = Set.empty
                    Check = fun _ -> raise (System.InvalidOperationException("boom")) }

              let passingAfter = passingRule "after throwing"
              let rules = [ throwingRule; passingAfter ]
              let artifacts = [ makeArtifact Wsd emptyDocument ]
              let report = Validator.validate rules artifacts

              Expect.equal report.TotalChecks 2 "Both rules should produce checks"
              Expect.equal report.TotalFailures 1 "Only the throwing rule should fail"

              // Verify the passing rule still ran
              let passChecks =
                  report.Checks |> List.filter (fun c -> c.Status = Pass)

              Expect.equal (List.length passChecks) 1 "Should have 1 passing check"
          }

          test "ArgumentException is caught" {
              let throwingRule: ValidationRule =
                  { Name = "arg-error rule"
                    RequiredFormats = Set.empty
                    Check = fun _ -> raise (System.ArgumentException("bad argument")) }

              let artifacts = [ makeArtifact Wsd emptyDocument ]
              let report = Validator.validate [ throwingRule ] artifacts

              Expect.equal report.TotalFailures 1 "Should report 1 failure"
              Expect.stringContains report.Failures.[0].Description "bad argument" "Should contain error message"
          }

          test "NullReferenceException is caught" {
              let throwingRule: ValidationRule =
                  { Name = "null-ref rule"
                    RequiredFormats = Set.empty
                    Check = fun _ -> raise (System.NullReferenceException("null reference")) }

              let artifacts = [ makeArtifact Wsd emptyDocument ]
              let report = Validator.validate [ throwingRule ] artifacts

              Expect.equal report.TotalFailures 1 "Should report 1 failure"
              Expect.stringContains report.Failures.[0].Description "null reference" "Should contain error message"
          }

          test "exception check has Fail status with reason containing exception info" {
              let throwingRule: ValidationRule =
                  { Name = "exploding rule"
                    RequiredFormats = Set.empty
                    Check = fun _ -> failwith "unexpected failure" }

              let artifacts = [ makeArtifact Wsd emptyDocument ]
              let report = Validator.validate [ throwingRule ] artifacts

              let check = report.Checks.[0]
              Expect.equal check.Status Fail "Status should be Fail"
              Expect.equal check.Name "exploding rule" "Check name should be the rule name"
              Expect.isSome check.Reason "Should have a reason"
              Expect.stringContains check.Reason.Value "unexpected failure" "Reason should contain exception message"
          }

          test "multiple throwing rules all caught independently" {
              let throw1: ValidationRule =
                  { Name = "thrower 1"
                    RequiredFormats = Set.empty
                    Check = fun _ -> raise (System.InvalidOperationException("error 1")) }

              let throw2: ValidationRule =
                  { Name = "thrower 2"
                    RequiredFormats = Set.empty
                    Check = fun _ -> raise (System.ArgumentException("error 2")) }

              let artifacts = [ makeArtifact Wsd emptyDocument ]
              let report = Validator.validate [ throw1; throw2 ] artifacts

              Expect.equal report.TotalChecks 2 "Both exceptions should produce checks"
              Expect.equal report.TotalFailures 2 "Both should be failures"
              Expect.equal (List.length report.Failures) 2 "Should have 2 failure entries"
          } ]

// ─────────────────────────────────────────────
// T033: Edge case tests
// ─────────────────────────────────────────────

[<Tests>]
let edgeCaseTests =
    testList
        "Validation.Validator.EdgeCases"
        [ test "empty artifact set with format-requiring rules skips all" {
              let rule = formatRequiringRule "needs-scxml" (set [ Scxml ])
              let report = Validator.validate [ rule ] []

              Expect.equal report.TotalSkipped 1 "Should skip the rule"
              Expect.equal report.TotalChecks 0 "No checks executed"
          }

          test "empty artifact set with universal rules still executes" {
              let rule = passingRule "universal"
              let report = Validator.validate [ rule ] []

              Expect.equal report.TotalChecks 1 "Universal rule should execute"
              Expect.equal report.TotalSkipped 0 "Should not be skipped"
          }

          test "no artifacts and no rules produces zero-valued report" {
              let report = Validator.validate [] []

              Expect.equal report.TotalChecks 0 "TotalChecks should be 0"
              Expect.equal report.TotalSkipped 0 "TotalSkipped should be 0"
              Expect.equal report.TotalFailures 0 "TotalFailures should be 0"
              Expect.isEmpty report.Checks "Checks should be empty"
              Expect.isEmpty report.Failures "Failures should be empty"
          }

          test "all rules skipped produces correct counts" {
              let rules =
                  [ formatRequiringRule "needs-scxml" (set [ Scxml ])
                    formatRequiringRule "needs-xstate" (set [ XState ])
                    formatRequiringRule "needs-alps" (set [ Alps ]) ]

              // Only provide Wsd artifact, none of the required formats
              let artifacts = [ makeArtifact Wsd emptyDocument ]
              let report = Validator.validate rules artifacts

              Expect.equal report.TotalChecks 0 "No checks executed"
              Expect.equal report.TotalSkipped 3 "All 3 rules should be skipped"
              Expect.equal report.TotalFailures 0 "No failures"
          }

          test "Unicode state identifiers handled correctly" {
              let unicodeDoc =
                  makeDocument [ "\u00e9tat"; "\u72b6\u6001"; "\u0441\u043e\u0441\u0442\u043e\u044f\u043d\u0438\u0435" ] []

              let rule: ValidationRule =
                  { Name = "unicode rule"
                    RequiredFormats = Set.empty
                    Check =
                      fun artifacts ->
                          let states =
                              artifacts
                              |> List.collect (fun a -> AstHelpers.stateIdentifiers a.Document |> Set.toList)

                          [ { Name = "unicode states exist"
                              Status =
                                if List.length states = 3 then
                                    Pass
                                else
                                    Fail
                              Reason = None } ] }

              let artifacts = [ makeArtifact Wsd unicodeDoc ]
              let report = Validator.validate [ rule ] artifacts

              Expect.equal report.TotalChecks 1 "Should execute"
              Expect.equal report.Checks.[0].Status Pass "Unicode states should be handled correctly"
          }

          test "report field consistency: TotalChecks equals Pass + Fail count" {
              let multiRule: ValidationRule =
                  { Name = "mixed"
                    RequiredFormats = Set.empty
                    Check =
                      fun _ ->
                          [ { Name = "pass1"
                              Status = Pass
                              Reason = None }
                            { Name = "fail1"
                              Status = Fail
                              Reason = Some "error" }
                            { Name = "pass2"
                              Status = Pass
                              Reason = None } ] }

              let skipRule = formatRequiringRule "skipped" (set [ Alps ])
              let rules = [ multiRule; skipRule ]
              let artifacts = [ makeArtifact Wsd emptyDocument ]
              let report = Validator.validate rules artifacts

              let passCount =
                  report.Checks
                  |> List.filter (fun c -> c.Status = Pass)
                  |> List.length

              let failCount =
                  report.Checks
                  |> List.filter (fun c -> c.Status = Fail)
                  |> List.length

              let skipCount =
                  report.Checks
                  |> List.filter (fun c -> c.Status = Skip)
                  |> List.length

              Expect.equal report.TotalChecks (passCount + failCount) "TotalChecks should equal Pass + Fail count"
              Expect.equal report.TotalSkipped skipCount "TotalSkipped should equal Skip count"
              Expect.equal report.TotalFailures (List.length report.Failures) "TotalFailures should equal Failures.Length"
          }

          test "large number of rules processed correctly" {
              let rules =
                  [ for i in 1..50 do
                        if i % 3 = 0 then
                            failingRule (sprintf "fail-%d" i)
                        else
                            passingRule (sprintf "pass-%d" i) ]

              let artifacts = [ makeArtifact Wsd emptyDocument ]
              let report = Validator.validate rules artifacts

              let expectedFails = [ 1..50 ] |> List.filter (fun i -> i % 3 = 0) |> List.length
              let expectedPasses = 50 - expectedFails

              Expect.equal report.TotalChecks 50 "All 50 rules should produce checks"
              Expect.equal report.TotalFailures expectedFails "Correct number of failures"

              let actualPasses =
                  report.Checks
                  |> List.filter (fun c -> c.Status = Pass)
                  |> List.length

              Expect.equal actualPasses expectedPasses "Correct number of passes"
          }

          test "empty statechart document processes without error" {
              let rule: ValidationRule =
                  { Name = "empty doc rule"
                    RequiredFormats = Set.empty
                    Check =
                      fun artifacts ->
                          let states =
                              artifacts
                              |> List.collect (fun a -> AstHelpers.allStates a.Document)

                          let transitions =
                              artifacts
                              |> List.collect (fun a -> AstHelpers.allTransitions a.Document)

                          [ { Name = "empty doc check"
                              Status =
                                if List.isEmpty states && List.isEmpty transitions then
                                    Pass
                                else
                                    Fail
                              Reason = None } ] }

              let artifacts = [ makeArtifact Wsd emptyDocument ]
              let report = Validator.validate [ rule ] artifacts

              Expect.equal report.Checks.[0].Status Pass "Empty document should produce pass"
          }

          test "checks preserve order from rules" {
              let rules =
                  [ passingRule "first"
                    failingRule "second"
                    passingRule "third" ]

              let artifacts = [ makeArtifact Wsd emptyDocument ]
              let report = Validator.validate rules artifacts

              Expect.equal report.Checks.[0].Name "first" "First check name"
              Expect.equal report.Checks.[1].Name "second" "Second check name"
              Expect.equal report.Checks.[2].Name "third" "Third check name"
          } ]
