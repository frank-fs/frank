module Frank.Statecharts.Tests.Smcat.GeneratorTests

open Expecto
open Frank.Statecharts.Smcat.Generator

// === Test domain types ===

type TestState =
    | Idle
    | Running
    | Stopped
    | Completed

type TestEvent =
    | Start
    | Stop
    | Finish

// === Helpers ===

let stateToName (s: TestState) =
    match s with
    | Idle -> "idle"
    | Running -> "running"
    | Stopped -> "stopped"
    | Completed -> "completed"

let eventToName (e: TestEvent) =
    match e with
    | Start -> "start"
    | Stop -> "stop"
    | Finish -> "finish"

let noGuards (_: TestState) (_: TestEvent) : string option = None

let private simpleStateInfos (finals: TestState list) (states: TestState list) =
    states
    |> List.map (fun s ->
        { State = s
          IsFinal = List.contains s finals })

// === Label formatting tests ===

[<Tests>]
let labelFormattingTests =
    testList
        "Smcat.Generator.formatLabel"
        [ test "event only" {
              let result = formatLabel (Some "start") None None
              Expect.equal result (Some "start") "should be just the event name"
          }

          test "event and guard" {
              let result = formatLabel (Some "start") (Some "isReady") None
              Expect.equal result (Some "start [isReady]") "should format event [guard]"
          }

          test "event and action" {
              let result = formatLabel (Some "start") None (Some "logStart")
              Expect.equal result (Some "start / logStart") "should format event / action"
          }

          test "all three components" {
              let result = formatLabel (Some "start") (Some "isReady") (Some "logStart")

              Expect.equal
                  result
                  (Some "start [isReady] / logStart")
                  "should format event [guard] / action"
          }

          test "guard only" {
              let result = formatLabel None (Some "isReady") None
              Expect.equal result (Some "[isReady]") "should format [guard]"
          }

          test "guard and action" {
              let result = formatLabel None (Some "isReady") (Some "logStart")
              Expect.equal result (Some "[isReady] / logStart") "should format [guard] / action"
          }

          test "action only" {
              let result = formatLabel None None (Some "logStart")
              Expect.equal result (Some "/ logStart") "should format / action"
          }

          test "no components" {
              let result = formatLabel None None None
              Expect.equal result None "should return None when all components are absent"
          } ]

// === Transition formatting tests ===

[<Tests>]
let transitionFormattingTests =
    testList
        "Smcat.Generator.formatTransition"
        [ test "transition with label" {
              let result = formatTransition "idle" "running" (Some "start")
              Expect.equal result "idle => running: start;" "should format source => target: label;"
          }

          test "transition without label" {
              let result = formatTransition "idle" "running" None
              Expect.equal result "idle => running;" "should format source => target;"
          }

          test "transition with quoted source" {
              let result = formatTransition "my state" "running" (Some "go")

              Expect.equal
                  result
                  "\"my state\" => running: go;"
                  "should quote source with spaces"
          }

          test "transition with quoted target" {
              let result = formatTransition "idle" "next state" None

              Expect.equal
                  result
                  "idle => \"next state\";"
                  "should quote target with spaces"
          }

          test "names with underscores and dots not quoted" {
              let result = formatTransition "my_state" "v2.0" None

              Expect.equal
                  result
                  "my_state => v2.0;"
                  "underscores and dots should not trigger quoting"
          }

          test "names with hyphens not quoted" {
              let result = formatTransition "state-one" "state-two" None

              Expect.equal
                  result
                  "state-one => state-two;"
                  "hyphens should not trigger quoting"
          } ]

// === Full generator tests - User Story 3 acceptance scenarios ===

[<Tests>]
let generatorTests =
    testList
        "Smcat.Generator"
        [
          // US3 Acceptance Scenario A: initial state
          test "emits initial state transition first" {
              let infos =
                  simpleStateInfos [] [ Idle; Running; Stopped ]

              let result =
                  generate Idle infos noGuards stateToName eventToName [ (Idle, Start, Running) ]

              let lines = result.Split('\n')
              Expect.equal lines.[0] "initial => idle;" "first line should be the initial transition"
          }

          // US3 Acceptance Scenario B: full label with event, guard, and action
          test "emits transition with event and guard" {
              let guardFn (s: TestState) (e: TestEvent) =
                  match s, e with
                  | Idle, Start -> Some "isReady"
                  | _ -> None

              let infos =
                  simpleStateInfos [] [ Idle; Running; Stopped ]

              let result =
                  generate Idle infos guardFn stateToName eventToName [ (Idle, Start, Running) ]

              let lines = result.Split('\n')

              Expect.equal
                  lines.[1]
                  "idle => running: start [isReady];"
                  "should include event and guard in label"
          }

          // US3 Acceptance Scenario C: final state
          test "emits final state transition" {
              let infos =
                  simpleStateInfos [ Completed ] [ Idle; Running; Completed ]

              let result =
                  generate
                      Idle
                      infos
                      noGuards
                      stateToName
                      eventToName
                      [ (Idle, Start, Running); (Running, Finish, Completed) ]

              let lines = result.Split('\n')
              let lastLine = lines.[lines.Length - 1]
              Expect.equal lastLine "completed => final;" "last line should transition to final"
          }

          // US3 Acceptance Scenario D: transitions with no guards or actions
          test "emits transition with event only (no guard or action)" {
              let infos =
                  simpleStateInfos [] [ Idle; Running; Stopped ]

              let result =
                  generate
                      Idle
                      infos
                      noGuards
                      stateToName
                      eventToName
                      [ (Idle, Start, Running); (Running, Stop, Stopped) ]

              let lines = result.Split('\n')
              Expect.equal lines.[1] "idle => running: start;" "should have event-only label"
              Expect.equal lines.[2] "running => stopped: stop;" "should have event-only label"
          }

          // Ordering: initial first, regular in order, final last
          test "ordering: initial first, regular in order, final last" {
              let infos =
                  simpleStateInfos [ Completed ] [ Idle; Running; Completed ]

              let guardFn (s: TestState) (e: TestEvent) =
                  match s, e with
                  | Idle, Start -> Some "isReady"
                  | _ -> None

              let result =
                  generate
                      Idle
                      infos
                      guardFn
                      stateToName
                      eventToName
                      [ (Idle, Start, Running); (Running, Finish, Completed) ]

              let lines = result.Split('\n')
              Expect.equal lines.[0] "initial => idle;" "first: initial transition"
              Expect.equal lines.[1] "idle => running: start [isReady];" "second: first regular transition"
              Expect.equal lines.[2] "running => completed: finish;" "third: second regular transition"
              Expect.equal lines.[3] "completed => final;" "last: final state transition"
          }

          // Edge case: no transitions
          test "no transitions produces only initial state line" {
              let infos = simpleStateInfos [] [ Idle ]
              let result = generate Idle infos noGuards stateToName eventToName []
              Expect.equal result "initial => idle;" "should have only the initial transition"
          }

          // Edge case: no final states
          test "no final states omits final transitions" {
              let infos =
                  simpleStateInfos [] [ Idle; Running; Stopped ]

              let result =
                  generate
                      Idle
                      infos
                      noGuards
                      stateToName
                      eventToName
                      [ (Idle, Start, Running) ]

              let lines = result.Split('\n')
              Expect.equal lines.Length 2 "should have only initial + one regular transition"
              Expect.equal lines.[0] "initial => idle;" "initial transition"
              Expect.equal lines.[1] "idle => running: start;" "regular transition"
          }

          // Edge case: state names needing quoting
          test "state names with spaces are quoted" {
              let stateNames (s: TestState) =
                  match s with
                  | Idle -> "idle state"
                  | Running -> "running state"
                  | _ -> stateToName s

              let infos = simpleStateInfos [] [ Idle; Running ]

              let result =
                  generate Idle infos noGuards stateNames eventToName [ (Idle, Start, Running) ]

              let lines = result.Split('\n')

              Expect.equal
                  lines.[0]
                  "initial => \"idle state\";"
                  "initial state name should be quoted"

              Expect.equal
                  lines.[1]
                  "\"idle state\" => \"running state\": start;"
                  "both state names should be quoted in transition"
          }

          // Multiple final states
          test "multiple final states each get a final transition" {
              let infos =
                  simpleStateInfos [ Stopped; Completed ] [ Idle; Running; Stopped; Completed ]

              let result =
                  generate
                      Idle
                      infos
                      noGuards
                      stateToName
                      eventToName
                      [ (Idle, Start, Running)
                        (Running, Stop, Stopped)
                        (Running, Finish, Completed) ]

              let lines = result.Split('\n')
              // initial + 3 regular + 2 final = 6 lines
              Expect.equal lines.Length 6 "should have 6 lines total"

              // Final transitions are the last two lines (order determined by Set ordering)
              let finalLines =
                  lines |> Array.filter (fun l -> l.Contains("=> final;"))

              Expect.equal finalLines.Length 2 "should have two final transitions"

              Expect.isTrue
                  (finalLines |> Array.exists (fun l -> l.Contains("completed")))
                  "should have completed => final"

              Expect.isTrue
                  (finalLines |> Array.exists (fun l -> l.Contains("stopped")))
                  "should have stopped => final"
          }

          // Guard with multiple transitions
          test "different guards for different transitions" {
              let guardFn (s: TestState) (e: TestEvent) =
                  match s, e with
                  | Idle, Start -> Some "isReady"
                  | Running, Stop -> Some "canStop"
                  | _ -> None

              let infos =
                  simpleStateInfos [] [ Idle; Running; Stopped ]

              let result =
                  generate
                      Idle
                      infos
                      guardFn
                      stateToName
                      eventToName
                      [ (Idle, Start, Running); (Running, Stop, Stopped) ]

              let lines = result.Split('\n')
              Expect.equal lines.[1] "idle => running: start [isReady];" "first transition with isReady guard"

              Expect.equal
                  lines.[2]
                  "running => stopped: stop [canStop];"
                  "second transition with canStop guard"
          } ]
