module Frank.Cli.Core.Tests.Unified.AffordanceMapGeneratorTests

open System
open System.Text.Json
open Expecto
open Frank.Statecharts
open Frank.Resources.Model
open Frank.Cli.Core.Unified
open Frank.Cli.Core.Statechart

// ══════════════════════════════════════════════════════════════════════════════
// Test Fixtures
// ══════════════════════════════════════════════════════════════════════════════

let private fixedTimestamp =
    DateTimeOffset(2026, 3, 19, 2, 15, 0, TimeSpan.Zero)

/// Tic-tac-toe stateful resource with 4 states.
let private ticTacToeResource: UnifiedResource =
    let stateNames = [ "XTurn"; "OTurn"; "Won"; "Draw" ]

    let statechart: ExtractedStatechart =
        { RouteTemplate = "/games/{gameId}"
          StateNames = stateNames
          InitialStateKey = "XTurn"
          GuardNames = []
          StateMetadata =
            [ "XTurn",
              { IsFinal = false
                AllowedMethods = [ "GET"; "POST" ]
                Description = None }
              "OTurn",
              { IsFinal = false
                AllowedMethods = [ "GET"; "POST" ]
                Description = None }
              "Won",
              { IsFinal = true
                AllowedMethods = [ "GET" ]
                Description = None }
              "Draw",
              { IsFinal = true
                AllowedMethods = [ "GET" ]
                Description = None } ]
            |> Map.ofList }

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
            LinkRelation = "https://example.com/alps/games#makeMove"
            IsSafe = false }
          { Method = "GET"
            StateKey = Some "OTurn"
            LinkRelation = "self"
            IsSafe = true }
          { Method = "POST"
            StateKey = Some "OTurn"
            LinkRelation = "https://example.com/alps/games#makeMove"
            IsSafe = false }
          { Method = "GET"
            StateKey = Some "Won"
            LinkRelation = "self"
            IsSafe = true }
          { Method = "GET"
            StateKey = Some "Draw"
            LinkRelation = "self"
            IsSafe = true } ]
      DerivedFields = ResourceModel.emptyDerivedFields }

/// Simple stateless health check resource.
let private healthResource: UnifiedResource =
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

/// Resource with no HTTP capabilities (edge case).
let private emptyResource: UnifiedResource =
    { RouteTemplate = "/empty"
      ResourceSlug = "empty"
      TypeInfo = []
      Statechart = None
      HttpCapabilities = []
      DerivedFields = ResourceModel.emptyDerivedFields }

let private baseUri = "https://example.com/alps"

// ══════════════════════════════════════════════════════════════════════════════
// Helper to parse and navigate JSON
// ══════════════════════════════════════════════════════════════════════════════

let private parseJson (json: string) = JsonDocument.Parse(json)

let private getEntry (doc: JsonDocument) (key: string) =
    doc.RootElement.GetProperty("entries").GetProperty(key)

let private getStringProp (elem: JsonElement) (name: string) =
    elem.GetProperty(name).GetString()

let private getStringArray (elem: JsonElement) (name: string) =
    elem.GetProperty(name).EnumerateArray()
    |> Seq.map (fun e -> e.GetString())
    |> Seq.toList

// ══════════════════════════════════════════════════════════════════════════════
// Tests
// ══════════════════════════════════════════════════════════════════════════════

[<Tests>]
let affordanceMapGeneratorTests =
    testList
        "AffordanceMapGenerator"
        [ testList
              "compositeKey"
              [ testCase "builds key with pipe separator"
                <| fun _ ->
                    let key = AffordanceMapGenerator.compositeKey "/games/{gameId}" "XTurn"
                    Expect.equal key "/games/{gameId}|XTurn" "Composite key uses | separator"

                testCase "builds key with wildcard for stateless"
                <| fun _ ->
                    let key = AffordanceMapGenerator.compositeKey "/health" "*"
                    Expect.equal key "/health|*" "Wildcard key for stateless resources" ]

          testList
              "profileUrl"
              [ testCase "derives profile URL from base URI and slug"
                <| fun _ ->
                    let url = AffordanceMapGenerator.profileUrl "https://example.com/alps" "games"
                    Expect.equal url "https://example.com/alps/games" "Profile URL"

                testCase "trims trailing slash from base URI"
                <| fun _ ->
                    let url = AffordanceMapGenerator.profileUrl "https://example.com/alps/" "health"
                    Expect.equal url "https://example.com/alps/health" "Trailing slash trimmed" ]

          testList
              "deriveSlug"
              [ testCase "simple route"
                <| fun _ ->
                    let slug = AffordanceMapGenerator.deriveSlug "/health"
                    Expect.equal slug "health" "Simple route slug"

                testCase "parameterized route"
                <| fun _ ->
                    let slug = AffordanceMapGenerator.deriveSlug "/games/{gameId}"
                    Expect.equal slug "games" "Parameter stripped"

                testCase "multi-segment route"
                <| fun _ ->
                    let slug = AffordanceMapGenerator.deriveSlug "/api/v1/games/{id}"
                    Expect.equal slug "api-v1-games" "Multi-segment joined with hyphens"

                testCase "root route"
                <| fun _ ->
                    let slug = AffordanceMapGenerator.deriveSlug "/"
                    Expect.equal slug "root" "Root route" ]

          testList
              "generate - structure"
              [ testCase "has version 1.0"
                <| fun _ ->
                    let json =
                        AffordanceMapGenerator.generate [ healthResource ] baseUri (Some fixedTimestamp)

                    use doc = parseJson json
                    let version = doc.RootElement.GetProperty("version").GetString()
                    Expect.equal version "1.0" "Version is 1.0"

                testCase "has baseUri"
                <| fun _ ->
                    let json =
                        AffordanceMapGenerator.generate [ healthResource ] baseUri (Some fixedTimestamp)

                    use doc = parseJson json
                    let uri = doc.RootElement.GetProperty("baseUri").GetString()
                    Expect.equal uri baseUri "Base URI matches"

                testCase "has generatedAt timestamp"
                <| fun _ ->
                    let json =
                        AffordanceMapGenerator.generate [ healthResource ] baseUri (Some fixedTimestamp)

                    use doc = parseJson json
                    let ts = doc.RootElement.GetProperty("generatedAt").GetString()
                    Expect.isTrue (ts.StartsWith("2026-03-19")) "Timestamp present"

                testCase "has entries object"
                <| fun _ ->
                    let json =
                        AffordanceMapGenerator.generate [ healthResource ] baseUri (Some fixedTimestamp)

                    use doc = parseJson json
                    let entries = doc.RootElement.GetProperty("entries")
                    Expect.equal entries.ValueKind JsonValueKind.Object "Entries is object" ]

          testList
              "generate - stateless resource"
              [ testCase "health resource produces wildcard entry"
                <| fun _ ->
                    let json =
                        AffordanceMapGenerator.generate [ healthResource ] baseUri (Some fixedTimestamp)

                    use doc = parseJson json
                    let entry = getEntry doc "/health|*"
                    let methods = getStringArray entry "allowedMethods"
                    Expect.equal methods [ "GET" ] "Health has GET only"

                testCase "health entry has correct profileUrl"
                <| fun _ ->
                    let json =
                        AffordanceMapGenerator.generate [ healthResource ] baseUri (Some fixedTimestamp)

                    use doc = parseJson json
                    let entry = getEntry doc "/health|*"
                    let profile = getStringProp entry "profileUrl"
                    Expect.equal profile "https://example.com/alps/health" "Profile URL"

                testCase "health entry has self link relation"
                <| fun _ ->
                    let json =
                        AffordanceMapGenerator.generate [ healthResource ] baseUri (Some fixedTimestamp)

                    use doc = parseJson json
                    let entry = getEntry doc "/health|*"
                    let rels = entry.GetProperty("linkRelations").EnumerateArray() |> Seq.toList
                    Expect.equal rels.Length 1 "One link relation"
                    let rel = rels.[0]
                    Expect.equal (rel.GetProperty("rel").GetString()) "self" "Rel is self"
                    Expect.equal (rel.GetProperty("method").GetString()) "GET" "Method is GET"
                    Expect.equal (rel.GetProperty("href").GetString()) "/health" "Href is route template" ]

          testList
              "generate - tic-tac-toe stateful resource"
              [ testCase "XTurn entry has GET and POST"
                <| fun _ ->
                    let json =
                        AffordanceMapGenerator.generate [ ticTacToeResource ] baseUri (Some fixedTimestamp)

                    use doc = parseJson json
                    let entry = getEntry doc "/games/{gameId}|XTurn"
                    let methods = getStringArray entry "allowedMethods"
                    Expect.equal methods [ "GET"; "POST" ] "XTurn has GET and POST"

                testCase "OTurn entry has GET and POST"
                <| fun _ ->
                    let json =
                        AffordanceMapGenerator.generate [ ticTacToeResource ] baseUri (Some fixedTimestamp)

                    use doc = parseJson json
                    let entry = getEntry doc "/games/{gameId}|OTurn"
                    let methods = getStringArray entry "allowedMethods"
                    Expect.equal methods [ "GET"; "POST" ] "OTurn has GET and POST"

                testCase "Won entry has GET only"
                <| fun _ ->
                    let json =
                        AffordanceMapGenerator.generate [ ticTacToeResource ] baseUri (Some fixedTimestamp)

                    use doc = parseJson json
                    let entry = getEntry doc "/games/{gameId}|Won"
                    let methods = getStringArray entry "allowedMethods"
                    Expect.equal methods [ "GET" ] "Won has GET only"

                testCase "Draw entry has GET only"
                <| fun _ ->
                    let json =
                        AffordanceMapGenerator.generate [ ticTacToeResource ] baseUri (Some fixedTimestamp)

                    use doc = parseJson json
                    let entry = getEntry doc "/games/{gameId}|Draw"
                    let methods = getStringArray entry "allowedMethods"
                    Expect.equal methods [ "GET" ] "Draw has GET only"

                testCase "all entries have correct profileUrl"
                <| fun _ ->
                    let json =
                        AffordanceMapGenerator.generate [ ticTacToeResource ] baseUri (Some fixedTimestamp)

                    use doc = parseJson json

                    for state in [ "XTurn"; "OTurn"; "Won"; "Draw" ] do
                        let key = sprintf "/games/{gameId}|%s" state
                        let entry = getEntry doc key
                        let profile = getStringProp entry "profileUrl"
                        Expect.equal profile "https://example.com/alps/games" (sprintf "Profile URL for %s" state)

                testCase "XTurn has self and domain link relations"
                <| fun _ ->
                    let json =
                        AffordanceMapGenerator.generate [ ticTacToeResource ] baseUri (Some fixedTimestamp)

                    use doc = parseJson json
                    let entry = getEntry doc "/games/{gameId}|XTurn"
                    let rels = entry.GetProperty("linkRelations").EnumerateArray() |> Seq.toList
                    Expect.equal rels.Length 2 "XTurn has 2 link relations"

                    let relTypes =
                        rels |> List.map (fun r -> r.GetProperty("rel").GetString())

                    Expect.contains relTypes "self" "Has self relation"

                    Expect.contains
                        relTypes
                        "https://example.com/alps/games#makeMove"
                        "Has domain-specific relation"

                testCase "Won has only self link relation"
                <| fun _ ->
                    let json =
                        AffordanceMapGenerator.generate [ ticTacToeResource ] baseUri (Some fixedTimestamp)

                    use doc = parseJson json
                    let entry = getEntry doc "/games/{gameId}|Won"
                    let rels = entry.GetProperty("linkRelations").EnumerateArray() |> Seq.toList
                    Expect.equal rels.Length 1 "Won has 1 link relation"
                    Expect.equal (rels.[0].GetProperty("rel").GetString()) "self" "Only self" ]

          testList
              "generate - edge cases"
              [ testCase "empty resource produces entry with empty methods"
                <| fun _ ->
                    let json =
                        AffordanceMapGenerator.generate [ emptyResource ] baseUri (Some fixedTimestamp)

                    use doc = parseJson json
                    let entry = getEntry doc "/empty|*"
                    let methods = getStringArray entry "allowedMethods"
                    Expect.isEmpty methods "Empty resource has no methods"
                    let rels = entry.GetProperty("linkRelations").EnumerateArray() |> Seq.toList
                    Expect.isEmpty rels "Empty resource has no link relations"

                testCase "multiple resources produce entries for all"
                <| fun _ ->
                    let json =
                        AffordanceMapGenerator.generate
                            [ ticTacToeResource; healthResource ]
                            baseUri
                            (Some fixedTimestamp)

                    use doc = parseJson json
                    let entries = doc.RootElement.GetProperty("entries")
                    let entryCount = entries.EnumerateObject() |> Seq.length
                    // tic-tac-toe: 4 states + health: 1 = 5
                    Expect.equal entryCount 5 "5 entries total (4 states + 1 stateless)"

                testCase "deterministic output for same input"
                <| fun _ ->
                    let json1 =
                        AffordanceMapGenerator.generate
                            [ ticTacToeResource; healthResource ]
                            baseUri
                            (Some fixedTimestamp)

                    let json2 =
                        AffordanceMapGenerator.generate
                            [ ticTacToeResource; healthResource ]
                            baseUri
                            (Some fixedTimestamp)

                    Expect.equal json1 json2 "Same input produces same output"

                testCase "empty resource list produces empty entries"
                <| fun _ ->
                    let json =
                        AffordanceMapGenerator.generate [] baseUri (Some fixedTimestamp)

                    use doc = parseJson json
                    let entries = doc.RootElement.GetProperty("entries")
                    let entryCount = entries.EnumerateObject() |> Seq.length
                    Expect.equal entryCount 0 "No entries for empty resource list" ]

          testList
              "generateEntries"
              [ testCase "produces AffordanceMapEntry list"
                <| fun _ ->
                    let entries =
                        AffordanceMapGenerator.generateEntries [ ticTacToeResource; healthResource ] baseUri

                    Expect.equal entries.Length 5 "5 entries total"

                    let xTurn =
                        entries |> List.find (fun e -> e.StateKey = "XTurn")

                    Expect.equal xTurn.AllowedMethods [ "GET"; "POST" ] "XTurn methods"
                    Expect.equal xTurn.ProfileUrl "https://example.com/alps/games" "XTurn profile"

                    let health =
                        entries |> List.find (fun e -> e.StateKey = "*")

                    Expect.equal health.AllowedMethods [ "GET" ] "Health methods"
                    Expect.equal health.ProfileUrl "https://example.com/alps/health" "Health profile" ] ]
