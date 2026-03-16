module Frank.Statecharts.Tests.Smcat.GeneratorTests

open System.Threading.Tasks
open Microsoft.AspNetCore.Http
open Expecto
open Frank.Statecharts
open Frank.Statecharts.Smcat.Generator

// --- Test state machine types ---

type TestState =
    | Idle
    | Running
    | Stopped
    | Completed

type TestEvent =
    | Start
    | Stop
    | Finish

// --- Helpers ---

let private dummyHandler = RequestDelegate(fun _ -> Task.CompletedTask)

let private makeMetadata
    (machine: StateMachine<'S, 'E, 'C>)
    (stateHandlerMap: Map<string, (string * RequestDelegate) list>)
    : StateMachineMetadata =
    let initialKey = string machine.Initial

    let guardNames =
        machine.Guards
        |> List.map (function
            | AccessControl(name, _) -> name
            | EventValidation(name, _) -> name)

    let stateMetadataMap =
        machine.StateMetadata |> Map.toList |> List.map (fun (s, info) -> (string s, info)) |> Map.ofList

    { Machine = box machine
      StateHandlerMap = stateHandlerMap
      ResolveInstanceId = fun _ -> "test"
      TransitionObservers = []
      InitialStateKey = initialKey
      GuardNames = guardNames
      StateMetadataMap = stateMetadataMap
      GetCurrentStateKey = fun _ _ _ -> Task.FromResult(initialKey)
      EvaluateGuards = fun _ -> Allowed
      EvaluateEventGuards = fun _ -> Allowed
      ExecuteTransition = fun _ _ _ -> Task.FromResult(TransitionAttemptResult.NoEvent) }

let private simpleMachine guards stateMetadata : StateMachine<TestState, TestEvent, unit> =
    { Initial = Idle
      InitialContext = ()
      Transition = fun _ _ _ -> TransitionResult.Invalid "test"
      Guards = guards
      StateMetadata = stateMetadata }

let private options = { ResourceName = "TestResource" }

// === Label formatting tests (internal helpers, still testable) ===

[<Tests>]
let labelFormattingTests =
    testList
        "Smcat.Generator.formatLabel"
        [ test "event only" {
              Expect.equal (formatLabel (Some "start") None None) (Some "start") ""
          }
          test "event and guard" {
              Expect.equal (formatLabel (Some "start") (Some "isReady") None) (Some "start [isReady]") ""
          }
          test "event and action" {
              Expect.equal (formatLabel (Some "start") None (Some "log")) (Some "start / log") ""
          }
          test "all three" {
              Expect.equal (formatLabel (Some "start") (Some "isReady") (Some "log")) (Some "start [isReady] / log") ""
          }
          test "guard only" {
              Expect.equal (formatLabel None (Some "isReady") None) (Some "[isReady]") ""
          }
          test "action only" {
              Expect.equal (formatLabel None None (Some "log")) (Some "/ log") ""
          }
          test "none" {
              Expect.equal (formatLabel None None None) None ""
          } ]

// === Transition formatting tests ===

[<Tests>]
let transitionFormattingTests =
    testList
        "Smcat.Generator.formatTransition"
        [ test "with label" {
              Expect.equal (formatTransition "idle" "running" (Some "start")) "idle => running: start;" ""
          }
          test "without label" {
              Expect.equal (formatTransition "idle" "running" None) "idle => running;" ""
          }
          test "quotes names with spaces" {
              Expect.equal (formatTransition "my state" "next" (Some "go")) "\"my state\" => next: go;" ""
          }
          test "no quoting for underscores, dots, hyphens" {
              Expect.equal (formatTransition "state_one" "v2.0-beta" None) "state_one => v2.0-beta;" ""
          } ]

// === Full generator tests using StateMachineMetadata ===

[<Tests>]
let generatorTests =
    testList
        "Smcat.Generator"
        [ test "emits initial transition first" {
              let machine =
                  simpleMachine
                      []
                      (Map.ofList
                          [ (Idle, { AllowedMethods = [ "GET" ]; IsFinal = false; Description = None })
                            (Running, { AllowedMethods = [ "GET" ]; IsFinal = false; Description = None }) ])

              let handlers =
                  Map.ofList
                      [ ("Idle", [ ("GET", dummyHandler) ])
                        ("Running", [ ("GET", dummyHandler) ]) ]

              let result = generate options (makeMetadata machine handlers)
              let lines = result.Split('\n')
              Expect.equal lines.[0] "initial => Idle;" "first line is initial transition"
          }

          test "emits self-messages for each HTTP method" {
              let machine =
                  simpleMachine
                      []
                      (Map.ofList
                          [ (Idle, { AllowedMethods = [ "GET"; "POST" ]; IsFinal = false; Description = None }) ])

              let handlers =
                  Map.ofList [ ("Idle", [ ("GET", dummyHandler); ("POST", dummyHandler) ]) ]

              let result = generate options (makeMetadata machine handlers)
              let lines = result.Split('\n')
              Expect.equal lines.[1] "Idle => Idle: GET;" "GET self-message"
              Expect.equal lines.[2] "Idle => Idle: POST;" "POST self-message"
          }

          test "emits final state transitions last" {
              let machine =
                  simpleMachine
                      []
                      (Map.ofList
                          [ (Idle, { AllowedMethods = [ "GET" ]; IsFinal = false; Description = None })
                            (Completed, { AllowedMethods = []; IsFinal = true; Description = None }) ])

              let handlers =
                  Map.ofList
                      [ ("Idle", [ ("GET", dummyHandler) ])
                        ("Completed", []) ]

              let result = generate options (makeMetadata machine handlers)
              let lines = result.Split('\n')
              let lastLine = lines.[lines.Length - 1]
              Expect.equal lastLine "Completed => final;" "last line is final transition"
          }

          test "states ordered: initial first, others alphabetically" {
              let machine =
                  simpleMachine
                      []
                      (Map.ofList
                          [ (Idle, { AllowedMethods = [ "GET" ]; IsFinal = false; Description = None })
                            (Running, { AllowedMethods = [ "GET" ]; IsFinal = false; Description = None })
                            (Stopped, { AllowedMethods = [ "GET" ]; IsFinal = false; Description = None }) ])

              let handlers =
                  Map.ofList
                      [ ("Idle", [ ("GET", dummyHandler) ])
                        ("Running", [ ("GET", dummyHandler) ])
                        ("Stopped", [ ("GET", dummyHandler) ]) ]

              let result = generate options (makeMetadata machine handlers)
              let lines = result.Split('\n')
              // initial => Idle; Idle self; Running self; Stopped self
              Expect.equal lines.[0] "initial => Idle;" "initial first"
              Expect.equal lines.[1] "Idle => Idle: GET;" "Idle (initial) second"
              Expect.equal lines.[2] "Running => Running: GET;" "Running alphabetically"
              Expect.equal lines.[3] "Stopped => Stopped: GET;" "Stopped alphabetically"
          }

          test "single state, no handlers" {
              let machine =
                  simpleMachine
                      []
                      (Map.ofList [ (Idle, { AllowedMethods = []; IsFinal = false; Description = None }) ])

              let handlers = Map.ofList [ ("Idle", []) ]
              let result = generate options (makeMetadata machine handlers)
              Expect.equal result "initial => Idle;" "only initial transition"
          } ]
