module Frank.Cli.Core.Tests.Unified.UnifiedExtractorTests

open Expecto
open FSharp.Compiler.CodeAnalysis
open FSharp.Compiler.Text
open Frank.Statecharts
open Frank.Resources.Model
open Frank.Cli.Core.Analysis
open Frank.Cli.Core.Unified
open Frank.Cli.Core.Unified.UnifiedExtractor

let private checker = FSharpChecker.Create()

let private parseSource (source: string) =
    async {
        let sourceText = SourceText.ofString source
        let parsingOptions = { FSharpParsingOptions.Default with SourceFiles = [| "test.fs" |] }
        let! parseResult = checker.ParseFile("test.fs", sourceText, parsingOptions)
        return parseResult.ParseTree
    }

[<Tests>]
let unifiedExtractorTests =
    testList
        "UnifiedExtractor"
        [ testList
              "syntax walker"
              [ testCaseAsync "finds plain resource CE"
                <| async {
                    let source =
                        """
module Test
let home = resource "/" { get (fun ctx -> task { return () }) }
"""

                    let! ast = parseSource source
                    let findings = findResourcesInParsedInput ast

                    Expect.equal findings.Length 1 "Should find 1 resource"

                    match findings.[0] with
                    | FoundPlainResource ar ->
                        Expect.equal ar.RouteTemplate "/" "Route should be /"
                        Expect.contains ar.HttpMethods Get "Should have GET method"
                    | _ -> failtest "Expected FoundPlainResource"
                }

                testCaseAsync "finds statefulResource CE"
                <| async {
                    let source =
                        """
module Test
let game = statefulResource "/games/{gameId}" {
    machine gameMachine
    inState (forState XTurn [ get handler; post handler ])
    inState (forState OTurn [ get handler; post handler ])
}
"""

                    let! ast = parseSource source
                    let findings = findResourcesInParsedInput ast

                    Expect.equal findings.Length 1 "Should find 1 resource"

                    match findings.[0] with
                    | FoundStatefulResource(route, machineName, stateHandlers, _roleNames) ->
                        Expect.equal route "/games/{gameId}" "Route should match"
                        Expect.equal machineName (Some "gameMachine") "Machine name should be gameMachine"
                        Expect.equal stateHandlers.Length 2 "Should have 2 state handlers"

                        let (case1, methods1) = stateHandlers.[0]
                        Expect.equal case1 "XTurn" "First case should be XTurn"
                        Expect.contains methods1 "GET" "XTurn should have GET"
                        Expect.contains methods1 "POST" "XTurn should have POST"
                    | _ -> failtest "Expected FoundStatefulResource"
                }

                testCaseAsync "finds both resource and statefulResource in same module"
                <| async {
                    let source =
                        """
module Test
let health = resource "/health" { get (fun ctx -> task { return () }) }
let game = statefulResource "/games/{gameId}" {
    machine gameMachine
    inState (forState XTurn [ get handler ])
}
"""

                    let! ast = parseSource source
                    let findings = findResourcesInParsedInput ast

                    Expect.equal findings.Length 2 "Should find 2 resources"

                    let hasPlain =
                        findings
                        |> List.exists (fun f ->
                            match f with
                            | FoundPlainResource ar -> ar.RouteTemplate = "/health"
                            | _ -> false)

                    let hasStateful =
                        findings
                        |> List.exists (fun f ->
                            match f with
                            | FoundStatefulResource(route, _, _, _) -> route = "/games/{gameId}"
                            | _ -> false)

                    Expect.isTrue hasPlain "Should find plain resource"
                    Expect.isTrue hasStateful "Should find stateful resource"
                }

                testCaseAsync "multi-method plain resource"
                <| async {
                    let source =
                        """
module Test
let items = resource "/items" {
    name "Items"
    get (fun ctx -> task { return () })
    post (fun ctx -> task { return () })
    delete (fun ctx -> task { return () })
}
"""

                    let! ast = parseSource source
                    let findings = findResourcesInParsedInput ast

                    Expect.equal findings.Length 1 "Should find 1 resource"

                    match findings.[0] with
                    | FoundPlainResource ar ->
                        Expect.equal ar.RouteTemplate "/items" "Route should be /items"
                        Expect.equal ar.Name (Some "Items") "Name should be Items"
                        Expect.equal ar.HttpMethods.Length 3 "Should have 3 methods"
                    | _ -> failtest "Expected FoundPlainResource"
                }

                testCaseAsync "resource with linkedData"
                <| async {
                    let source =
                        """
module Test
let home = resource "/" {
    get (fun ctx -> task { return () })
    linkedData
}
"""

                    let! ast = parseSource source
                    let findings = findResourcesInParsedInput ast

                    match findings.[0] with
                    | FoundPlainResource ar -> Expect.isTrue ar.HasLinkedData "Should have linkedData"
                    | _ -> failtest "Expected FoundPlainResource"
                }

                testCaseAsync "statefulResource with no machine binding"
                <| async {
                    let source =
                        """
module Test
let game = statefulResource "/games/{gameId}" {
    inState (forState Playing [ get handler; post handler ])
    inState (forState Finished [ get handler ])
}
"""

                    let! ast = parseSource source
                    let findings = findResourcesInParsedInput ast

                    match findings.[0] with
                    | FoundStatefulResource(_, machineName, stateHandlers, _) ->
                        Expect.isNone machineName "Machine name should be None"
                        Expect.equal stateHandlers.Length 2 "Should have 2 state handlers"
                    | _ -> failtest "Expected FoundStatefulResource"
                } ]

          testList
              "computeDerivedFields"
              [ testCase "returns empty for plain resource"
                <| fun _ ->
                    let resource =
                        { RouteTemplate = "/health"
                          ResourceSlug = "health"
                          TypeInfo = []
                          Statechart = None
                          HttpCapabilities =
                            [ { Method = "GET"
                                StateKey = None
                                LinkRelation = "self"
                                IsSafe = true } ]
                          DerivedFields = ResourceModel.emptyDerivedFields }

                    let result = computeDerivedFields resource []
                    Expect.equal result.OrphanStates [] "No orphan states for plain resource"
                    Expect.equal result.TypeCoverage 1.0 "Full coverage for plain resource"

                testCase "identifies orphan states"
                <| fun _ ->
                    let statechart: Frank.Resources.Model.ExtractedStatechart =
                        { RouteTemplate = "/games/{gameId}"
                          StateNames = [ "XTurn"; "OTurn"; "Won"; "Draw" ]
                          InitialStateKey = "XTurn"
                          GuardNames = []
                          StateMetadata =
                            [ "XTurn", { IsFinal = false; AllowedMethods = [ "GET"; "POST" ]; Description = None }
                              "OTurn", { IsFinal = false; AllowedMethods = [ "GET"; "POST" ]; Description = None }
                              "Won", { IsFinal = true; AllowedMethods = [ "GET" ]; Description = None }
                              "Draw", { IsFinal = true; AllowedMethods = [ "GET" ]; Description = None } ]
                            |> Map.ofList
                          Roles = []
                          Transitions = [] }

                    let resource =
                        { RouteTemplate = "/games/{gameId}"
                          ResourceSlug = "games"
                          TypeInfo = []
                          Statechart = Some statechart
                          HttpCapabilities =
                            [ { Method = "GET"
                                StateKey = Some "XTurn"
                                LinkRelation = "self"
                                IsSafe = true }
                              { Method = "POST"
                                StateKey = Some "XTurn"
                                LinkRelation = "post"
                                IsSafe = false } ]
                          DerivedFields = ResourceModel.emptyDerivedFields }

                    let result = computeDerivedFields resource []
                    Expect.contains result.OrphanStates "OTurn" "OTurn should be orphan"
                    Expect.contains result.OrphanStates "Won" "Won should be orphan"
                    Expect.contains result.OrphanStates "Draw" "Draw should be orphan"
                    Expect.equal (List.contains "XTurn" result.OrphanStates) false "XTurn is handled"

                testCase "identifies unhandled DU cases"
                <| fun _ ->
                    let statechart: Frank.Resources.Model.ExtractedStatechart =
                        { RouteTemplate = "/games/{gameId}"
                          StateNames = [ "XTurn"; "OTurn" ]
                          InitialStateKey = "XTurn"
                          GuardNames = []
                          StateMetadata = Map.empty
                          Roles = []
                          Transitions = [] }

                    let stateType =
                        { FullName = "Test.GameState"
                          ShortName = "GameState"
                          Kind =
                            DiscriminatedUnion
                                [ { Name = "XTurn"; Fields = [] }
                                  { Name = "OTurn"; Fields = [] }
                                  { Name = "Won"; Fields = [] }
                                  { Name = "Draw"; Fields = [] } ]
                          GenericParameters = []
                          SourceLocation = None
                          IsClosed = false }

                    let resource =
                        { RouteTemplate = "/games/{gameId}"
                          ResourceSlug = "games"
                          TypeInfo = []
                          Statechart = Some statechart
                          HttpCapabilities = []
                          DerivedFields = ResourceModel.emptyDerivedFields }

                    let result = computeDerivedFields resource [ stateType ]
                    Expect.contains result.UnhandledCases "Won" "Won should be unhandled"
                    Expect.contains result.UnhandledCases "Draw" "Draw should be unhandled"

                testCase "computes state structure from DU fields"
                <| fun _ ->
                    let statechart: Frank.Resources.Model.ExtractedStatechart =
                        { RouteTemplate = "/games/{gameId}"
                          StateNames = [ "Playing"; "Won" ]
                          InitialStateKey = "Playing"
                          GuardNames = []
                          StateMetadata = Map.empty
                          Roles = []
                          Transitions = [] }

                    let stateType =
                        { FullName = "Test.GameState"
                          ShortName = "GameState"
                          Kind =
                            DiscriminatedUnion
                                [ { Name = "Playing"
                                    Fields =
                                      [ { Name = "board"
                                          Kind = Primitive "xsd:string"
                                          IsRequired = true
                                          IsScalar = true
                                          Constraints = [] } ] }
                                  { Name = "Won"
                                    Fields =
                                      [ { Name = "winner"
                                          Kind = Primitive "xsd:string"
                                          IsRequired = true
                                          IsScalar = true
                                          Constraints = [] } ] } ]
                          GenericParameters = []
                          SourceLocation = None
                          IsClosed = false }

                    let resource =
                        { RouteTemplate = "/games/{gameId}"
                          ResourceSlug = "games"
                          TypeInfo = []
                          Statechart = Some statechart
                          HttpCapabilities = []
                          DerivedFields = ResourceModel.emptyDerivedFields }

                    let result = computeDerivedFields resource [ stateType ]
                    Expect.isTrue (Map.containsKey "Playing" result.StateStructure) "Should have Playing entry"
                    Expect.isTrue (Map.containsKey "Won" result.StateStructure) "Should have Won entry"

                    let playingFields = result.StateStructure.["Playing"]
                    Expect.equal playingFields.Length 1 "Playing should have 1 field"
                    Expect.equal playingFields.[0].Name "board" "Playing field should be board"

                    let wonFields = result.StateStructure.["Won"]
                    Expect.equal wonFields.Length 1 "Won should have 1 field"
                    Expect.equal wonFields.[0].Name "winner" "Won field should be winner"

                testCase "type coverage is 1.0 when all states have type info"
                <| fun _ ->
                    let statechart: Frank.Resources.Model.ExtractedStatechart =
                        { RouteTemplate = "/test"
                          StateNames = [ "A"; "B" ]
                          InitialStateKey = "A"
                          GuardNames = []
                          StateMetadata = Map.empty
                          Roles = []
                          Transitions = [] }

                    let stateType =
                        { FullName = "Test.State"
                          ShortName = "State"
                          Kind =
                            DiscriminatedUnion
                                [ { Name = "A"; Fields = [] }; { Name = "B"; Fields = [] } ]
                          GenericParameters = []
                          SourceLocation = None
                          IsClosed = false }

                    let resource =
                        { RouteTemplate = "/test"
                          ResourceSlug = "test"
                          TypeInfo = []
                          Statechart = Some statechart
                          HttpCapabilities = []
                          DerivedFields = ResourceModel.emptyDerivedFields }

                    let result = computeDerivedFields resource [ stateType ]
                    Expect.equal result.TypeCoverage 1.0 "Coverage should be 1.0" ]

          testList
              "comparison with old extractors"
              [ testCaseAsync "unified walker finds same plain resources as AstAnalyzer"
                <| async {
                    let source =
                        """
module Test
let home = resource "/" { get (fun ctx -> task { return () }) }
let items = resource "/items" {
    get (fun ctx -> task { return () })
    post (fun ctx -> task { return () })
}
"""

                    let! ast = parseSource source

                    // Old extractor
                    let oldResults = AstAnalyzer.analyzeFile ast

                    // Unified extractor
                    let unifiedResults = findResourcesInParsedInput ast

                    let unifiedPlain =
                        unifiedResults
                        |> List.choose (fun f ->
                            match f with
                            | FoundPlainResource ar -> Some ar
                            | _ -> None)

                    Expect.equal unifiedPlain.Length oldResults.Length "Same number of resources"

                    for old in oldResults do
                        let matching =
                            unifiedPlain |> List.tryFind (fun u -> u.RouteTemplate = old.RouteTemplate)

                        Expect.isSome matching $"Should find resource {old.RouteTemplate}"
                        let m = matching.Value
                        Expect.equal m.HttpMethods old.HttpMethods $"Methods should match for {old.RouteTemplate}"
                        Expect.equal m.Name old.Name $"Name should match for {old.RouteTemplate}"
                        Expect.equal m.HasLinkedData old.HasLinkedData $"LinkedData should match for {old.RouteTemplate}"
                } ] ]
