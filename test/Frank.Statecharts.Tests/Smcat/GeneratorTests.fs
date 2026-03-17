module Frank.Statecharts.Tests.Smcat.GeneratorTests

open System.Threading.Tasks
open Microsoft.AspNetCore.Http
open Expecto
open Frank.Statecharts
open Frank.Statecharts.Ast
open Frank.Statecharts.Smcat.Generator
open Frank.Statecharts.Smcat.Serializer

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

/// Unwrap generator Result or fail the test.
let private unwrapResult (result: Result<StatechartDocument, GeneratorError>) : StatechartDocument =
    match result with
    | Ok doc -> doc
    | Error e -> failwithf "Generator returned error: %A" e

/// Extract transitions from a StatechartDocument.
let private extractTransitions (doc: StatechartDocument) =
    doc.Elements
    |> List.choose (fun e ->
        match e with
        | TransitionElement t -> Some t
        | _ -> None)

/// Extract state declarations from a StatechartDocument.
let private extractStates (doc: StatechartDocument) =
    doc.Elements
    |> List.choose (fun e ->
        match e with
        | StateDecl s -> Some s
        | _ -> None)

/// Generate and serialize to smcat text for text-based assertions.
let private generateText (options: GenerateOptions) (metadata: StateMachineMetadata) : string =
    let doc = generate options metadata |> unwrapResult
    serialize doc

// === Full generator tests using StateMachineMetadata ===

[<Tests>]
let generatorTests =
    testList
        "Smcat.Generator"
        [ test "emits initial transition" {
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

              let doc = generate options (makeMetadata machine handlers) |> unwrapResult
              let ts = extractTransitions doc
              let initialT = ts |> List.tryFind (fun t -> t.Source = "initial")
              Expect.isSome initialT "has initial transition"
              Expect.equal initialT.Value.Target (Some "Idle") "initial transition targets Idle"
          }

          test "emits self-transitions for each HTTP method" {
              let machine =
                  simpleMachine
                      []
                      (Map.ofList
                          [ (Idle, { AllowedMethods = [ "GET"; "POST" ]; IsFinal = false; Description = None }) ])

              let handlers =
                  Map.ofList [ ("Idle", [ ("GET", dummyHandler); ("POST", dummyHandler) ]) ]

              let doc = generate options (makeMetadata machine handlers) |> unwrapResult
              let ts = extractTransitions doc
              let selfTs = ts |> List.filter (fun t -> t.Source = "Idle" && t.Target = Some "Idle")
              Expect.equal selfTs.Length 2 "two self-transitions"
              Expect.equal selfTs[0].Event (Some "GET") "GET self-transition"
              Expect.equal selfTs[1].Event (Some "POST") "POST self-transition"
          }

          test "emits final state transitions" {
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

              let doc = generate options (makeMetadata machine handlers) |> unwrapResult
              let ts = extractTransitions doc
              let finalT = ts |> List.tryFind (fun t -> t.Target = Some "final")
              Expect.isSome finalT "has final transition"
              Expect.equal finalT.Value.Source "Completed" "Completed => final"
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

              let doc = generate options (makeMetadata machine handlers) |> unwrapResult
              let ss = extractStates doc
              // States should be ordered: Idle (initial) first, then Running, Stopped alphabetically
              Expect.equal ss[0].Identifier (Some "Idle") "Idle (initial) first"
              Expect.equal ss[1].Identifier (Some "Running") "Running alphabetically"
              Expect.equal ss[2].Identifier (Some "Stopped") "Stopped alphabetically"
          }

          test "single state, no handlers" {
              let machine =
                  simpleMachine
                      []
                      (Map.ofList [ (Idle, { AllowedMethods = []; IsFinal = false; Description = None }) ])

              let handlers = Map.ofList [ ("Idle", []) ]
              let doc = generate options (makeMetadata machine handlers) |> unwrapResult
              let ts = extractTransitions doc
              // Only the initial => Idle transition
              Expect.equal ts.Length 1 "one transition"
              Expect.equal ts[0].Source "initial" "source is initial"
              Expect.equal ts[0].Target (Some "Idle") "target is Idle"
          }

          test "serialized output contains all elements" {
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

              let result = generateText options (makeMetadata machine handlers)
              // Serialized text should contain key elements
              Expect.stringContains result "initial => Idle" "has initial transition"
              Expect.stringContains result "Idle => Idle: GET" "has GET self-transition"
              Expect.stringContains result "Completed => final" "has final transition"
          } ]
