module Frank.Cli.Core.Tests.Unified.SpecCoExtractionTests

open System
open System.IO
open Expecto
open Frank.Resources.Model
open Frank.Cli.Core.Unified.UnifiedExtractor
open Frank.Statecharts.Smcat

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
let private makeResourceWithStates
    (slug: string)
    (stateNames: string list)
    (transitions: TransitionSpec list)
    : UnifiedResource =
    let route = $"/{slug}/{{id}}"

    { RouteTemplate = route
      ResourceSlug = slug
      TypeInfo = []
      Statechart =
        Some
            { RouteTemplate = route
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
                            """{"alps":{"version":"1.0","descriptor":[]}}"""
                        )

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

                        let resource = makeResourceWithStates "games" [ "XTurn"; "OTurn" ] []
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
                        let resource = makeResourceWithStates "games" [ "XTurn"; "OTurn" ] []
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

                        let resource = makeResourceWithStates "tasks" [ "Active"; "Completed" ] []
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
                              Constraint = Unrestricted
                              Safety = Unsafe }

                        let resource =
                            makeResourceWithStates "games" [ "XTurn"; "OTurn" ] [ existingTransition ]

                        let enriched, _warnings = enrichWithSpecTransitions dir [ resource ]

                        match enriched.[0].Statechart with
                        | Some sc ->
                            Expect.equal sc.Transitions.Length 1 "Should keep existing transition"
                            Expect.equal sc.Transitions.[0].Event "Existing" "Should keep existing event name"
                        | None -> failtest "Should have statechart"
                    finally
                        cleanup dir

                testCase "matches correct resource by state name overlap"
                <| fun _ ->
                    let dir = setupTempDir ()

                    try
                        let specsDir = Path.Combine(dir, "specs")
                        Directory.CreateDirectory(specsDir) |> ignore
                        File.WriteAllText(Path.Combine(specsDir, "game.smcat"), tictactoeSmcat)

                        let matching = makeResourceWithStates "games" [ "XTurn"; "OTurn" ] []
                        let nonMatching = makeResourceWithStates "tasks" [ "Active"; "Done" ] []

                        let enriched, _warnings = enrichWithSpecTransitions dir [ nonMatching; matching ]

                        Expect.equal enriched.[0].ResourceSlug "tasks" "Non-matching resource preserved"

                        match enriched.[0].Statechart with
                        | Some sc -> Expect.isEmpty sc.Transitions "Non-matching resource should have no transitions"
                        | None -> failtest "Should have statechart"

                        Expect.equal enriched.[1].ResourceSlug "games" "Matching resource preserved"

                        match enriched.[1].Statechart with
                        | Some sc ->
                            Expect.isGreaterThan
                                sc.Transitions.Length
                                0
                                "Matching resource should have transitions after enrichment"
                        | None -> failtest "Should have statechart"
                    finally
                        cleanup dir ]

          testList
              "applySpecTransitions (pure, in-memory)"
              [ testCase "applies transitions from matching document"
                <| fun _ ->
                    let smcat = "XTurn => OTurn: makeMove;\nOTurn => XTurn: makeMove;"

                    let doc = (Frank.Statecharts.Smcat.Parser.parseSmcat smcat).Document

                    let resource = makeResourceWithStates "games" [ "XTurn"; "OTurn" ] []
                    let result = applySpecTransitions [ doc ] [ resource ]

                    match result.[0].Statechart with
                    | Some sc ->
                        Expect.isGreaterThan sc.Transitions.Length 0 "Should have transitions"

                        let sources = sc.Transitions |> List.map _.Source |> Set.ofList
                        Expect.isTrue (sources.Contains "XTurn") "Should have XTurn source"
                        Expect.isTrue (sources.Contains "OTurn") "Should have OTurn source"
                    | None -> failtest "Should have statechart"

                testCase "does not overwrite existing transitions"
                <| fun _ ->
                    let smcat = "XTurn => OTurn: makeMove;"

                    let doc = (Frank.Statecharts.Smcat.Parser.parseSmcat smcat).Document

                    let existing =
                        { Event = "Existing"
                          Source = "XTurn"
                          Target = "OTurn"
                          Guard = None
                          Constraint = Unrestricted
                          Safety = Unsafe }

                    let resource = makeResourceWithStates "games" [ "XTurn"; "OTurn" ] [ existing ]
                    let result = applySpecTransitions [ doc ] [ resource ]

                    match result.[0].Statechart with
                    | Some sc ->
                        Expect.equal sc.Transitions.Length 1 "Should keep existing"
                        Expect.equal sc.Transitions.[0].Event "Existing" "Should keep existing event"
                    | None -> failtest "Should have statechart"

                testCase "no match when states don't overlap"
                <| fun _ ->
                    let smcat = "XTurn => OTurn: makeMove;"

                    let doc = (Frank.Statecharts.Smcat.Parser.parseSmcat smcat).Document

                    let resource = makeResourceWithStates "tasks" [ "Active"; "Done" ] []
                    let result = applySpecTransitions [ doc ] [ resource ]

                    match result.[0].Statechart with
                    | Some sc -> Expect.isEmpty sc.Transitions "No overlap means no transitions"
                    | None -> failtest "Should have statechart"

                testCase "empty docs list returns resources unchanged"
                <| fun _ ->
                    let resource = makeResourceWithStates "games" [ "XTurn"; "OTurn" ] []
                    let result = applySpecTransitions [] [ resource ]

                    match result.[0].Statechart with
                    | Some sc -> Expect.isEmpty sc.Transitions "Empty docs = no transitions"
                    | None -> failtest "Should have statechart"

                testCase "matches correct resource among multiple"
                <| fun _ ->
                    let smcat = "XTurn => OTurn: makeMove;\nOTurn => XTurn: makeMove;"

                    let doc = (Frank.Statecharts.Smcat.Parser.parseSmcat smcat).Document

                    let matching = makeResourceWithStates "games" [ "XTurn"; "OTurn" ] []
                    let nonMatching = makeResourceWithStates "tasks" [ "Active"; "Done" ] []
                    let result = applySpecTransitions [ doc ] [ nonMatching; matching ]

                    match result.[0].Statechart with
                    | Some sc -> Expect.isEmpty sc.Transitions "Non-matching unchanged"
                    | None -> failtest "Should have statechart"

                    match result.[1].Statechart with
                    | Some sc -> Expect.isGreaterThan sc.Transitions.Length 0 "Matching gets transitions"
                    | None -> failtest "Should have statechart" ] ]
