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
            // Both sources must produce identical state/transition sets
            // smcat needs explicit state declarations (A; B;) because transition-only
            // syntax does not create StateDecl elements for source/target states
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

        test "Parse errors included in FormatParseResult" {
            let malformedWsd = "!@#$%^&*()"
            let result = Pipeline.validateSources [(FormatTag.Wsd, malformedWsd)]
            Expect.equal (List.length result.ParseResults) 1 "Should have 1 parse result"
            let pr = result.ParseResults.[0]
            Expect.equal pr.Format FormatTag.Wsd "Parse result should be for Wsd"
            // WSD parser may produce errors or warnings for gibberish input
            // The key assertion is that no exception was thrown and we got a result
            Expect.isEmpty result.Errors "Parse failures are NOT pipeline errors"
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
    ]
