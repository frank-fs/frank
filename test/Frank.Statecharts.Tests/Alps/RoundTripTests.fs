module Frank.Statecharts.Tests.Alps.RoundTripTests

open Expecto
open Frank.Statecharts.Alps.Types
open Frank.Statecharts.Alps.JsonParser
open Frank.Statecharts.Alps.JsonGenerator
open Frank.Statecharts.Tests.Alps.GoldenFiles

/// Recursively collect all extensions from a descriptor tree.
let private collectAllExts (doc: AlpsDocument) =
    let rec collect (descriptors: Descriptor list) =
        descriptors
        |> List.collect (fun d -> d.Extensions @ collect d.Descriptors)

    doc.Extensions @ collect doc.Descriptors

[<Tests>]
let roundTripTests =
    testList
        "Alps.RoundTrip"
        [ testCase "tic-tac-toe JSON roundtrip preserves all information"
          <| fun _ ->
              let original =
                  parseAlpsJson ticTacToeAlpsJson
                  |> Result.defaultWith (fun _ -> failwith "parse failed")

              let generated = generateAlpsJson original

              let roundTripped =
                  parseAlpsJson generated
                  |> Result.defaultWith (fun _ -> failwith "re-parse failed")

              Expect.equal roundTripped original "roundtrip preserves all information"

          testCase "onboarding JSON roundtrip preserves all information"
          <| fun _ ->
              let original =
                  parseAlpsJson onboardingAlpsJson
                  |> Result.defaultWith (fun _ -> failwith "parse failed")

              let generated = generateAlpsJson original

              let roundTripped =
                  parseAlpsJson generated
                  |> Result.defaultWith (fun _ -> failwith "re-parse failed")

              Expect.equal roundTripped original "roundtrip preserves all information"

          testCase "roundtrip preserves descriptor ids and types"
          <| fun _ ->
              let original =
                  parseAlpsJson ticTacToeAlpsJson
                  |> Result.defaultWith (fun _ -> failwith "parse failed")

              let roundTripped =
                  parseAlpsJson (generateAlpsJson original)
                  |> Result.defaultWith (fun _ -> failwith "re-parse failed")

              let originalIds =
                  original.Descriptors |> List.choose (fun d -> d.Id) |> Set.ofList

              let roundTrippedIds =
                  roundTripped.Descriptors |> List.choose (fun d -> d.Id) |> Set.ofList

              Expect.equal roundTrippedIds originalIds "descriptor ids preserved"

          testCase "roundtrip preserves ext elements"
          <| fun _ ->
              let original =
                  parseAlpsJson ticTacToeAlpsJson
                  |> Result.defaultWith (fun _ -> failwith "parse failed")

              let roundTripped =
                  parseAlpsJson (generateAlpsJson original)
                  |> Result.defaultWith (fun _ -> failwith "re-parse failed")

              let originalExts = collectAllExts original
              let roundTrippedExts = collectAllExts roundTripped
              Expect.equal roundTrippedExts originalExts "ext elements preserved"

          testCase "roundtrip preserves links"
          <| fun _ ->
              let original =
                  parseAlpsJson ticTacToeAlpsJson
                  |> Result.defaultWith (fun _ -> failwith "parse failed")

              let roundTripped =
                  parseAlpsJson (generateAlpsJson original)
                  |> Result.defaultWith (fun _ -> failwith "re-parse failed")

              Expect.equal roundTripped.Links original.Links "links preserved"

          testCase "roundtrip preserves version"
          <| fun _ ->
              let original =
                  parseAlpsJson ticTacToeAlpsJson
                  |> Result.defaultWith (fun _ -> failwith "parse failed")

              let roundTripped =
                  parseAlpsJson (generateAlpsJson original)
                  |> Result.defaultWith (fun _ -> failwith "re-parse failed")

              Expect.equal roundTripped.Version original.Version "version preserved"

          testCase "roundtrip preserves documentation"
          <| fun _ ->
              let original =
                  parseAlpsJson ticTacToeAlpsJson
                  |> Result.defaultWith (fun _ -> failwith "parse failed")

              let roundTripped =
                  parseAlpsJson (generateAlpsJson original)
                  |> Result.defaultWith (fun _ -> failwith "re-parse failed")

              Expect.equal roundTripped.Documentation original.Documentation "documentation preserved"

          testCase "roundtrip preserves nested descriptor hierarchy"
          <| fun _ ->
              let original =
                  parseAlpsJson ticTacToeAlpsJson
                  |> Result.defaultWith (fun _ -> failwith "parse failed")

              let roundTripped =
                  parseAlpsJson (generateAlpsJson original)
                  |> Result.defaultWith (fun _ -> failwith "re-parse failed")

              // Check XTurn's nested descriptors specifically
              let originalXTurn =
                  original.Descriptors |> List.find (fun d -> d.Id = Some "XTurn")

              let roundTrippedXTurn =
                  roundTripped.Descriptors |> List.find (fun d -> d.Id = Some "XTurn")

              Expect.equal
                  roundTrippedXTurn.Descriptors.Length
                  originalXTurn.Descriptors.Length
                  "XTurn nested descriptor count preserved"

              Expect.equal roundTrippedXTurn.Descriptors originalXTurn.Descriptors "XTurn nested descriptors preserved"

          testCase "empty document roundtrips"
          <| fun _ ->
              let original =
                  { Version = None
                    Documentation = None
                    Descriptors = []
                    Links = []
                    Extensions = [] }

              let roundTripped =
                  parseAlpsJson (generateAlpsJson original)
                  |> Result.defaultWith (fun _ -> failwith "re-parse failed")

              Expect.equal roundTripped original "empty document roundtrips"

          testCase "document with only version roundtrips"
          <| fun _ ->
              let original =
                  { Version = Some "1.0"
                    Documentation = None
                    Descriptors = []
                    Links = []
                    Extensions = [] }

              let roundTripped =
                  parseAlpsJson (generateAlpsJson original)
                  |> Result.defaultWith (fun _ -> failwith "re-parse failed")

              Expect.equal roundTripped original "version-only document roundtrips" ]
