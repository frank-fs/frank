module Frank.Cli.Core.Tests.TextOutputTests

open System
open Expecto
open Frank.Cli.Core.Commands.ExtractCommand
open Frank.Cli.Core.Commands.ClarifyCommand
open Frank.Cli.Core.State
open Frank.Cli.Core.Output.TextOutput

[<Tests>]
let tests =
    testList "TextOutput" [
        testCase "formatExtractResult contains summary table with proper alignment" <| fun _ ->
            let result : ExtractResult =
                { OntologySummary =
                    { ClassCount = 3
                      PropertyCount = 8
                      AlignedCount = 2
                      UnalignedCount = 6 }
                  ShapesSummary =
                    { ShapeCount = 3
                      ConstraintCount = 5 }
                  UnmappedTypes = []
                  StateFilePath = "/tmp/state.json" }

            let text = formatExtractResult result
            Expect.stringContains text "Extraction Summary" "Should contain header"
            Expect.stringContains text "Category" "Should contain Category header"
            Expect.stringContains text "Count" "Should contain Count header"
            Expect.stringContains text "------------------+------" "Should contain table separator"
            Expect.stringContains text "Classes" "Should contain Classes row"
            Expect.stringContains text "Properties" "Should contain Properties row"
            Expect.stringContains text "Shapes" "Should contain Shapes row"
            Expect.stringContains text "Aligned" "Should contain Aligned row"
            Expect.stringContains text "Unmapped Types" "Should contain Unmapped Types row"
            Expect.stringContains text "/tmp/state.json" "Should contain state file path"

        testCase "formatExtractResult lists unmapped types" <| fun _ ->
            let result : ExtractResult =
                { OntologySummary =
                    { ClassCount = 1
                      PropertyCount = 2
                      AlignedCount = 0
                      UnalignedCount = 2 }
                  ShapesSummary =
                    { ShapeCount = 1
                      ConstraintCount = 2 }
                  UnmappedTypes =
                    [ { TypeName = "Widget"
                        Reason = "no rule"
                        Location = { File = "Widget.fs"; Line = 15; Column = 0 } } ]
                  StateFilePath = "/tmp/state.json" }

            let text = formatExtractResult result
            Expect.stringContains text "Unmapped types" "Should have unmapped section"
            Expect.stringContains text "Widget" "Should mention Widget"
            Expect.stringContains text "Widget.fs:15" "Should show source location"

        testCase "formatClarifyResult contains numbered questions" <| fun _ ->
            let result : ClarifyResult =
                { Questions =
                    [ { Id = "unmapped-type-Widget"
                        Category = "unmapped-type"
                        QuestionText = "Map Widget?"
                        Context = {| SourceType = "Widget"; Location = Some "Widget.fs:5" |}
                        Options =
                          [ {| Label = "map it"; Impact = "adds class" |}
                            {| Label = "ignore it"; Impact = "skips" |} ] } ]
                  ResolvedCount = 0
                  TotalCount = 1 }

            let text = formatClarifyResult result
            Expect.stringContains text "Clarification Questions" "Should contain header"
            Expect.stringContains text "Resolved: 0 / 1" "Should show resolved count"
            Expect.stringContains text "1." "Should have numbered question"
            Expect.stringContains text "Map Widget?" "Should contain question text"
            Expect.stringContains text "a)" "Should have lettered options"
            Expect.stringContains text "b)" "Should have second option"

        testCase "formatError contains error message" <| fun _ ->
            let text = formatError "bad things happened"
            Expect.stringContains text "bad things happened" "Should contain error message"
            Expect.stringContains text "Error" "Should contain Error prefix"

        testCase "NO_COLOR env var suppresses ANSI codes" <| fun _ ->
            let prev = Environment.GetEnvironmentVariable("NO_COLOR")
            try
                Environment.SetEnvironmentVariable("NO_COLOR", "1")

                let result : ExtractResult =
                    { OntologySummary =
                        { ClassCount = 1
                          PropertyCount = 2
                          AlignedCount = 0
                          UnalignedCount = 2 }
                      ShapesSummary =
                        { ShapeCount = 1
                          ConstraintCount = 2 }
                      UnmappedTypes = []
                      StateFilePath = "/tmp/state.json" }

                let text = formatExtractResult result
                Expect.isFalse (text.Contains("\033[")) "Should not contain ANSI escape codes when NO_COLOR is set"
            finally
                if isNull prev then
                    Environment.SetEnvironmentVariable("NO_COLOR", null)
                else
                    Environment.SetEnvironmentVariable("NO_COLOR", prev)
    ]
