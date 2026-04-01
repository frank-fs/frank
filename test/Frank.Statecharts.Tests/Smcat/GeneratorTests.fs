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
      ExecuteTransition = fun _ _ _ -> Task.FromResult(TransitionAttemptResult.NoEvent)
      Roles = []
      ResolveRoles = fun _ -> Set.empty
      Hierarchy = StateHierarchy.build { States = [] } }

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

/// Check whether an annotation list contains a SmcatStateType annotation with the given kind and origin.
let private hasSmcatStateType kind origin (annotations: Annotation list) =
    annotations
    |> List.exists (function
        | SmcatAnnotation(SmcatStateType(k, o)) -> k = kind && o = origin
        | _ -> false)

/// Check whether an annotation list contains a SmcatTransition annotation with the expected kind.
let private hasSmcatTransition expected (annotations: Annotation list) =
    annotations
    |> List.exists (function
        | SmcatAnnotation(SmcatTransition tk) -> tk = expected
        | _ -> false)

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
              let ss = extractStates doc
              let ts = extractTransitions doc

              // Initial pseudo-state declaration: Kind=Initial, SmcatStateType(Initial, Explicit)
              let initialStateDecl = ss |> List.tryFind (fun s -> s.Identifier = Some "initial")
              Expect.isSome initialStateDecl "has initial pseudo-state declaration"
              Expect.equal initialStateDecl.Value.Kind Initial "initial StateDecl has Kind=Initial"
              Expect.isTrue
                  (hasSmcatStateType Initial Explicit initialStateDecl.Value.Annotations)
                  "initial StateDecl has SmcatStateType(Initial, Explicit) annotation"

              // Initial transition targets the first domain state
              let initialT = ts |> List.tryFind (fun t -> t.Source = "initial")
              Expect.isSome initialT "has initial transition"
              Expect.equal initialT.Value.Target (Some "Idle") "initial transition targets Idle"
              Expect.isTrue
                  (hasSmcatTransition InitialTransition initialT.Value.Annotations)
                  "initial transition has SmcatTransition InitialTransition annotation"
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
              Expect.isTrue
                  (hasSmcatTransition SelfTransition selfTs[0].Annotations)
                  "GET self-transition has SmcatTransition SelfTransition annotation"
              Expect.isTrue
                  (hasSmcatTransition SelfTransition selfTs[1].Annotations)
                  "POST self-transition has SmcatTransition SelfTransition annotation"
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
              let ss = extractStates doc
              let ts = extractTransitions doc

              // Final pseudo-state declaration: Kind=Final, SmcatStateType(Final, Explicit)
              let finalStateDecl = ss |> List.tryFind (fun s -> s.Identifier = Some "final")
              Expect.isSome finalStateDecl "has final pseudo-state declaration"
              Expect.equal finalStateDecl.Value.Kind Final "final StateDecl has Kind=Final"
              Expect.isTrue
                  (hasSmcatStateType Final Explicit finalStateDecl.Value.Annotations)
                  "final StateDecl has SmcatStateType(Final, Explicit) annotation"

              // Final transition from Completed to final
              let finalT = ts |> List.tryFind (fun t -> t.Target = Some "final")
              Expect.isSome finalT "has final transition"
              Expect.equal finalT.Value.Source "Completed" "Completed => final"
              Expect.isTrue
                  (hasSmcatTransition FinalTransition finalT.Value.Annotations)
                  "final transition has SmcatTransition FinalTransition annotation"
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
              // Element ordering: initial pseudo-state, then domain states (Idle first, then alphabetically)
              // ss[0] = "initial" (pseudo-state), ss[1] = "Idle", ss[2] = "Running", ss[3] = "Stopped"
              Expect.equal ss[0].Identifier (Some "initial") "initial pseudo-state first"
              Expect.equal ss[1].Identifier (Some "Idle") "Idle (initial domain state) second"
              Expect.equal ss[2].Identifier (Some "Running") "Running alphabetically"
              Expect.equal ss[3].Identifier (Some "Stopped") "Stopped alphabetically"
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
              Expect.isTrue
                  (hasSmcatTransition InitialTransition ts[0].Annotations)
                  "initial transition has SmcatTransition InitialTransition annotation"
          }

          test "regular states have no SmcatStateType annotation" {
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
              let ss = extractStates doc
              // Domain states (non-pseudo) should have no SmcatStateType annotation
              let domainStates =
                  ss
                  |> List.filter (fun s ->
                      s.Identifier <> Some "initial" && s.Identifier <> Some "final")

              for s in domainStates do
                  let hasStateTypeAnnotation =
                      s.Annotations
                      |> List.exists (function
                          | SmcatAnnotation(SmcatStateType _) -> true
                          | _ -> false)

                  Expect.isFalse
                      hasStateTypeAnnotation
                      (sprintf "domain state '%A' should not have SmcatStateType annotation" s.Identifier)
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
