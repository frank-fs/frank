module Frank.Statecharts.Tests.Validation.TypeTests

open Expecto
open Frank.Statecharts.Validation
open Frank.Statecharts.Ast

// Test helper: create a minimal StatechartDocument
let emptyDocument: StatechartDocument =
    { Title = None
      InitialStateId = None
      Elements = []
      DataEntries = []
      Annotations = [] }

[<Tests>]
let formatTagTests =
    testList
        "Validation.Types.FormatTag"
        [ test "FormatTag cases are distinct" {
              Expect.notEqual Wsd Alps "Wsd <> Alps"
              Expect.notEqual Scxml Smcat "Scxml <> Smcat"
              Expect.notEqual XState Wsd "XState <> Wsd"
              Expect.notEqual Alps Scxml "Alps <> Scxml"
              Expect.notEqual Smcat XState "Smcat <> XState"
              Expect.notEqual AlpsXml Alps "AlpsXml <> Alps"
              Expect.notEqual AlpsXml Wsd "AlpsXml <> Wsd"
              Expect.notEqual AlpsXml Scxml "AlpsXml <> Scxml"
          }

          test "FormatTag structural equality" {
              Expect.equal Wsd Wsd "Wsd = Wsd"
              Expect.equal Alps Alps "Alps = Alps"
              Expect.equal Scxml Scxml "Scxml = Scxml"
              Expect.equal Smcat Smcat "Smcat = Smcat"
              Expect.equal XState XState "XState = XState"
              Expect.equal AlpsXml AlpsXml "AlpsXml = AlpsXml"
          }

          test "All six FormatTag cases exist" {
              let tags = [ Wsd; Alps; AlpsXml; Scxml; Smcat; XState ]
              Expect.equal (List.length tags) 6 "Should have 6 format tags"
              Expect.equal (tags |> Set.ofList |> Set.count) 6 "All tags should be distinct"
          } ]

[<Tests>]
let formatArtifactTests =
    testList
        "Validation.Types.FormatArtifact"
        [ test "FormatArtifact construction with each format tag" {
              let artifact = { Format = Scxml; Document = emptyDocument }
              Expect.equal artifact.Format Scxml "Format should be Scxml"
              Expect.equal artifact.Document emptyDocument "Document should be emptyDocument"
          }

          test "FormatArtifact structural equality" {
              let a = { Format = Wsd; Document = emptyDocument }
              let b = { Format = Wsd; Document = emptyDocument }
              Expect.equal a b "Same format and document should be equal"
          }

          test "FormatArtifact inequality with different formats" {
              let a = { Format = Wsd; Document = emptyDocument }
              let b = { Format = Alps; Document = emptyDocument }
              Expect.notEqual a b "Different formats should not be equal"
          }

          test "FormatArtifact inequality with different documents" {
              let doc2 =
                  { emptyDocument with
                      Title = Some "test" }

              let a = { Format = Wsd; Document = emptyDocument }
              let b = { Format = Wsd; Document = doc2 }
              Expect.notEqual a b "Different documents should not be equal"
          } ]

[<Tests>]
let checkStatusTests =
    testList
        "Validation.Types.CheckStatus"
        [ test "CheckStatus cases are distinct" {
              Expect.notEqual Pass Fail "Pass <> Fail"
              Expect.notEqual Fail Skip "Fail <> Skip"
              Expect.notEqual Pass Skip "Pass <> Skip"
          }

          test "CheckStatus structural equality" {
              Expect.equal Pass Pass "Pass = Pass"
              Expect.equal Fail Fail "Fail = Fail"
              Expect.equal Skip Skip "Skip = Skip"
          } ]

[<Tests>]
let validationCheckTests =
    testList
        "Validation.Types.ValidationCheck"
        [ test "ValidationCheck construction with Pass" {
              let check =
                  { Name = "test check"
                    Status = Pass
                    Reason = None }

              Expect.equal check.Name "test check" "Name should match"
              Expect.equal check.Status Pass "Status should be Pass"
              Expect.isNone check.Reason "Reason should be None"
          }

          test "ValidationCheck construction with Fail and reason" {
              let check =
                  { Name = "failing check"
                    Status = Fail
                    Reason = Some "Mismatch detected" }

              Expect.equal check.Status Fail "Status should be Fail"
              Expect.isSome check.Reason "Reason should be Some"
              Expect.equal check.Reason (Some "Mismatch detected") "Reason should match"
          }

          test "ValidationCheck construction with Skip and reason" {
              let check =
                  { Name = "skipped check"
                    Status = Skip
                    Reason = Some "Missing SCXML" }

              Expect.equal check.Status Skip "Status should be Skip"
              Expect.isSome check.Reason "Reason should be Some"
          }

          test "ValidationCheck structural equality" {
              let a =
                  { Name = "test"
                    Status = Pass
                    Reason = None }

              let b =
                  { Name = "test"
                    Status = Pass
                    Reason = None }

              Expect.equal a b "Identical checks should be equal"
          }

          test "ValidationCheck inequality with different status" {
              let a =
                  { Name = "test"
                    Status = Pass
                    Reason = None }

              let b =
                  { Name = "test"
                    Status = Fail
                    Reason = None }

              Expect.notEqual a b "Different status should not be equal"
          } ]

[<Tests>]
let validationFailureTests =
    testList
        "Validation.Types.ValidationFailure"
        [ test "ValidationFailure construction" {
              let failure =
                  { Formats = [ Scxml; XState ]
                    EntityType = "state name"
                    Expected = "waiting"
                    Actual = "pending"
                    Description = "State name mismatch" }

              Expect.equal failure.Formats [ Scxml; XState ] "Formats should match"
              Expect.equal failure.EntityType "state name" "EntityType should match"
              Expect.equal failure.Expected "waiting" "Expected should match"
              Expect.equal failure.Actual "pending" "Actual should match"
              Expect.equal failure.Description "State name mismatch" "Description should match"
          }

          test "ValidationFailure structural equality" {
              let a =
                  { Formats = [ Wsd ]
                    EntityType = "state"
                    Expected = "A"
                    Actual = "B"
                    Description = "mismatch" }

              let b =
                  { Formats = [ Wsd ]
                    EntityType = "state"
                    Expected = "A"
                    Actual = "B"
                    Description = "mismatch" }

              Expect.equal a b "Identical failures should be equal"
          }

          test "ValidationFailure inequality with different fields" {
              let a =
                  { Formats = [ Wsd ]
                    EntityType = "state"
                    Expected = "A"
                    Actual = "B"
                    Description = "mismatch" }

              let b =
                  { Formats = [ Alps ]
                    EntityType = "state"
                    Expected = "A"
                    Actual = "B"
                    Description = "mismatch" }

              Expect.notEqual a b "Different formats should not be equal"
          }

          test "ValidationFailure with single format (self-consistency)" {
              let failure =
                  { Formats = [ Scxml ]
                    EntityType = "transition target"
                    Expected = "state 'review' should exist"
                    Actual = "state 'review' not found"
                    Description = "Orphan transition target" }

              Expect.equal (List.length failure.Formats) 1 "Self-consistency failure has 1 format"
          }

          test "ValidationFailure with two formats (cross-format)" {
              let failure =
                  { Formats = [ Scxml; XState ]
                    EntityType = "state name"
                    Expected = "waiting"
                    Actual = "pending"
                    Description = "State name mismatch between SCXML and XState" }

              Expect.equal (List.length failure.Formats) 2 "Cross-format failure has 2 formats"
          } ]

[<Tests>]
let validationReportTests =
    testList
        "Validation.Types.ValidationReport"
        [ test "ValidationReport construction" {
              let report =
                  { TotalChecks = 5
                    TotalSkipped = 2
                    TotalFailures = 1
                    Checks = []
                    Failures = [] }

              Expect.equal report.TotalChecks 5 "TotalChecks should be 5"
              Expect.equal report.TotalSkipped 2 "TotalSkipped should be 2"
              Expect.equal report.TotalFailures 1 "TotalFailures should be 1"
          }

          test "ValidationReport structural equality" {
              let a =
                  { TotalChecks = 0
                    TotalSkipped = 0
                    TotalFailures = 0
                    Checks = []
                    Failures = [] }

              let b =
                  { TotalChecks = 0
                    TotalSkipped = 0
                    TotalFailures = 0
                    Checks = []
                    Failures = [] }

              Expect.equal a b "Empty reports should be equal"
          }

          test "ValidationReport inequality with different totals" {
              let a =
                  { TotalChecks = 1
                    TotalSkipped = 0
                    TotalFailures = 0
                    Checks = []
                    Failures = [] }

              let b =
                  { TotalChecks = 2
                    TotalSkipped = 0
                    TotalFailures = 0
                    Checks = []
                    Failures = [] }

              Expect.notEqual a b "Different TotalChecks should not be equal"
          } ]

[<Tests>]
let validationRuleTests =
    testList
        "Validation.Types.ValidationRule"
        [ test "ValidationRule construction" {
              let rule =
                  { Name = "test rule"
                    RequiredFormats = set [ Scxml; XState ]
                    Check = fun _ -> ([], []) }

              Expect.equal rule.Name "test rule" "Name should match"
              Expect.equal rule.RequiredFormats (set [ Scxml; XState ]) "RequiredFormats should match"
          }

          test "ValidationRule with empty RequiredFormats (universal rule)" {
              let rule =
                  { Name = "universal rule"
                    RequiredFormats = Set.empty
                    Check = fun _ -> ([], []) }

              Expect.equal rule.RequiredFormats Set.empty "RequiredFormats should be empty for universal rules"
          }

          test "ValidationRule Check function executes" {
              let rule =
                  { Name = "test rule"
                    RequiredFormats = Set.empty
                    Check =
                      fun _ ->
                          ([ { Name = "check1"
                               Status = Pass
                               Reason = None } ],
                           []) }

              let checks, _failures = rule.Check []
              Expect.equal (List.length checks) 1 "Should return one check"
              Expect.equal checks.[0].Status Pass "Check should pass"
          } ]
