module Frank.Statecharts.Tests.Wsd.GeneratorTests

open System.Threading.Tasks
open Microsoft.AspNetCore.Http
open Expecto
open Frank.Statecharts
open Frank.Statecharts.Ast
open Frank.Statecharts.Wsd.Generator

// --- Test state machine types ---

type TurnstileState =
    | Locked
    | Unlocked
    | Broken

type TurnstileEvent =
    | Coin
    | Push
    | Break

type SingleState = Only

type SingleEvent = Noop

// --- Helper: construct minimal StateMachineMetadata for testing ---

let private makeMetadata
    (machine: StateMachine<'S, 'E, 'C>)
    (stateHandlerMap: Map<string, (string * RequestDelegate) list>)
    : StateMachineMetadata =
    let initialKey = machine.Initial.ToString()

    let guardNames =
        machine.Guards
        |> List.map (fun g ->
            match g with
            | AccessControl(name, _) -> name
            | EventValidation(name, _) -> name)

    { Machine = box machine
      StateHandlerMap = stateHandlerMap
      ResolveInstanceId = fun _ -> "test"
      TransitionObservers = []
      InitialStateKey = initialKey
      GuardNames = guardNames
      StateMetadataMap = Map.empty
      GetCurrentStateKey = fun _ _ _ -> Task.FromResult(initialKey)
      EvaluateGuards = fun _ -> Allowed
      EvaluateEventGuards = fun _ -> Allowed
      ExecuteTransition = fun _ _ _ -> Task.FromResult(TransitionAttemptResult.NoEvent)
      Roles = []
      ResolveRoles = fun _ -> Set.empty
      Hierarchy = None }

// --- Test state machines ---

let private dummyHandler = RequestDelegate(fun _ -> Task.CompletedTask)

let private turnstileMachine: StateMachine<TurnstileState, TurnstileEvent, unit> =
    { Initial = Locked
      InitialContext = ()
      Transition = fun _ _ _ -> TransitionResult.Invalid "test"
      Guards = []
      StateMetadata = Map.empty }

let private turnstileHandlerMap =
    Map.ofList
        [ "Locked",
          [ ("GET", dummyHandler)
            ("POST", dummyHandler) ]
          "Unlocked",
          [ ("GET", dummyHandler)
            ("POST", dummyHandler) ]
          "Broken", [ ("GET", dummyHandler) ] ]

let private singleStateMachine: StateMachine<SingleState, SingleEvent, unit> =
    { Initial = Only
      InitialContext = ()
      Transition = fun _ _ _ -> TransitionResult.Invalid "test"
      Guards = []
      StateMetadata = Map.empty }

let private guardedMachine: StateMachine<TurnstileState, TurnstileEvent, unit> =
    { Initial = Locked
      InitialContext = ()
      Transition = fun _ _ _ -> TransitionResult.Invalid "test"
      Guards =
        [ AccessControl("role", fun _ -> Allowed)
          AccessControl("state", fun _ -> Allowed) ]
      StateMetadata = Map.empty }

let private singleGuardMachine: StateMachine<TurnstileState, TurnstileEvent, unit> =
    { Initial = Locked
      InitialContext = ()
      Transition = fun _ _ _ -> TransitionResult.Invalid "test"
      Guards =
        [ AccessControl("admin", fun _ -> Allowed) ]
      StateMetadata = Map.empty }

// --- Helper to unwrap Result or fail with error message ---

let private unwrap (result: Result<'T, GeneratorError>) : 'T =
    match result with
    | Ok value -> value
    | Error err -> failtest $"Expected Ok but got Error: %A{err}"

// --- Helper to extract elements by type ---

let private transitions (doc: StatechartDocument) =
    doc.Elements
    |> List.choose (function
        | TransitionElement t -> Some t
        | _ -> None)

let private notes (doc: StatechartDocument) =
    doc.Elements
    |> List.choose (function
        | NoteElement n -> Some n
        | _ -> None)

let private stateDecls (doc: StatechartDocument) =
    doc.Elements
    |> List.choose (function
        | StateDecl s -> Some s
        | _ -> None)

// --- Annotation extraction helpers ---

let private tryWsdNotePosition (annotations: Annotation list) =
    annotations
    |> List.tryPick (function
        | WsdAnnotation(WsdNotePosition pos) -> Some pos
        | _ -> None)

let private tryWsdGuardPairs (annotations: Annotation list) =
    annotations
    |> List.tryPick (function
        | WsdAnnotation(WsdGuardData pairs) -> Some pairs
        | _ -> None)

let private tryWsdTransitionStyle (annotations: Annotation list) =
    annotations
    |> List.tryPick (function
        | WsdAnnotation(WsdTransitionStyle style) -> Some style
        | _ -> None)

// --- Tests ---

[<Tests>]
let generatorTests =
    testList
        "Generator"
        [
          // === Happy path: turnstile ===
          testCase "generate turnstile produces Ok"
          <| fun _ ->
              let metadata = makeMetadata turnstileMachine turnstileHandlerMap
              let result = generate { ResourceName = "turnstile" } metadata
              Expect.isOk result "should produce Ok"

          testCase "title is resource name"
          <| fun _ ->
              let metadata = makeMetadata turnstileMachine turnstileHandlerMap
              let doc = generate { ResourceName = "turnstile" } metadata |> unwrap
              Expect.equal doc.Title (Some "turnstile") "title matches resource name"

          testCase "initial state is first state declaration"
          <| fun _ ->
              let metadata = makeMetadata turnstileMachine turnstileHandlerMap
              let doc = generate { ResourceName = "turnstile" } metadata |> unwrap
              let states = stateDecls doc
              Expect.equal states.[0].Identifier (Some "Locked") "Locked is first"

          testCase "all states present as state declarations"
          <| fun _ ->
              let metadata = makeMetadata turnstileMachine turnstileHandlerMap
              let doc = generate { ResourceName = "turnstile" } metadata |> unwrap
              let states = stateDecls doc
              Expect.equal states.Length 3 "3 state declarations"

              let names = states |> List.choose (fun s -> s.Identifier)
              Expect.contains names "Locked" "has Locked"
              Expect.contains names "Unlocked" "has Unlocked"
              Expect.contains names "Broken" "has Broken"

          testCase "state declarations ordered: initial first then alphabetical"
          <| fun _ ->
              let metadata = makeMetadata turnstileMachine turnstileHandlerMap
              let doc = generate { ResourceName = "turnstile" } metadata |> unwrap
              let names = stateDecls doc |> List.choose (fun s -> s.Identifier)
              // Locked first (initial), then Broken, Unlocked (alphabetical)
              Expect.equal names [ "Locked"; "Broken"; "Unlocked" ] "correct order"

          testCase "transitions for each handler"
          <| fun _ ->
              let metadata = makeMetadata turnstileMachine turnstileHandlerMap
              let doc = generate { ResourceName = "turnstile" } metadata |> unwrap
              let trans = transitions doc
              // Locked: GET, POST; Broken: GET; Unlocked: GET, POST = 5 total
              Expect.equal trans.Length 5 "5 transitions total"

              let lockedTrans =
                  trans |> List.filter (fun t -> t.Source = "Locked") |> List.map (fun t -> t.Event.Value)

              Expect.containsAll lockedTrans [ "GET"; "POST" ] "Locked has GET and POST"

              let brokenTrans =
                  trans |> List.filter (fun t -> t.Source = "Broken") |> List.map (fun t -> t.Event.Value)

              Expect.containsAll brokenTrans [ "GET" ] "Broken has GET"

              let unlockedTrans =
                  trans |> List.filter (fun t -> t.Source = "Unlocked") |> List.map (fun t -> t.Event.Value)

              Expect.containsAll unlockedTrans [ "GET"; "POST" ] "Unlocked has GET and POST"

          testCase "all arrows are solid forward"
          <| fun _ ->
              let metadata = makeMetadata turnstileMachine turnstileHandlerMap
              let doc = generate { ResourceName = "turnstile" } metadata |> unwrap
              let trans = transitions doc

              for t in trans do
                  let style = tryWsdTransitionStyle t.Annotations
                  Expect.isSome style $"transition style present for {t.Event}"
                  Expect.equal style.Value.ArrowStyle Solid $"arrow style Solid for {t.Event}"
                  Expect.equal style.Value.Direction Forward $"direction Forward for {t.Event}"

          testCase "self-transitions: source equals target"
          <| fun _ ->
              let metadata = makeMetadata turnstileMachine turnstileHandlerMap
              let doc = generate { ResourceName = "turnstile" } metadata |> unwrap
              let trans = transitions doc

              for t in trans do
                  Expect.equal t.Source t.Target.Value $"self-transition for {t.Event} in {t.Source}"

          testCase "state declarations have no label (explicit)"
          <| fun _ ->
              let metadata = makeMetadata turnstileMachine turnstileHandlerMap
              let doc = generate { ResourceName = "turnstile" } metadata |> unwrap

              for s in stateDecls doc do
                  let stateId = s.Identifier |> Option.defaultValue ""
                  Expect.isNone s.Label (sprintf "state %s has no label (explicit declaration)" stateId)

          testCase "no auto-number directive present"
          <| fun _ ->
              let metadata = makeMetadata turnstileMachine turnstileHandlerMap
              let doc = generate { ResourceName = "turnstile" } metadata |> unwrap
              let hasAutoNumber =
                  doc.Elements
                  |> List.exists (function
                      | DirectiveElement(AutoNumberDirective _) -> true
                      | _ -> false)
              Expect.isFalse hasAutoNumber "no auto-number directive should be present"

          testCase "all positions are synthetic (0,0)"
          <| fun _ ->
              let metadata = makeMetadata turnstileMachine turnstileHandlerMap
              let doc = generate { ResourceName = "turnstile" } metadata |> unwrap
              let synth: SourcePosition = { Line = 0; Column = 0 }

              for s in stateDecls doc do
                  let stateId = s.Identifier |> Option.defaultValue ""
                  Expect.equal s.Position (Some synth) (sprintf "state %s has synthetic position" stateId)

              for t in transitions doc do
                  Expect.equal t.Position (Some synth) $"transition {t.Event} has synthetic position"

          // === Single state, no transitions ===
          testCase "single state no transitions"
          <| fun _ ->
              let metadata = makeMetadata singleStateMachine (Map.ofList [ "Only", [] ])
              let doc = generate { ResourceName = "single" } metadata |> unwrap
              let states = stateDecls doc
              Expect.equal states.Length 1 "1 state declaration"
              Expect.equal states.[0].Identifier (Some "Only") "state is Only"
              let trans = transitions doc
              Expect.isEmpty trans "no transitions"

          // === Empty handler map (only initial state) ===
          testCase "empty handler map: initial state as sole state declaration"
          <| fun _ ->
              let metadata = makeMetadata turnstileMachine Map.empty
              let doc = generate { ResourceName = "empty" } metadata |> unwrap
              let states = stateDecls doc
              Expect.equal states.Length 1 "1 state declaration (initial state only)"
              Expect.equal states.[0].Identifier (Some "Locked") "state is initial state"
              let trans = transitions doc
              Expect.isEmpty trans "no transitions"

          // === Guard emission ===
          testCase "machine with guards emits note"
          <| fun _ ->
              let metadata = makeMetadata singleGuardMachine (Map.ofList [ "Locked", [] ])
              let doc = generate { ResourceName = "guarded" } metadata |> unwrap
              let noteElems = notes doc
              Expect.hasLength noteElems 1 "one note element"
              let note = noteElems.[0]
              let notePos = tryWsdNotePosition note.Annotations
              Expect.isSome notePos "note position annotation present"
              Expect.equal notePos.Value Over "note position is Over"
              Expect.equal note.Target "Locked" "note target is initial state"
              let guardPairs = tryWsdGuardPairs note.Annotations
              Expect.isSome guardPairs "guard annotation present"
              Expect.equal guardPairs.Value [ ("admin", "*") ] "guard pair with wildcard"

          testCase "multiple guards combined in single note"
          <| fun _ ->
              let metadata = makeMetadata guardedMachine (Map.ofList [ "Locked", [] ])
              let doc = generate { ResourceName = "multi-guard" } metadata |> unwrap
              let noteElems = notes doc
              Expect.hasLength noteElems 1 "one note element"
              let guardPairs = tryWsdGuardPairs noteElems.[0].Annotations
              Expect.isSome guardPairs "guard annotation present"
              Expect.equal guardPairs.Value [ ("role", "*"); ("state", "*") ] "both guards with wildcards"

          testCase "machine with no guards emits no notes"
          <| fun _ ->
              let metadata = makeMetadata turnstileMachine turnstileHandlerMap
              let doc = generate { ResourceName = "no-guards" } metadata |> unwrap
              let noteElems = notes doc
              Expect.isEmpty noteElems "no note elements"

          // === Error cases ===
          testCase "unrecognized machine type returns error"
          <| fun _ ->
              let metadata =
                  { Machine = box "not a machine"
                    StateHandlerMap = Map.empty
                    ResolveInstanceId = fun _ -> "test"
                    TransitionObservers = []
                    InitialStateKey = "test"
                    GuardNames = []
                    StateMetadataMap = Map.empty
                    GetCurrentStateKey = fun _ _ _ -> Task.FromResult("test")
                    EvaluateGuards = fun _ -> Allowed
                    EvaluateEventGuards = fun _ -> Allowed
                    ExecuteTransition = fun _ _ _ -> Task.FromResult(TransitionAttemptResult.NoEvent)
                    Roles = []
                    ResolveRoles = fun _ -> Set.empty
                    Hierarchy = None }

              let result = generate { ResourceName = "bad" } metadata

              match result with
              | Error(UnrecognizedMachineType typeName) ->
                  Expect.stringContains typeName "String" "type name includes String"
              | _ -> failtest "expected UnrecognizedMachineType error"

          // === Element ordering ===
          testCase "element order: state declarations, then guards, then transitions"
          <| fun _ ->
              let metadata = makeMetadata guardedMachine turnstileHandlerMap
              let doc = generate { ResourceName = "ordered" } metadata |> unwrap

              // Verify: all StateDecls come first, then NoteElements, then TransitionElements
              let mutable phase = 0 // 0=state decls, 1=notes, 2=transitions

              for elem in doc.Elements do
                  match elem with
                  | StateDecl _ ->
                      Expect.equal phase 0 "state declarations come first"
                  | NoteElement _ ->
                      if phase = 0 then phase <- 1
                      Expect.isLessThanOrEqual phase 1 "notes come after state declarations"
                  | TransitionElement _ ->
                      if phase < 2 then phase <- 2
                      Expect.equal phase 2 "transitions come last"
                  | _ -> () ]
