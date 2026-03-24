module Frank.Cli.Core.Tests.Unified.SpecCoExtractionTests

open System
open System.IO
open Expecto
open Frank.Resources.Model
open Frank.Cli.Core.Unified.UnifiedExtractor

// ══════════════════════════════════════════════════════════════════════════════
// Helpers
// ══════════════════════════════════════════════════════════════════════════════

let private setupTempDir () =
    let dir = Path.Combine(Path.GetTempPath(), $"frank-spec-test-{Guid.NewGuid():N}")
    Directory.CreateDirectory(dir) |> ignore
    dir

let private cleanup (dir: string) =
    try
        Directory.Delete(dir, true)
    with _ ->
        ()

/// A minimal smcat file with XTurn/OTurn states.
let private tictactoeSmcat =
    """
XTurn => OTurn: makeMove;
OTurn => XTurn: makeMove;
"""

/// A resource with a statechart whose state names overlap with the smcat file.
let private makeResourceWithStates (stateNames: string list) (transitions: TransitionSpec list) : UnifiedResource =
    { RouteTemplate = "/games/{gameId}"
      ResourceSlug = "games"
      TypeInfo = []
      Statechart =
        Some
            { RouteTemplate = "/games/{gameId}"
              StateNames = stateNames
              InitialStateKey = stateNames |> List.tryHead |> Option.defaultValue "Unknown"
              GuardNames = []
              StateMetadata = Map.empty
              Roles = []
              Transitions = transitions }
      HttpCapabilities = []
      DerivedFields = ResourceModel.emptyDerivedFields }

// ══════════════════════════════════════════════════════════════════════════════
// Tests
// ══════════════════════════════════════════════════════════════════════════════

[<Tests>]
let specCoExtractionTests =
    testList
        "Spec co-extraction"
        [ testList
              "findSpecFiles"
              [ testCase "finds spec files in specs/ directory"
                <| fun _ ->
                    let dir = setupTempDir ()

                    try
                        let specsDir = Path.Combine(dir, "specs")
                        Directory.CreateDirectory(specsDir) |> ignore
                        File.WriteAllText(Path.Combine(specsDir, "test.smcat"), tictactoeSmcat)
                        File.WriteAllText(Path.Combine(specsDir, "test.wsd"), "@startuml\n@enduml")
                        File.WriteAllText(Path.Combine(specsDir, "readme.txt"), "not a spec")

                        let files = findSpecFiles dir
                        Expect.equal files.Length 2 "Should find 2 spec files (not txt)"

                        let extensions = files |> List.map Path.GetExtension
                        Expect.contains extensions ".smcat" "Should include .smcat"
                        Expect.contains extensions ".wsd" "Should include .wsd"
                    finally
                        cleanup dir

                testCase "returns empty when no specs/ directory"
                <| fun _ ->
                    let dir = setupTempDir ()

                    try
                        let files = findSpecFiles dir
                        Expect.isEmpty files "Should return empty list"
                    finally
                        cleanup dir

                testCase "returns empty when specs/ is empty"
                <| fun _ ->
                    let dir = setupTempDir ()

                    try
                        Directory.CreateDirectory(Path.Combine(dir, "specs")) |> ignore
                        let files = findSpecFiles dir
                        Expect.isEmpty files "Should return empty list for empty specs dir"
                    finally
                        cleanup dir

                testCase "finds alps.json files"
                <| fun _ ->
                    let dir = setupTempDir ()

                    try
                        let specsDir = Path.Combine(dir, "specs")
                        Directory.CreateDirectory(specsDir) |> ignore

                        File.WriteAllText(
                            Path.Combine(specsDir, "game.alps.json"),
                            """{"alps":{"version":"1.0","descriptor":[]}}""")

                        let files = findSpecFiles dir
                        Expect.equal files.Length 1 "Should find alps.json file"
                    finally
                        cleanup dir ]

          testList
              "tryParseSpecFile"
              [ testCase "parses valid smcat file"
                <| fun _ ->
                    let dir = setupTempDir ()

                    try
                        let filePath = Path.Combine(dir, "test.smcat")
                        File.WriteAllText(filePath, tictactoeSmcat)
                        let result = tryParseSpecFile filePath
                        Expect.isOk result "Should parse smcat file"
                    finally
                        cleanup dir

                testCase "returns Error for unknown extension"
                <| fun _ ->
                    let dir = setupTempDir ()

                    try
                        let filePath = Path.Combine(dir, "test.txt")
                        File.WriteAllText(filePath, "not a spec")
                        let result = tryParseSpecFile filePath
                        Expect.isError result "Should return Error for .txt"
                    finally
                        cleanup dir

                testCase "returns Error for nonexistent file"
                <| fun _ ->
                    let result = tryParseSpecFile "/nonexistent/path/file.smcat"
                    Expect.isError result "Should return Error for nonexistent file" ]

          testList
              "documentStateNames"
              [ testCase "extracts state names from parsed smcat document"
                <| fun _ ->
                    let dir = setupTempDir ()

                    try
                        let filePath = Path.Combine(dir, "test.smcat")
                        File.WriteAllText(filePath, tictactoeSmcat)

                        match tryParseSpecFile filePath with
                        | Ok doc ->
                            let names = documentStateNames doc
                            Expect.isTrue (names.Contains "XTurn") "Should contain XTurn"
                            Expect.isTrue (names.Contains "OTurn") "Should contain OTurn"
                        | Error msg -> failtest $"Should parse the smcat file: {msg}"
                    finally
                        cleanup dir ]

          testList
              "enrichWithSpecTransitions"
              [ testCase "populates transitions from matching spec file"
                <| fun _ ->
                    let dir = setupTempDir ()

                    try
                        let specsDir = Path.Combine(dir, "specs")
                        Directory.CreateDirectory(specsDir) |> ignore
                        File.WriteAllText(Path.Combine(specsDir, "game.smcat"), tictactoeSmcat)

                        let resource = makeResourceWithStates [ "XTurn"; "OTurn" ] []
                        let enriched, _warnings = enrichWithSpecTransitions dir [ resource ]

                        match enriched.[0].Statechart with
                        | Some sc ->
                            Expect.isGreaterThan sc.Transitions.Length 0 "Should have transitions after enrichment"

                            let sources = sc.Transitions |> List.map _.Source |> Set.ofList
                            Expect.isTrue (sources.Contains "XTurn") "Should have transition from XTurn"
                            Expect.isTrue (sources.Contains "OTurn") "Should have transition from OTurn"
                        | None -> failtest "Should have statechart"
                    finally
                        cleanup dir

                testCase "no crash when project has no specs directory"
                <| fun _ ->
                    let dir = setupTempDir ()

                    try
                        let resource = makeResourceWithStates [ "XTurn"; "OTurn" ] []
                        let enriched, _warnings = enrichWithSpecTransitions dir [ resource ]

                        match enriched.[0].Statechart with
                        | Some sc -> Expect.isEmpty sc.Transitions "Transitions should remain empty"
                        | None -> failtest "Should have statechart"
                    finally
                        cleanup dir

                testCase "no match when spec state names don't overlap"
                <| fun _ ->
                    let dir = setupTempDir ()

                    try
                        let specsDir = Path.Combine(dir, "specs")
                        Directory.CreateDirectory(specsDir) |> ignore
                        File.WriteAllText(Path.Combine(specsDir, "game.smcat"), tictactoeSmcat)

                        let resource = makeResourceWithStates [ "Active"; "Completed" ] []
                        let enriched, _warnings = enrichWithSpecTransitions dir [ resource ]

                        match enriched.[0].Statechart with
                        | Some sc -> Expect.isEmpty sc.Transitions "Transitions should remain empty when no overlap"
                        | None -> failtest "Should have statechart"
                    finally
                        cleanup dir

                testCase "does not overwrite existing transitions"
                <| fun _ ->
                    let dir = setupTempDir ()

                    try
                        let specsDir = Path.Combine(dir, "specs")
                        Directory.CreateDirectory(specsDir) |> ignore
                        File.WriteAllText(Path.Combine(specsDir, "game.smcat"), tictactoeSmcat)

                        let existingTransition =
                            { Event = "Existing"
                              Source = "XTurn"
                              Target = "OTurn"
                              Guard = None
                              Constraint = Unrestricted }

                        let resource = makeResourceWithStates [ "XTurn"; "OTurn" ] [ existingTransition ]
                        let enriched, _warnings = enrichWithSpecTransitions dir [ resource ]

                        match enriched.[0].Statechart with
                        | Some sc ->
                            Expect.equal sc.Transitions.Length 1 "Should keep existing transition"
                            Expect.equal sc.Transitions.[0].Event "Existing" "Should keep existing event name"
                        | None -> failtest "Should have statechart"
                    finally
                        cleanup dir

                testCase "matchDocToResource matches by state name overlap"
                <| fun _ ->
                    let dir = setupTempDir ()

                    try
                        let filePath = Path.Combine(dir, "test.smcat")
                        File.WriteAllText(filePath, tictactoeSmcat)

                        match tryParseSpecFile filePath with
                        | Ok doc ->
                            let matching = makeResourceWithStates [ "XTurn"; "OTurn" ] []
                            let nonMatching = makeResourceWithStates [ "Active"; "Done" ] []

                            let result = matchDocToResource doc [ nonMatching; matching ]
                            Expect.isSome result "Should find matching resource"
                            Expect.equal result.Value.ResourceSlug "games" "Should match the games resource"
                        | Error msg -> failtest $"Should parse smcat: {msg}"
                    finally
                        cleanup dir ] ]
