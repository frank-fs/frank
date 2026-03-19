module Frank.Cli.Core.Tests.Unified.UnifiedAlpsGeneratorTests

open System.Text.Json
open Expecto
open Frank.Statecharts
open Frank.Cli.Core.Analysis
open Frank.Cli.Core.Statechart
open Frank.Cli.Core.Unified
open Frank.Cli.Core.Unified.UnifiedAlpsGenerator

// ============================================================================
// Test Fixtures
// ============================================================================

let private makeField (name: string) (kind: FieldKind) : AnalyzedField =
    { Name = name
      Kind = kind
      IsRequired = true
      IsScalar = true
      Constraints = [] }

let private boardType: AnalyzedType =
    { FullName = "TicTacToe.Board"
      ShortName = "Board"
      Kind =
        Record
            [ makeField "cells" (Collection(Primitive "xsd:string"))
              makeField "size" (Primitive "xsd:integer") ]
      GenericParameters = []
      SourceLocation = None
      IsClosed = true }

let private ticTacToeStateType: AnalyzedType =
    { FullName = "TicTacToe.TicTacToeState"
      ShortName = "TicTacToeState"
      Kind =
        DiscriminatedUnion
            [ { Name = "XTurn"
                Fields = [ makeField "board" (Reference "Board") ] }
              { Name = "OTurn"
                Fields = [ makeField "board" (Reference "Board") ] }
              { Name = "Won"
                Fields =
                  [ makeField "winner" (Primitive "xsd:string")
                    makeField "board" (Reference "Board") ] }
              { Name = "Draw"
                Fields = [ makeField "board" (Reference "Board") ] } ]
      GenericParameters = []
      SourceLocation = None
      IsClosed = false }

let private ticTacToeStatechart: ExtractedStatechart =
    { RouteTemplate = "/games/{gameId}"
      StateNames = [ "XTurn"; "OTurn"; "Won"; "Draw" ]
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

let private ticTacToeResource: UnifiedResource =
    { RouteTemplate = "/games/{gameId}"
      ResourceSlug = "games"
      TypeInfo = [ ticTacToeStateType; boardType ]
      Statechart = Some ticTacToeStatechart
      HttpCapabilities =
        [ { Method = "GET"
            StateKey = Some "XTurn"
            LinkRelation = "self"
            IsSafe = true }
          { Method = "POST"
            StateKey = Some "XTurn"
            LinkRelation = "makeMove"
            IsSafe = false }
          { Method = "GET"
            StateKey = Some "OTurn"
            LinkRelation = "self"
            IsSafe = true }
          { Method = "POST"
            StateKey = Some "OTurn"
            LinkRelation = "makeMove"
            IsSafe = false }
          { Method = "GET"
            StateKey = Some "Won"
            LinkRelation = "self"
            IsSafe = true }
          { Method = "GET"
            StateKey = Some "Draw"
            LinkRelation = "self"
            IsSafe = true } ]
      DerivedFields = UnifiedModel.emptyDerivedFields }

let private plainHealthResource: UnifiedResource =
    { RouteTemplate = "/health"
      ResourceSlug = "health"
      TypeInfo =
        [ { FullName = "App.HealthStatus"
            ShortName = "HealthStatus"
            Kind =
              Record
                  [ makeField "status" (Primitive "xsd:string")
                    makeField "name" (Primitive "xsd:string") ]
            GenericParameters = []
            SourceLocation = None
            IsClosed = true } ]
      Statechart = None
      HttpCapabilities =
        [ { Method = "GET"
            StateKey = None
            LinkRelation = "self"
            IsSafe = true } ]
      DerivedFields = UnifiedModel.emptyDerivedFields }

let private minimalResource: UnifiedResource =
    { RouteTemplate = "/ping"
      ResourceSlug = "ping"
      TypeInfo = []
      Statechart = None
      HttpCapabilities =
        [ { Method = "GET"
            StateKey = None
            LinkRelation = "self"
            IsSafe = true } ]
      DerivedFields = UnifiedModel.emptyDerivedFields }

// ============================================================================
// Helper: Parse JSON and find descriptors
// ============================================================================

let private parseJson (json: string) =
    use doc = JsonDocument.Parse(json)
    let root = doc.RootElement
    let alps = root.GetProperty("alps")
    alps

let private getDescriptors (json: string) =
    use doc = JsonDocument.Parse(json)
    let root = doc.RootElement
    let alps = root.GetProperty("alps")

    match alps.TryGetProperty("descriptor") with
    | true, descs -> [ for d in descs.EnumerateArray() -> d.Clone() ]
    | _ -> []

let private descriptorHasType (typeStr: string) (desc: JsonElement) =
    match desc.TryGetProperty("type") with
    | true, t -> t.GetString() = typeStr
    | _ -> false

let private descriptorHasId (id: string) (desc: JsonElement) =
    match desc.TryGetProperty("id") with
    | true, i -> i.GetString() = id
    | _ -> false

// ============================================================================
// Tests
// ============================================================================

[<Tests>]
let unifiedAlpsGeneratorTests =
    testList
        "UnifiedAlpsGenerator"
        [
          // ── T027: Vocabulary Alignment ──
          testList
              "vocabularyAlignment"
              [ testCase "tryFindAlignment matches known Schema.org fields"
                <| fun _ ->
                    Expect.equal (tryFindAlignment "name") (Some "https://schema.org/name") "name -> schema.org/name"
                    Expect.equal (tryFindAlignment "description") (Some "https://schema.org/description") "description"
                    Expect.equal (tryFindAlignment "email") (Some "https://schema.org/email") "email"
                    Expect.equal (tryFindAlignment "url") (Some "https://schema.org/url") "url"
                    Expect.equal (tryFindAlignment "price") (Some "https://schema.org/price") "price"
                    Expect.equal (tryFindAlignment "image") (Some "https://schema.org/image") "image"
                    Expect.equal (tryFindAlignment "telephone") (Some "https://schema.org/telephone") "telephone"

                testCase "tryFindAlignment handles camelCase splitting"
                <| fun _ ->
                    Expect.equal
                        (tryFindAlignment "emailAddress")
                        (Some "https://schema.org/email")
                        "emailAddress -> email"

                    Expect.equal
                        (tryFindAlignment "createdAt")
                        (Some "https://schema.org/dateCreated")
                        "createdAt -> dateCreated"

                    Expect.equal
                        (tryFindAlignment "dateModified")
                        (Some "https://schema.org/dateModified")
                        "dateModified"

                testCase "tryFindAlignment returns None for unknown fields"
                <| fun _ ->
                    Expect.isNone (tryFindAlignment "board") "board has no Schema.org alignment"
                    Expect.isNone (tryFindAlignment "winner") "winner has no Schema.org alignment"
                    Expect.isNone (tryFindAlignment "cells") "cells has no Schema.org alignment" ]

          // ── T028: Link Relation Derivation ──
          testList
              "linkRelationDerivation"
              [ testCase "GET on single resource returns self"
                <| fun _ ->
                    let rel =
                        deriveRelationTypeForRoute "https://example.com/alps" "games" "/games/{gameId}" "GET" None

                    Expect.equal rel "self" "GET on /games/{gameId} should be self"

                testCase "GET on collection resource returns collection"
                <| fun _ ->
                    let rel =
                        deriveRelationTypeForRoute "https://example.com/alps" "games" "/games" "GET" None

                    Expect.equal rel "collection" "GET on /games should be collection"

                testCase "PUT returns edit"
                <| fun _ ->
                    let rel =
                        deriveRelationTypeForRoute "https://example.com/alps" "games" "/games/{gameId}" "PUT" None

                    Expect.equal rel "edit" "PUT should be edit"

                testCase "PATCH returns edit"
                <| fun _ ->
                    let rel =
                        deriveRelationTypeForRoute "https://example.com/alps" "games" "/games/{gameId}" "PATCH" None

                    Expect.equal rel "edit" "PATCH should be edit"

                testCase "POST returns ALPS fragment URI (not IANA)"
                <| fun _ ->
                    let rel =
                        deriveRelationTypeForRoute "https://example.com/alps" "games" "/games" "POST" None

                    Expect.stringContains rel "https://example.com/alps/games#" "POST should use ALPS fragment"

                testCase "DELETE returns ALPS fragment URI (not IANA)"
                <| fun _ ->
                    let rel =
                        deriveRelationTypeForRoute "https://example.com/alps" "games" "/games/{gameId}" "DELETE" None

                    Expect.stringContains rel "https://example.com/alps/games#" "DELETE should use ALPS fragment"

                testCase "state-scoped relation includes state in fragment"
                <| fun _ ->
                    let rel =
                        deriveRelationTypeForRoute
                            "https://example.com/alps"
                            "games"
                            "/games/{gameId}"
                            "POST"
                            (Some "XTurn")

                    Expect.stringContains rel "XTurn" "Should include state name in fragment" ]

          // ── T026 + T029: ALPS Generation ──
          testList
              "generate"
              [ testCase "plain resource produces valid ALPS with semantic and safe descriptors"
                <| fun _ ->
                    let result = generate plainHealthResource "https://example.com/alps"

                    match result with
                    | Error errors -> failtest $"Generation failed: {errors}"
                    | Ok json ->
                        let descriptors = getDescriptors json

                        // Should have semantic descriptors for fields
                        let semanticDescs =
                            descriptors |> List.filter (descriptorHasType "semantic")

                        Expect.isGreaterThan
                            semanticDescs.Length
                            0
                            "Should have at least one semantic descriptor"

                        // Should have safe descriptor for GET
                        let safeDescs =
                            descriptors |> List.filter (descriptorHasType "safe")

                        Expect.isGreaterThan
                            safeDescs.Length
                            0
                            "Should have at least one safe descriptor"

                        // Check status field is present
                        let hasStatus =
                            descriptors |> List.exists (descriptorHasId "status")

                        Expect.isTrue hasStatus "Should have 'status' descriptor"

                testCase "plain resource with Schema.org-aligned field gets href"
                <| fun _ ->
                    let result = generate plainHealthResource "https://example.com/alps"

                    match result with
                    | Error errors -> failtest $"Generation failed: {errors}"
                    | Ok json ->
                        let descriptors = getDescriptors json

                        // The 'name' field should have an href to schema.org
                        let nameDesc =
                            descriptors |> List.tryFind (descriptorHasId "name")

                        Expect.isSome nameDesc "Should have 'name' descriptor"

                        let nameElem = nameDesc.Value

                        match nameElem.TryGetProperty("href") with
                        | true, href ->
                            Expect.equal
                                (href.GetString())
                                "https://schema.org/name"
                                "name should link to Schema.org"
                        | _ -> failtest "name descriptor should have href"

                testCase "plain resource has no state-dependent transition descriptors"
                <| fun _ ->
                    let result = generate plainHealthResource "https://example.com/alps"

                    match result with
                    | Error errors -> failtest $"Generation failed: {errors}"
                    | Ok json ->
                        // Should not contain any state-prefixed descriptor ids
                        Expect.isFalse
                            (json.Contains("XTurn"))
                            "Plain resource should not contain state names"

                        Expect.isFalse
                            (json.Contains("OTurn"))
                            "Plain resource should not contain state names"

                testCase "minimal resource with no type info produces valid ALPS"
                <| fun _ ->
                    let result = generate minimalResource "https://example.com/alps"

                    match result with
                    | Error errors -> failtest $"Generation failed: {errors}"
                    | Ok json ->
                        let descriptors = getDescriptors json

                        // Should have at least the transition descriptor
                        Expect.isGreaterThan
                            descriptors.Length
                            0
                            "Should have at least one descriptor"

                        let safeDescs =
                            descriptors |> List.filter (descriptorHasType "safe")

                        Expect.isGreaterThan
                            safeDescs.Length
                            0
                            "Should have safe descriptor for GET"

                testCase "tic-tac-toe resource contains semantic descriptors"
                <| fun _ ->
                    let result = generate ticTacToeResource "https://example.com/alps"

                    match result with
                    | Error errors -> failtest $"Generation failed: {errors}"
                    | Ok json ->
                        let descriptors = getDescriptors json

                        // Should have the DU type descriptor
                        let hasTicTacToeState =
                            descriptors
                            |> List.exists (descriptorHasId "TicTacToeState")

                        Expect.isTrue hasTicTacToeState "Should have TicTacToeState descriptor"

                        // Should have semantic descriptors
                        let semanticDescs =
                            descriptors |> List.filter (descriptorHasType "semantic")

                        Expect.isGreaterThan
                            semanticDescs.Length
                            0
                            "Should have semantic descriptors"

                testCase "tic-tac-toe resource contains safe and unsafe transition descriptors"
                <| fun _ ->
                    let result = generate ticTacToeResource "https://example.com/alps"

                    match result with
                    | Error errors -> failtest $"Generation failed: {errors}"
                    | Ok json ->
                        let descriptors = getDescriptors json

                        // Should have safe descriptors (GET)
                        let safeDescs =
                            descriptors |> List.filter (descriptorHasType "safe")

                        Expect.isGreaterThan
                            safeDescs.Length
                            0
                            "Should have safe descriptors for GET"

                        // Should have unsafe descriptors (POST)
                        let unsafeDescs =
                            descriptors |> List.filter (descriptorHasType "unsafe")

                        Expect.isGreaterThan
                            unsafeDescs.Length
                            0
                            "Should have unsafe descriptors for POST"

                testCase "tic-tac-toe transitions include state-scoped descriptor ids"
                <| fun _ ->
                    let result = generate ticTacToeResource "https://example.com/alps"

                    match result with
                    | Error errors -> failtest $"Generation failed: {errors}"
                    | Ok json ->
                        // State-scoped descriptors should include state names
                        Expect.stringContains json "XTurn" "Should contain XTurn state reference"
                        Expect.stringContains json "OTurn" "Should contain OTurn state reference"
                        Expect.stringContains json "Won" "Should contain Won state reference"

                testCase "tic-tac-toe transitions have rt linking to semantic descriptors"
                <| fun _ ->
                    let result = generate ticTacToeResource "https://example.com/alps"

                    match result with
                    | Error errors -> failtest $"Generation failed: {errors}"
                    | Ok json ->
                        // Transition descriptors should have rt referencing semantic ids
                        Expect.stringContains json "\"rt\"" "Should have rt property"
                        Expect.stringContains json "#TicTacToeState" "rt should reference TicTacToeState"

                testCase "Board record fields appear as semantic descriptors"
                <| fun _ ->
                    let result = generate ticTacToeResource "https://example.com/alps"

                    match result with
                    | Error errors -> failtest $"Generation failed: {errors}"
                    | Ok json ->
                        let descriptors = getDescriptors json

                        let hasCells =
                            descriptors |> List.exists (descriptorHasId "cells")

                        let hasSize =
                            descriptors |> List.exists (descriptorHasId "size")

                        Expect.isTrue hasCells "Should have 'cells' descriptor from Board record"
                        Expect.isTrue hasSize "Should have 'size' descriptor from Board record" ]

          // ── T030: Round-trip Validation ──
          testList
              "roundTripValidation"
              [ testCase "generated ALPS parses without errors via Alps.JsonParser"
                <| fun _ ->
                    let result = generate ticTacToeResource "https://example.com/alps"

                    match result with
                    | Error errors -> failtest $"Round-trip failed: {errors}"
                    | Ok _json -> () // Success -- no parse errors

                testCase "plain resource ALPS parses without errors"
                <| fun _ ->
                    let result = generate plainHealthResource "https://example.com/alps"

                    match result with
                    | Error errors -> failtest $"Round-trip failed: {errors}"
                    | Ok _json -> ()

                testCase "minimal resource ALPS parses without errors"
                <| fun _ ->
                    let result = generate minimalResource "https://example.com/alps"

                    match result with
                    | Error errors -> failtest $"Round-trip failed: {errors}"
                    | Ok _json -> ()

                testCase "generated JSON is valid JSON with alps root"
                <| fun _ ->
                    let result = generate ticTacToeResource "https://example.com/alps"

                    match result with
                    | Error errors -> failtest $"Generation failed: {errors}"
                    | Ok json ->
                        use doc = JsonDocument.Parse(json)
                        let root = doc.RootElement
                        let alps = root.GetProperty("alps")

                        match alps.TryGetProperty("version") with
                        | true, v -> Expect.equal (v.GetString()) "1.0" "Version should be 1.0"
                        | _ -> failtest "Should have version property" ] ]
