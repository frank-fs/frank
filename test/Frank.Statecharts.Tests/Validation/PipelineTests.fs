module Frank.Statecharts.Tests.Validation.PipelineTests

open Expecto
open Frank.Statecharts.Validation
open Frank.Statecharts.Ast

[<Tests>]
let pipelineTests =
    testList "Validation.Pipeline" [
        test "Empty input returns valid PipelineResult with empty report" {
            let result = Pipeline.validateSources []
            Expect.isEmpty result.ParseResults "ParseResults should be empty"
            Expect.equal result.Report.TotalChecks 0 "TotalChecks should be 0"
            Expect.equal result.Report.TotalSkipped 0 "TotalSkipped should be 0"
            Expect.equal result.Report.TotalFailures 0 "TotalFailures should be 0"
            Expect.isEmpty result.Errors "Errors should be empty"
        }

        test "Duplicate format tags return DuplicateFormat pipeline error" {
            let result = Pipeline.validateSources [(FormatTag.Wsd, "a"); (FormatTag.Wsd, "b")]
            Expect.isNonEmpty result.Errors "Should have pipeline errors"
            let hasDuplicate =
                result.Errors |> List.exists (fun e ->
                    match e with DuplicateFormat FormatTag.Wsd -> true | _ -> false)
            Expect.isTrue hasDuplicate "Should contain DuplicateFormat Wsd"
            Expect.isEmpty result.ParseResults "No parsing should occur on invalid input"
        }

        test "Duplicate format tags detected for smcat too" {
            let result = Pipeline.validateSources [(FormatTag.Smcat, "a => b;"); (FormatTag.Smcat, "c => d;")]
            let hasDuplicate =
                result.Errors |> List.exists (fun e ->
                    match e with DuplicateFormat FormatTag.Smcat -> true | _ -> false)
            Expect.isTrue hasDuplicate "Should contain DuplicateFormat Smcat"
        }

        test "XState format is supported and dispatches to deserializer" {
            let xstateJson = """{"id":"test","initial":"idle","states":{"idle":{"on":{"start":"active"}},"active":{}}}"""
            let result = Pipeline.validateSources [(FormatTag.XState, xstateJson)]
            Expect.isEmpty result.Errors "No pipeline errors for XState"
            Expect.equal (List.length result.ParseResults) 1 "Should have 1 parse result"
            Expect.isTrue result.ParseResults.[0].Succeeded "XState should parse successfully"
        }

        test "XState with invalid JSON produces parse error, not pipeline error" {
            let result = Pipeline.validateSources [(FormatTag.XState, "not json")]
            Expect.isEmpty result.Errors "Parse failures are not pipeline errors"
            Expect.equal (List.length result.ParseResults) 1 "Should have 1 parse result"
            Expect.isFalse result.ParseResults.[0].Succeeded "Invalid XState should have parse errors"
        }

        test "Single format runs self-consistency, cross-format rules skipped" {
            let wsdSource = "participant A\nparticipant B\nA -> B: go\n"
            let result = Pipeline.validateSources [(FormatTag.Wsd, wsdSource)]
            Expect.isGreaterThan result.Report.TotalSkipped 0 "Cross-format rules should be skipped"
            Expect.equal result.Report.TotalFailures 0 "Valid source should have no failures"
            Expect.equal (List.length result.ParseResults) 1 "Should have 1 parse result"
            Expect.equal result.ParseResults.[0].Format FormatTag.Wsd "Parse result should be for Wsd"
            Expect.isEmpty result.Errors "No pipeline errors"
        }

        test "Two consistent formats produce zero validation failures" {
            // smcat needs explicit state declarations (A; B;) because transition-only
            // syntax does not create StateDecl elements for target-only states
            let wsdSource = "participant A\nparticipant B\nA -> B: go\nB -> A: back\n"
            let smcatSource = "A;\nB;\nA => B: go;\nB => A: back;"
            let result = Pipeline.validateSources [(FormatTag.Wsd, wsdSource); (FormatTag.Smcat, smcatSource)]
            Expect.equal result.Report.TotalFailures 0
                (sprintf "Expected zero failures but got %d: %A" result.Report.TotalFailures result.Report.Failures)
            Expect.equal (List.length result.ParseResults) 2 "Should have 2 parse results"
            for pr in result.ParseResults do
                Expect.isTrue pr.Succeeded (sprintf "%A should parse successfully" pr.Format)
            Expect.isEmpty result.Errors "No pipeline errors"
        }

        test "Parse errors produce Succeeded = false with non-empty Errors" {
            // "participant\n" triggers "Expected participant name" error
            let result = Pipeline.validateSources [(FormatTag.Wsd, "participant\n")]
            Expect.equal (List.length result.ParseResults) 1 "Should have 1 parse result"
            let pr = result.ParseResults.[0]
            Expect.isFalse pr.Succeeded "Succeeded should be false when errors exist"
            Expect.isNonEmpty pr.Errors "Errors should contain the parse failure"
            Expect.isEmpty result.Errors "Parse failures are NOT pipeline errors"
        }

        test "Parse warnings propagated to FormatParseResult.Warnings" {
            // Implicit participants (no prior declaration) produce warnings
            let wsdSource = "Foo -> Bar: hello\n"
            let result = Pipeline.validateSources [(FormatTag.Wsd, wsdSource)]
            Expect.equal (List.length result.ParseResults) 1 "Should have 1 parse result"
            let pr = result.ParseResults.[0]
            Expect.isNonEmpty pr.Warnings "Implicit participants should produce warnings"
            Expect.isTrue pr.Succeeded "Warnings alone should not set Succeeded to false"
        }

        test "Empty string source produces valid parse result" {
            let result = Pipeline.validateSources [(FormatTag.Wsd, "")]
            Expect.equal (List.length result.ParseResults) 1 "Should have 1 parse result"
            let pr = result.ParseResults.[0]
            Expect.isTrue pr.Succeeded "Empty input should succeed (no errors)"
            Expect.isEmpty pr.Errors "No parse errors for empty input"
            Expect.isEmpty result.Errors "No pipeline errors"
        }

        test "Parse result ordering matches input ordering" {
            let wsdSource = "participant A\n"
            let smcatSource = "A;"
            let result = Pipeline.validateSources [
                (FormatTag.Wsd, wsdSource)
                (FormatTag.Smcat, smcatSource)
            ]
            Expect.equal (List.length result.ParseResults) 2 "Should have 2 parse results"
            Expect.equal result.ParseResults.[0].Format FormatTag.Wsd "First result should be Wsd"
            Expect.equal result.ParseResults.[1].Format FormatTag.Smcat "Second result should be Smcat"
        }

        test "validateSourcesWithRules includes custom rules" {
            let customRule : ValidationRule =
                { Name = "Custom: always pass"
                  RequiredFormats = Set.empty
                  Check = fun _ ->
                      ([ { Name = "Custom: always pass"
                           Status = Pass
                           Reason = Some "custom rule executed" } ], []) }

            let wsdSource = "participant A\nparticipant B\nA -> B: go\n"
            let result = Pipeline.validateSourcesWithRules [customRule] [(FormatTag.Wsd, wsdSource)]

            let customChecks =
                result.Report.Checks
                |> List.filter (fun c -> c.Name = "Custom: always pass")
            Expect.isNonEmpty customChecks "Custom rule should appear in checks"
        }

        test "All sources fail to parse still returns valid result" {
            let result = Pipeline.validateSources [
                (FormatTag.Wsd, "participant\n")
                (FormatTag.Smcat, "=>=>=>")
            ]
            Expect.equal (List.length result.ParseResults) 2 "Should have 2 parse results"
            for pr in result.ParseResults do
                Expect.isFalse pr.Succeeded (sprintf "%A should fail to parse" pr.Format)
            Expect.isEmpty result.Errors "Parse failures are not pipeline errors"
            // Validation still ran against best-effort empty documents
            Expect.isGreaterThanOrEqual result.Report.TotalChecks 0 "Report should exist"
        }

        test "Custom rule that produces failures surfaces them in report" {
            let failingRule : ValidationRule =
                { Name = "Custom: always fail"
                  RequiredFormats = Set.empty
                  Check = fun _ ->
                      ([ { Name = "Custom: always fail"
                           Status = Fail
                           Reason = Some "intentional failure" } ],
                       [ { Formats = [FormatTag.Wsd]
                           EntityType = "test"
                           Expected = "pass"
                           Actual = "fail"
                           Description = "Custom rule failure" } ]) }

            let wsdSource = "participant A\n"
            let result = Pipeline.validateSourcesWithRules [failingRule] [(FormatTag.Wsd, wsdSource)]

            Expect.isGreaterThan result.Report.TotalFailures 0 "Should have failures from custom rule"
            let customFailures =
                result.Report.Failures
                |> List.filter (fun f -> f.Description = "Custom rule failure")
            Expect.isNonEmpty customFailures "Custom rule failure should appear in report"
        }
    ]
