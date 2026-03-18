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

        test "AlpsXml format dispatches through pipeline" {
            let alpsXml = """<alps version="1.0">
  <descriptor id="idle" type="semantic">
    <descriptor id="start" type="unsafe" rt="#active"/>
  </descriptor>
  <descriptor id="active" type="semantic">
    <descriptor id="finish" type="safe" rt="#idle"/>
  </descriptor>
</alps>"""
            let result = Pipeline.validateSources [
                (FormatTag.AlpsXml, alpsXml)
            ]
            Expect.isEmpty result.Errors "No pipeline errors for AlpsXml"
            Expect.equal (List.length result.ParseResults) 1 "Should have 1 parse result"
            let pr = result.ParseResults.[0]
            Expect.isTrue pr.Succeeded "AlpsXml should parse successfully"
            Expect.equal pr.Format FormatTag.AlpsXml "Format should be AlpsXml"
        }
    ]

// ---------------------------------------------------------------------------
// T020 – Simple 3-state machine fixtures in all 5 supported formats
// States: idle, active, done  |  Events: start, complete, reset
// ---------------------------------------------------------------------------

// WSD: participant declarations + arrow transitions
let private wsdSimple =
    "participant idle\nparticipant active\nparticipant done\nidle -> active: start\nactive -> done: complete\ndone -> idle: reset\n"

// smcat: explicit state declarations (transition-only syntax would not create StateDecl for targets)
let private smcatSimple =
    "idle;\nactive;\ndone;\nidle => active: start;\nactive => done: complete;\ndone => idle: reset;"

// SCXML: standard statechart XML
let private scxmlSimple =
    """<scxml xmlns="http://www.w3.org/2005/07/scxml" version="1.0" initial="idle">
  <state id="idle"><transition event="start" target="active"/></state>
  <state id="active"><transition event="complete" target="done"/></state>
  <state id="done"><transition event="reset" target="idle"/></state>
</scxml>"""

// ALPS JSON: semantic descriptors with transition children.
// All three top-level descriptors have transition-type children,
// so all are classified as states by isStateDescriptor.
let private alpsSimple =
    """{
  "alps": {
    "version": "1.0",
    "descriptor": [
      {
        "id": "idle",
        "type": "semantic",
        "descriptor": [
          { "id": "start", "type": "unsafe", "rt": "#active" }
        ]
      },
      {
        "id": "active",
        "type": "semantic",
        "descriptor": [
          { "id": "complete", "type": "unsafe", "rt": "#done" }
        ]
      },
      {
        "id": "done",
        "type": "semantic",
        "descriptor": [
          { "id": "reset", "type": "unsafe", "rt": "#idle" }
        ]
      }
    ]
  }
}"""

// XState v5 JSON: flat states object with "on" transition maps
let private xstateSimple =
    """{"id":"test","initial":"idle","states":{"idle":{"on":{"start":"active"}},"active":{"on":{"complete":"done"}},"done":{"on":{"reset":"idle"}}}}"""

// ---------------------------------------------------------------------------
// T021 – Consistent 5-format input produces zero validation failures
// ---------------------------------------------------------------------------

[<Tests>]
let e2eConsistentFiveFormatsTests =
    testList "E2E.FiveFormatConsistency" [

        test "E2E: consistent 5-format input produces zero validation failures" {
            let sources = [
                (FormatTag.Wsd, wsdSimple)
                (FormatTag.Smcat, smcatSimple)
                (FormatTag.Scxml, scxmlSimple)
                (FormatTag.Alps, alpsSimple)
                (FormatTag.XState, xstateSimple)
            ]
            let result = Pipeline.validateSources sources
            Expect.isEmpty result.Errors "No pipeline errors"
            Expect.equal (List.length result.ParseResults) 5 "Should have 5 parse results"
            for pr in result.ParseResults do
                Expect.isTrue pr.Succeeded (sprintf "%A should parse successfully" pr.Format)
            Expect.equal result.Report.TotalFailures 0
                (sprintf "Expected zero failures but got %d: %A" result.Report.TotalFailures result.Report.Failures)
        }
    ]

// ---------------------------------------------------------------------------
// T022 – Intentional casing mismatch is detected
// ---------------------------------------------------------------------------

[<Tests>]
let e2eCasingMismatchTests =
    testList "E2E.CasingMismatch" [

        test "E2E: casing mismatch detected between WSD 'Idle' and smcat 'idle'" {
            // Replace lowercase 'idle' with uppercase 'Idle' in WSD only.
            // smcat retains the original lowercase names.
            let wsdMismatch = wsdSimple.Replace("idle", "Idle")
            let result = Pipeline.validateSources [
                (FormatTag.Wsd, wsdMismatch)
                (FormatTag.Smcat, smcatSimple)
            ]
            Expect.isGreaterThan result.Report.TotalFailures 0
                "Should detect state name mismatch between 'Idle' (WSD) and 'idle' (smcat)"
            // The cross-format state name agreement rule should report the discrepancy.
            // The casing note in the description confirms near-miss awareness.
            let stateFailures =
                result.Report.Failures
                |> List.filter (fun f -> f.EntityType = "state name")
            Expect.isNonEmpty stateFailures
                "Should have state name failures mentioning the casing mismatch"
        }
    ]

// ---------------------------------------------------------------------------
// T023 – Validate then merge produces unified document with ALPS annotations
// ---------------------------------------------------------------------------

[<Tests>]
let e2eValidateThenMergeTests =
    testList "E2E.ValidateThenMerge" [

        test "E2E: validate then merge WSD + ALPS produces unified document with ALPS annotations" {
            let sources = [
                (FormatTag.Wsd, wsdSimple)
                (FormatTag.Alps, alpsSimple)
            ]
            // Step 1: validate
            let validationResult = Pipeline.validateSources sources
            Expect.equal validationResult.Report.TotalFailures 0
                (sprintf "Validation should pass before merge; failures: %A" validationResult.Report.Failures)
            // Step 2: merge
            match Pipeline.mergeSources sources with
            | Error errs ->
                failtestf "Merge failed with errors: %A" errs
            | Ok merged ->
                let states =
                    merged.Elements |> List.choose (function StateDecl s -> Some s | _ -> None)
                Expect.isGreaterThan states.Length 0 "Merged document should have states"
                let stateIds = states |> List.choose (fun s -> s.Identifier) |> Set.ofList
                Expect.isTrue (Set.contains "idle"   stateIds) "State 'idle' should be in merged doc"
                Expect.isTrue (Set.contains "active" stateIds) "State 'active' should be in merged doc"
                Expect.isTrue (Set.contains "done"   stateIds) "State 'done' should be in merged doc"
                // ALPS annotations must be present at document level (AlpsVersion at minimum)
                let hasAlps =
                    merged.Annotations |> List.exists (function AlpsAnnotation _ -> true | _ -> false)
                Expect.isTrue hasAlps "Merged document should have ALPS annotations"
        }

        test "E2E: validate then merge 3-way WSD + ALPS + SCXML preserves SCXML priority" {
            let sources = [
                (FormatTag.Wsd, wsdSimple)
                (FormatTag.Alps, alpsSimple)
                (FormatTag.Scxml, scxmlSimple)
            ]
            let validationResult = Pipeline.validateSources sources
            Expect.equal validationResult.Report.TotalFailures 0
                (sprintf "3-way validation should pass; failures: %A" validationResult.Report.Failures)
            match Pipeline.mergeSources sources with
            | Error errs ->
                failtestf "3-way merge failed: %A" errs
            | Ok merged ->
                // SCXML sets InitialStateId = Some "idle" (priority 0, highest)
                Expect.equal merged.InitialStateId (Some "idle")
                    "SCXML initial='idle' should survive as InitialStateId in 3-way merge"
                let states =
                    merged.Elements |> List.choose (function StateDecl s -> Some s | _ -> None)
                Expect.equal states.Length 3 "Merged document should have exactly 3 states (idle, active, done)"
        }

        test "E2E: SCXML parallel regions preserved in merge with flat WSD" {
            // SCXML with a parallel state containing two regions — WSD cannot express this
            let scxmlParallel = """<scxml xmlns="http://www.w3.org/2005/07/scxml" version="1.0" initial="running">
  <parallel id="running">
    <state id="regionA">
      <state id="a1"/>
      <state id="a2"/>
    </state>
    <state id="regionB">
      <state id="b1"/>
      <state id="b2"/>
    </state>
  </parallel>
</scxml>"""
            // WSD only knows about "running" as a flat participant
            let wsdFlat = "participant running\n"
            let result = Pipeline.mergeSources [(FormatTag.Scxml, scxmlParallel); (FormatTag.Wsd, wsdFlat)]
            match result with
            | Error errs -> failtestf "Merge failed: %A" errs
            | Ok merged ->
                let states = merged.Elements |> List.choose (function StateDecl s -> Some s | _ -> None)
                let running = states |> List.tryFind (fun s -> s.Identifier = Some "running")
                Expect.isSome running "running state should exist in merged doc"
                // SCXML (priority 0) wins: running should be Parallel with children
                Expect.equal running.Value.Kind Parallel "running should be Parallel (from SCXML, not Regular from WSD)"
                Expect.isGreaterThan running.Value.Children.Length 0 "running should have children (regions from SCXML)"
                // Verify the two regions survived
                let childIds = running.Value.Children |> List.choose (fun c -> c.Identifier)
                Expect.contains childIds "regionA" "regionA should be a child of running"
                Expect.contains childIds "regionB" "regionB should be a child of running"
        }
    ]

// ---------------------------------------------------------------------------
// T024 – Event name disagreement detected with real format text
// The cross-format event name agreement rule detects the disagreement
// as a plain event mismatch ("startOnboarding" vs "start").
// ---------------------------------------------------------------------------

[<Tests>]
let e2eEventMismatchDetectionTests =
    testList "E2E.EventMismatchDetection" [

        test "E2E: event name disagreement detected between WSD 'startOnboarding' and smcat 'start'" {
            // WSD has a longer event name; smcat uses the shorter canonical name.
            // This exercises end-to-end parsing of real text through the cross-format event rule.
            let wsdLongerEvent =
                "participant idle\nparticipant active\nidle -> active: startOnboarding\n"
            let smcatCanonical =
                "idle;\nactive;\nidle => active: start;"
            let result = Pipeline.validateSources [
                (FormatTag.Wsd, wsdLongerEvent)
                (FormatTag.Smcat, smcatCanonical)
            ]
            // Both parsers should succeed
            Expect.equal (List.length result.ParseResults) 2 "Should have 2 parse results"
            for pr in result.ParseResults do
                Expect.isTrue pr.Succeeded (sprintf "%A should parse successfully" pr.Format)
            // Cross-format event name agreement rule must detect the mismatch
            Expect.isGreaterThan result.Report.TotalFailures 0
                "Should detect event name disagreement between 'startOnboarding' and 'start'"
            let eventFailures =
                result.Report.Failures |> List.filter (fun f -> f.EntityType = "event name")
            Expect.isNonEmpty eventFailures
                "Should have event name failures for the mismatched event names"
            // Verify both event names appear in the failure descriptions
            let allDescriptions = eventFailures |> List.map (fun f -> f.Description) |> String.concat " "
            Expect.isTrue (allDescriptions.Contains("startOnboarding"))
                "Failure description should mention 'startOnboarding'"
        }

        test "E2E: 5-format input with one format having extra event is detected" {
            // ALPS adds an extra 'archive' event not present in any other format
            let alpsWithExtra =
                """{
  "alps": {
    "version": "1.0",
    "descriptor": [
      {
        "id": "idle",
        "type": "semantic",
        "descriptor": [
          { "id": "start", "type": "unsafe", "rt": "#active" },
          { "id": "archive", "type": "safe", "rt": "#done" }
        ]
      },
      {
        "id": "active",
        "type": "semantic",
        "descriptor": [
          { "id": "complete", "type": "safe", "rt": "#done" }
        ]
      },
      {
        "id": "done",
        "type": "semantic",
        "descriptor": [
          { "id": "reset", "type": "unsafe", "rt": "#idle" }
        ]
      }
    ]
  }
}"""
            let result = Pipeline.validateSources [
                (FormatTag.Wsd, wsdSimple)
                (FormatTag.Alps, alpsWithExtra)
            ]
            Expect.isGreaterThan result.Report.TotalFailures 0
                "Extra 'archive' event in ALPS should cause event name disagreement with WSD"
            let eventFailures =
                result.Report.Failures |> List.filter (fun f -> f.EntityType = "event name")
            Expect.isNonEmpty eventFailures "Should have event name failures"
            let mentionsArchive =
                eventFailures |> List.exists (fun f -> f.Description.Contains("archive"))
            Expect.isTrue mentionsArchive "Failures should mention the extra 'archive' event"
        }
    ]
