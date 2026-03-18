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

        test "Unsupported format tag XState returns UnsupportedFormat error" {
            let result = Pipeline.validateSources [(FormatTag.XState, "anything")]
            Expect.isNonEmpty result.Errors "Should have pipeline errors"
            let hasUnsupported =
                result.Errors |> List.exists (fun e ->
                    match e with UnsupportedFormat FormatTag.XState -> true | _ -> false)
            Expect.isTrue hasUnsupported "Should contain UnsupportedFormat XState"
        }

        test "Mixed supported and unsupported formats: XState produces error, WSD still parsed" {
            let wsdSource = "participant A\nparticipant B\nA -> B: go\n"
            let result = Pipeline.validateSources [(FormatTag.Wsd, wsdSource); (FormatTag.XState, "x")]
            let hasUnsupported =
                result.Errors |> List.exists (fun e ->
                    match e with UnsupportedFormat FormatTag.XState -> true | _ -> false)
            Expect.isTrue hasUnsupported "Should contain UnsupportedFormat XState"
            Expect.equal (List.length result.ParseResults) 1 "WSD should still be parsed"
            Expect.equal result.ParseResults.[0].Format FormatTag.Wsd "Parse result should be for Wsd"
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
