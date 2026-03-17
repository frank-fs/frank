module Frank.Statecharts.Tests.Validation.PipelineIntegrationTests

open Expecto
open Frank.Statecharts.Validation
open Frank.Statecharts.Ast

// Tic-tac-toe state machine: states idle, playerX, playerO, gameOver
// Events: start, move, win

let wsdTicTacToe = """participant idle
participant playerX
participant playerO
participant gameOver
idle -> playerX: start
playerX -> playerO: move
playerO -> playerX: move
playerX -> gameOver: win
playerO -> gameOver: win
"""

// smcat requires explicit state declarations; transition-only syntax
// does not create StateDecl elements for target-only states
let smcatTicTacToe = """idle;
playerX;
playerO;
gameOver;
idle => playerX: start;
playerX => playerO: move;
playerO => playerX: move;
playerX => gameOver: win;
playerO => gameOver: win;
"""

let scxmlTicTacToe = """<?xml version="1.0" encoding="UTF-8"?>
<scxml xmlns="http://www.w3.org/2005/07/scxml" version="1.0" initial="idle">
  <state id="idle">
    <transition event="start" target="playerX"/>
  </state>
  <state id="playerX">
    <transition event="move" target="playerO"/>
    <transition event="win" target="gameOver"/>
  </state>
  <state id="playerO">
    <transition event="move" target="playerX"/>
    <transition event="win" target="gameOver"/>
  </state>
  <state id="gameOver"/>
</scxml>
"""

// ALPS: states are semantic descriptors with IDs. Transitions are child
// descriptors with type safe/unsafe and rt pointing to target via #id.
// gameOver is classified as a state because it appears as an rt target.
let alpsTicTacToe = """{
  "alps": {
    "version": "1.0",
    "descriptor": [
      {
        "id": "idle",
        "type": "semantic",
        "descriptor": [
          { "id": "start", "type": "safe", "rt": "#playerX" }
        ]
      },
      {
        "id": "playerX",
        "type": "semantic",
        "descriptor": [
          { "id": "move", "type": "safe", "rt": "#playerO" },
          { "id": "win", "type": "safe", "rt": "#gameOver" }
        ]
      },
      {
        "id": "playerO",
        "type": "semantic",
        "descriptor": [
          { "id": "move", "type": "safe", "rt": "#playerX" },
          { "id": "win", "type": "safe", "rt": "#gameOver" }
        ]
      },
      {
        "id": "gameOver",
        "type": "semantic"
      }
    ]
  }
}"""

[<Tests>]
let pipelineIntegrationTests =
    testList "Validation.PipelineIntegration" [
        test "Consistent tic-tac-toe in 4 formats produces zero validation failures" {
            let result = Pipeline.validateSources [
                (FormatTag.Wsd, wsdTicTacToe)
                (FormatTag.Smcat, smcatTicTacToe)
                (FormatTag.Scxml, scxmlTicTacToe)
                (FormatTag.Alps, alpsTicTacToe)
            ]
            Expect.isEmpty result.Errors "No pipeline errors expected"
            Expect.equal (List.length result.ParseResults) 4 "Should have 4 parse results"
            for pr in result.ParseResults do
                Expect.isTrue pr.Succeeded (sprintf "%A should parse successfully" pr.Format)
            Expect.equal result.Report.TotalFailures 0
                (sprintf "Expected zero failures but got %d: %A" result.Report.TotalFailures result.Report.Failures)
        }

        test "State name mismatch detected: gameOver vs finished in smcat" {
            let smcatMismatch = smcatTicTacToe.Replace("gameOver", "finished")
            let result = Pipeline.validateSources [
                (FormatTag.Wsd, wsdTicTacToe)
                (FormatTag.Smcat, smcatMismatch)
            ]
            Expect.isGreaterThan result.Report.TotalFailures 0 "Should detect state name mismatch"
            let relevantFailures =
                result.Report.Failures
                |> List.filter (fun f ->
                    f.Description.Contains("gameOver") || f.Description.Contains("finished"))
            Expect.isNonEmpty relevantFailures "Should have failures mentioning gameOver or finished"
        }

        test "Missing event detected: remove start event from ALPS" {
            // ALPS source with idle having no transitions (start event removed)
            let alpsNoStart = """{
  "alps": {
    "version": "1.0",
    "descriptor": [
      { "id": "idle", "type": "semantic" },
      {
        "id": "playerX",
        "type": "semantic",
        "descriptor": [
          { "id": "move", "type": "safe", "rt": "#playerO" },
          { "id": "win", "type": "safe", "rt": "#gameOver" }
        ]
      },
      {
        "id": "playerO",
        "type": "semantic",
        "descriptor": [
          { "id": "move", "type": "safe", "rt": "#playerX" },
          { "id": "win", "type": "safe", "rt": "#gameOver" }
        ]
      },
      { "id": "gameOver", "type": "semantic" }
    ]
  }
}"""
            let result = Pipeline.validateSources [
                (FormatTag.Wsd, wsdTicTacToe)
                (FormatTag.Alps, alpsNoStart)
            ]
            Expect.isGreaterThan result.Report.TotalFailures 0 "Should detect missing event"
        }

        test "Parse error in SCXML still validates WSD" {
            let malformedScxml = "<scxml><state id='a'><transition event='go' target='b'/>"
            let result = Pipeline.validateSources [
                (FormatTag.Wsd, wsdTicTacToe)
                (FormatTag.Scxml, malformedScxml)
            ]
            Expect.equal (List.length result.ParseResults) 2 "Should have 2 parse results"
            let scxmlResult = result.ParseResults |> List.find (fun pr -> pr.Format = FormatTag.Scxml)
            Expect.isFalse scxmlResult.Succeeded "SCXML should have parse errors"
            Expect.isNonEmpty scxmlResult.Errors "SCXML should have parse error details"
            let wsdResult = result.ParseResults |> List.find (fun pr -> pr.Format = FormatTag.Wsd)
            Expect.isTrue wsdResult.Succeeded "WSD should parse successfully"
            Expect.isEmpty result.Errors "Parse failures are not pipeline errors"
        }

        test "Performance: 4 formats parsed and validated in under 2 seconds" {
            let sw = System.Diagnostics.Stopwatch.StartNew()
            let _result = Pipeline.validateSources [
                (FormatTag.Wsd, wsdTicTacToe)
                (FormatTag.Smcat, smcatTicTacToe)
                (FormatTag.Scxml, scxmlTicTacToe)
                (FormatTag.Alps, alpsTicTacToe)
            ]
            sw.Stop()
            Expect.isLessThan sw.Elapsed.TotalSeconds 2.0
                (sprintf "Pipeline took %.3f seconds, expected < 2.0" sw.Elapsed.TotalSeconds)
        }
    ]
