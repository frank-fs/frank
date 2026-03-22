module Frank.Statecharts.Tests.Alps.RoundTripTests

open Expecto
open Frank.Statecharts.Ast
open Frank.Statecharts.Alps.JsonParser
open Frank.Statecharts.Alps.JsonGenerator
open Frank.Statecharts.Tests.Alps.GoldenFiles

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

/// Extract ALPS link annotations from document annotations.
let private getLinks (doc: StatechartDocument) =
    doc.Annotations
    |> List.choose (fun a ->
        match a with
        | AlpsAnnotation(AlpsLink(rel, href)) -> Some(rel, href)
        | _ -> None)

/// Extract ALPS documentation from document annotations.
let private getDocumentation (doc: StatechartDocument) =
    doc.Annotations
    |> List.tryPick (fun a ->
        match a with
        | AlpsAnnotation(AlpsDocumentation(fmt, value)) -> Some(fmt, value)
        | _ -> None)

/// Extract ALPS extension annotations from all annotations (document + state + transition).
let private collectAllExtAnnotations (doc: StatechartDocument) =
    let docExts =
        doc.Annotations
        |> List.choose (fun a ->
            match a with
            | AlpsAnnotation(AlpsExtension(id, href, value)) -> Some(id, href, value)
            | _ -> None)

    let stateExts =
        getStates doc
        |> List.collect (fun s ->
            s.Annotations
            |> List.choose (fun a ->
                match a with
                | AlpsAnnotation(AlpsExtension(id, href, value)) -> Some(id, href, value)
                | _ -> None))

    let transExts =
        getTransitions doc
        |> List.collect (fun t ->
            t.Annotations
            |> List.choose (fun a ->
                match a with
                | AlpsAnnotation(AlpsExtension(id, href, value)) -> Some(id, href, value)
                | _ -> None))

    docExts @ stateExts @ transExts

/// Helper: parse and get document (assert no errors).
let private parseOk json msg =
    let result = parseAlpsJson json
    Expect.isEmpty result.Errors msg
    result.Document

[<Tests>]
let roundTripTests =
    testList
        "Alps.RoundTrip"
        [ testCase "tic-tac-toe JSON roundtrip preserves all information"
          <| fun _ ->
              let original = parseOk ticTacToeAlpsJson "parse failed"
              let generated = generateAlpsJson original
              let roundTripped = parseOk generated "re-parse failed"
              Expect.equal roundTripped original "roundtrip preserves all information"

          testCase "onboarding JSON roundtrip preserves all information"
          <| fun _ ->
              let original = parseOk onboardingAlpsJson "parse failed"
              let generated = generateAlpsJson original
              let roundTripped = parseOk generated "re-parse failed"
              Expect.equal roundTripped original "roundtrip preserves all information"

          testCase "roundtrip preserves state identifiers"
          <| fun _ ->
              let original = parseOk ticTacToeAlpsJson "parse failed"
              let roundTripped = parseOk (generateAlpsJson original) "re-parse failed"

              let originalIds =
                  getStates original |> List.choose (fun s -> s.Identifier) |> Set.ofList

              let roundTrippedIds =
                  getStates roundTripped |> List.choose (fun s -> s.Identifier) |> Set.ofList

              Expect.equal roundTrippedIds originalIds "state identifiers preserved"

          testCase "roundtrip preserves ext annotations"
          <| fun _ ->
              let original = parseOk ticTacToeAlpsJson "parse failed"
              let roundTripped = parseOk (generateAlpsJson original) "re-parse failed"

              let originalExts = collectAllExtAnnotations original
              let roundTrippedExts = collectAllExtAnnotations roundTripped
              Expect.equal roundTrippedExts originalExts "ext annotations preserved"

          testCase "roundtrip preserves links"
          <| fun _ ->
              let original = parseOk ticTacToeAlpsJson "parse failed"
              let roundTripped = parseOk (generateAlpsJson original) "re-parse failed"
              Expect.equal (getLinks roundTripped) (getLinks original) "links preserved"

          testCase "roundtrip preserves version"
          <| fun _ ->
              let original = parseOk ticTacToeAlpsJson "parse failed"
              let roundTripped = parseOk (generateAlpsJson original) "re-parse failed"
              Expect.equal (getVersion roundTripped) (getVersion original) "version preserved"

          testCase "roundtrip preserves documentation"
          <| fun _ ->
              let original = parseOk ticTacToeAlpsJson "parse failed"
              let roundTripped = parseOk (generateAlpsJson original) "re-parse failed"
              Expect.equal (getDocumentation roundTripped) (getDocumentation original) "documentation preserved"

          testCase "roundtrip preserves nested transition structure"
          <| fun _ ->
              let original = parseOk ticTacToeAlpsJson "parse failed"
              let roundTripped = parseOk (generateAlpsJson original) "re-parse failed"

              // Check XTurn's transitions specifically
              let originalXTurnTransitions =
                  getTransitions original |> List.filter (fun t -> t.Source = "XTurn")

              let roundTrippedXTurnTransitions =
                  getTransitions roundTripped |> List.filter (fun t -> t.Source = "XTurn")

              Expect.equal
                  roundTrippedXTurnTransitions.Length
                  originalXTurnTransitions.Length
                  "XTurn transition count preserved"

              Expect.equal roundTrippedXTurnTransitions originalXTurnTransitions "XTurn transitions preserved"

          testCase "empty document roundtrips"
          <| fun _ ->
              let original =
                  { Title = None
                    InitialStateId = None
                    Elements = []
                    DataEntries = []
                    Annotations = [] }

              let roundTripped = parseOk (generateAlpsJson original) "re-parse failed"
              // Generator adds default version "1.0", so the roundtripped doc will have it
              let roundTrippedWithoutVersion =
                  { roundTripped with
                      Annotations =
                          roundTripped.Annotations
                          |> List.filter (fun a ->
                              match a with
                              | AlpsAnnotation(AlpsVersion _) -> false
                              | _ -> true) }

              Expect.equal roundTrippedWithoutVersion original "empty document roundtrips (ignoring default version)"

          testCase "document with only version roundtrips"
          <| fun _ ->
              let original =
                  { Title = None
                    InitialStateId = None
                    Elements = []
                    DataEntries = []
                    Annotations = [ AlpsAnnotation(AlpsVersion "1.0") ] }

              let roundTripped = parseOk (generateAlpsJson original) "re-parse failed"
              Expect.equal roundTripped original "version-only document roundtrips"

          // ---------------------------------------------------------------
          // Absorbed from MapperTests.Roundtrip
          // ---------------------------------------------------------------

          testCase "roundtrip preserves transition events"
          <| fun _ ->
              let original = parseOk ticTacToeAlpsJson "parse failed"
              let roundTripped = parseOk (generateAlpsJson original) "re-parse failed"

              let originalEvents =
                  getTransitions original |> List.choose (fun t -> t.Event) |> Set.ofList

              let roundTrippedEvents =
                  getTransitions roundTripped |> List.choose (fun t -> t.Event) |> Set.ofList

              Expect.equal roundTrippedEvents originalEvents "transition events preserved"

          testCase "roundtrip preserves rt targets"
          <| fun _ ->
              let original = parseOk ticTacToeAlpsJson "parse failed"
              let roundTripped = parseOk (generateAlpsJson original) "re-parse failed"

              let originalTargets =
                  getTransitions original |> List.choose (fun t -> t.Target) |> Set.ofList

              let roundTrippedTargets =
                  getTransitions roundTripped |> List.choose (fun t -> t.Target) |> Set.ofList

              Expect.equal roundTrippedTargets originalTargets "rt targets preserved"

          testCase "onboarding roundtrip preserves state ids"
          <| fun _ ->
              let original = parseOk onboardingAlpsJson "parse failed"
              let roundTripped = parseOk (generateAlpsJson original) "re-parse failed"

              let originalIds =
                  getStates original |> List.choose (fun s -> s.Identifier) |> Set.ofList

              let roundTrippedIds =
                  getStates roundTripped |> List.choose (fun s -> s.Identifier) |> Set.ofList

              Expect.equal roundTrippedIds originalIds "onboarding state identifiers preserved"

          testCase "roundtrip preserves guard labels"
          <| fun _ ->
              let original = parseOk ticTacToeAlpsJson "parse failed"
              let roundTripped = parseOk (generateAlpsJson original) "re-parse failed"

              let originalGuards =
                  getTransitions original |> List.choose (fun t -> t.Guard) |> Set.ofList

              let roundTrippedGuards =
                  getTransitions roundTripped |> List.choose (fun t -> t.Guard) |> Set.ofList

              Expect.equal roundTrippedGuards originalGuards "guard labels preserved"

          testCase "roundtrip sets version to 1.0"
          <| fun _ ->
              let original =
                  { Title = None
                    InitialStateId = None
                    Elements =
                      [ StateDecl
                            { Identifier = Some "A"
                              Label = None
                              Kind = StateKind.Regular
                              Children = []
                              Activities = None
                              Position = None
                              Annotations = [] } ]
                    DataEntries = []
                    Annotations = [] }

              let roundTripped = parseOk (generateAlpsJson original) "re-parse failed"
              Expect.equal (getVersion roundTripped) (Some "1.0") "generator sets version to 1.0"

          testCase "roundtrip preserves title as documentation"
          <| fun _ ->
              let original = parseOk ticTacToeAlpsJson "parse failed"
              let roundTripped = parseOk (generateAlpsJson original) "re-parse failed"

              let originalTitle = original.Title
              let roundTrippedTitle = roundTripped.Title
              Expect.equal roundTrippedTitle originalTitle "title preserved"

              let originalDocAnnotation = getDocumentation original
              let roundTrippedDocAnnotation = getDocumentation roundTripped
              Expect.equal roundTrippedDocAnnotation originalDocAnnotation "documentation annotation preserved" ]

// ---------------------------------------------------------------------------
// SC-008: Cross-format validator compatibility test
// ---------------------------------------------------------------------------

[<Tests>]
let crossFormatCompatibilityTests =
    testList
        "Alps.CrossFormatValidatorCompatibility"
        [ testCase "ALPS parser output is accepted by cross-format validator"
          <| fun _ ->
              let result = parseAlpsJson ticTacToeAlpsJson
              Expect.isEmpty result.Errors "parse failed"

              let artifact: Frank.Statecharts.Validation.FormatArtifact =
                  { Format = Frank.Statecharts.Validation.Alps
                    Document = result.Document }

              // Verify that a self-consistency validation run succeeds without exceptions
              let report =
                  Frank.Statecharts.Validation.Validator.validate
                      Frank.Statecharts.Validation.SelfConsistencyRules.rules
                      [ artifact ]

              Expect.isGreaterThan report.TotalChecks 0 "self-consistency rules should run against ALPS artifact"
              Expect.equal report.TotalFailures 0 "ALPS tic-tac-toe should pass self-consistency"

          testCase "ALPS parser output works in cross-format comparison"
          <| fun _ ->
              let result = parseAlpsJson ticTacToeAlpsJson
              Expect.isEmpty result.Errors "parse failed"

              let alpsArtifact: Frank.Statecharts.Validation.FormatArtifact =
                  { Format = Frank.Statecharts.Validation.Alps
                    Document = result.Document }

              // Build a matching SCXML-tagged artifact from the same doc
              let scxmlArtifact: Frank.Statecharts.Validation.FormatArtifact =
                  { Format = Frank.Statecharts.Validation.Scxml
                    Document = result.Document }

              let report =
                  Frank.Statecharts.Validation.Validator.validate
                      Frank.Statecharts.Validation.CrossFormatRules.rules
                      [ alpsArtifact; scxmlArtifact ]

              Expect.isGreaterThan report.TotalChecks 0 "cross-format rules should run"
              Expect.equal report.TotalFailures 0 "identical documents should have zero cross-format failures" ]

// ---------------------------------------------------------------------------
// Amundsen onboarding fixture round-trip tests (WP02)
// ---------------------------------------------------------------------------

[<Tests>]
let amundsenRoundTripTests =
    testList
        "Alps.RoundTrip.Amundsen"
        [ testCase "Amundsen onboarding JSON roundtrip produces structurally equal ASTs"
          <| fun _ ->
              let result1 = parseAlpsJson amundsenOnboardingAlpsJson
              Expect.isEmpty result1.Errors "initial parse should succeed"
              let generated = generateAlpsJson result1.Document
              let result2 = parseAlpsJson generated
              Expect.isEmpty result2.Errors "re-parse of generated JSON should succeed"
              Expect.equal result1.Document result2.Document "ASTs structurally equal after round-trip"

          testCase "Amundsen onboarding roundtrip preserves states (home, wip, customerData)"
          <| fun _ ->
              let original = parseOk amundsenOnboardingAlpsJson "initial parse failed"
              let roundTripped = parseOk (generateAlpsJson original) "re-parse failed"

              let originalStateIds =
                  getStates original |> List.choose (fun s -> s.Identifier) |> Set.ofList

              let roundTrippedStateIds =
                  getStates roundTripped |> List.choose (fun s -> s.Identifier) |> Set.ofList

              Expect.containsAll
                  originalStateIds
                  (Set.ofList [ "home"; "wip"; "customerData" ])
                  "original has all three Amundsen states"

              Expect.equal roundTrippedStateIds originalStateIds "roundtrip preserves state identifiers"

          testCase "Amundsen onboarding roundtrip preserves data descriptors (identifier, name, email)"
          <| fun _ ->
              let original = parseOk amundsenOnboardingAlpsJson "initial parse failed"
              let roundTripped = parseOk (generateAlpsJson original) "re-parse failed"

              let getDataDescriptorIds (doc: StatechartDocument) =
                  doc.Annotations
                  |> List.choose (fun a ->
                      match a with
                      | AlpsAnnotation(AlpsDataDescriptor(id, _)) -> Some id
                      | _ -> None)
                  |> Set.ofList

              let originalDataIds = getDataDescriptorIds original
              let roundTrippedDataIds = getDataDescriptorIds roundTripped

              Expect.containsAll
                  originalDataIds
                  (Set.ofList [ "identifier"; "name"; "email" ])
                  "original has all three data descriptors"

              Expect.equal roundTrippedDataIds originalDataIds "roundtrip preserves data descriptor ids"

          testCase "Amundsen onboarding roundtrip preserves transitions (startOnboarding, collectCustomerData, completeOnboarding)"
          <| fun _ ->
              let original = parseOk amundsenOnboardingAlpsJson "initial parse failed"
              let roundTripped = parseOk (generateAlpsJson original) "re-parse failed"

              let originalEvents =
                  getTransitions original |> List.choose (fun t -> t.Event) |> Set.ofList

              let roundTrippedEvents =
                  getTransitions roundTripped |> List.choose (fun t -> t.Event) |> Set.ofList

              Expect.containsAll
                  originalEvents
                  (Set.ofList [ "startOnboarding"; "collectCustomerData"; "completeOnboarding" ])
                  "original has all three transitions"

              Expect.equal roundTrippedEvents originalEvents "roundtrip preserves transition events"

          testCase "Amundsen onboarding roundtrip preserves root-level link annotation"
          <| fun _ ->
              let original = parseOk amundsenOnboardingAlpsJson "initial parse failed"
              let roundTripped = parseOk (generateAlpsJson original) "re-parse failed"

              let originalLinks = getLinks original
              let roundTrippedLinks = getLinks roundTripped

              Expect.equal originalLinks.Length 1 "original has one link"
              Expect.equal (fst originalLinks.[0]) "self" "link rel is self"
              Expect.equal roundTrippedLinks originalLinks "roundtrip preserves root-level links"

          testCase "Amundsen onboarding roundtrip preserves root-level ext annotation"
          <| fun _ ->
              let original = parseOk amundsenOnboardingAlpsJson "initial parse failed"
              let roundTripped = parseOk (generateAlpsJson original) "re-parse failed"

              let getDocExts (doc: StatechartDocument) =
                  doc.Annotations
                  |> List.choose (fun a ->
                      match a with
                      | AlpsAnnotation(AlpsExtension(id, href, value)) -> Some(id, href, value)
                      | _ -> None)

              let originalExts = getDocExts original
              let roundTrippedExts = getDocExts roundTripped

              Expect.equal originalExts.Length 1 "original has one root ext"
              let (extId, _, extVal) = originalExts.[0]
              Expect.equal extId "author" "ext id is author"
              Expect.equal extVal (Some "amundsen") "ext value is amundsen"
              Expect.equal roundTrippedExts originalExts "roundtrip preserves root-level ext annotations"

          testCase "Amundsen onboarding roundtrip preserves guard extension on collectCustomerData"
          <| fun _ ->
              let original = parseOk amundsenOnboardingAlpsJson "initial parse failed"
              let roundTripped = parseOk (generateAlpsJson original) "re-parse failed"

              let findCollectCustomerData (doc: StatechartDocument) =
                  getTransitions doc
                  |> List.find (fun t -> t.Event = Some "collectCustomerData")

              let originalTransition = findCollectCustomerData original
              let roundTrippedTransition = findCollectCustomerData roundTripped

              Expect.equal originalTransition.Guard (Some "emailValid") "original guard is emailValid"
              Expect.equal roundTrippedTransition.Guard originalTransition.Guard "roundtrip preserves guard"

          testCase "Amundsen onboarding roundtrip preserves transition parameters"
          <| fun _ ->
              let original = parseOk amundsenOnboardingAlpsJson "initial parse failed"
              let roundTripped = parseOk (generateAlpsJson original) "re-parse failed"

              let collectParams doc =
                  getTransitions doc
                  |> List.find (fun t -> t.Event = Some "collectCustomerData")
                  |> fun t -> t.Parameters

              let originalParams = collectParams original
              let roundTrippedParams = collectParams roundTripped

              Expect.containsAll
                  (Set.ofList originalParams)
                  (Set.ofList [ "name"; "email" ])
                  "collectCustomerData has name and email parameters"

              Expect.equal (Set.ofList roundTrippedParams) (Set.ofList originalParams) "roundtrip preserves parameters"

          testCase "Amundsen onboarding roundtrip preserves documentation at all levels"
          <| fun _ ->
              let original = parseOk amundsenOnboardingAlpsJson "initial parse failed"
              let roundTripped = parseOk (generateAlpsJson original) "re-parse failed"

              // Document-level doc
              Expect.equal (getDocumentation roundTripped) (getDocumentation original) "document-level doc preserved"

              // State-level doc: 'home' state should have doc annotation
              let findStateDoc (doc: StatechartDocument) (stateId: string) =
                  getStates doc
                  |> List.find (fun s -> s.Identifier = Some stateId)
                  |> fun s ->
                      s.Annotations
                      |> List.tryPick (fun a ->
                          match a with
                          | AlpsAnnotation(AlpsDocumentation(fmt, v)) -> Some(fmt, v)
                          | _ -> None)

              let originalHomeDoc = findStateDoc original "home"
              let roundTrippedHomeDoc = findStateDoc roundTripped "home"

              Expect.isSome originalHomeDoc "home state has documentation"
              Expect.equal roundTrippedHomeDoc originalHomeDoc "state-level doc preserved"

              // Transition-level doc: startOnboarding should have doc annotation
              let findTransDoc (doc: StatechartDocument) (event: string) =
                  getTransitions doc
                  |> List.find (fun t -> t.Event = Some event)
                  |> fun t ->
                      t.Annotations
                      |> List.tryPick (fun a ->
                          match a with
                          | AlpsAnnotation(AlpsDocumentation(fmt, v)) -> Some(fmt, v)
                          | _ -> None)

              let originalStartDoc = findTransDoc original "startOnboarding"
              let roundTrippedStartDoc = findTransDoc roundTripped "startOnboarding"

              Expect.isSome originalStartDoc "startOnboarding transition has documentation"
              Expect.equal roundTrippedStartDoc originalStartDoc "transition-level doc preserved"

          testCase "minimal document (version only) roundtrips without data loss"
          <| fun _ ->
              let minimal = """{"alps":{"version":"1.0"}}"""
              let result1 = parseAlpsJson minimal
              Expect.isEmpty result1.Errors "parse of minimal document should succeed"
              let generated = generateAlpsJson result1.Document
              let result2 = parseAlpsJson generated
              Expect.isEmpty result2.Errors "re-parse of generated minimal document should succeed"
              Expect.equal result1.Document result2.Document "minimal document ASTs structurally equal"

          testCase "idempotent transition type roundtrips"
          <| fun _ ->
              let json = """{"alps":{"version":"1.0","descriptor":[{"id":"resource","type":"semantic","descriptor":[{"id":"update","type":"idempotent","rt":"#resource"}]}]}}"""
              let result1 = parseAlpsJson json
              Expect.isEmpty result1.Errors "parse succeeds"
              let generated = generateAlpsJson result1.Document
              let result2 = parseAlpsJson generated
              Expect.isEmpty result2.Errors "re-parse succeeds"
              Expect.equal result1.Document result2.Document "idempotent transition roundtrips"

          testCase "role extensions golden file roundtrips"
          <| fun _ ->
              let original = parseOk roleExtensionsAlpsJson "parse failed"
              let generated = generateAlpsJson original
              let roundTripped = parseOk generated "re-parse failed"
              Expect.equal roundTripped original "role extensions round-trip preserves AST" ]
