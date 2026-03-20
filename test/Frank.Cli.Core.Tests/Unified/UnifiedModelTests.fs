module Frank.Cli.Core.Tests.Unified.UnifiedModelTests

open System
open Expecto
open MessagePack
open MessagePack.Resolvers
open MessagePack.FSharp
open Frank.Resources.Model
open Frank.Cli.Core.Unified

let private options =
    MessagePackSerializerOptions
        .Standard
        .WithResolver(
            CompositeResolver.Create(FSharpResolver.Instance, ContractlessStandardResolver.Instance)
        )

[<Tests>]
let unifiedModelTests =
    testList
        "UnifiedModel"
        [ testList
              "MessagePack roundtrip"
              [ testCase "roundtrips UnifiedResource"
                <| fun _ ->
                    let resource: UnifiedResource =
                        { RouteTemplate = "/games/{gameId}"
                          ResourceSlug = "games"
                          TypeInfo = []
                          Statechart = None
                          HttpCapabilities =
                            [ { Method = "GET"
                                StateKey = None
                                LinkRelation = "self"
                                IsSafe = true } ]
                          DerivedFields = ResourceModel.emptyDerivedFields }

                    let bytes = MessagePackSerializer.Serialize(resource, options)

                    let deserialized =
                        MessagePackSerializer.Deserialize<UnifiedResource>(bytes, options)

                    Expect.equal deserialized.RouteTemplate resource.RouteTemplate "RouteTemplate roundtrips"
                    Expect.equal deserialized.HttpCapabilities.Length 1 "HttpCapabilities roundtrip"

                testCase "roundtrips DerivedResourceFields"
                <| fun _ ->
                    let fields: DerivedResourceFields =
                        { OrphanStates = [ "Abandoned" ]
                          UnhandledCases = [ "Draw" ]
                          StateStructure = Map.ofList [ "XTurn", [] ]
                          TypeCoverage = 0.75 }

                    let bytes = MessagePackSerializer.Serialize(fields, options)

                    let deserialized =
                        MessagePackSerializer.Deserialize<DerivedResourceFields>(bytes, options)

                    Expect.equal deserialized.OrphanStates [ "Abandoned" ] "OrphanStates roundtrip"
                    Expect.equal deserialized.TypeCoverage 0.75 "TypeCoverage roundtrip"

                testCase "roundtrips UnifiedExtractionState"
                <| fun _ ->
                    let state: UnifiedExtractionState =
                        { Resources = []
                          SourceHash = "abc123"
                          BaseUri = "https://example.com/"
                          Vocabularies = [ "schema.org" ]
                          ExtractedAt = DateTimeOffset(2026, 3, 19, 0, 0, 0, TimeSpan.Zero)
                          ToolVersion = "7.0.0"
                          Profiles = ProjectedProfiles.empty }

                    let bytes = MessagePackSerializer.Serialize(state, options)

                    let deserialized =
                        MessagePackSerializer.Deserialize<UnifiedExtractionState>(bytes, options)

                    Expect.equal deserialized.SourceHash "abc123" "SourceHash roundtrips"
                    Expect.equal deserialized.Vocabularies [ "schema.org" ] "Vocabularies roundtrip" ] ]
