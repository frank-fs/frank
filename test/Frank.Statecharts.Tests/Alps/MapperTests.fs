module Frank.Statecharts.Tests.Alps.MapperTests

open Expecto
open Frank.Statecharts.Alps.Types
open Frank.Statecharts.Alps.JsonParser
open Frank.Statecharts.Alps.Mapper
open Frank.Statecharts.Ast
open Frank.Statecharts.Tests.Alps.GoldenFiles

/// Helper: extract all StateNodes from a StatechartDocument's elements.
let private getStates (doc: StatechartDocument) =
    doc.Elements
    |> List.choose (fun el ->
        match el with
        | StateDecl s -> Some s
        | _ -> None)

/// Helper: extract all TransitionEdges from a StatechartDocument's elements.
let private getTransitions (doc: StatechartDocument) =
    doc.Elements
    |> List.choose (fun el ->
        match el with
        | TransitionElement t -> Some t
        | _ -> None)

/// Helper: parse tic-tac-toe golden file.
let private parseTicTacToe () =
    parseAlpsJson ticTacToeAlpsJson
    |> Result.defaultWith (fun _ -> failwith "parse failed")

/// Helper: parse onboarding golden file.
let private parseOnboarding () =
    parseAlpsJson onboardingAlpsJson
    |> Result.defaultWith (fun _ -> failwith "parse failed")

[<Tests>]
let stateExtractionTests =
    testList
        "Alps.Mapper.StateExtraction"
        [ testCase "tic-tac-toe states are extracted"
          <| fun _ ->
              let alpsDoc = parseTicTacToe ()
              let statechart = toStatechartDocument alpsDoc
              let stateIds = getStates statechart |> List.map (fun s -> s.Identifier) |> Set.ofList

              Expect.containsAll
                  stateIds
                  (Set.ofList [ "XTurn"; "OTurn"; "Won"; "Draw" ])
                  "all game states extracted"

          testCase "tic-tac-toe extracts gameState as a state (it is an rt target)"
          <| fun _ ->
              let alpsDoc = parseTicTacToe ()
              let statechart = toStatechartDocument alpsDoc
              let stateIds = getStates statechart |> List.map (fun s -> s.Identifier) |> Set.ofList
              Expect.isTrue (Set.contains "gameState" stateIds) "gameState is an rt target so it is a state"

          testCase "tic-tac-toe does not extract pure data descriptors as states"
          <| fun _ ->
              let alpsDoc = parseTicTacToe ()
              let statechart = toStatechartDocument alpsDoc
              let stateIds = getStates statechart |> List.map (fun s -> s.Identifier) |> Set.ofList
              // position and player are pure data descriptors (no transition children, not rt targets)
              Expect.isFalse (Set.contains "position" stateIds) "position is not a state"
              Expect.isFalse (Set.contains "player" stateIds) "player is not a state"

          testCase "onboarding states are extracted"
          <| fun _ ->
              let alpsDoc = parseOnboarding ()
              let statechart = toStatechartDocument alpsDoc
              let stateIds = getStates statechart |> List.map (fun s -> s.Identifier) |> Set.ofList

              Expect.containsAll
                  stateIds
                  (Set.ofList [ "Welcome"; "CollectEmail"; "CollectProfile"; "Review"; "Complete" ])
                  "all onboarding states extracted"

          testCase "all states have Regular kind"
          <| fun _ ->
              let alpsDoc = parseTicTacToe ()
              let statechart = toStatechartDocument alpsDoc
              let states = getStates statechart

              for s in states do
                  Expect.equal s.Kind StateKind.Regular (sprintf "state %s should be Regular" s.Identifier) ]

[<Tests>]
let transitionMappingTests =
    testList
        "Alps.Mapper.TransitionMapping"
        [ testCase "makeMove transitions have correct source and target"
          <| fun _ ->
              let alpsDoc = parseTicTacToe ()
              let statechart = toStatechartDocument alpsDoc
              let makeMoves = getTransitions statechart |> List.filter (fun t -> t.Event = Some "makeMove")
              Expect.isNonEmpty makeMoves "should have makeMove transitions"

              // Verify XTurn -> OTurn transition exists
              let xToO =
                  makeMoves
                  |> List.tryFind (fun t -> t.Source = "XTurn" && t.Target = Some "OTurn")

              Expect.isSome xToO "XTurn -> OTurn transition"

              // Verify OTurn -> XTurn transition exists
              let oToX =
                  makeMoves
                  |> List.tryFind (fun t -> t.Source = "OTurn" && t.Target = Some "XTurn")

              Expect.isSome oToX "OTurn -> XTurn transition"

          testCase "makeMove transitions to Won exist from both XTurn and OTurn"
          <| fun _ ->
              let alpsDoc = parseTicTacToe ()
              let statechart = toStatechartDocument alpsDoc
              let makeMoves = getTransitions statechart |> List.filter (fun t -> t.Event = Some "makeMove")

              let xToWon =
                  makeMoves
                  |> List.tryFind (fun t -> t.Source = "XTurn" && t.Target = Some "Won")

              Expect.isSome xToWon "XTurn -> Won transition"

              let oToWon =
                  makeMoves
                  |> List.tryFind (fun t -> t.Source = "OTurn" && t.Target = Some "Won")

              Expect.isSome oToWon "OTurn -> Won transition"

          testCase "viewGame transitions are extracted from href references"
          <| fun _ ->
              let alpsDoc = parseTicTacToe ()
              let statechart = toStatechartDocument alpsDoc
              let viewGames = getTransitions statechart |> List.filter (fun t -> t.Event = Some "viewGame")
              Expect.isNonEmpty viewGames "should have viewGame transitions"

              // viewGame should appear from multiple states (XTurn, OTurn, Won, Draw)
              let sources = viewGames |> List.map (fun t -> t.Source) |> Set.ofList
              Expect.containsAll sources (Set.ofList [ "XTurn"; "OTurn"; "Won"; "Draw" ]) "viewGame from all states"

          testCase "onboarding transitions have correct source and target"
          <| fun _ ->
              let alpsDoc = parseOnboarding ()
              let statechart = toStatechartDocument alpsDoc
              let transitions = getTransitions statechart

              let startTrans =
                  transitions
                  |> List.tryFind (fun t -> t.Event = Some "start" && t.Source = "Welcome")

              Expect.isSome startTrans "Welcome -> CollectEmail via start"
              Expect.equal startTrans.Value.Target (Some "CollectEmail") "start targets CollectEmail"

          testCase "transition parameters are extracted"
          <| fun _ ->
              let alpsDoc = parseTicTacToe ()
              let statechart = toStatechartDocument alpsDoc

              let makeMove =
                  getTransitions statechart
                  |> List.find (fun t -> t.Event = Some "makeMove" && t.Source = "XTurn" && t.Target = Some "OTurn")

              Expect.containsAll
                  (Set.ofList makeMove.Parameters)
                  (Set.ofList [ "position"; "player" ])
                  "makeMove has position and player parameters" ]

[<Tests>]
let guardExtractionTests =
    testList
        "Alps.Mapper.GuardExtraction"
        [ testCase "guard labels extracted from ext elements"
          <| fun _ ->
              let alpsDoc = parseTicTacToe ()
              let statechart = toStatechartDocument alpsDoc
              let guarded = getTransitions statechart |> List.filter (fun t -> t.Guard.IsSome)
              Expect.isNonEmpty guarded "should have guarded transitions"

          testCase "role=PlayerX guard on XTurn -> OTurn makeMove"
          <| fun _ ->
              let alpsDoc = parseTicTacToe ()
              let statechart = toStatechartDocument alpsDoc

              let xToO =
                  getTransitions statechart
                  |> List.find (fun t -> t.Source = "XTurn" && t.Target = Some "OTurn" && t.Event = Some "makeMove")

              Expect.equal xToO.Guard (Some "role=PlayerX") "guard is role=PlayerX"

          testCase "wins guard on XTurn -> Won makeMove"
          <| fun _ ->
              let alpsDoc = parseTicTacToe ()
              let statechart = toStatechartDocument alpsDoc

              let xToWon =
                  getTransitions statechart
                  |> List.find (fun t -> t.Source = "XTurn" && t.Target = Some "Won" && t.Event = Some "makeMove")

              Expect.equal xToWon.Guard (Some "wins") "guard is wins"

          testCase "boardFull guard on XTurn -> Draw makeMove"
          <| fun _ ->
              let alpsDoc = parseTicTacToe ()
              let statechart = toStatechartDocument alpsDoc

              let xToDraw =
                  getTransitions statechart
                  |> List.find (fun t -> t.Source = "XTurn" && t.Target = Some "Draw" && t.Event = Some "makeMove")

              Expect.equal xToDraw.Guard (Some "boardFull") "guard is boardFull"

          testCase "transitions without ext elements have no guard"
          <| fun _ ->
              let alpsDoc = parseOnboarding ()
              let statechart = toStatechartDocument alpsDoc

              let startTrans =
                  getTransitions statechart
                  |> List.find (fun t -> t.Event = Some "start")

              Expect.isNone startTrans.Guard "start transition has no guard" ]

[<Tests>]
let httpMethodHintTests =
    testList
        "Alps.Mapper.HttpMethodHints"
        [ testCase "safe descriptor maps to Safe annotation"
          <| fun _ ->
              let alpsDoc = parseTicTacToe ()
              let statechart = toStatechartDocument alpsDoc

              let viewGame =
                  getTransitions statechart
                  |> List.find (fun t -> t.Event = Some "viewGame" && t.Source = "XTurn")

              let hasAlpsSafe =
                  viewGame.Annotations
                  |> List.exists (fun a ->
                      match a with
                      | AlpsAnnotation(AlpsTransitionType AlpsTransitionKind.Safe) -> true
                      | _ -> false)

              Expect.isTrue hasAlpsSafe "viewGame has Safe annotation"

          testCase "unsafe descriptor maps to Unsafe annotation"
          <| fun _ ->
              let alpsDoc = parseTicTacToe ()
              let statechart = toStatechartDocument alpsDoc

              let makeMove =
                  getTransitions statechart
                  |> List.find (fun t -> t.Event = Some "makeMove" && t.Source = "XTurn" && t.Target = Some "OTurn")

              let hasAlpsUnsafe =
                  makeMove.Annotations
                  |> List.exists (fun a ->
                      match a with
                      | AlpsAnnotation(AlpsTransitionType AlpsTransitionKind.Unsafe) -> true
                      | _ -> false)

              Expect.isTrue hasAlpsUnsafe "makeMove has Unsafe annotation"

          testCase "idempotent descriptor maps to Idempotent annotation"
          <| fun _ ->
              // Build a minimal ALPS doc with an idempotent descriptor
              let alpsDoc =
                  { Version = Some "1.0"
                    Documentation = None
                    Descriptors =
                      [ { Id = Some "StateA"
                          Type = DescriptorType.Semantic
                          Href = None
                          ReturnType = None
                          Documentation = None
                          Descriptors =
                            [ { Id = Some "updateThing"
                                Type = DescriptorType.Idempotent
                                Href = None
                                ReturnType = Some "#StateA"
                                Documentation = None
                                Descriptors = []
                                Extensions = []
                                Links = [] } ]
                          Extensions = []
                          Links = [] } ]
                    Links = []
                    Extensions = [] }

              let statechart = toStatechartDocument alpsDoc

              let updateThing =
                  getTransitions statechart
                  |> List.find (fun t -> t.Event = Some "updateThing")

              let hasAlpsIdempotent =
                  updateThing.Annotations
                  |> List.exists (fun a ->
                      match a with
                      | AlpsAnnotation(AlpsTransitionType AlpsTransitionKind.Idempotent) -> true
                      | _ -> false)

              Expect.isTrue hasAlpsIdempotent "updateThing has Idempotent annotation" ]

[<Tests>]
let roundtripMapperTests =
    testList
        "Alps.Mapper.Roundtrip"
        [ testCase "toStatechartDocument -> fromStatechartDocument preserves state ids"
          <| fun _ ->
              let original = parseTicTacToe ()
              let statechart = toStatechartDocument original
              let roundTripped = fromStatechartDocument statechart

              let roundTrippedIds =
                  roundTripped.Descriptors |> List.choose (fun d -> d.Id) |> Set.ofList

              // The original has state descriptors (XTurn, OTurn, Won, Draw, gameState) plus
              // data descriptors (position, player) and a shared transition (viewGame).
              // The mapper only preserves states, so we compare state ids.
              let stateIds = getStates statechart |> List.map (fun s -> s.Identifier) |> Set.ofList
              Expect.equal roundTrippedIds stateIds "state descriptor ids preserved through mapper roundtrip"

          testCase "toStatechartDocument -> fromStatechartDocument preserves transition events"
          <| fun _ ->
              let original = parseTicTacToe ()
              let statechart = toStatechartDocument original
              let roundTripped = fromStatechartDocument statechart

              // Collect all transition ids from the roundtripped doc
              let rec collectTransitionIds (descs: Descriptor list) =
                  descs
                  |> List.collect (fun d ->
                      let childIds = collectTransitionIds d.Descriptors

                      if d.Type <> DescriptorType.Semantic && d.Id.IsSome then
                          d.Id.Value :: childIds
                      else
                          childIds)

              let roundTrippedTransIds =
                  collectTransitionIds roundTripped.Descriptors |> Set.ofList

              let originalTransIds =
                  getTransitions statechart
                  |> List.choose (fun t -> t.Event)
                  |> Set.ofList

              Expect.equal roundTrippedTransIds originalTransIds "transition event names preserved"

          testCase "toStatechartDocument -> fromStatechartDocument preserves rt targets"
          <| fun _ ->
              let original = parseTicTacToe ()
              let statechart = toStatechartDocument original
              let roundTripped = fromStatechartDocument statechart

              // Collect all rt values from the roundtripped doc
              let rec collectRts (descs: Descriptor list) =
                  descs
                  |> List.collect (fun d ->
                      let childRts = collectRts d.Descriptors

                      match d.ReturnType with
                      | Some rt -> rt :: childRts
                      | None -> childRts)

              let roundTrippedRts = collectRts roundTripped.Descriptors |> Set.ofList

              let originalTargets =
                  getTransitions statechart
                  |> List.choose (fun t -> t.Target |> Option.map (fun tgt -> "#" + tgt))
                  |> Set.ofList

              Expect.equal roundTrippedRts originalTargets "rt targets preserved (with # prefix)"

          testCase "onboarding mapper roundtrip preserves state ids"
          <| fun _ ->
              let original = parseOnboarding ()
              let statechart = toStatechartDocument original
              let roundTripped = fromStatechartDocument statechart

              let roundTrippedIds =
                  roundTripped.Descriptors |> List.choose (fun d -> d.Id) |> Set.ofList

              let stateIds = getStates statechart |> List.map (fun s -> s.Identifier) |> Set.ofList
              Expect.equal roundTrippedIds stateIds "onboarding state ids preserved"

          testCase "roundtrip preserves guard labels"
          <| fun _ ->
              let original = parseTicTacToe ()
              let statechart = toStatechartDocument original
              let roundTripped = fromStatechartDocument statechart

              // Collect all guard ext values from the roundtripped doc
              let rec collectGuards (descs: Descriptor list) =
                  descs
                  |> List.collect (fun d ->
                      let childGuards = collectGuards d.Descriptors

                      let guards =
                          d.Extensions
                          |> List.choose (fun e ->
                              if e.Id = "guard" then e.Value else None)

                      guards @ childGuards)

              let roundTrippedGuards = collectGuards roundTripped.Descriptors |> Set.ofList

              let originalGuards =
                  getTransitions statechart
                  |> List.choose (fun t -> t.Guard)
                  |> Set.ofList

              Expect.equal roundTrippedGuards originalGuards "guard labels preserved"

          testCase "fromStatechartDocument sets version to 1.0"
          <| fun _ ->
              let original = parseTicTacToe ()
              let statechart = toStatechartDocument original
              let roundTripped = fromStatechartDocument statechart
              Expect.equal roundTripped.Version (Some "1.0") "version is 1.0"

          testCase "fromStatechartDocument preserves title as documentation"
          <| fun _ ->
              let original = parseTicTacToe ()
              let statechart = toStatechartDocument original
              let roundTripped = fromStatechartDocument statechart
              Expect.isSome roundTripped.Documentation "documentation present"

              Expect.equal
                  roundTripped.Documentation.Value.Value
                  "Tic-Tac-Toe game state machine"
                  "title preserved as documentation" ]

[<Tests>]
let edgeCaseTests =
    testList
        "Alps.Mapper.EdgeCases"
        [ testCase "empty ALPS document maps to empty statechart"
          <| fun _ ->
              let doc =
                  { Version = None
                    Documentation = None
                    Descriptors = []
                    Links = []
                    Extensions = [] }

              let statechart = toStatechartDocument doc
              Expect.isEmpty (getStates statechart) "no states"
              Expect.isEmpty (getTransitions statechart) "no transitions"
              Expect.isNone statechart.InitialStateId "no initial state"

          testCase "empty statechart maps to minimal ALPS document"
          <| fun _ ->
              let doc =
                  { Title = None
                    InitialStateId = None
                    Elements = []
                    DataEntries = []
                    Annotations = [] }

              let alps = fromStatechartDocument doc
              Expect.equal alps.Version (Some "1.0") "version set"
              Expect.isEmpty alps.Descriptors "no descriptors"
              Expect.isNone alps.Documentation "no documentation"

          testCase "descriptor with external URL in rt is preserved as target"
          <| fun _ ->
              let alpsDoc =
                  { Version = Some "1.0"
                    Documentation = None
                    Descriptors =
                      [ { Id = Some "StateA"
                          Type = DescriptorType.Semantic
                          Href = None
                          ReturnType = None
                          Documentation = None
                          Descriptors =
                            [ { Id = Some "goExternal"
                                Type = DescriptorType.Safe
                                Href = None
                                ReturnType = Some "http://example.com/other"
                                Documentation = None
                                Descriptors = []
                                Extensions = []
                                Links = [] } ]
                          Extensions = []
                          Links = [] } ]
                    Links = []
                    Extensions = [] }

              let statechart = toStatechartDocument alpsDoc

              let goExternal =
                  getTransitions statechart
                  |> List.find (fun t -> t.Event = Some "goExternal")

              // External URL is preserved as-is (no '#' to strip)
              Expect.equal goExternal.Target (Some "http://example.com/other") "external URL preserved"

          testCase "semantic descriptor with no transition children is still a state when referenced as rt target"
          <| fun _ ->
              let alpsDoc =
                  { Version = Some "1.0"
                    Documentation = None
                    Descriptors =
                      [ { Id = Some "Active"
                          Type = DescriptorType.Semantic
                          Href = None
                          ReturnType = None
                          Documentation = None
                          Descriptors =
                            [ { Id = Some "finish"
                                Type = DescriptorType.Unsafe
                                Href = None
                                ReturnType = Some "#Done"
                                Documentation = None
                                Descriptors = []
                                Extensions = []
                                Links = [] } ]
                          Extensions = []
                          Links = [] }
                        { Id = Some "Done"
                          Type = DescriptorType.Semantic
                          Href = None
                          ReturnType = None
                          Documentation = None
                          Descriptors = [] // no transitions
                          Extensions = []
                          Links = [] } ]
                    Links = []
                    Extensions = [] }

              let statechart = toStatechartDocument alpsDoc
              let stateIds = getStates statechart |> List.map (fun s -> s.Identifier) |> Set.ofList
              Expect.isTrue (Set.contains "Done" stateIds) "Done is a state (referenced as rt target)"

          testCase "missing workflow ordering leaves InitialStateId as None"
          <| fun _ ->
              let alpsDoc = parseTicTacToe ()
              let statechart = toStatechartDocument alpsDoc
              Expect.isNone statechart.InitialStateId "ALPS limitation: no initial state concept"

          testCase "fromStatechartDocument defaults to Unsafe when no ALPS annotation present"
          <| fun _ ->
              let doc =
                  { Title = None
                    InitialStateId = None
                    Elements =
                      [ StateDecl
                            { Identifier = "A"
                              Label = None
                              Kind = StateKind.Regular
                              Children = []
                              Activities = None
                              Position = None
                              Annotations = [] }
                        TransitionElement
                            { Source = "A"
                              Target = Some "A"
                              Event = Some "doSomething"
                              Guard = None
                              Action = None
                              Parameters = []
                              Position = None
                              Annotations = [] } ] // no ALPS annotation
                    DataEntries = []
                    Annotations = [] }

              let alps = fromStatechartDocument doc

              let transDesc =
                  alps.Descriptors
                  |> List.find (fun d -> d.Id = Some "A")
                  |> fun d -> d.Descriptors |> List.head

              Expect.equal transDesc.Type DescriptorType.Unsafe "defaults to Unsafe when no annotation"

          testCase "multiple ext elements with id=guard uses first one"
          <| fun _ ->
              let alpsDoc =
                  { Version = Some "1.0"
                    Documentation = None
                    Descriptors =
                      [ { Id = Some "StateA"
                          Type = DescriptorType.Semantic
                          Href = None
                          ReturnType = None
                          Documentation = None
                          Descriptors =
                            [ { Id = Some "action"
                                Type = DescriptorType.Unsafe
                                Href = None
                                ReturnType = Some "#StateA"
                                Documentation = None
                                Descriptors = []
                                Extensions =
                                  [ { Id = "guard"; Href = None; Value = Some "firstGuard" }
                                    { Id = "guard"; Href = None; Value = Some "secondGuard" } ]
                                Links = [] } ]
                          Extensions = []
                          Links = [] } ]
                    Links = []
                    Extensions = [] }

              let statechart = toStatechartDocument alpsDoc

              let action =
                  getTransitions statechart
                  |> List.find (fun t -> t.Event = Some "action")

              Expect.equal action.Guard (Some "firstGuard") "first guard ext wins"

          testCase "fromStatechartDocument re-adds # prefix to local rt targets"
          <| fun _ ->
              let doc =
                  { Title = None
                    InitialStateId = None
                    Elements =
                      [ StateDecl
                            { Identifier = "A"
                              Label = None
                              Kind = StateKind.Regular
                              Children = []
                              Activities = None
                              Position = None
                              Annotations = [] }
                        StateDecl
                            { Identifier = "B"
                              Label = None
                              Kind = StateKind.Regular
                              Children = []
                              Activities = None
                              Position = None
                              Annotations = [] }
                        TransitionElement
                            { Source = "A"
                              Target = Some "B"
                              Event = Some "go"
                              Guard = None
                              Action = None
                              Parameters = []
                              Position = None
                              Annotations =
                                [ AlpsAnnotation(AlpsTransitionType AlpsTransitionKind.Safe) ] } ]
                    DataEntries = []
                    Annotations = [] }

              let alps = fromStatechartDocument doc

              let stateA =
                  alps.Descriptors |> List.find (fun d -> d.Id = Some "A")

              let goDesc = stateA.Descriptors |> List.head
              Expect.equal goDesc.ReturnType (Some "#B") "rt has # prefix"
              Expect.equal goDesc.Type DescriptorType.Safe "safe type preserved" ]
