module Frank.Statecharts.Tests.Wsd.GeneratorRoundTripTests

open System.Threading.Tasks
open Microsoft.AspNetCore.Http
open Expecto
open Frank.Statecharts
open Frank.Statecharts.Ast
open Frank.Statecharts.Wsd.Generator
open Frank.Statecharts.Wsd.Serializer
open Frank.Statecharts.Wsd.Parser

// --- Test state machine types ---

type TurnstileState =
    | Locked
    | Unlocked
    | Broken

type TurnstileEvent =
    | Coin
    | Push
    | Break

// --- Helpers ---

/// Construct minimal StateMachineMetadata for roundtrip testing.
let private makeMetadata
    (machine: StateMachine<'S, 'E, 'C>)
    (stateHandlerMap: Map<string, (string * RequestDelegate) list>)
    : StateMachineMetadata =
    let initialKey = machine.Initial.ToString()

    let guardNames =
        machine.Guards
        |> List.map (function
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
      Hierarchy = StateHierarchy.build { States = [] }
      Statechart = None }

let private dummyHandler = RequestDelegate(fun _ -> Task.CompletedTask)

/// Extract participant names (state identifiers) from a ParseResult.
let private participantNames (r: ParseResult) =
    r.Document.Elements
    |> List.choose (function
        | StateDecl s -> s.Identifier
        | _ -> None)

/// Extract (source, target, event) triples from transitions in a ParseResult.
let private messageTriples (r: ParseResult) =
    r.Document.Elements
    |> List.choose (function
        | TransitionElement t -> Some(t.Source, t.Target |> Option.defaultValue "", t.Event |> Option.defaultValue "")
        | _ -> None)

/// Extract guard pairs from note annotations in a ParseResult.
let private guardPairs (r: ParseResult) =
    r.Document.Elements
    |> List.choose (function
        | NoteElement n ->
            n.Annotations
            |> List.tryPick (function
                | WsdAnnotation(WsdGuardData pairs) -> Some pairs
                | _ -> None)
        | _ -> None)
    |> List.concat

/// Run the full roundtrip pipeline and return the generated StatechartDocument, serialized text, and ParseResult.
let private roundtrip (options: GenerateOptions) (metadata: StateMachineMetadata) =
    match generate options metadata with
    | Error e -> failwithf "Generator failed: %A" e
    | Ok document ->
        let wsdText = serialize document
        let parseResult = parseWsd wsdText
        (document, wsdText, parseResult)

// --- Test state machines ---

let private turnstileMachine: StateMachine<TurnstileState, TurnstileEvent, unit> =
    { Initial = Locked
      InitialContext = ()
      Transition = fun _ _ _ -> TransitionResult.Invalid "stub"
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

// --- Tests ---

[<Tests>]
let generatorRoundTripTests =
    testList
        "GeneratorRoundTrip"
        [
          // === T014: Turnstile roundtrip ===
          testList
              "Turnstile roundtrip"
              [ test "parse succeeds with zero errors" {
                    let metadata = makeMetadata turnstileMachine turnstileHandlerMap
                    let (_, _, result) = roundtrip { ResourceName = "turnstile" } metadata
                    Expect.isEmpty result.Errors "no parse errors"
                }

                test "title matches resource name" {
                    let metadata = makeMetadata turnstileMachine turnstileHandlerMap
                    let (_, _, result) = roundtrip { ResourceName = "turnstile" } metadata
                    Expect.equal result.Document.Title (Some "turnstile") "title"
                }

                test "three participants" {
                    let metadata = makeMetadata turnstileMachine turnstileHandlerMap
                    let (_, _, result) = roundtrip { ResourceName = "turnstile" } metadata
                    Expect.equal (participantNames result).Length 3 "three participants"
                }

                test "initial state is first participant" {
                    let metadata = makeMetadata turnstileMachine turnstileHandlerMap
                    let (_, _, result) = roundtrip { ResourceName = "turnstile" } metadata
                    let names = participantNames result
                    Expect.equal names.[0] "Locked" "Locked is first"
                }

                test "all state names present" {
                    let metadata = makeMetadata turnstileMachine turnstileHandlerMap
                    let (_, _, result) = roundtrip { ResourceName = "turnstile" } metadata
                    let names = participantNames result |> Set.ofList
                    Expect.equal names (Set.ofList [ "Locked"; "Unlocked"; "Broken" ]) "all states"
                }

                test "correct number of messages" {
                    let metadata = makeMetadata turnstileMachine turnstileHandlerMap
                    let (_, _, result) = roundtrip { ResourceName = "turnstile" } metadata
                    let msgs = messageTriples result
                    // Locked: GET, POST; Broken: GET; Unlocked: GET, POST = 5 total
                    Expect.equal msgs.Length 5 "5 messages"
                }

                test "message labels are HTTP methods" {
                    let metadata = makeMetadata turnstileMachine turnstileHandlerMap
                    let (_, _, result) = roundtrip { ResourceName = "turnstile" } metadata
                    let labels = messageTriples result |> List.map (fun (_, _, l) -> l)
                    Expect.containsAll labels [ "GET"; "POST" ] "labels include GET and POST"
                }

                test "all messages are self-messages" {
                    let metadata = makeMetadata turnstileMachine turnstileHandlerMap
                    let (_, _, result) = roundtrip { ResourceName = "turnstile" } metadata
                    let msgs = messageTriples result

                    for (s, r, _) in msgs do
                        Expect.equal s r $"self-message: sender={s} receiver={r}"
                }

                test "no guard annotations" {
                    let metadata = makeMetadata turnstileMachine turnstileHandlerMap
                    let (_, _, result) = roundtrip { ResourceName = "turnstile" } metadata
                    let pairs = guardPairs result
                    Expect.isEmpty pairs "no guard annotations"
                }

                test "all participants are regular states" {
                    let metadata = makeMetadata turnstileMachine turnstileHandlerMap
                    let (_, _, result) = roundtrip { ResourceName = "turnstile" } metadata

                    let states =
                        result.Document.Elements
                        |> List.choose (function
                            | StateDecl s -> Some s
                            | _ -> None)

                    for s in states do
                        let stateId = s.Identifier |> Option.defaultValue ""
                        Expect.equal s.Kind Regular (sprintf "participant %s is Regular" stateId)
                }

                test "no implicit-participant warnings" {
                    let metadata = makeMetadata turnstileMachine turnstileHandlerMap
                    let (_, _, result) = roundtrip { ResourceName = "turnstile" } metadata

                    let implicitWarnings =
                        result.Warnings
                        |> List.filter (fun w -> w.Description.Contains("Implicit participant"))

                    Expect.isEmpty implicitWarnings "no implicit-participant warnings"
                } ]

          // === T015: Edge case roundtrip tests ===
          testList
              "Edge cases"
              [ test "single state no transitions roundtrips" {
                    let machine: StateMachine<string, string, unit> =
                        { Initial = "Terminal"
                          InitialContext = ()
                          Transition = fun _ _ _ -> TransitionResult.Invalid "stub"
                          Guards = []
                          StateMetadata = Map.empty }

                    let handlers = Map.ofList [ "Terminal", [] ]
                    let metadata = makeMetadata machine handlers
                    let (_, _, result) = roundtrip { ResourceName = "terminal" } metadata
                    Expect.isEmpty result.Errors "no errors"
                    Expect.equal (participantNames result) [ "Terminal" ] "one participant"
                    Expect.isEmpty (messageTriples result) "no messages"
                }

                test "self-transition roundtrips" {
                    let machine: StateMachine<string, string, unit> =
                        { Initial = "Active"
                          InitialContext = ()
                          Transition = fun _ _ _ -> TransitionResult.Invalid "stub"
                          Guards = []
                          StateMetadata = Map.empty }

                    let handlers =
                        Map.ofList [ "Active", [ ("POST", dummyHandler) ] ]

                    let metadata = makeMetadata machine handlers
                    let (_, _, result) = roundtrip { ResourceName = "self" } metadata
                    Expect.isEmpty result.Errors "no errors"
                    let msgs = messageTriples result
                    Expect.equal msgs.Length 1 "one message"

                    Expect.isTrue
                        (msgs |> List.forall (fun (s, r, _) -> s = r))
                        "all self-messages"
                }

                test "quoted state names roundtrip" {
                    let machine: StateMachine<string, string, unit> =
                        { Initial = "In Progress"
                          InitialContext = ()
                          Transition = fun _ _ _ -> TransitionResult.Invalid "stub"
                          Guards = []
                          StateMetadata = Map.empty }

                    let handlers =
                        Map.ofList [ "In Progress", [ ("GET", dummyHandler) ] ]

                    let metadata = makeMetadata machine handlers
                    let (_, _, result) = roundtrip { ResourceName = "workflow" } metadata
                    Expect.isEmpty result.Errors "no errors"

                    Expect.isTrue
                        (participantNames result |> List.contains "In Progress")
                        "quoted name survived roundtrip"
                }

                test "guards roundtrip as wildcard annotations" {
                    let guards: Guard<TurnstileState, TurnstileEvent, unit> list =
                        [ AccessControl("role", fun _ -> Allowed)
                          AccessControl("auth", fun _ -> Allowed) ]

                    let machine: StateMachine<TurnstileState, TurnstileEvent, unit> =
                        { Initial = Locked
                          InitialContext = ()
                          Transition = fun _ _ _ -> TransitionResult.Invalid "stub"
                          Guards = guards
                          StateMetadata = Map.empty }

                    let handlers =
                        Map.ofList [ "Locked", [ ("GET", dummyHandler) ] ]

                    let metadata = makeMetadata machine handlers
                    let (_, _, result) = roundtrip { ResourceName = "guarded" } metadata
                    Expect.isEmpty result.Errors "no errors"
                    let pairs = guardPairs result

                    Expect.isTrue
                        (pairs |> List.exists (fun (k, v) -> k = "role" && v = "*"))
                        "role guard survived roundtrip"

                    Expect.isTrue
                        (pairs |> List.exists (fun (k, v) -> k = "auth" && v = "*"))
                        "auth guard survived roundtrip"
                }

                test "20+ states roundtrip without error" {
                    let states = [ for i in 1..25 -> sprintf "State%d" i ]

                    let handlers =
                        states
                        |> List.map (fun s -> s, [ ("GET", dummyHandler) ])
                        |> Map.ofList

                    let machine: StateMachine<string, string, unit> =
                        { Initial = "State1"
                          InitialContext = ()
                          Transition = fun _ _ _ -> TransitionResult.Invalid "stub"
                          Guards = []
                          StateMetadata = Map.empty }

                    let metadata = makeMetadata machine handlers
                    let (_, _, result) = roundtrip { ResourceName = "large" } metadata
                    Expect.isEmpty result.Errors "no errors"
                    Expect.equal (participantNames result).Length 25 "25 participants"
                }

                test "initial state first in 20+ state roundtrip" {
                    let states = [ for i in 1..25 -> sprintf "State%d" i ]

                    let handlers =
                        states
                        |> List.map (fun s -> s, [ ("GET", dummyHandler) ])
                        |> Map.ofList

                    let machine: StateMachine<string, string, unit> =
                        { Initial = "State1"
                          InitialContext = ()
                          Transition = fun _ _ _ -> TransitionResult.Invalid "stub"
                          Guards = []
                          StateMetadata = Map.empty }

                    let metadata = makeMetadata machine handlers
                    let (_, _, result) = roundtrip { ResourceName = "large" } metadata
                    Expect.equal (participantNames result).[0] "State1" "initial state first"
                }

                test "single guard roundtrips" {
                    let guards: Guard<TurnstileState, TurnstileEvent, unit> list =
                        [ AccessControl("admin", fun _ -> Allowed) ]

                    let machine: StateMachine<TurnstileState, TurnstileEvent, unit> =
                        { Initial = Locked
                          InitialContext = ()
                          Transition = fun _ _ _ -> TransitionResult.Invalid "stub"
                          Guards = guards
                          StateMetadata = Map.empty }

                    let handlers =
                        Map.ofList [ "Locked", [ ("GET", dummyHandler) ] ]

                    let metadata = makeMetadata machine handlers
                    let (_, _, result) = roundtrip { ResourceName = "single-guard" } metadata
                    Expect.isEmpty result.Errors "no errors"
                    let pairs = guardPairs result
                    Expect.equal pairs.Length 1 "one guard pair"
                    Expect.equal (fst pairs.[0]) "admin" "guard name"
                    Expect.equal (snd pairs.[0]) "*" "wildcard value"
                } ]

          // === Roundtrip serialization consistency ===
          test "generated WSD text is parseable" {
              let metadata = makeMetadata turnstileMachine turnstileHandlerMap
              let (_, wsdText, result) = roundtrip { ResourceName = "turnstile" } metadata
              Expect.isEmpty result.Errors $"generated text should be parseable, got errors for:\n{wsdText}"
          }

          test "roundtrip is idempotent (generate->serialize->parse->serialize matches)" {
              let metadata = makeMetadata turnstileMachine turnstileHandlerMap
              let (_, wsdText1, result1) = roundtrip { ResourceName = "turnstile" } metadata
              // Re-serialize the parsed result
              let wsdText2 = serialize result1.Document
              // Both serialized forms should produce the same parse result
              let result2 = parseWsd wsdText2
              Expect.isEmpty result2.Errors "second parse has no errors"

              Expect.equal
                  (participantNames result1)
                  (participantNames result2)
                  "participants match after re-roundtrip"

              Expect.equal
                  (messageTriples result1)
                  (messageTriples result2)
                  "messages match after re-roundtrip"
          } ]
