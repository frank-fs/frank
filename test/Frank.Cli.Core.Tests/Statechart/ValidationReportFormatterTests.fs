module Frank.Cli.Core.Tests.Statechart.ValidationReportFormatterTests

open Expecto
open Frank.Statecharts.Validation
open Frank.Cli.Core.Statechart.ValidationReportFormatter

let private emptyReport : ValidationReport =
    { TotalChecks = 0; TotalSkipped = 0; TotalFailures = 0
      Checks = []; Failures = [] }

let private passingReport : ValidationReport =
    { TotalChecks = 2; TotalSkipped = 0; TotalFailures = 0
      Checks =
        [ { Name = "state-count"; Status = Pass; Reason = None }
          { Name = "transition-count"; Status = Pass; Reason = None } ]
      Failures = [] }

let private failingReport : ValidationReport =
    { TotalChecks = 3; TotalSkipped = 1; TotalFailures = 1
      Checks =
        [ { Name = "state-count"; Status = Pass; Reason = None }
          { Name = "state-names"; Status = Fail; Reason = Some "Names differ" }
          { Name = "guard-names"; Status = Skip; Reason = Some "No guards" } ]
      Failures =
        [ { Formats = [ Wsd; Alps ]
            EntityType = "state name"
            Expected = "WaitingForPlayers"
            Actual = "Waiting"
            Description = "State name mismatch" } ] }

[<Tests>]
let tests =
    testList "ValidationReportFormatter" [
        testList "formatText" [
            testCase "empty report shows PASSED" <| fun _ ->
                let text = formatText emptyReport
                Expect.stringContains text "PASSED" "should show PASSED"

            testCase "passing report includes check names" <| fun _ ->
                let text = formatText passingReport
                Expect.stringContains text "state-count" "should include check name"

            testCase "failing report shows FAILED" <| fun _ ->
                let text = formatText failingReport
                Expect.stringContains text "FAILED" "should show FAILED"

            testCase "failing report includes failure details" <| fun _ ->
                let text = formatText failingReport
                Expect.stringContains text "State name mismatch" "should include failure description"
                Expect.stringContains text "WaitingForPlayers" "should include expected value"
        ]

        testList "formatJson" [
            testCase "empty report has status passed" <| fun _ ->
                let json = formatJson emptyReport
                Expect.stringContains json "\"passed\"" "should have passed status"

            testCase "failing report has status failed" <| fun _ ->
                let json = formatJson failingReport
                Expect.stringContains json "\"failed\"" "should have failed status"

            testCase "JSON includes totalChecks" <| fun _ ->
                let json = formatJson failingReport
                Expect.stringContains json "\"totalChecks\": 3" "should include totalChecks"

            testCase "JSON includes failure details" <| fun _ ->
                let json = formatJson failingReport
                Expect.stringContains json "\"entityType\": \"state name\"" "should include entityType"
        ]
    ]
