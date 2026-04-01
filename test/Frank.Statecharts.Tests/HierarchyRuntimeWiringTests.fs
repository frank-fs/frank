module HierarchyRuntimeWiringTests

/// Tests for issue #242: Hierarchy runtime wiring — CE operation, middleware dispatch, parser bridge.
///
/// TDD red phase: all tests in this file must fail before implementation is written.
/// Acceptance criteria:
///   1. useHierarchyWith CE operation sets StateMachineMetadata.Hierarchy
///   2. Middleware uses HierarchicalRuntime.resolveHandlers when Hierarchy = Some _
///   3. Middleware uses HierarchicalRuntime.resolveAllowedMethods for Allow header
///   4. HierarchyBridge.fromDocument converts StatechartDocument to HierarchySpec
///   5. stateMetadataMap uses StateKeyExtractor.keyOf (not string cast) for parameterized DUs
///   6. Deep history enterWithHistory uses enterState (not Set.fold) for XOR enforcement

open System.Net
open System.Net.Http
open System.Threading.Tasks
open Expecto
open Frank.Builder
open Frank.Statecharts
open Frank.Statecharts.Ast
open Microsoft.AspNetCore.Builder
open Microsoft.AspNetCore.Http
open Microsoft.AspNetCore.Routing
open Microsoft.AspNetCore.TestHost
open Microsoft.Extensions.DependencyInjection
open Microsoft.Extensions.Hosting
open Frank.Tests.Shared.TestEndpointDataSource

// ===========================================================================
// Test domain: Document workflow (flat states that map to hierarchy IDs)
// ===========================================================================

type DocState =
    | Editing
    | Reviewing
    | Published
    | Archived

type DocEvent =
    | Submit
    | Approve
    | Reject
    | Archive

let docTransition (state: DocState) (event: DocEvent) (_ctx: unit) =
    match state, event with
    | Editing, Submit -> TransitionResult.Transitioned(Reviewing, ())
    | Reviewing, Approve -> TransitionResult.Transitioned(Published, ())
    | Reviewing, Reject -> TransitionResult.Transitioned(Editing, ())
    | Published, Archive -> TransitionResult.Transitioned(Archived, ())
    | _ -> TransitionResult.Invalid "Invalid transition"

let docMachine: StateMachine<DocState, DocEvent, unit> =
    { Initial = Editing
      InitialContext = ()
      Transition = docTransition
      Guards = []
      StateMetadata = Map.empty }

// A minimal HierarchySpec used for CE tests
let sampleHierarchySpec: HierarchySpec =
    { States =
        [ { Id = "Root"
            Kind = CompositeKind.XOR
            Children = [ "Draft"; "Published" ]
            InitialChild = Some "Draft"
            CompletionTarget = None } ] }

// ===========================================================================
// Test domain: parameterized DU for stateMetadataMap key mismatch regression
// ===========================================================================

type GameState =
    | Playing
    | Won of winner: string
    | Draw

type GameEvent = Move

let gameMachineWithMetadata: StateMachine<GameState, GameEvent, unit> =
    { Initial = Playing
      InitialContext = ()
      Transition =
        (fun state event _ctx ->
            match state, event with
            | Playing, Move -> TransitionResult.Transitioned(Won "X", ())
            | _ -> TransitionResult.Invalid "done")
      Guards = []
      StateMetadata =
        Map.ofList
            [ (Playing,
               { IsFinal = false
                 AllowedMethods = []
                 Description = None })
              (Won "X",
               { IsFinal = true
                 AllowedMethods = []
                 Description = None }) ] }

// ===========================================================================
// Helper: build a minimal test server for stateful resources (no user injection)
// ===========================================================================

let buildStatefulServer (resource: Resource) (configServices: IServiceCollection -> unit) =
    let builder = WebApplication.CreateBuilder([||])
    builder.WebHost.UseTestServer() |> ignore
    builder.Services.AddRouting() |> ignore
    builder.Services.AddLogging() |> ignore
    configServices builder.Services
    let app = builder.Build()
    app.UseRouting() |> ignore
    (app :> IApplicationBuilder).UseMiddleware<StateMachineMiddleware>() |> ignore

    app.UseEndpoints(fun endpoints -> endpoints.DataSources.Add(TestEndpointDataSource(resource.Endpoints)))
    |> ignore

    app.Start()
    app.GetTestServer()

let addDocStore (services: IServiceCollection) =
    services.AddStatechartsStore<DocState, unit>() |> ignore

let addGameStore (services: IServiceCollection) =
    services.AddStatechartsStore<GameState, unit>() |> ignore

let withDocServer (resource: Resource) (f: HttpClient -> Task) =
    task {
        let server = buildStatefulServer resource addDocStore
        let client = server.CreateClient()

        try
            do! f client
        finally
            client.Dispose()
            server.Dispose()
    }
    :> Task

/// Retrieve StateMachineMetadata from a Resource's endpoints.
let getMetadata (resource: Resource) : StateMachineMetadata option =
    resource.Endpoints
    |> Array.tryPick (fun ep ->
        let m = ep.Metadata.GetMetadata<StateMachineMetadata>()
        if obj.ReferenceEquals(m, null) then None else Some m)

// ===========================================================================
// 1. CE operation: useHierarchyWith sets Hierarchy = Some _ on metadata
// ===========================================================================

[<Tests>]
let ceOperationTests =
    testList
        "useHierarchyWith CE operation"
        [ testCase "useHierarchyWith sets Hierarchy on StateMachineMetadata"
          <| fun () ->
              let res =
                  statefulResource "/docs/{id}" {
                      machine docMachine
                      useHierarchyWith sampleHierarchySpec

                      inState (
                          forState Editing [ StateHandlerBuilder.get (fun ctx -> ctx.Response.WriteAsync("editing")) ]
                      )
                  }

              let meta = getMetadata res
              Expect.isSome meta "StateMachineMetadata should be present"
              // Hierarchy is always built; verify it reflects the provided spec (Draft is a child of Root)
              let h = meta.Value.Hierarchy
              Expect.isTrue (Map.containsKey "Draft" h.ParentMap) "Hierarchy should reflect the sampleHierarchySpec"

          testCase "Without useHierarchyWith, Hierarchy is auto-wrapped with __root__"
          <| fun () ->
              let res =
                  statefulResource "/docs/{id}" {
                      machine docMachine

                      inState (
                          forState Editing [ StateHandlerBuilder.get (fun ctx -> ctx.Response.WriteAsync("editing")) ]
                      )
                  }

              let meta = getMetadata res
              Expect.isSome meta "StateMachineMetadata should be present"
              // Without useHierarchyWith, flat FSM is auto-wrapped in synthetic __root__ XOR composite
              let h = meta.Value.Hierarchy
              Expect.isTrue (Map.containsKey "__root__" h.ChildrenMap) "Auto-wrapped hierarchy should have __root__"

          testCase "useHierarchyWith wires the correct HierarchySpec"
          <| fun () ->
              let spec: HierarchySpec =
                  { States =
                      [ { Id = "Parent"
                          Kind = CompositeKind.XOR
                          Children = [ "Child1"; "Child2" ]
                          InitialChild = Some "Child1"
                          CompletionTarget = None } ] }

              let res =
                  statefulResource "/docs/{id}" {
                      machine docMachine
                      useHierarchyWith spec

                      inState (
                          forState Editing [ StateHandlerBuilder.get (fun ctx -> ctx.Response.WriteAsync("editing")) ]
                      )
                  }

              let meta = getMetadata res |> Option.get
              let hierarchy = meta.Hierarchy
              // Verify the hierarchy has been built (ParentMap should contain Child1 -> Parent)
              Expect.equal
                  (Map.tryFind "Child1" hierarchy.ParentMap)
                  (Some "Parent")
                  "Child1 should have Parent in hierarchy" ]

// ===========================================================================
// 2. Hierarchical resolution (pure unit tests — no HTTP server needed)
//    These verify the algorithms that middleware will call.
// ===========================================================================

[<Tests>]
let hierarchicalResolutionTests =
    testList
        "HierarchicalRuntime resolution"
        [ testCase "resolveHandlers finds parent handler when child has no handler for that method"
          <| fun () ->
              let spec: HierarchySpec =
                  { States =
                      [ { Id = "Active"
                          Kind = CompositeKind.XOR
                          Children = [ "Red" ]
                          InitialChild = Some "Red"
                          CompletionTarget = None } ] }

              let hierarchy = StateHierarchy.build spec

              let handlerMap: Map<string, (string * RequestDelegate) list> =
                  Map.ofList
                      [ ("Active", [ ("GET", RequestDelegate(fun ctx -> ctx.Response.WriteAsync("active-get"))) ])
                        ("Red", [ ("POST", RequestDelegate(fun ctx -> ctx.Response.WriteAsync("red-post"))) ]) ]

              let config = ActiveStateConfiguration.empty |> ActiveStateConfiguration.add "Red"

              let resolved = HierarchicalRuntime.resolveHandlers hierarchy handlerMap config
              let methodNames = resolved |> List.map fst |> Set.ofList

              Expect.contains methodNames "GET" "GET should be resolved from parent Active"
              Expect.contains methodNames "POST" "POST should be resolved from Red"

          testCase "resolveHandlers child overrides parent for same method"
          <| fun () ->
              let spec: HierarchySpec =
                  { States =
                      [ { Id = "Active"
                          Kind = CompositeKind.XOR
                          Children = [ "Red" ]
                          InitialChild = Some "Red"
                          CompletionTarget = None } ] }

              let hierarchy = StateHierarchy.build spec

              let parentHandler =
                  RequestDelegate(fun ctx -> ctx.Response.WriteAsync("parent-get"))

              let childHandler = RequestDelegate(fun ctx -> ctx.Response.WriteAsync("child-get"))

              let handlerMap: Map<string, (string * RequestDelegate) list> =
                  Map.ofList [ ("Active", [ ("GET", parentHandler) ]); ("Red", [ ("GET", childHandler) ]) ]

              let config = ActiveStateConfiguration.empty |> ActiveStateConfiguration.add "Red"

              let resolved = HierarchicalRuntime.resolveHandlers hierarchy handlerMap config
              // Child should win for GET
              let getHandler = resolved |> List.tryFind (fun (m, _) -> m = "GET")
              Expect.isSome getHandler "GET should be present"
              // The handler should be the child's (reference equality)
              Expect.isTrue
                  (obj.ReferenceEquals(snd getHandler.Value, childHandler))
                  "Child GET handler should override parent"

          testCase "resolveAllowedMethods returns union of active state and ancestor methods"
          <| fun () ->
              let spec: HierarchySpec =
                  { States =
                      [ { Id = "Active"
                          Kind = CompositeKind.XOR
                          Children = [ "Red" ]
                          InitialChild = Some "Red"
                          CompletionTarget = None } ] }

              let hierarchy = StateHierarchy.build spec

              let methodMap: Map<string, string list> =
                  Map.ofList [ ("Active", [ "GET"; "OPTIONS" ]); ("Red", [ "POST" ]) ]

              let config = ActiveStateConfiguration.empty |> ActiveStateConfiguration.add "Red"

              let allowed = HierarchicalRuntime.resolveAllowedMethods hierarchy methodMap config

              Expect.contains allowed "GET" "GET from Active (ancestor)"
              Expect.contains allowed "POST" "POST from Red (active)"
              Expect.contains allowed "OPTIONS" "OPTIONS from Active (ancestor)" ]

// ===========================================================================
// 3. Integration: middleware uses hierarchical dispatch when Hierarchy = Some _
// (HTTP-level test)
// ===========================================================================

[<Tests>]
let middlewareIntegrationTests =
    testList
        "Middleware integration with hierarchy"
        [ testCase "Returns 405 for method not in state or ancestors when hierarchy configured"
          <| fun () ->
              let res =
                  statefulResource "/docs/{id}" {
                      machine docMachine
                      useHierarchyWith sampleHierarchySpec

                      inState (
                          forState Editing [ StateHandlerBuilder.get (fun ctx -> ctx.Response.WriteAsync("editing")) ]
                      )
                  }

              (withDocServer res (fun client ->
                  task {
                      let! (response: HttpResponseMessage) = client.DeleteAsync("/docs/1")
                      Expect.equal response.StatusCode HttpStatusCode.MethodNotAllowed "DELETE should be 405"
                  }))
                  .GetAwaiter()
                  .GetResult() ]

// ===========================================================================
// 4. Parser bridge: HierarchyBridge.fromDocument
// ===========================================================================

[<Tests>]
let hierarchyBridgeTests =
    testList
        "HierarchyBridge.fromDocument"
        [ testCase "Atomic document (no children) returns empty HierarchySpec"
          <| fun () ->
              let doc: StatechartDocument =
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
                              Annotations = [] }
                        StateDecl
                            { Identifier = Some "B"
                              Label = None
                              Kind = StateKind.Regular
                              Children = []
                              Activities = None
                              Position = None
                              Annotations = [] } ]
                    DataEntries = []
                    Annotations = [] }

              let spec = HierarchyBridge.fromDocument doc
              Expect.isEmpty spec.States "No composite states for flat document"

          testCase "Single composite parent with children produces one CompositeStateSpec"
          <| fun () ->
              let doc: StatechartDocument =
                  { Title = None
                    InitialStateId = None
                    Elements =
                      [ StateDecl
                            { Identifier = Some "Active"
                              Label = None
                              Kind = StateKind.Composite
                              Children =
                                [ { Identifier = Some "Red"
                                    Label = None
                                    Kind = StateKind.Initial
                                    Children = []
                                    Activities = None
                                    Position = None
                                    Annotations = [] }
                                  { Identifier = Some "Green"
                                    Label = None
                                    Kind = StateKind.Regular
                                    Children = []
                                    Activities = None
                                    Position = None
                                    Annotations = [] } ]
                              Activities = None
                              Position = None
                              Annotations = [] } ]
                    DataEntries = []
                    Annotations = [] }

              let spec = HierarchyBridge.fromDocument doc
              Expect.equal (List.length spec.States) 1 "One composite state"
              let cs = spec.States.[0]
              Expect.equal cs.Id "Active" "Id is Active"
              Expect.equal cs.Kind CompositeKind.XOR "Composite maps to XOR"
              Expect.contains cs.Children "Red" "Red is a child"
              Expect.contains cs.Children "Green" "Green is a child"
              Expect.equal cs.InitialChild (Some "Red") "Red is initial (Kind = Initial)"

          testCase "Parallel state maps to AND CompositeKind"
          <| fun () ->
              let doc: StatechartDocument =
                  { Title = None
                    InitialStateId = None
                    Elements =
                      [ StateDecl
                            { Identifier = Some "Par"
                              Label = None
                              Kind = StateKind.Parallel
                              Children =
                                [ { Identifier = Some "Region1"
                                    Label = None
                                    Kind = StateKind.Regular
                                    Children = []
                                    Activities = None
                                    Position = None
                                    Annotations = [] }
                                  { Identifier = Some "Region2"
                                    Label = None
                                    Kind = StateKind.Regular
                                    Children = []
                                    Activities = None
                                    Position = None
                                    Annotations = [] } ]
                              Activities = None
                              Position = None
                              Annotations = [] } ]
                    DataEntries = []
                    Annotations = [] }

              let spec = HierarchyBridge.fromDocument doc
              Expect.equal (List.length spec.States) 1 "One composite state"
              Expect.equal spec.States.[0].Kind CompositeKind.AND "Parallel maps to AND"

          testCase "Nested composite states are all included in spec"
          <| fun () ->
              // Root (Composite)
              //   Active (Composite)
              //     Red (Initial)
              //     Green
              //   Off
              let doc: StatechartDocument =
                  { Title = None
                    InitialStateId = None
                    Elements =
                      [ StateDecl
                            { Identifier = Some "Root"
                              Label = None
                              Kind = StateKind.Composite
                              Children =
                                [ { Identifier = Some "Active"
                                    Label = None
                                    Kind = StateKind.Composite
                                    Children =
                                      [ { Identifier = Some "Red"
                                          Label = None
                                          Kind = StateKind.Initial
                                          Children = []
                                          Activities = None
                                          Position = None
                                          Annotations = [] }
                                        { Identifier = Some "Green"
                                          Label = None
                                          Kind = StateKind.Regular
                                          Children = []
                                          Activities = None
                                          Position = None
                                          Annotations = [] } ]
                                    Activities = None
                                    Position = None
                                    Annotations = [] }
                                  { Identifier = Some "Off"
                                    Label = None
                                    Kind = StateKind.Regular
                                    Children = []
                                    Activities = None
                                    Position = None
                                    Annotations = [] } ]
                              Activities = None
                              Position = None
                              Annotations = [] } ]
                    DataEntries = []
                    Annotations = [] }

              let spec = HierarchyBridge.fromDocument doc
              Expect.equal (List.length spec.States) 2 "Two composite states (Root and Active)"

              let ids = spec.States |> List.map (fun s -> s.Id) |> Set.ofList
              Expect.contains ids "Root" "Root is in spec"
              Expect.contains ids "Active" "Active is in spec"

              let root = spec.States |> List.find (fun s -> s.Id = "Root")
              Expect.contains root.Children "Active" "Root has Active as child"
              Expect.contains root.Children "Off" "Root has Off as child"

          testCase "States with no identifier are skipped"
          <| fun () ->
              let doc: StatechartDocument =
                  { Title = None
                    InitialStateId = None
                    Elements =
                      [ StateDecl
                            { Identifier = None
                              Label = None
                              Kind = StateKind.Composite
                              Children =
                                [ { Identifier = Some "Child"
                                    Label = None
                                    Kind = StateKind.Regular
                                    Children = []
                                    Activities = None
                                    Position = None
                                    Annotations = [] } ]
                              Activities = None
                              Position = None
                              Annotations = [] } ]
                    DataEntries = []
                    Annotations = [] }

              let spec = HierarchyBridge.fromDocument doc
              Expect.isEmpty spec.States "States with no identifier are skipped" ]

// ===========================================================================
// 5. stateMetadataMap key mismatch: parameterized DU regression
//    StateKeyExtractor.keyOf(Won "X") = "Won"
//    string (Won "X") produces a different representation
// ===========================================================================

[<Tests>]
let stateMetadataKeyTests =
    testList
        "stateMetadataMap key correctness"
        [ testCase "stateMetadataMap uses StateKeyExtractor.keyOf for parameterized DU"
          <| fun () ->
              let res =
                  statefulResource "/games/{id}" {
                      machine gameMachineWithMetadata

                      inState (
                          forState Playing [ StateHandlerBuilder.get (fun ctx -> ctx.Response.WriteAsync("playing")) ]
                      )

                      inState (
                          forState (Won "X") [ StateHandlerBuilder.get (fun ctx -> ctx.Response.WriteAsync("won")) ]
                      )
                  }

              let meta = getMetadata res |> Option.get
              // "Won" should be the key (not "Won \"X\"" which string cast would produce)
              Expect.isTrue
                  (Map.containsKey "Won" meta.StateMetadataMap)
                  "StateMetadataMap should have key 'Won' (not 'Won \"X\"')"

              Expect.isTrue
                  (Map.containsKey "Playing" meta.StateMetadataMap)
                  "StateMetadataMap should have key 'Playing'" ]

// ===========================================================================
// 6. Deep history XOR enforcement: enterWithHistory Deep uses enterState
//    Bug: Deep history uses Set.fold which bypasses XOR exclusivity.
//    Fix: Deep history should re-enter states via enterState top-down.
// ===========================================================================

[<Tests>]
let deepHistoryXorEnforcementTests =
    testList
        "Deep history XOR enforcement"
        [ testCase "Deep history restore respects XOR exclusivity"
          <| fun () ->
              // Hierarchy: Active (XOR, children: [Red, Green])
              let spec: HierarchySpec =
                  { States =
                      [ { Id = "Active"
                          Kind = CompositeKind.XOR
                          Children = [ "Red"; "Green" ]
                          InitialChild = Some "Red"
                          CompletionTarget = None } ] }

              let hierarchy = StateHierarchy.build spec

              // Record that Green was active inside Active
              let history =
                  HistoryRecord.empty
                  |> HistoryRecord.record
                      "Active"
                      (ActiveStateConfiguration.empty |> ActiveStateConfiguration.add "Green")

              // Re-enter Active via Deep history from empty config
              let freshConfig = ActiveStateConfiguration.empty

              let result =
                  HierarchicalRuntime.enterWithHistory hierarchy HistoryKind.Deep "Active" freshConfig history

              let activeStates = ActiveStateConfiguration.toSet result

              // XOR: only one of Red/Green should be active, not both
              let redActive = Set.contains "Red" activeStates
              let greenActive = Set.contains "Green" activeStates

              Expect.isFalse
                  (redActive && greenActive)
                  "XOR composite: Red and Green cannot both be active after Deep history restore"

              Expect.isTrue greenActive "Green should be restored by Deep history" ]
