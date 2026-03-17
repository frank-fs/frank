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
                  getStates original |> List.map (fun s -> s.Identifier) |> Set.ofList

              let roundTrippedIds =
                  getStates roundTripped |> List.map (fun s -> s.Identifier) |> Set.ofList

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
              Expect.equal roundTripped original "version-only document roundtrips" ]
