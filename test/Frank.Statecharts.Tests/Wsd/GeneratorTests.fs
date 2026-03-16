module Frank.Statecharts.Tests.Wsd.GeneratorTests

open System.Threading.Tasks
open Microsoft.AspNetCore.Http
open Expecto
open Frank.Statecharts
open Frank.Statecharts.Wsd.Types
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

    let guardNames = machine.Guards |> List.map (fun g -> g.Name)

    let stateMetadataMap =
        machine.StateMetadata
        |> Map.toList
        |> List.map (fun (s, info) -> (string s, info))
        |> Map.ofList

    { Machine = box machine
      StateHandlerMap = stateHandlerMap
      ResolveInstanceId = fun _ -> "test"
      TransitionObservers = []
      InitialStateKey = initialKey
      GuardNames = guardNames
      StateMetadataMap = stateMetadataMap
      GetCurrentStateKey = fun _ _ _ -> Task.FromResult(initialKey)
      EvaluateGuards = fun _ -> Allowed
      ExecuteTransition = fun _ _ _ -> Task.FromResult(TransitionAttemptResult.NoEvent) }

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
        [ { Name = "role"
            Predicate = fun _ -> Allowed }
          { Name = "state"
            Predicate = fun _ -> Allowed } ]
      StateMetadata = Map.empty }

let private singleGuardMachine: StateMachine<TurnstileState, TurnstileEvent, unit> =
    { Initial = Locked
      InitialContext = ()
      Transition = fun _ _ _ -> TransitionResult.Invalid "test"
      Guards =
        [ { Name = "admin"
            Predicate = fun _ -> Allowed } ]
      StateMetadata = Map.empty }

// --- Helper to extract elements by type ---

let private messages (diagram: Diagram) =
    diagram.Elements
    |> List.choose (function
        | MessageElement m -> Some m
        | _ -> None)

let private notes (diagram: Diagram) =
    diagram.Elements
    |> List.choose (function
        | NoteElement n -> Some n
        | _ -> None)

let private participantDecls (diagram: Diagram) =
    diagram.Elements
    |> List.choose (function
        | ParticipantDecl p -> Some p
        | _ -> None)

// --- Tests ---

[<Tests>]
let generatorTests =
    testList
        "Generator"
        [
          // === Happy path: turnstile ===
          testCase "generate turnstile produces diagram"
          <| fun _ ->
              let metadata = makeMetadata turnstileMachine turnstileHandlerMap
              let diagram = generate { ResourceName = "turnstile" } metadata
              Expect.isSome diagram.Title "should produce a diagram with a title"

          testCase "title is resource name"
          <| fun _ ->
              let metadata = makeMetadata turnstileMachine turnstileHandlerMap
              let diagram = generate { ResourceName = "turnstile" } metadata
              Expect.equal diagram.Title (Some "turnstile") "title matches resource name"

          testCase "initial state is first participant"
          <| fun _ ->
              let metadata = makeMetadata turnstileMachine turnstileHandlerMap
              let diagram = generate { ResourceName = "turnstile" } metadata
              Expect.equal diagram.Participants.[0].Name "Locked" "Locked is first"

          testCase "all states present as participants"
          <| fun _ ->
              let metadata = makeMetadata turnstileMachine turnstileHandlerMap
              let diagram = generate { ResourceName = "turnstile" } metadata
              Expect.equal diagram.Participants.Length 3 "3 participants"

              let names = diagram.Participants |> List.map (fun p -> p.Name)
              Expect.contains names "Locked" "has Locked"
              Expect.contains names "Unlocked" "has Unlocked"
              Expect.contains names "Broken" "has Broken"

          testCase "participants ordered: initial first then alphabetical"
          <| fun _ ->
              let metadata = makeMetadata turnstileMachine turnstileHandlerMap
              let diagram = generate { ResourceName = "turnstile" } metadata
              let names = diagram.Participants |> List.map (fun p -> p.Name)
              // Locked first (initial), then Broken, Unlocked (alphabetical)
              Expect.equal names [ "Locked"; "Broken"; "Unlocked" ] "correct order"

          testCase "messages for each handler"
          <| fun _ ->
              let metadata = makeMetadata turnstileMachine turnstileHandlerMap
              let diagram = generate { ResourceName = "turnstile" } metadata
              let msgs = messages diagram
              // Locked: GET, POST; Broken: GET; Unlocked: GET, POST = 5 total
              Expect.equal msgs.Length 5 "5 messages total"

              let lockedMsgs =
                  msgs |> List.filter (fun m -> m.Sender = "Locked") |> List.map (fun m -> m.Label)

              Expect.containsAll lockedMsgs [ "GET"; "POST" ] "Locked has GET and POST"

              let brokenMsgs =
                  msgs |> List.filter (fun m -> m.Sender = "Broken") |> List.map (fun m -> m.Label)

              Expect.containsAll brokenMsgs [ "GET" ] "Broken has GET"

              let unlockedMsgs =
                  msgs |> List.filter (fun m -> m.Sender = "Unlocked") |> List.map (fun m -> m.Label)

              Expect.containsAll unlockedMsgs [ "GET"; "POST" ] "Unlocked has GET and POST"

          testCase "all arrows are solid forward"
          <| fun _ ->
              let metadata = makeMetadata turnstileMachine turnstileHandlerMap
              let diagram = generate { ResourceName = "turnstile" } metadata
              let msgs = messages diagram

              for m in msgs do
                  Expect.equal m.ArrowStyle Solid $"arrow style Solid for {m.Label}"
                  Expect.equal m.Direction Forward $"direction Forward for {m.Label}"

          testCase "self-messages: sender equals receiver"
          <| fun _ ->
              let metadata = makeMetadata turnstileMachine turnstileHandlerMap
              let diagram = generate { ResourceName = "turnstile" } metadata
              let msgs = messages diagram

              for m in msgs do
                  Expect.equal m.Sender m.Receiver $"self-message for {m.Label} in {m.Sender}"

          testCase "participants are explicit"
          <| fun _ ->
              let metadata = makeMetadata turnstileMachine turnstileHandlerMap
              let diagram = generate { ResourceName = "turnstile" } metadata
              for p in diagram.Participants do
                  Expect.isTrue p.Explicit $"participant {p.Name} is explicit"

          testCase "autoNumber is false"
          <| fun _ ->
              let metadata = makeMetadata turnstileMachine turnstileHandlerMap
              let diagram = generate { ResourceName = "turnstile" } metadata
              Expect.isFalse diagram.AutoNumber "autoNumber should be false"

          testCase "all positions are synthetic (0,0)"
          <| fun _ ->
              let metadata = makeMetadata turnstileMachine turnstileHandlerMap
              let diagram = generate { ResourceName = "turnstile" } metadata
              let synth = { Line = 0; Column = 0 }

              for p in diagram.Participants do
                  Expect.equal p.Position synth $"participant {p.Name} has synthetic position"

              for m in messages diagram do
                  Expect.equal m.Position synth $"message {m.Label} has synthetic position"

          // === Single state, no transitions ===
          testCase "single state no transitions"
          <| fun _ ->
              let metadata = makeMetadata singleStateMachine (Map.ofList [ "Only", [] ])
              let diagram = generate { ResourceName = "single" } metadata
              Expect.equal diagram.Participants.Length 1 "1 participant"
              Expect.equal diagram.Participants.[0].Name "Only" "participant is Only"
              let msgs = messages diagram
              Expect.isEmpty msgs "no messages"

          // === Empty handler map (only initial state) ===
          testCase "empty handler map: initial state as sole participant"
          <| fun _ ->
              let metadata = makeMetadata turnstileMachine Map.empty
              let diagram = generate { ResourceName = "empty" } metadata
              Expect.equal diagram.Participants.Length 1 "1 participant (initial state only)"
              Expect.equal diagram.Participants.[0].Name "Locked" "participant is initial state"
              let msgs = messages diagram
              Expect.isEmpty msgs "no messages"

          // === Guard emission ===
          testCase "machine with guards emits note"
          <| fun _ ->
              let metadata = makeMetadata singleGuardMachine (Map.ofList [ "Locked", [] ])
              let diagram = generate { ResourceName = "guarded" } metadata
              let noteElems = notes diagram
              Expect.hasLength noteElems 1 "one note element"
              let note = noteElems.[0]
              Expect.equal note.NotePosition Over "note position is Over"
              Expect.equal note.Target "Locked" "note target is initial state"
              Expect.isSome note.Guard "guard annotation present"
              Expect.equal note.Guard.Value.Pairs [ ("admin", "*") ] "guard pair with wildcard"

          testCase "multiple guards combined in single note"
          <| fun _ ->
              let metadata = makeMetadata guardedMachine (Map.ofList [ "Locked", [] ])
              let diagram = generate { ResourceName = "multi-guard" } metadata
              let noteElems = notes diagram
              Expect.hasLength noteElems 1 "one note element"
              let guard = noteElems.[0].Guard.Value
              Expect.equal guard.Pairs [ ("role", "*"); ("state", "*") ] "both guards with wildcards"

          testCase "machine with no guards emits no notes"
          <| fun _ ->
              let metadata = makeMetadata turnstileMachine turnstileHandlerMap
              let diagram = generate { ResourceName = "no-guards" } metadata
              let noteElems = notes diagram
              Expect.isEmpty noteElems "no note elements"

          // === Element ordering ===
          testCase "element order: participants, then guards, then messages"
          <| fun _ ->
              let metadata = makeMetadata guardedMachine turnstileHandlerMap
              let diagram = generate { ResourceName = "ordered" } metadata
              // Verify: all ParticipantDecls come first, then NoteElements, then MessageElements
              let mutable phase = 0 // 0=participants, 1=notes, 2=messages

              for elem in diagram.Elements do
                  match elem with
                  | ParticipantDecl _ ->
                      Expect.equal phase 0 "participant declarations come first"
                  | NoteElement _ ->
                      if phase = 0 then phase <- 1
                      Expect.isLessThanOrEqual phase 1 "notes come after participants"
                  | MessageElement _ ->
                      if phase < 2 then phase <- 2
                      Expect.equal phase 2 "messages come last"
                  | _ -> () ]
