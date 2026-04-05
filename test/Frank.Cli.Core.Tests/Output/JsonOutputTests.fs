module Frank.Cli.Core.Tests.JsonOutputTests

open System
open System.Text.Json
open Expecto
open Frank.Cli.Core.Commands.ExtractCommand
open Frank.Cli.Core.Commands.ClarifyCommand
open Frank.Cli.Core.State
open Frank.Cli.Core.Output.JsonOutput
open Frank.Resources.Model

// ---------------------------------------------------------------------------
// Helpers for constructing minimal test instances
// ---------------------------------------------------------------------------

let private minimalDerivedFields: DerivedResourceFields =
    { OrphanStates = []
      UnhandledCases = []
      StateStructure = Map.empty
      TypeCoverage = 1.0 }

let private minimalExtractionState (resources: UnifiedResource list) : UnifiedExtractionState =
    { Resources = resources
      SourceHash = "abc123"
      BaseUri = "https://example.com"
      Vocabularies = []
      ExtractedAt = DateTimeOffset.UtcNow
      ToolVersion = "0.0.0"
      Profiles = ProjectedProfiles.empty }

let private minimalResource (route: string) (sc: ExtractedStatechart option) : UnifiedResource =
    { RouteTemplate = route
      ResourceSlug = ResourceModel.resourceSlug route
      TypeInfo = []
      Statechart = sc
      HttpCapabilities = []
      DerivedFields = minimalDerivedFields }

[<Tests>]
let tests =
    testList "JsonOutput" [
        testCase "formatExtractResult produces valid JSON with nested summaries and ok status" <| fun _ ->
            let result : ExtractResult =
                { OntologySummary =
                    { ClassCount = 5
                      PropertyCount = 12
                      AlignedCount = 3
                      UnalignedCount = 9 }
                  ShapesSummary =
                    { ShapeCount = 5
                      ConstraintCount = 15 }
                  UnmappedTypes =
                    [ { TypeName = "MyApp.Foo"
                        Reason = "no rule"
                        Location = { File = "Foo.fs"; Line = 10; Column = 0 } } ]
                  StateFilePath = "/tmp/state.json" }

            let json = formatExtractResult result
            let doc = JsonDocument.Parse(json)
            let root = doc.RootElement

            Expect.equal (root.GetProperty("status").GetString()) "ok" "status should be ok"

            // Verify nested ontologySummary
            let ontSummary = root.GetProperty("ontologySummary")
            Expect.equal (ontSummary.GetProperty("classCount").GetInt32()) 5 "classCount should be 5"
            Expect.equal (ontSummary.GetProperty("propertyCount").GetInt32()) 12 "propertyCount should be 12"
            Expect.equal (ontSummary.GetProperty("alignedCount").GetInt32()) 3 "alignedCount should be 3"
            Expect.equal (ontSummary.GetProperty("unalignedCount").GetInt32()) 9 "unalignedCount should be 9"

            // Verify nested shapesSummary
            let shapesSummary = root.GetProperty("shapesSummary")
            Expect.equal (shapesSummary.GetProperty("shapeCount").GetInt32()) 5 "shapeCount should be 5"
            Expect.equal (shapesSummary.GetProperty("constraintCount").GetInt32()) 15 "constraintCount should be 15"

            Expect.equal (root.GetProperty("stateFilePath").GetString()) "/tmp/state.json" "stateFilePath"

            let unmapped = root.GetProperty("unmappedTypes")
            Expect.equal (unmapped.GetArrayLength()) 1 "Should have 1 unmapped type"
            Expect.equal (unmapped.[0].GetProperty("typeName").GetString()) "MyApp.Foo" "typeName"

        testCase "formatClarifyResult produces valid JSON with questions and ok status" <| fun _ ->
            let result : ClarifyResult =
                { Questions =
                    [ { Id = "unmapped-type-Foo"
                        Category = "unmapped-type"
                        QuestionText = "Map it?"
                        Context = {| SourceType = "Foo"; Location = Some "Foo.fs:10" |}
                        Options =
                          [ {| Label = "yes"; Impact = "will map" |}
                            {| Label = "no"; Impact = "will skip" |} ] } ]
                  ResolvedCount = 0
                  TotalCount = 1 }

            let json = formatClarifyResult result
            let doc = JsonDocument.Parse(json)
            let root = doc.RootElement

            Expect.equal (root.GetProperty("status").GetString()) "ok" "status should be ok"
            Expect.equal (root.GetProperty("resolvedCount").GetInt32()) 0 "resolvedCount"
            Expect.equal (root.GetProperty("totalCount").GetInt32()) 1 "totalCount"

            let questions = root.GetProperty("questions")
            Expect.equal (questions.GetArrayLength()) 1 "Should have 1 question"
            Expect.equal (questions.[0].GetProperty("id").GetString()) "unmapped-type-Foo" "question id"
            Expect.equal (questions.[0].GetProperty("category").GetString()) "unmapped-type" "category"

            let options = questions.[0].GetProperty("options")
            Expect.equal (options.GetArrayLength()) 2 "Should have 2 options"

        testCase "formatError produces valid JSON error object" <| fun _ ->
            let json = formatError "something went wrong"
            let doc = JsonDocument.Parse(json)
            let root = doc.RootElement

            Expect.equal (root.GetProperty("status").GetString()) "error" "status should be error"
            Expect.equal (root.GetProperty("message").GetString()) "something went wrong" "message"

        testCase "formatExtractResult round-trips key fields" <| fun _ ->
            let original : ExtractResult =
                { OntologySummary =
                    { ClassCount = 3
                      PropertyCount = 7
                      AlignedCount = 2
                      UnalignedCount = 5 }
                  ShapesSummary =
                    { ShapeCount = 3
                      ConstraintCount = 8 }
                  UnmappedTypes = []
                  StateFilePath = "/some/path" }

            let json = formatExtractResult original
            let doc = JsonDocument.Parse(json)
            let root = doc.RootElement

            let ontSummary = root.GetProperty("ontologySummary")
            Expect.equal (ontSummary.GetProperty("classCount").GetInt32()) original.OntologySummary.ClassCount "ClassCount"
            Expect.equal (ontSummary.GetProperty("propertyCount").GetInt32()) original.OntologySummary.PropertyCount "PropertyCount"

            let shapesSummary = root.GetProperty("shapesSummary")
            Expect.equal (shapesSummary.GetProperty("shapeCount").GetInt32()) original.ShapesSummary.ShapeCount "ShapeCount"

            Expect.equal (root.GetProperty("stateFilePath").GetString()) original.StateFilePath "StateFilePath"

        // -----------------------------------------------------------------------
        // formatStatechartExtractResult
        // -----------------------------------------------------------------------

        testCase "formatStatechartExtractResult serializes states and guards arrays" <| fun _ ->
            let sm: ExtractedStatechart =
                { RouteTemplate = "/games/{gameId}"
                  StateNames = [ "XTurn"; "OTurn"; "GameOver" ]
                  InitialStateKey = "XTurn"
                  GuardNames = [ "isValidMove" ]
                  StateMetadata =
                    Map.ofList
                        [ "XTurn",
                          { AllowedMethods = [ "GET"; "POST" ]
                            IsFinal = false
                            Description = Some "X player's turn" }
                          "GameOver",
                          { AllowedMethods = [ "GET" ]
                            IsFinal = true
                            Description = None } ]
                  Roles = []
                  Transitions = [] }

            let result: Frank.Cli.Core.Commands.StatechartExtractCommand.ExtractResult =
                { StateMachines = [ sm ] }

            let json = formatStatechartExtractResult result
            let doc = JsonDocument.Parse(json)
            let root = doc.RootElement

            Expect.equal (root.GetProperty("status").GetString()) "ok" "status should be ok"

            let machines = root.GetProperty("stateMachines")
            Expect.equal (machines.GetArrayLength()) 1 "one state machine"

            let m = machines.[0]
            Expect.equal (m.GetProperty("routeTemplate").GetString()) "/games/{gameId}" "routeTemplate"
            Expect.equal (m.GetProperty("initialState").GetString()) "XTurn" "initialState"

            let states = m.GetProperty("states")
            Expect.equal (states.GetArrayLength()) 3 "three states"
            Expect.equal (states.[0].GetString()) "XTurn" "first state"
            Expect.equal (states.[1].GetString()) "OTurn" "second state"
            Expect.equal (states.[2].GetString()) "GameOver" "third state"

            let guards = m.GetProperty("guards")
            Expect.equal (guards.GetArrayLength()) 1 "one guard"
            Expect.equal (guards.[0].GetString()) "isValidMove" "guard name"

        testCase "formatStatechartExtractResult serializes stateMetadata with allowedMethods and isFinal" <| fun _ ->
            let sm: ExtractedStatechart =
                { RouteTemplate = "/orders/{id}"
                  StateNames = [ "Pending"; "Shipped" ]
                  InitialStateKey = "Pending"
                  GuardNames = []
                  StateMetadata =
                    Map.ofList
                        [ "Pending",
                          { AllowedMethods = [ "GET"; "PUT"; "DELETE" ]
                            IsFinal = false
                            Description = Some "Awaiting shipment" }
                          "Shipped",
                          { AllowedMethods = [ "GET" ]
                            IsFinal = true
                            Description = None } ]
                  Roles = []
                  Transitions = [] }

            let result: Frank.Cli.Core.Commands.StatechartExtractCommand.ExtractResult =
                { StateMachines = [ sm ] }

            let json = formatStatechartExtractResult result
            let doc = JsonDocument.Parse(json)
            let m = doc.RootElement.GetProperty("stateMachines").[0]
            let meta = m.GetProperty("stateMetadata")

            let pending = meta.GetProperty("Pending")
            let methods = pending.GetProperty("allowedMethods")
            Expect.equal (methods.GetArrayLength()) 3 "Pending has 3 allowed methods"
            Expect.equal (methods.[0].GetString()) "GET" "first method"
            Expect.equal (methods.[1].GetString()) "PUT" "second method"
            Expect.equal (methods.[2].GetString()) "DELETE" "third method"
            Expect.isFalse (pending.GetProperty("isFinal").GetBoolean()) "Pending is not final"

            let shipped = meta.GetProperty("Shipped")
            Expect.isTrue (shipped.GetProperty("isFinal").GetBoolean()) "Shipped is final"
            let shippedMethods = shipped.GetProperty("allowedMethods")
            Expect.equal (shippedMethods.GetArrayLength()) 1 "Shipped has 1 allowed method"

        testCase "formatStatechartExtractResult writes description string when Some" <| fun _ ->
            let sm: ExtractedStatechart =
                { RouteTemplate = "/items/{id}"
                  StateNames = [ "Active" ]
                  InitialStateKey = "Active"
                  GuardNames = []
                  StateMetadata =
                    Map.ofList
                        [ "Active",
                          { AllowedMethods = [ "GET" ]
                            IsFinal = false
                            Description = Some "Item is active" } ]
                  Roles = []
                  Transitions = [] }

            let result: Frank.Cli.Core.Commands.StatechartExtractCommand.ExtractResult =
                { StateMachines = [ sm ] }

            let json = formatStatechartExtractResult result
            let doc = JsonDocument.Parse(json)
            let meta =
                doc.RootElement.GetProperty("stateMachines").[0].GetProperty("stateMetadata")

            let active = meta.GetProperty("Active")
            Expect.equal (active.GetProperty("description").GetString()) "Item is active" "description string"

        testCase "formatStatechartExtractResult writes null description when None" <| fun _ ->
            let sm: ExtractedStatechart =
                { RouteTemplate = "/items/{id}"
                  StateNames = [ "Terminal" ]
                  InitialStateKey = "Terminal"
                  GuardNames = []
                  StateMetadata =
                    Map.ofList
                        [ "Terminal",
                          { AllowedMethods = []
                            IsFinal = true
                            Description = None } ]
                  Roles = []
                  Transitions = [] }

            let result: Frank.Cli.Core.Commands.StatechartExtractCommand.ExtractResult =
                { StateMachines = [ sm ] }

            let json = formatStatechartExtractResult result
            let doc = JsonDocument.Parse(json)
            let meta =
                doc.RootElement.GetProperty("stateMachines").[0].GetProperty("stateMetadata")

            let terminal = meta.GetProperty("Terminal")
            Expect.equal (terminal.GetProperty("description").ValueKind) JsonValueKind.Null "description should be null"

        testCase "formatStatechartExtractResult handles empty state machine list" <| fun _ ->
            let result: Frank.Cli.Core.Commands.StatechartExtractCommand.ExtractResult =
                { StateMachines = [] }

            let json = formatStatechartExtractResult result
            let doc = JsonDocument.Parse(json)
            let root = doc.RootElement

            Expect.equal (root.GetProperty("status").GetString()) "ok" "status ok"
            Expect.equal (root.GetProperty("stateMachines").GetArrayLength()) 0 "empty machines array"

        // -----------------------------------------------------------------------
        // formatResourceExtractResult — roles array
        // -----------------------------------------------------------------------

        testCase "formatResourceExtractResult serializes roles with name and Some description" <| fun _ ->
            let sc: ExtractedStatechart =
                { RouteTemplate = "/games/{gameId}"
                  StateNames = [ "XTurn" ]
                  InitialStateKey = "XTurn"
                  GuardNames = []
                  StateMetadata =
                    Map.ofList
                        [ "XTurn",
                          { AllowedMethods = [ "GET" ]
                            IsFinal = false
                            Description = None } ]
                  Roles =
                    [ { Name = "PlayerX"; Description = Some "The X player" }
                      { Name = "PlayerO"; Description = Some "The O player" } ]
                  Transitions = [] }

            let resource = minimalResource "/games/{gameId}" (Some sc)

            let result: Frank.Cli.Core.Commands.ExtractResourcesCommand.ExtractResult =
                { ResourceCount = 1
                  StatefulResourceCount = 1
                  PlainResourceCount = 0
                  TypeCount = 0
                  Warnings = []
                  CacheFilePath = "/tmp/cache.bin"
                  FromCache = false
                  State = minimalExtractionState [ resource ] }

            let json = formatResourceExtractResult result
            let doc = JsonDocument.Parse(json)
            let sc_json =
                doc.RootElement.GetProperty("resources").[0].GetProperty("statechart")

            let roles = sc_json.GetProperty("roles")
            Expect.equal (roles.GetArrayLength()) 2 "two roles"
            Expect.equal (roles.[0].GetProperty("name").GetString()) "PlayerX" "first role name"
            Expect.equal (roles.[0].GetProperty("description").GetString()) "The X player" "first role description"
            Expect.equal (roles.[1].GetProperty("name").GetString()) "PlayerO" "second role name"
            Expect.equal (roles.[1].GetProperty("description").GetString()) "The O player" "second role description"

        testCase "formatResourceExtractResult omits description field when None on role" <| fun _ ->
            let sc: ExtractedStatechart =
                { RouteTemplate = "/items/{id}"
                  StateNames = [ "Active" ]
                  InitialStateKey = "Active"
                  GuardNames = []
                  StateMetadata =
                    Map.ofList
                        [ "Active",
                          { AllowedMethods = [ "GET" ]
                            IsFinal = false
                            Description = None } ]
                  Roles = [ { Name = "Viewer"; Description = None } ]
                  Transitions = [] }

            let resource = minimalResource "/items/{id}" (Some sc)

            let result: Frank.Cli.Core.Commands.ExtractResourcesCommand.ExtractResult =
                { ResourceCount = 1
                  StatefulResourceCount = 1
                  PlainResourceCount = 0
                  TypeCount = 0
                  Warnings = []
                  CacheFilePath = "/tmp/cache.bin"
                  FromCache = false
                  State = minimalExtractionState [ resource ] }

            let json = formatResourceExtractResult result
            let doc = JsonDocument.Parse(json)
            let roles =
                doc.RootElement.GetProperty("resources").[0]
                    .GetProperty("statechart")
                    .GetProperty("roles")

            Expect.equal (roles.GetArrayLength()) 1 "one role"
            Expect.equal (roles.[0].GetProperty("name").GetString()) "Viewer" "role name"
            let mutable descProp = Unchecked.defaultof<JsonElement>
            let hasDesc = roles.[0].TryGetProperty("description", &descProp)
            Expect.isFalse hasDesc "description field should be absent when None"

        // -----------------------------------------------------------------------
        // formatResourceExtractResult — transitions array
        // -----------------------------------------------------------------------

        testCase "formatResourceExtractResult serializes transitions with event, source, target" <| fun _ ->
            let sc: ExtractedStatechart =
                { RouteTemplate = "/games/{gameId}"
                  StateNames = [ "XTurn"; "OTurn" ]
                  InitialStateKey = "XTurn"
                  GuardNames = []
                  StateMetadata =
                    Map.ofList
                        [ "XTurn",
                          { AllowedMethods = [ "POST" ]
                            IsFinal = false
                            Description = None }
                          "OTurn",
                          { AllowedMethods = [ "POST" ]
                            IsFinal = false
                            Description = None } ]
                  Roles = []
                  Transitions =
                    [ { Event = "makeMove"
                        Source = "XTurn"
                        Target = "OTurn"
                        Guard = None
                        Constraint = Unrestricted
                        Safety = Unsafe
                        SenderRole = None
                        ReceiverRole = None } ] }

            let resource = minimalResource "/games/{gameId}" (Some sc)

            let result: Frank.Cli.Core.Commands.ExtractResourcesCommand.ExtractResult =
                { ResourceCount = 1
                  StatefulResourceCount = 1
                  PlainResourceCount = 0
                  TypeCount = 0
                  Warnings = []
                  CacheFilePath = "/tmp/cache.bin"
                  FromCache = false
                  State = minimalExtractionState [ resource ] }

            let json = formatResourceExtractResult result
            let doc = JsonDocument.Parse(json)
            let transitions =
                doc.RootElement.GetProperty("resources").[0]
                    .GetProperty("statechart")
                    .GetProperty("transitions")

            Expect.equal (transitions.GetArrayLength()) 1 "one transition"
            let t = transitions.[0]
            Expect.equal (t.GetProperty("event").GetString()) "makeMove" "event"
            Expect.equal (t.GetProperty("source").GetString()) "XTurn" "source"
            Expect.equal (t.GetProperty("target").GetString()) "OTurn" "target"

        testCase "formatResourceExtractResult omits guard field when None" <| fun _ ->
            let sc: ExtractedStatechart =
                { RouteTemplate = "/orders/{id}"
                  StateNames = [ "Open"; "Closed" ]
                  InitialStateKey = "Open"
                  GuardNames = []
                  StateMetadata =
                    Map.ofList
                        [ "Open",
                          { AllowedMethods = [ "POST" ]
                            IsFinal = false
                            Description = None }
                          "Closed",
                          { AllowedMethods = [ "GET" ]
                            IsFinal = true
                            Description = None } ]
                  Roles = []
                  Transitions =
                    [ { Event = "close"
                        Source = "Open"
                        Target = "Closed"
                        Guard = None
                        Constraint = Unrestricted
                        Safety = Unsafe
                        SenderRole = None
                        ReceiverRole = None } ] }

            let resource = minimalResource "/orders/{id}" (Some sc)

            let result: Frank.Cli.Core.Commands.ExtractResourcesCommand.ExtractResult =
                { ResourceCount = 1
                  StatefulResourceCount = 1
                  PlainResourceCount = 0
                  TypeCount = 0
                  Warnings = []
                  CacheFilePath = "/tmp/cache.bin"
                  FromCache = false
                  State = minimalExtractionState [ resource ] }

            let json = formatResourceExtractResult result
            let doc = JsonDocument.Parse(json)
            let t =
                doc.RootElement.GetProperty("resources").[0]
                    .GetProperty("statechart")
                    .GetProperty("transitions").[0]

            let mutable guardProp = Unchecked.defaultof<JsonElement>
            let hasGuard = t.TryGetProperty("guard", &guardProp)
            Expect.isFalse hasGuard "guard field should be absent when None"

        testCase "formatResourceExtractResult writes guard string when Some" <| fun _ ->
            let sc: ExtractedStatechart =
                { RouteTemplate = "/games/{gameId}"
                  StateNames = [ "XTurn"; "OTurn" ]
                  InitialStateKey = "XTurn"
                  GuardNames = [ "isValidMove" ]
                  StateMetadata =
                    Map.ofList
                        [ "XTurn",
                          { AllowedMethods = [ "POST" ]
                            IsFinal = false
                            Description = None }
                          "OTurn",
                          { AllowedMethods = [ "POST" ]
                            IsFinal = false
                            Description = None } ]
                  Roles = []
                  Transitions =
                    [ { Event = "makeMove"
                        Source = "XTurn"
                        Target = "OTurn"
                        Guard = Some "isValidMove"
                        Constraint = Unrestricted
                        Safety = Unsafe
                        SenderRole = None
                        ReceiverRole = None } ] }

            let resource = minimalResource "/games/{gameId}" (Some sc)

            let result: Frank.Cli.Core.Commands.ExtractResourcesCommand.ExtractResult =
                { ResourceCount = 1
                  StatefulResourceCount = 1
                  PlainResourceCount = 0
                  TypeCount = 0
                  Warnings = []
                  CacheFilePath = "/tmp/cache.bin"
                  FromCache = false
                  State = minimalExtractionState [ resource ] }

            let json = formatResourceExtractResult result
            let doc = JsonDocument.Parse(json)
            let t =
                doc.RootElement.GetProperty("resources").[0]
                    .GetProperty("statechart")
                    .GetProperty("transitions").[0]

            Expect.equal (t.GetProperty("guard").GetString()) "isValidMove" "guard name"

        testCase "formatResourceExtractResult writes constraint unrestricted string" <| fun _ ->
            let sc: ExtractedStatechart =
                { RouteTemplate = "/items/{id}"
                  StateNames = [ "Active"; "Archived" ]
                  InitialStateKey = "Active"
                  GuardNames = []
                  StateMetadata =
                    Map.ofList
                        [ "Active",
                          { AllowedMethods = [ "POST" ]
                            IsFinal = false
                            Description = None }
                          "Archived",
                          { AllowedMethods = [ "GET" ]
                            IsFinal = true
                            Description = None } ]
                  Roles = []
                  Transitions =
                    [ { Event = "archive"
                        Source = "Active"
                        Target = "Archived"
                        Guard = None
                        Constraint = Unrestricted
                        Safety = Unsafe
                        SenderRole = None
                        ReceiverRole = None } ] }

            let resource = minimalResource "/items/{id}" (Some sc)

            let result: Frank.Cli.Core.Commands.ExtractResourcesCommand.ExtractResult =
                { ResourceCount = 1
                  StatefulResourceCount = 1
                  PlainResourceCount = 0
                  TypeCount = 0
                  Warnings = []
                  CacheFilePath = "/tmp/cache.bin"
                  FromCache = false
                  State = minimalExtractionState [ resource ] }

            let json = formatResourceExtractResult result
            let doc = JsonDocument.Parse(json)
            let t =
                doc.RootElement.GetProperty("resources").[0]
                    .GetProperty("statechart")
                    .GetProperty("transitions").[0]

            Expect.equal (t.GetProperty("constraint").GetString()) "unrestricted" "Unrestricted writes string"

        testCase "formatResourceExtractResult writes restrictedTo array for RestrictedTo constraint" <| fun _ ->
            let sc: ExtractedStatechart =
                { RouteTemplate = "/games/{gameId}"
                  StateNames = [ "XTurn"; "OTurn" ]
                  InitialStateKey = "XTurn"
                  GuardNames = []
                  StateMetadata =
                    Map.ofList
                        [ "XTurn",
                          { AllowedMethods = [ "POST" ]
                            IsFinal = false
                            Description = None }
                          "OTurn",
                          { AllowedMethods = [ "POST" ]
                            IsFinal = false
                            Description = None } ]
                  Roles =
                    [ { Name = "PlayerX"; Description = None }
                      { Name = "PlayerO"; Description = None } ]
                  Transitions =
                    [ { Event = "makeMove"
                        Source = "XTurn"
                        Target = "OTurn"
                        Guard = None
                        Constraint = RestrictedTo [ "PlayerX" ]
                        Safety = Unsafe
                        SenderRole = None
                        ReceiverRole = None } ] }

            let resource = minimalResource "/games/{gameId}" (Some sc)

            let result: Frank.Cli.Core.Commands.ExtractResourcesCommand.ExtractResult =
                { ResourceCount = 1
                  StatefulResourceCount = 1
                  PlainResourceCount = 0
                  TypeCount = 0
                  Warnings = []
                  CacheFilePath = "/tmp/cache.bin"
                  FromCache = false
                  State = minimalExtractionState [ resource ] }

            let json = formatResourceExtractResult result
            let doc = JsonDocument.Parse(json)
            let t =
                doc.RootElement.GetProperty("resources").[0]
                    .GetProperty("statechart")
                    .GetProperty("transitions").[0]

            let mutable constraintProp = Unchecked.defaultof<JsonElement>
            let hasConstraint = t.TryGetProperty("constraint", &constraintProp)
            Expect.isFalse hasConstraint "constraint field absent for RestrictedTo"

            let restricted = t.GetProperty("restrictedTo")
            Expect.equal (restricted.GetArrayLength()) 1 "one restricted role"
            Expect.equal (restricted.[0].GetString()) "PlayerX" "role name in restrictedTo"

        testCase "formatResourceExtractResult writes null statechart when resource has none" <| fun _ ->
            let resource = minimalResource "/plain" None

            let result: Frank.Cli.Core.Commands.ExtractResourcesCommand.ExtractResult =
                { ResourceCount = 1
                  StatefulResourceCount = 0
                  PlainResourceCount = 1
                  TypeCount = 0
                  Warnings = []
                  CacheFilePath = "/tmp/cache.bin"
                  FromCache = false
                  State = minimalExtractionState [ resource ] }

            let json = formatResourceExtractResult result
            let doc = JsonDocument.Parse(json)
            let r = doc.RootElement.GetProperty("resources").[0]

            Expect.equal (r.GetProperty("statechart").ValueKind) JsonValueKind.Null "statechart is null"

        testCase "formatResourceExtractResult serializes multiple transitions" <| fun _ ->
            let sc: ExtractedStatechart =
                { RouteTemplate = "/games/{gameId}"
                  StateNames = [ "XTurn"; "OTurn"; "GameOver" ]
                  InitialStateKey = "XTurn"
                  GuardNames = [ "isValidMove" ]
                  StateMetadata =
                    Map.ofList
                        [ "XTurn",
                          { AllowedMethods = [ "GET"; "POST" ]
                            IsFinal = false
                            Description = None }
                          "OTurn",
                          { AllowedMethods = [ "GET"; "POST" ]
                            IsFinal = false
                            Description = None }
                          "GameOver",
                          { AllowedMethods = [ "GET" ]
                            IsFinal = true
                            Description = None } ]
                  Roles =
                    [ { Name = "PlayerX"; Description = None }
                      { Name = "PlayerO"; Description = None } ]
                  Transitions =
                    [ { Event = "makeMove"
                        Source = "XTurn"
                        Target = "OTurn"
                        Guard = Some "isValidMove"
                        Constraint = RestrictedTo [ "PlayerX" ]
                        Safety = Unsafe
                        SenderRole = None
                        ReceiverRole = None }
                      { Event = "makeMove"
                        Source = "OTurn"
                        Target = "XTurn"
                        Guard = Some "isValidMove"
                        Constraint = RestrictedTo [ "PlayerO" ]
                        Safety = Unsafe
                        SenderRole = None
                        ReceiverRole = None }
                      { Event = "getGame"
                        Source = "XTurn"
                        Target = "XTurn"
                        Guard = None
                        Constraint = Unrestricted
                        Safety = Safe
                        SenderRole = None
                        ReceiverRole = None } ] }

            let resource = minimalResource "/games/{gameId}" (Some sc)

            let result: Frank.Cli.Core.Commands.ExtractResourcesCommand.ExtractResult =
                { ResourceCount = 1
                  StatefulResourceCount = 1
                  PlainResourceCount = 0
                  TypeCount = 0
                  Warnings = []
                  CacheFilePath = "/tmp/cache.bin"
                  FromCache = false
                  State = minimalExtractionState [ resource ] }

            let json = formatResourceExtractResult result
            let doc = JsonDocument.Parse(json)
            let scNode =
                doc.RootElement.GetProperty("resources").[0].GetProperty("statechart")

            let transitions = scNode.GetProperty("transitions")
            Expect.equal (transitions.GetArrayLength()) 3 "three transitions"

            // First transition: RestrictedTo, guard present
            let t0 = transitions.[0]
            Expect.equal (t0.GetProperty("event").GetString()) "makeMove" "t0 event"
            Expect.equal (t0.GetProperty("source").GetString()) "XTurn" "t0 source"
            Expect.equal (t0.GetProperty("target").GetString()) "OTurn" "t0 target"
            Expect.equal (t0.GetProperty("guard").GetString()) "isValidMove" "t0 guard"
            let t0Restricted = t0.GetProperty("restrictedTo")
            Expect.equal (t0Restricted.[0].GetString()) "PlayerX" "t0 restrictedTo"

            // Third transition: Unrestricted, no guard
            let t2 = transitions.[2]
            Expect.equal (t2.GetProperty("event").GetString()) "getGame" "t2 event"
            Expect.equal (t2.GetProperty("constraint").GetString()) "unrestricted" "t2 constraint"
            let mutable g = Unchecked.defaultof<JsonElement>
            Expect.isFalse (t2.TryGetProperty("guard", &g)) "t2 has no guard field"
    ]
