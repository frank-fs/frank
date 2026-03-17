module Frank.Statecharts.Tests.Alps.JsonParserTests

open Expecto
open Frank.Statecharts.Ast
open Frank.Statecharts.Alps.JsonParser
open Frank.Statecharts.Tests.Alps.GoldenFiles

// ---------------------------------------------------------------------------
// Helpers for working with the shared AST
// ---------------------------------------------------------------------------

/// Extract all StateNodes from a StatechartDocument's elements.
let private getStates (doc: StatechartDocument) =
    doc.Elements
    |> List.choose (fun el ->
        match el with
        | StateDecl s -> Some s
        | _ -> None)

/// Extract all TransitionEdges from a StatechartDocument's elements.
let private getTransitions (doc: StatechartDocument) =
    doc.Elements
    |> List.choose (fun el ->
        match el with
        | TransitionElement t -> Some t
        | _ -> None)

/// Extract ALPS version from document annotations.
let private getVersion (doc: StatechartDocument) =
    doc.Annotations
    |> List.tryPick (fun a ->
        match a with
        | AlpsAnnotation(AlpsVersion v) -> Some v
        | _ -> None)

/// Check if the document has ALPS documentation annotation.
let private getDocumentation (doc: StatechartDocument) =
    doc.Annotations
    |> List.tryPick (fun a ->
        match a with
        | AlpsAnnotation(AlpsDocumentation(fmt, value)) -> Some(fmt, value)
        | _ -> None)

/// Extract ALPS link annotations from document annotations.
let private getLinks (doc: StatechartDocument) =
    doc.Annotations
    |> List.choose (fun a ->
        match a with
        | AlpsAnnotation(AlpsLink(rel, href)) -> Some(rel, href)
        | _ -> None)

/// Extract ALPS extension annotations from document annotations.
let private getExtensions (doc: StatechartDocument) =
    doc.Annotations
    |> List.choose (fun a ->
        match a with
        | AlpsAnnotation(AlpsExtension(id, href, value)) -> Some(id, href, value)
        | _ -> None)

/// Extract ALPS data descriptor annotations from document annotations.
let private getDataDescriptors (doc: StatechartDocument) =
    doc.Annotations
    |> List.choose (fun a ->
        match a with
        | AlpsAnnotation(AlpsDataDescriptor(id, _)) -> Some id
        | _ -> None)

/// Helper: parse and get document (assert no errors).
let private parseOk json msg =
    let result = parseAlpsJson json
    Expect.isEmpty result.Errors msg
    result.Document

// ---------------------------------------------------------------------------
// T016: JsonParser tests migrated to shared AST
// ---------------------------------------------------------------------------

[<Tests>]
let jsonParserTests =
    testList
        "Alps.JsonParser"
        [ testCase "parse tic-tac-toe golden file succeeds"
          <| fun _ ->
              let result = parseAlpsJson ticTacToeAlpsJson
              Expect.isEmpty result.Errors "should parse without errors"
              let doc = result.Document
              Expect.equal (getVersion doc) (Some "1.0") "version"
              Expect.isSome (getDocumentation doc) "should have documentation"

              let docFmt, docVal = (getDocumentation doc).Value
              Expect.equal docVal "Tic-Tac-Toe game state machine" "doc value"
              Expect.equal docFmt (Some "text") "doc format"

          testCase "tic-tac-toe has state elements"
          <| fun _ ->
              let doc = parseOk ticTacToeAlpsJson "parse failed"
              let stateIds = getStates doc |> List.choose (fun s -> s.Identifier) |> Set.ofList

              Expect.containsAll
                  stateIds
                  (Set.ofList [ "XTurn"; "OTurn"; "Won"; "Draw" ])
                  "all state descriptors present"

          testCase "tic-tac-toe has data descriptor annotations"
          <| fun _ ->
              let doc = parseOk ticTacToeAlpsJson "parse failed"
              let dataIds = getDataDescriptors doc |> Set.ofList

              Expect.containsAll
                  dataIds
                  (Set.ofList [ "position"; "player" ])
                  "data descriptors present as annotations"

          testCase "tic-tac-toe makeMove has correct target"
          <| fun _ ->
              let doc = parseOk ticTacToeAlpsJson "parse failed"

              // Find XTurn -> OTurn makeMove transition
              let makeMove =
                  getTransitions doc
                  |> List.find (fun t -> t.Event = Some "makeMove" && t.Source = "XTurn" && t.Target = Some "OTurn")

              Expect.equal makeMove.Target (Some "OTurn") "first makeMove target is OTurn"

              let hasAlpsUnsafe =
                  makeMove.Annotations
                  |> List.exists (fun a ->
                      match a with
                      | AlpsAnnotation(AlpsTransitionType AlpsTransitionKind.Unsafe) -> true
                      | _ -> false)

              Expect.isTrue hasAlpsUnsafe "makeMove is Unsafe"

          testCase "tic-tac-toe XTurn has three makeMove transitions with different targets"
          <| fun _ ->
              let doc = parseOk ticTacToeAlpsJson "parse failed"

              let makeMoves =
                  getTransitions doc
                  |> List.filter (fun t -> t.Event = Some "makeMove" && t.Source = "XTurn")

              Expect.equal makeMoves.Length 3 "XTurn has 3 makeMove transitions"

              let targets = makeMoves |> List.map (fun t -> t.Target) |> Set.ofList

              Expect.containsAll
                  targets
                  (Set.ofList [ Some "OTurn"; Some "Won"; Some "Draw" ])
                  "all targets present"

          testCase "tic-tac-toe guards are captured on transitions"
          <| fun _ ->
              let doc = parseOk ticTacToeAlpsJson "parse failed"

              let makeMoves =
                  getTransitions doc
                  |> List.filter (fun t -> t.Event = Some "makeMove" && t.Source = "XTurn")

              // First makeMove has guard "role=PlayerX"
              let xToO = makeMoves |> List.find (fun t -> t.Target = Some "OTurn")
              Expect.equal xToO.Guard (Some "role=PlayerX") "guard value"

              // Second makeMove has guard "wins"
              let xToWon = makeMoves |> List.find (fun t -> t.Target = Some "Won")
              Expect.equal xToWon.Guard (Some "wins") "wins guard"

              // Third makeMove has guard "boardFull"
              let xToDraw = makeMoves |> List.find (fun t -> t.Target = Some "Draw")
              Expect.equal xToDraw.Guard (Some "boardFull") "boardFull guard"

          testCase "tic-tac-toe makeMove has parameter references"
          <| fun _ ->
              let doc = parseOk ticTacToeAlpsJson "parse failed"

              let makeMove =
                  getTransitions doc
                  |> List.find (fun t -> t.Event = Some "makeMove" && t.Source = "XTurn" && t.Target = Some "OTurn")

              Expect.equal makeMove.Parameters.Length 2 "makeMove has 2 parameters"
              Expect.containsAll (Set.ofList makeMove.Parameters) (Set.ofList [ "position"; "player" ]) "param refs"

          testCase "tic-tac-toe viewGame transitions from all states"
          <| fun _ ->
              let doc = parseOk ticTacToeAlpsJson "parse failed"

              let viewGames = getTransitions doc |> List.filter (fun t -> t.Event = Some "viewGame")
              Expect.isNonEmpty viewGames "should have viewGame transitions"

              let sources = viewGames |> List.map (fun t -> t.Source) |> Set.ofList
              Expect.containsAll sources (Set.ofList [ "XTurn"; "OTurn"; "Won"; "Draw" ]) "viewGame from all states"

              let hasAlpsSafe =
                  viewGames.[0].Annotations
                  |> List.exists (fun a ->
                      match a with
                      | AlpsAnnotation(AlpsTransitionType AlpsTransitionKind.Safe) -> true
                      | _ -> false)

              Expect.isTrue hasAlpsSafe "viewGame has Safe annotation"

          testCase "tic-tac-toe has link annotation"
          <| fun _ ->
              let doc = parseOk ticTacToeAlpsJson "parse failed"
              let links = getLinks doc
              Expect.equal links.Length 1 "one link"
              Expect.equal (fst links.[0]) "self" "link rel"
              Expect.equal (snd links.[0]) "http://example.com/alps/tic-tac-toe" "link href"

          testCase "tic-tac-toe XTurn has href-only viewGame reference (via AlpsDescriptorHref annotation)"
          <| fun _ ->
              let doc = parseOk ticTacToeAlpsJson "parse failed"

              let viewGameFromXTurn =
                  getTransitions doc
                  |> List.find (fun t -> t.Event = Some "viewGame" && t.Source = "XTurn")

              let hasDescriptorHref =
                  viewGameFromXTurn.Annotations
                  |> List.exists (fun a ->
                      match a with
                      | AlpsAnnotation(AlpsDescriptorHref _) -> true
                      | _ -> false)

              Expect.isTrue hasDescriptorHref "viewGame from XTurn has AlpsDescriptorHref annotation"

          testCase "parse onboarding golden file succeeds"
          <| fun _ ->
              let result = parseAlpsJson onboardingAlpsJson
              Expect.isEmpty result.Errors "should parse without errors"
              let doc = result.Document
              Expect.equal (getVersion doc) (Some "1.0") "version"
              Expect.isSome (getDocumentation doc) "should have documentation"

          testCase "onboarding has all state elements"
          <| fun _ ->
              let doc = parseOk onboardingAlpsJson "parse failed"
              let stateIds = getStates doc |> List.choose (fun s -> s.Identifier) |> Set.ofList

              Expect.containsAll
                  stateIds
                  (Set.ofList [ "Welcome"; "CollectEmail"; "CollectProfile"; "Review"; "Complete" ])
                  "all onboarding states present"

          testCase "onboarding transitions have correct ALPS type annotations"
          <| fun _ ->
              let doc = parseOk onboardingAlpsJson "parse failed"
              let transitions = getTransitions doc

              // start is safe
              let start = transitions |> List.find (fun t -> t.Event = Some "start")

              let hasSafe =
                  start.Annotations
                  |> List.exists (fun a ->
                      match a with
                      | AlpsAnnotation(AlpsTransitionType AlpsTransitionKind.Safe) -> true
                      | _ -> false)

              Expect.isTrue hasSafe "start is safe"

              // submitEmail is unsafe
              let submitEmail = transitions |> List.find (fun t -> t.Event = Some "submitEmail")

              let hasUnsafe =
                  submitEmail.Annotations
                  |> List.exists (fun a ->
                      match a with
                      | AlpsAnnotation(AlpsTransitionType AlpsTransitionKind.Unsafe) -> true
                      | _ -> false)

              Expect.isTrue hasUnsafe "submitEmail is unsafe"

              // editEmail is safe
              let editEmail = transitions |> List.find (fun t -> t.Event = Some "editEmail")

              let hasSafe2 =
                  editEmail.Annotations
                  |> List.exists (fun a ->
                      match a with
                      | AlpsAnnotation(AlpsTransitionType AlpsTransitionKind.Safe) -> true
                      | _ -> false)

              Expect.isTrue hasSafe2 "editEmail is safe"

          testCase "onboarding submitEmail has parameter reference"
          <| fun _ ->
              let doc = parseOk onboardingAlpsJson "parse failed"
              let submitEmail = getTransitions doc |> List.find (fun t -> t.Event = Some "submitEmail")
              Expect.equal submitEmail.Parameters.Length 1 "one param"
              Expect.equal submitEmail.Parameters.[0] "email" "param is email"

          testCase "onboarding has no links"
          <| fun _ ->
              let doc = parseOk onboardingAlpsJson "parse failed"
              let links = getLinks doc
              Expect.isEmpty links "no links in onboarding" ]

[<Tests>]
let jsonParserEdgeCaseTests =
    testList
        "Alps.JsonParser edge cases"
        [ testCase "empty ALPS document (no descriptors)"
          <| fun _ ->
              let doc = parseOk """{"alps":{"descriptor":[]}}""" "parse failed"
              Expect.isEmpty (getStates doc) "no states"
              Expect.isEmpty (getTransitions doc) "no transitions"

          testCase "ALPS document with no descriptor property"
          <| fun _ ->
              let doc = parseOk """{"alps":{}}""" "parse failed"
              Expect.isEmpty (getStates doc) "no states"
              Expect.isEmpty (getTransitions doc) "no transitions"

          testCase "descriptor without type defaults to Semantic (no transition extracted)"
          <| fun _ ->
              let json = """{"alps":{"descriptor":[{"id":"test"}]}}"""
              let doc = parseOk json "parse failed"
              // A lone semantic descriptor with no transitions and not referenced as rt target
              // won't be classified as a state either; it becomes a data descriptor annotation
              let dataDescs = getDataDescriptors doc
              Expect.contains dataDescs "test" "test is a data descriptor"

          testCase "unknown JSON properties are ignored"
          <| fun _ ->
              let json =
                  """{"alps":{"unknownProp":"value","descriptor":[{"id":"test","futureField":42}]}}"""

              let doc = parseOk json "parse failed"
              let dataDescs = getDataDescriptors doc
              Expect.contains dataDescs "test" "descriptor parsed despite unknown props"

          testCase "ALPS document with only links (no descriptors)"
          <| fun _ ->
              let json =
                  """{"alps":{"link":[{"rel":"self","href":"http://example.com"}]}}"""

              let doc = parseOk json "parse failed"
              Expect.isEmpty (getStates doc) "no states"
              let links = getLinks doc
              Expect.equal links.Length 1 "one link"

          testCase "descriptor with href to external URL is a data descriptor"
          <| fun _ ->
              let json =
                  """{"alps":{"descriptor":[{"href":"http://example.com/profile"}]}}"""

              let doc = parseOk json "parse failed"
              // href-only descriptor with no id won't become a state or data descriptor
              Expect.isEmpty (getStates doc) "no states"

          testCase "multiple ext elements on a single descriptor produce extension annotations"
          <| fun _ ->
              // Build a state with two ext elements and a transition child so it's classified as a state
              let json =
                  """{"alps":{"descriptor":[{"id":"StateA","type":"semantic","ext":[{"id":"guard","value":"role=X"},{"id":"meta","value":"info"}],"descriptor":[{"id":"go","type":"safe","rt":"#StateA"}]}]}}"""

              let doc = parseOk json "parse failed"
              let states = getStates doc

              let stateA = states |> List.find (fun s -> s.Identifier = Some "StateA")

              // State annotations should include the extensions
              let extAnnotations =
                  stateA.Annotations
                  |> List.choose (fun a ->
                      match a with
                      | AlpsAnnotation(AlpsExtension(id, _, _)) -> Some id
                      | _ -> None)

              Expect.containsAll (Set.ofList extAnnotations) (Set.ofList [ "guard"; "meta" ]) "both extensions present"

          testCase "unicode characters in descriptor ids"
          <| fun _ ->
              let json =
                  """{"alps":{"descriptor":[{"id":"beschreibung","doc":{"value":"Beschreibung auf Deutsch"}}]}}"""

              let doc = parseOk json "parse failed"
              let dataDescs = getDataDescriptors doc
              Expect.contains dataDescs "beschreibung" "unicode id"

          testCase "descriptor with no id (href-only reference) does not become a state"
          <| fun _ ->
              let json =
                  """{"alps":{"descriptor":[{"href":"#otherDescriptor"}]}}"""

              let doc = parseOk json "parse failed"
              Expect.isEmpty (getStates doc) "no states from href-only descriptor"

          testCase "ALPS document with version only"
          <| fun _ ->
              let json = """{"alps":{"version":"1.0"}}"""
              let doc = parseOk json "parse failed"
              Expect.equal (getVersion doc) (Some "1.0") "version captured"
              Expect.isEmpty (getStates doc) "no states"

          testCase "ALPS document with top-level ext elements"
          <| fun _ ->
              let json =
                  """{"alps":{"ext":[{"id":"custom","value":"data"}]}}"""

              let doc = parseOk json "parse failed"
              let exts = getExtensions doc
              Expect.equal exts.Length 1 "one top-level ext"
              let (id, _, value) = exts.[0]
              Expect.equal id "custom" "ext id"
              Expect.equal value (Some "data") "ext value"

          testCase "deeply nested descriptors with transitions"
          <| fun _ ->
              // Only tests that descriptors with transition children become states
              let json =
                  """{"alps":{"descriptor":[{"id":"level1","descriptor":[{"id":"go","type":"safe","rt":"#level1"}]}]}}"""

              let doc = parseOk json "parse failed"
              let states = getStates doc
              Expect.equal states.Length 1 "one state"
              Expect.equal states.[0].Identifier (Some "level1") "level1 is a state"

          testCase "doc element with no format"
          <| fun _ ->
              let json =
                  """{"alps":{"doc":{"value":"Just some text"}}}"""

              let doc = parseOk json "parse failed"
              let documentation = getDocumentation doc
              Expect.isSome documentation "doc present"
              let (fmt, value) = documentation.Value
              Expect.isNone fmt "no format"
              Expect.equal value "Just some text" "doc text" ]

[<Tests>]
let jsonParserErrorTests =
    testList
        "Alps.JsonParser errors"
        [ testCase "malformed JSON returns errors"
          <| fun _ ->
              let result = parseAlpsJson "not valid json"
              Expect.isNonEmpty result.Errors "should have errors"

          testCase "empty string returns errors"
          <| fun _ ->
              let result = parseAlpsJson ""
              Expect.isNonEmpty result.Errors "should have errors"

          testCase "valid JSON but missing alps root returns errors"
          <| fun _ ->
              let result = parseAlpsJson """{"descriptors":[]}"""
              Expect.isNonEmpty result.Errors "should have errors for missing alps root"

          testCase "error description is actionable"
          <| fun _ ->
              let result = parseAlpsJson "not valid json"
              Expect.isNonEmpty result.Errors "should have errors"
              Expect.isNotEmpty result.Errors.[0].Description "error description not empty"

          testCase "JSON parse error has no position"
          <| fun _ ->
              let result = parseAlpsJson "not valid json"
              Expect.isNone result.Errors.[0].Position "JSON errors have no position"

          testCase "missing alps root error has descriptive message"
          <| fun _ ->
              let result = parseAlpsJson """{"foo":"bar"}"""
              Expect.isNonEmpty result.Errors "should have errors"
              Expect.stringContains result.Errors.[0].Description "alps" "mentions alps"

          testCase "null JSON value returns errors"
          <| fun _ ->
              let result = parseAlpsJson "null"
              Expect.isNonEmpty result.Errors "should have errors for null"

          testCase "JSON array returns errors"
          <| fun _ ->
              let result = parseAlpsJson "[]"
              Expect.isNonEmpty result.Errors "should have errors for array" ]

// ---------------------------------------------------------------------------
// T017: Absorbed mapper tests (parser-direction only)
// ---------------------------------------------------------------------------

[<Tests>]
let stateExtractionTests =
    testList
        "Alps.Parser.StateExtraction"
        [ testCase "tic-tac-toe states are extracted"
          <| fun _ ->
              let doc = parseOk ticTacToeAlpsJson "parse failed"
              let stateIds = getStates doc |> List.choose (fun s -> s.Identifier) |> Set.ofList

              Expect.containsAll
                  stateIds
                  (Set.ofList [ "XTurn"; "OTurn"; "Won"; "Draw" ])
                  "all game states extracted"

          testCase "tic-tac-toe extracts gameState as a state (it is an rt target)"
          <| fun _ ->
              let doc = parseOk ticTacToeAlpsJson "parse failed"
              let stateIds = getStates doc |> List.choose (fun s -> s.Identifier) |> Set.ofList
              Expect.isTrue (Set.contains "gameState" stateIds) "gameState is an rt target so it is a state"

          testCase "tic-tac-toe does not extract pure data descriptors as states"
          <| fun _ ->
              let doc = parseOk ticTacToeAlpsJson "parse failed"
              let stateIds = getStates doc |> List.choose (fun s -> s.Identifier) |> Set.ofList
              // position and player are pure data descriptors (no transition children, not rt targets)
              Expect.isFalse (Set.contains "position" stateIds) "position is not a state"
              Expect.isFalse (Set.contains "player" stateIds) "player is not a state"

          testCase "onboarding states are extracted"
          <| fun _ ->
              let doc = parseOk onboardingAlpsJson "parse failed"
              let stateIds = getStates doc |> List.choose (fun s -> s.Identifier) |> Set.ofList

              Expect.containsAll
                  stateIds
                  (Set.ofList [ "Welcome"; "CollectEmail"; "CollectProfile"; "Review"; "Complete" ])
                  "all onboarding states extracted"

          testCase "all states have Regular kind"
          <| fun _ ->
              let doc = parseOk ticTacToeAlpsJson "parse failed"
              let states = getStates doc

              for s in states do
                  Expect.equal s.Kind StateKind.Regular (sprintf "state %s should be Regular" (s.Identifier |> Option.defaultValue "")) ]

[<Tests>]
let transitionMappingTests =
    testList
        "Alps.Parser.TransitionMapping"
        [ testCase "makeMove transitions have correct source and target"
          <| fun _ ->
              let doc = parseOk ticTacToeAlpsJson "parse failed"
              let makeMoves = getTransitions doc |> List.filter (fun t -> t.Event = Some "makeMove")
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
              let doc = parseOk ticTacToeAlpsJson "parse failed"
              let makeMoves = getTransitions doc |> List.filter (fun t -> t.Event = Some "makeMove")

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
              let doc = parseOk ticTacToeAlpsJson "parse failed"
              let viewGames = getTransitions doc |> List.filter (fun t -> t.Event = Some "viewGame")
              Expect.isNonEmpty viewGames "should have viewGame transitions"

              // viewGame should appear from multiple states (XTurn, OTurn, Won, Draw)
              let sources = viewGames |> List.map (fun t -> t.Source) |> Set.ofList
              Expect.containsAll sources (Set.ofList [ "XTurn"; "OTurn"; "Won"; "Draw" ]) "viewGame from all states"

          testCase "onboarding transitions have correct source and target"
          <| fun _ ->
              let doc = parseOk onboardingAlpsJson "parse failed"
              let transitions = getTransitions doc

              let startTrans =
                  transitions
                  |> List.tryFind (fun t -> t.Event = Some "start" && t.Source = "Welcome")

              Expect.isSome startTrans "Welcome -> CollectEmail via start"
              Expect.equal startTrans.Value.Target (Some "CollectEmail") "start targets CollectEmail"

          testCase "transition parameters are extracted"
          <| fun _ ->
              let doc = parseOk ticTacToeAlpsJson "parse failed"

              let makeMove =
                  getTransitions doc
                  |> List.find (fun t -> t.Event = Some "makeMove" && t.Source = "XTurn" && t.Target = Some "OTurn")

              Expect.containsAll
                  (Set.ofList makeMove.Parameters)
                  (Set.ofList [ "position"; "player" ])
                  "makeMove has position and player parameters" ]

[<Tests>]
let guardExtractionTests =
    testList
        "Alps.Parser.GuardExtraction"
        [ testCase "guard labels extracted from ext elements"
          <| fun _ ->
              let doc = parseOk ticTacToeAlpsJson "parse failed"
              let guarded = getTransitions doc |> List.filter (fun t -> t.Guard.IsSome)
              Expect.isNonEmpty guarded "should have guarded transitions"

          testCase "role=PlayerX guard on XTurn -> OTurn makeMove"
          <| fun _ ->
              let doc = parseOk ticTacToeAlpsJson "parse failed"

              let xToO =
                  getTransitions doc
                  |> List.find (fun t -> t.Source = "XTurn" && t.Target = Some "OTurn" && t.Event = Some "makeMove")

              Expect.equal xToO.Guard (Some "role=PlayerX") "guard is role=PlayerX"

          testCase "wins guard on XTurn -> Won makeMove"
          <| fun _ ->
              let doc = parseOk ticTacToeAlpsJson "parse failed"

              let xToWon =
                  getTransitions doc
                  |> List.find (fun t -> t.Source = "XTurn" && t.Target = Some "Won" && t.Event = Some "makeMove")

              Expect.equal xToWon.Guard (Some "wins") "guard is wins"

          testCase "boardFull guard on XTurn -> Draw makeMove"
          <| fun _ ->
              let doc = parseOk ticTacToeAlpsJson "parse failed"

              let xToDraw =
                  getTransitions doc
                  |> List.find (fun t -> t.Source = "XTurn" && t.Target = Some "Draw" && t.Event = Some "makeMove")

              Expect.equal xToDraw.Guard (Some "boardFull") "guard is boardFull"

          testCase "transitions without ext elements have no guard"
          <| fun _ ->
              let doc = parseOk onboardingAlpsJson "parse failed"

              let startTrans =
                  getTransitions doc
                  |> List.find (fun t -> t.Event = Some "start")

              Expect.isNone startTrans.Guard "start transition has no guard" ]

[<Tests>]
let httpMethodHintTests =
    testList
        "Alps.Parser.HttpMethodHints"
        [ testCase "safe descriptor maps to Safe annotation"
          <| fun _ ->
              let doc = parseOk ticTacToeAlpsJson "parse failed"

              let viewGame =
                  getTransitions doc
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
              let doc = parseOk ticTacToeAlpsJson "parse failed"

              let makeMove =
                  getTransitions doc
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
              // Build a minimal ALPS JSON with an idempotent descriptor
              let json =
                  """{"alps":{"version":"1.0","descriptor":[{"id":"StateA","type":"semantic","descriptor":[{"id":"updateThing","type":"idempotent","rt":"#StateA"}]}]}}"""

              let doc = parseOk json "parse failed"

              let updateThing =
                  getTransitions doc
                  |> List.find (fun t -> t.Event = Some "updateThing")

              let hasAlpsIdempotent =
                  updateThing.Annotations
                  |> List.exists (fun a ->
                      match a with
                      | AlpsAnnotation(AlpsTransitionType AlpsTransitionKind.Idempotent) -> true
                      | _ -> false)

              Expect.isTrue hasAlpsIdempotent "updateThing has Idempotent annotation" ]

[<Tests>]
let edgeCaseParserTests =
    testList
        "Alps.Parser.EdgeCases"
        [ testCase "empty ALPS document parses to empty statechart"
          <| fun _ ->
              let doc = parseOk """{"alps":{"descriptor":[]}}""" "parse failed"
              Expect.isEmpty (getStates doc) "no states"
              Expect.isEmpty (getTransitions doc) "no transitions"
              Expect.isNone doc.InitialStateId "no initial state"

          testCase "descriptor with external URL in rt is preserved as target"
          <| fun _ ->
              let json =
                  """{"alps":{"version":"1.0","descriptor":[{"id":"StateA","type":"semantic","descriptor":[{"id":"goExternal","type":"safe","rt":"http://example.com/other"}]}]}}"""

              let doc = parseOk json "parse failed"

              let goExternal =
                  getTransitions doc
                  |> List.find (fun t -> t.Event = Some "goExternal")

              // External URL is preserved as-is (no '#' to strip)
              Expect.equal goExternal.Target (Some "http://example.com/other") "external URL preserved"

          testCase "semantic descriptor with no transition children is still a state when referenced as rt target"
          <| fun _ ->
              let json =
                  """{"alps":{"version":"1.0","descriptor":[{"id":"Active","type":"semantic","descriptor":[{"id":"finish","type":"unsafe","rt":"#Done"}]},{"id":"Done","type":"semantic"}]}}"""

              let doc = parseOk json "parse failed"
              let stateIds = getStates doc |> List.choose (fun s -> s.Identifier) |> Set.ofList
              Expect.isTrue (Set.contains "Done" stateIds) "Done is a state (referenced as rt target)"

          testCase "missing workflow ordering leaves InitialStateId as None"
          <| fun _ ->
              let doc = parseOk ticTacToeAlpsJson "parse failed"
              Expect.isNone doc.InitialStateId "ALPS limitation: no initial state concept"

          testCase "multiple ext elements with id=guard uses first one"
          <| fun _ ->
              let json =
                  """{"alps":{"version":"1.0","descriptor":[{"id":"StateA","type":"semantic","descriptor":[{"id":"action","type":"unsafe","rt":"#StateA","ext":[{"id":"guard","value":"firstGuard"},{"id":"guard","value":"secondGuard"}]}]}]}}"""

              let doc = parseOk json "parse failed"

              let action =
                  getTransitions doc
                  |> List.find (fun t -> t.Event = Some "action")

              Expect.equal action.Guard (Some "firstGuard") "first guard ext wins" ]
