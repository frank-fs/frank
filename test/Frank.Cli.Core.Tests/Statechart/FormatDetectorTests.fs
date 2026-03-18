module Frank.Cli.Core.Tests.Statechart.FormatDetectorTests

open Expecto
open Frank.Statecharts.Validation
open Frank.Cli.Core.Statechart.FormatDetector

[<Tests>]
let tests =
    testList "FormatDetector" [
        testList "detect" [
            testCase "detects .wsd as WSD" <| fun _ ->
                Expect.equal (detect "game.wsd") (Detected Wsd) "should detect WSD"

            testCase "detects .scxml as SCXML" <| fun _ ->
                Expect.equal (detect "game.scxml") (Detected Scxml) "should detect SCXML"

            testCase "detects .smcat as smcat" <| fun _ ->
                Expect.equal (detect "game.smcat") (Detected Smcat) "should detect smcat"

            testCase "detects .alps.json as ALPS" <| fun _ ->
                Expect.equal (detect "game.alps.json") (Detected Alps) "should detect ALPS"

            testCase "detects .xstate.json as XState" <| fun _ ->
                Expect.equal (detect "game.xstate.json") (Detected XState) "should detect XState"

            testCase "plain .json is ambiguous" <| fun _ ->
                Expect.equal (detect "game.json") (Ambiguous [ Alps; XState ]) "should be ambiguous"

            testCase "unsupported extension" <| fun _ ->
                match detect "game.txt" with
                | Unsupported ".txt" -> ()
                | other -> failtest (sprintf "Expected Unsupported .txt, got %A" other)

            testCase "case insensitive" <| fun _ ->
                Expect.equal (detect "GAME.WSD") (Detected Wsd) "should detect WSD case-insensitively"

            testCase "full path works" <| fun _ ->
                Expect.equal (detect "/path/to/game.wsd") (Detected Wsd) "should detect WSD from full path"
        ]

        testList "formatExtension" [
            testCase "WSD -> .wsd" <| fun _ ->
                Expect.equal (formatExtension Wsd) ".wsd" ""
            testCase "ALPS -> .alps.json" <| fun _ ->
                Expect.equal (formatExtension Alps) ".alps.json" ""
            testCase "XState -> .xstate.json" <| fun _ ->
                Expect.equal (formatExtension XState) ".xstate.json" ""
            testCase "SCXML -> .scxml" <| fun _ ->
                Expect.equal (formatExtension Scxml) ".scxml" ""
            testCase "smcat -> .smcat" <| fun _ ->
                Expect.equal (formatExtension Smcat) ".smcat" ""
        ]

        testList "FormatTag.toString" [
            testCase "Wsd -> WSD" <| fun _ ->
                Expect.equal (FormatTag.toString Wsd) "WSD" ""
            testCase "Alps -> ALPS" <| fun _ ->
                Expect.equal (FormatTag.toString Alps) "ALPS" ""
            testCase "Smcat -> smcat" <| fun _ ->
                Expect.equal (FormatTag.toString Smcat) "smcat" ""
            testCase "Scxml -> SCXML" <| fun _ ->
                Expect.equal (FormatTag.toString Scxml) "SCXML" ""
            testCase "XState -> XState" <| fun _ ->
                Expect.equal (FormatTag.toString XState) "XState" ""
        ]

        testList "FormatTag.toLower" [
            testCase "Wsd -> wsd" <| fun _ ->
                Expect.equal (FormatTag.toLower Wsd) "wsd" ""
            testCase "Alps -> alps" <| fun _ ->
                Expect.equal (FormatTag.toLower Alps) "alps" ""
            testCase "XState -> xstate" <| fun _ ->
                Expect.equal (FormatTag.toLower XState) "xstate" ""
        ]
    ]
