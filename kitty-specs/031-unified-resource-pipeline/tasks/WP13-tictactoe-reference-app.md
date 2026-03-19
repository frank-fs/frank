---
work_package_id: WP13
title: Tic-Tac-Toe Reference App & End-to-End Validation
lane: "doing"
dependencies:
- WP07
base_branch: 031-unified-resource-pipeline-WP07
base_commit: b9db0f0447b4f9f24313b68a6a272b4a3fa6e18f
created_at: '2026-03-19T04:06:56.944778+00:00'
subtasks:
- T074
- T075
- T076
- T077
- T078
- T079
- T080
phase: Phase 4 - Validation
assignee: ''
agent: ''
shell_pid: "26479"
review_status: ''
reviewed_by: ''
history:
- timestamp: '2026-03-19T02:15:00Z'
  lane: planned
  agent: system
  shell_pid: ''
  action: Prompt generated via /spec-kitty.tasks
requirement_refs:
- FR-001
- FR-008
- FR-010
- FR-017
- FR-018
- FR-022
---

# Work Package Prompt: WP13 -- Tic-Tac-Toe Reference App & End-to-End Validation

## Important: Review Feedback Status

**Read this first if you are implementing this task!**

- **Has review feedback?**: Check the `review_status` field above. If it says `has_feedback`, scroll to the **Review Feedback** section immediately.
- **You must address all feedback** before your work is complete. Feedback items are your implementation TODO list.
- **Mark as acknowledged**: When you understand the feedback and begin addressing it, update `review_status: acknowledged` in the frontmatter.
- **Report progress**: As you address each feedback item, update the Activity Log explaining what you changed.

---

## Review Feedback

> **Populated by `/spec-kitty.review`** -- Reviewers add detailed feedback here when work needs changes. Implementation must address every item listed below before returning for re-review.

*[This section is empty initially. Reviewers will populate it if the work is returned from review. If you see feedback here, treat each item as a must-do before completion.]*

---

## Markdown Formatting
Wrap HTML/XML tags in backticks: `` `<div>` ``, `` `<script>` ``
Use language identifiers in code blocks: ````fsharp`, ````json`, ````bash`

---

## Implementation Command

Depends on WP07 (affordance middleware), WP08 (MSBuild embedding), WP09 (ALPS generation from unified model), WP10 (Datastar affordance helper). This is the **Phase 4 validation target** -- it exercises the entire unified pipeline end-to-end.

```bash
spec-kitty implement WP13 --base WP07
```

---

## Objectives & Success Criteria

1. Create `sample/Frank.TicTacToe.Sample/` project with a complete tic-tac-toe stateful resource, reusing the domain types and transition logic from `test/Frank.Statecharts.Tests/StatefulResourceTests.fs`.
2. Wire up `Frank.Affordances` middleware (`useAffordances`) and verify affordance map is loaded from embedded resource.
3. Run the full unified CLI pipeline against the sample: `extract`, `generate --format alps`, `generate --format affordance-map`.
4. Verify unified extraction contains both type and behavior data.
5. Verify ALPS document contains both semantic and transition descriptors.
6. Verify affordance map entries for all 4 states.
7. Start the sample via TestHost, send requests in different states, verify `Link` + `Allow` + `profile` + `describedby` headers change correctly per state.
8. Add a Datastar SSE handler using `affordancesFor()` and verify fragments change between interactive (XTurn) and read-only (Won) states.

**Success**: A single sample application demonstrates the full pipeline from F# source to runtime affordance headers, proving that compile-time extraction, binary embedding, and runtime projection work end-to-end. This is the validation target referenced throughout the spec.

---

## Context & Constraints

- **Spec**: `kitty-specs/031-unified-resource-pipeline/spec.md` -- all user stories, all acceptance scenarios
- **Plan**: `kitty-specs/031-unified-resource-pipeline/plan.md` -- `sample/Frank.TicTacToe.Sample/` in project structure
- **Data Model**: `kitty-specs/031-unified-resource-pipeline/data-model.md` -- all types exercised end-to-end
- **Contracts**: `kitty-specs/031-unified-resource-pipeline/contracts/affordance-map-schema.json` -- affordance map JSON validated against schema
- **Research**: `kitty-specs/031-unified-resource-pipeline/research.md` -- all decisions validated in practice

**Key design decisions**:
- The sample reuses existing tic-tac-toe domain types from `test/Frank.Statecharts.Tests/StatefulResourceTests.fs`:
  - `TicTacToeState` DU: `XTurn | OTurn | Won of winner: string | Draw`
  - `TicTacToeEvent`: `MakeMove of position: int`
  - `gameTransition` function
  - `turnGuard` guard
  - `gameMachine` state machine
- The sample is a standalone ASP.NET Core application, NOT a test project. It should be runnable with `dotnet run`.
- Integration tests exercise the sample via `Microsoft.AspNetCore.TestHost`.
- The sample project targets `net10.0` (single target, matching other sample projects).

**Dependencies** (all from earlier WPs):
- `Frank.Affordances` package (WP07: middleware, WP08: MSBuild embedding)
- Unified ALPS generation (WP09)
- `Frank.Datastar.AffordanceHelper` (WP10)
- Unified extraction (WP03) and unified model types (WP02)
- Affordance map generation (WP06)

---

## Subtasks & Detailed Guidance

### Subtask T074 -- Create sample project with tic-tac-toe stateful resource

- **Purpose**: Build a standalone sample application that serves as the end-to-end validation target for the entire unified resource pipeline. This application will be used to verify every spec requirement.

- **Steps**:
  1. Create the project directory: `sample/Frank.TicTacToe.Sample/`

  2. Create `sample/Frank.TicTacToe.Sample/Frank.TicTacToe.Sample.fsproj`:
     ```xml
     <Project Sdk="Microsoft.NET.Sdk.Web">
       <PropertyGroup>
         <TargetFramework>net10.0</TargetFramework>
       </PropertyGroup>

       <ItemGroup>
         <Compile Include="Domain.fs" />
         <Compile Include="Program.fs" />
       </ItemGroup>

       <ItemGroup>
         <ProjectReference Include="../../src/Frank/Frank.fsproj" />
         <ProjectReference Include="../../src/Frank.Statecharts/Frank.Statecharts.fsproj" />
         <ProjectReference Include="../../src/Frank.Affordances/Frank.Affordances.fsproj" />
         <ProjectReference Include="../../src/Frank.Datastar/Frank.Datastar.fsproj" />
       </ItemGroup>
     </Project>
     ```

  3. Create `sample/Frank.TicTacToe.Sample/Domain.fs` with the tic-tac-toe domain types. Copy the relevant types from `test/Frank.Statecharts.Tests/StatefulResourceTests.fs`:

     ```fsharp
     module Frank.TicTacToe.Sample.Domain

     open Frank.Statecharts

     type TicTacToeState =
         | XTurn
         | OTurn
         | Won of winner: string
         | Draw

     type TicTacToeEvent = MakeMove of position: int

     let gameTransition (state: TicTacToeState) (_event: TicTacToeEvent) (moveCount: int) =
         match state with
         | XTurn ->
             let n = moveCount + 1
             if n >= 5 then TransitionResult.Transitioned(Won "X", n)
             else TransitionResult.Transitioned(OTurn, n)
         | OTurn ->
             let n = moveCount + 1
             if n >= 9 then TransitionResult.Transitioned(Draw, n)
             else TransitionResult.Transitioned(XTurn, n)
         | Won _ -> TransitionResult.Invalid "Game already over"
         | Draw -> TransitionResult.Invalid "Game already over"

     let gameMachine: StateMachine<TicTacToeState, TicTacToeEvent, int> =
         { Initial = XTurn
           Transition = gameTransition
           Guards = []
           StateMetadata = Map.ofList [
               "XTurn", { AllowedMethods = [ "GET"; "POST" ]; IsFinal = false; Description = Some "X's turn to move" }
               "OTurn", { AllowedMethods = [ "GET"; "POST" ]; IsFinal = false; Description = Some "O's turn to move" }
               "Won",   { AllowedMethods = [ "GET" ]; IsFinal = true; Description = Some "Game over - winner declared" }
               "Draw",  { AllowedMethods = [ "GET" ]; IsFinal = true; Description = Some "Game over - draw" }
           ] }
     ```

  4. Create `sample/Frank.TicTacToe.Sample/Program.fs` with the application setup:
     ```fsharp
     module Frank.TicTacToe.Sample.Program

     open Frank.Builder
     open Frank.Statecharts
     open Frank.TicTacToe.Sample.Domain
     open Microsoft.AspNetCore.Builder
     open Microsoft.AspNetCore.Http
     open Microsoft.Extensions.Hosting

     [<EntryPoint>]
     let main args =
         let builder = WebApplication.CreateBuilder(args)
         let app = builder.Build()

         // Register statechart middleware
         app.UseStatecharts() |> ignore

         // Register affordance middleware (loads embedded affordance map)
         useAffordances |> ignore

         // Define the stateful resource
         let gameResource =
             statefulResource "/games/{gameId}" {
                 stateMachine gameMachine
                 inState XTurn [
                     get (fun (ctx: HttpContext) -> ctx.Response.WriteAsync("X's turn"))
                     post (fun (ctx: HttpContext) -> ctx.Response.WriteAsync("Move accepted"))
                 ]
                 inState OTurn [
                     get (fun (ctx: HttpContext) -> ctx.Response.WriteAsync("O's turn"))
                     post (fun (ctx: HttpContext) -> ctx.Response.WriteAsync("Move accepted"))
                 ]
                 inState (Won "X") [
                     get (fun (ctx: HttpContext) -> ctx.Response.WriteAsync("X wins!"))
                 ]
                 inState Draw [
                     get (fun (ctx: HttpContext) -> ctx.Response.WriteAsync("Draw!"))
                 ]
             }

         // Also add a plain resource (non-stateful) for contrast
         let healthResource =
             resource "/health" {
                 get (fun (ctx: HttpContext) -> ctx.Response.WriteAsync("OK"))
             }

         app.MapResource(gameResource) |> ignore
         app.MapResource(healthResource) |> ignore

         app.Run()
         0
     ```

  5. Add the sample project to the solution (if Frank.sln includes sample projects):
     ```bash
     dotnet sln Frank.sln add sample/Frank.TicTacToe.Sample/Frank.TicTacToe.Sample.fsproj
     ```

  6. Verify build: `dotnet build sample/Frank.TicTacToe.Sample/Frank.TicTacToe.Sample.fsproj`

- **Files**:
  - `sample/Frank.TicTacToe.Sample/Frank.TicTacToe.Sample.fsproj` (NEW)
  - `sample/Frank.TicTacToe.Sample/Domain.fs` (NEW)
  - `sample/Frank.TicTacToe.Sample/Program.fs` (NEW)
- **Notes**:
  - The `Won` state has a payload (`winner: string`). In the `inState` call, we use `Won "X"` as a specific case. The state key extracted from the DU will be `"Won"` (case name only, not the payload). Verify this matches the affordance map key.
  - The `statefulResource` and `inState` CE syntax must match the current Frank.Statecharts API. Read `test/Frank.Statecharts.Tests/StatefulResourceTests.fs` for the exact CE usage pattern.
  - The `healthResource` provides test coverage for plain (non-stateful) resources in the unified pipeline.

### Subtask T075 -- Add `Frank.Affordances` package reference and `useAffordances` middleware

- **Purpose**: Wire the affordance middleware into the sample application so it loads the embedded affordance map at startup and injects `Link` + `Allow` headers at request time.

- **Steps**:
  1. The project reference to `Frank.Affordances` was already added in T074's fsproj. Verify it exists.

  2. In `Program.fs`, ensure `useAffordances` is called AFTER `app.UseStatecharts()` and BEFORE `app.MapResource()`:
     ```fsharp
     app.UseStatecharts() |> ignore       // Resolves current state into HttpContext.Items
     useAffordances |> ignore       // Reads state from context, injects Link + Allow headers
     ```

  3. The middleware ordering is critical:
     - `UseStatecharts()` runs first and sets `StateMachineMetadata` (current state key) in `HttpContext.Items`
     - `useAffordances` runs second and reads the state key from `HttpContext.Items` to look up the affordance map entry
     - If the order is reversed, the affordance middleware will not find the state key

  4. Verify that the `Frank.Affordances.MSBuild` targets are triggered during build:
     ```bash
     dotnet build sample/Frank.TicTacToe.Sample/Frank.TicTacToe.Sample.fsproj -v n
     ```
     Look for output indicating the `unified-state.bin` was embedded as a resource.

  5. If the MSBuild embedding requires the CLI to have been run first (to generate the binary), ensure the build pipeline handles this:
     - Option A: Run `frank-cli extract --project` as a pre-build step
     - Option B: The MSBuild target gracefully skips embedding if the binary does not exist (and the middleware degrades gracefully per FR-021)

- **Files**: `sample/Frank.TicTacToe.Sample/Program.fs` (MODIFY if needed), `sample/Frank.TicTacToe.Sample/Frank.TicTacToe.Sample.fsproj` (verify references)
- **Notes**:
  - The affordance middleware must degrade gracefully if no embedded resource is found (FR-021). During development, the first build will not have an affordance map. The middleware should log a warning and pass through.
  - After running `frank-cli extract --project` and `frank-cli generate --format affordance-map`, rebuilding the project should embed the map and the middleware should pick it up.

### Subtask T076 -- Run unified extraction and verify both type and behavior data

- **Purpose**: Validate User Story 1 (unified extraction) against the reference application.

- **Steps**:
  1. Run the unified extraction:
     ```bash
     frank-cli extract --project sample/Frank.TicTacToe.Sample/Frank.TicTacToe.Sample.fsproj --output-format json
     ```

  2. Verify the JSON output contains a unified resource for `/games/{gameId}` with:
     - **Type info**: `TicTacToeState` with 4 DU cases (XTurn, OTurn, Won, Draw), `TicTacToeEvent` with 1 case (MakeMove)
     - **Behavioral info**: 4 states, initial state `XTurn`, HTTP methods GET+POST for XTurn/OTurn, GET-only for Won/Draw
     - **Route**: `/games/{gameId}`
     - **Derived fields**: No orphan states (all DU cases are covered by `inState` calls)

  3. Verify the output also contains a unified resource for `/health` with:
     - **Type info**: No specific types (or minimal types from the handler)
     - **Behavioral info**: None (plain resource, no statechart)
     - **Route**: `/health`

  4. Verify the cache file was created: `sample/Frank.TicTacToe.Sample/obj/frank-cli/unified-state.bin`

  5. Run extraction again without changing source and verify it completes faster (cache hit):
     ```bash
     time frank-cli extract --project sample/Frank.TicTacToe.Sample/Frank.TicTacToe.Sample.fsproj
     ```

  6. Script these checks as automated tests if possible (or document as manual acceptance steps for the reviewer).

- **Files**: No source changes. CLI commands exercised against the sample.
- **Notes**:
  - The first extraction may take 10-15 seconds (FCS typecheck). Subsequent extractions from cache should complete in under 1 second.
  - If the extraction fails, check that `Ionide.ProjInfo` can load the sample project's fsproj correctly. The sample references multiple projects -- ensure the project graph resolves.
  - The `Won of winner: string` DU case may require special handling -- verify the extractor reports the case name as `"Won"` (not `"Won \"X\""` or `"Won of winner"`).

### Subtask T077 -- Run ALPS generation and verify semantic + transition descriptors

- **Purpose**: Validate User Story 2 (ALPS generation from unified model).

- **Steps**:
  1. Run ALPS generation:
     ```bash
     frank-cli generate --project sample/Frank.TicTacToe.Sample/Frank.TicTacToe.Sample.fsproj --format alps --base-uri https://example.com/alps/games
     ```

  2. Verify the ALPS document contains **semantic descriptors** for type properties:
     ```json
     { "id": "board", "type": "semantic" }
     { "id": "currentTurn", "type": "semantic" }
     { "id": "winner", "type": "semantic" }
     ```

  3. Verify the ALPS document contains **transition descriptors** for HTTP methods:
     ```json
     { "id": "gameState", "type": "safe", "rt": "#board #currentTurn #winner" }
     { "id": "makeMove", "type": "unsafe", "rt": "#board #currentTurn" }
     ```

  4. Parse the ALPS JSON and verify:
     - It is valid ALPS (version 1.0)
     - Both `semantic` and `safe`/`unsafe` descriptor types are present
     - The `rt` (return type) fields reference the semantic descriptors

  5. If the existing ALPS parser can validate the document, run it:
     ```fsharp
     // In a test:
     let alps = AlpsParser.parseJson alpsJsonString
     match alps with
     | Ok doc -> Expect.isTrue (doc.Descriptors.Length > 0) "Should have descriptors"
     | Error e -> failtest $"ALPS parsing failed: {e}"
     ```

- **Files**: No source changes. CLI commands and test assertions.
- **Notes**:
  - The exact descriptor IDs and `rt` values depend on the ALPS generator implementation (WP09). The values above are from the research document and may differ slightly.
  - The `--base-uri` flag sets the ALPS profile namespace used for fragment URIs in link relations.
  - If the ALPS document includes only behavioral descriptors (no semantic descriptors), that is a bug in WP09's unified ALPS generator.

### Subtask T078 -- Run affordance map generation and verify entries for all 4 states

- **Purpose**: Validate User Story 3 (affordance map generation).

- **Steps**:
  1. Run affordance map generation:
     ```bash
     frank-cli generate --project sample/Frank.TicTacToe.Sample/Frank.TicTacToe.Sample.fsproj --format affordance-map --base-uri https://example.com/alps/games
     ```

  2. Verify the JSON output matches the affordance map schema (`contracts/affordance-map-schema.json`):
     ```json
     {
       "version": "1.0",
       "baseUri": "https://example.com/alps/games",
       "entries": {
         "/games/{gameId}|XTurn": {
           "allowedMethods": ["GET", "POST"],
           "linkRelations": [
             { "rel": "https://example.com/alps/games#makeMove", "method": "POST", "href": "/games/{gameId}" }
           ]
         },
         "/games/{gameId}|OTurn": {
           "allowedMethods": ["GET", "POST"],
           "linkRelations": [
             { "rel": "https://example.com/alps/games#makeMove", "method": "POST", "href": "/games/{gameId}" }
           ]
         },
         "/games/{gameId}|Won": {
           "allowedMethods": ["GET"],
           "linkRelations": []
         },
         "/games/{gameId}|Draw": {
           "allowedMethods": ["GET"],
           "linkRelations": []
         },
         "/health|*": {
           "allowedMethods": ["GET"],
           "linkRelations": []
         }
       }
     }
     ```

  3. Verify:
     - XTurn and OTurn have `GET` + `POST`
     - Won and Draw have `GET` only
     - `/health` has a wildcard state key `*` with `GET` only
     - Link relations use ALPS-derived URIs with the `--base-uri` namespace
     - The map validates against the JSON Schema contract

  4. Verify the map file was written to: `sample/Frank.TicTacToe.Sample/obj/frank-cli/affordance-map.json`

- **Files**: No source changes. CLI commands and assertions.
- **Notes**:
  - The affordance map is the artifact consumed by the runtime middleware. Its correctness is critical for T079 (runtime header injection).
  - If the health resource's wildcard key is `""` instead of `"*"`, that indicates a bug in the affordance map generator (WP06).
  - The link relations depend on the `--base-uri` value. Without it, they may use a default namespace.

### Subtask T079 -- Build sample, start with TestHost, verify affordance headers

- **Purpose**: Validate User Story 5 (runtime affordance middleware) end-to-end. This is the most important validation step -- it proves the full pipeline from source to runtime.

- **Steps**:
  1. Create an integration test file: `test/Frank.TicTacToe.Tests/AffordanceIntegrationTests.fs` (or add to existing test project).

  2. Set up TestHost for the sample application:
     ```fsharp
     let createTestServer () =
         let builder = WebApplication.CreateBuilder()
         // Configure the sample app identically to Program.fs
         let app = builder.Build()
         app.UseStatecharts() |> ignore
         useAffordances |> ignore
         // Map resources...
         let testServer = new TestServer(app.Services)
         testServer
     ```

  3. **Test 1 -- XTurn state has correct headers**:
     ```fsharp
     testCase "XTurn state returns Allow: GET, POST and Link headers" (fun () ->
         use server = createTestServer()
         use client = server.CreateClient()

         // Create a game (puts it in initial XTurn state)
         let response = client.GetAsync("/games/game1").Result

         // Verify Allow header
         let allowHeader = response.Headers.GetValues("Allow") |> Seq.head
         Expect.stringContains allowHeader "GET" "Should allow GET"
         Expect.stringContains allowHeader "POST" "Should allow POST"

         // Verify Link header with profile
         let linkHeaders = response.Headers.GetValues("Link") |> Seq.toList
         let hasProfile = linkHeaders |> List.exists (fun l -> l.Contains "rel=\"profile\"")
         Expect.isTrue hasProfile "Should have profile Link header"

         // Verify Link header with transition
         let hasTransition = linkHeaders |> List.exists (fun l -> l.Contains "#makeMove")
         Expect.isTrue hasTransition "Should have makeMove transition Link header"
     )
     ```

  4. **Test 2 -- Won state has restricted headers**:
     ```fsharp
     testCase "Won state returns Allow: GET only and no transition links" (fun () ->
         use server = createTestServer()
         use client = server.CreateClient()

         // Advance game to Won state (make moves until won)
         // ... POST moves to advance the game ...

         let response = client.GetAsync("/games/game1").Result
         let allowHeader = response.Headers.GetValues("Allow") |> Seq.head
         Expect.stringContains allowHeader "GET" "Should allow GET"
         Expect.isFalse (allowHeader.Contains("POST")) "Should NOT allow POST in Won state"

         // No transition links (game is over)
         let linkHeaders = response.Headers.GetValues("Link") |> Seq.toList
         let hasTransition = linkHeaders |> List.exists (fun l -> l.Contains "#makeMove")
         Expect.isFalse hasTransition "Should NOT have makeMove link in Won state"
     )
     ```

  5. **Test 3 -- Plain resource passes through without affordance injection**:
     ```fsharp
     testCase "Health endpoint has no affordance headers" (fun () ->
         use server = createTestServer()
         use client = server.CreateClient()

         let response = client.GetAsync("/health").Result
         // Plain resource should not have state-dependent affordance headers
         // It may have basic Link headers if the wildcard entry provides them
         Expect.equal (int response.StatusCode) 200 "Should return 200"
     )
     ```

  6. **Test 4 -- Missing affordance map degrades gracefully**:
     ```fsharp
     testCase "Without affordance map, middleware passes through" (fun () ->
         // Build test server WITHOUT the embedded affordance map
         use server = createTestServerWithoutMap()
         use client = server.CreateClient()

         let response = client.GetAsync("/games/game1").Result
         // Should work without errors, just no affordance headers
         Expect.equal (int response.StatusCode) 200 "Should return 200 without map"
     )
     ```

  7. **Test 5 -- `describedby` header present**:
     ```fsharp
     testCase "Response includes describedby Link header" (fun () ->
         use server = createTestServer()
         use client = server.CreateClient()

         let response = client.GetAsync("/games/game1").Result
         let linkHeaders = response.Headers.GetValues("Link") |> Seq.toList
         let hasDescribedBy = linkHeaders |> List.exists (fun l -> l.Contains "rel=\"describedby\"")
         Expect.isTrue hasDescribedBy "Should have describedby Link header"
     )
     ```

  8. Run tests: `dotnet test test/Frank.TicTacToe.Tests/`

- **Files**:
  - `test/Frank.TicTacToe.Tests/Frank.TicTacToe.Tests.fsproj` (NEW)
  - `test/Frank.TicTacToe.Tests/AffordanceIntegrationTests.fs` (NEW, ~200-300 lines)
  - `test/Frank.TicTacToe.Tests/Program.fs` (NEW, Expecto entry point)
- **Notes**:
  - Advancing the game to specific states requires sending POST requests with move events. The exact API for submitting events depends on the `statefulResource` handler implementation. Read the handler patterns in `StatefulResourceTests.fs` for how events are submitted.
  - The TestHost setup must replicate the exact middleware pipeline from the sample's `Program.fs`. Any middleware ordering difference will cause test failures.
  - The test project needs `<PackageReference Include="Microsoft.AspNetCore.TestHost" />` and project references to the sample project (or copy the setup code).
  - Edge case: the embedded affordance map may not be available during the first build (chicken-and-egg). Run the CLI pipeline first, then rebuild, then run tests.

### Subtask T080 -- Add Datastar SSE handler using `affordancesFor()` for conditional rendering

- **Purpose**: Validate User Story 6 (Datastar affordance-driven fragments). Prove that the `affordancesFor()` helper enables state-aware fragment rendering.

- **Steps**:
  1. Add a Datastar SSE endpoint to the sample application. In `Program.fs` (or a separate `SseHandlers.fs`):
     ```fsharp
     open Frank.Datastar
     open Frank.Datastar.AffordanceHelper

     let affordanceMap = loadAffordanceMap()  // Load from embedded resource or JSON file

     let gameFragmentHandler (ctx: HttpContext) = task {
         let gameId = ctx.GetRouteValue("gameId") :?> string
         let currentState = ctx.Items.["statechart:stateKey"] :?> string  // Set by statechart middleware

         let affordances = affordancesFor "/games/{gameId}" currentState (Some affordanceMap)

         // Stream SSE fragments based on affordances
         use sse = ctx.Response |> ServerSentEventGenerator.create

         // Always show the game board
         do! sse.MergeFragment("<div id=\"board\">...</div>")

         // Conditionally show the move button based on affordances
         if affordances.CanPost then
             do! sse.MergeFragment("""<button id="move-btn" data-on-click="$$post('/games/{gameId}')">Make Move</button>""")
         else
             do! sse.MergeFragment("""<div id="move-btn" class="game-over">Game Over</div>""")

         // Show link relations for agent-readable clients
         for link in affordances.LinkRelations do
             do! sse.MergeFragment($"""<link rel="{link.Rel}" href="{link.Href}" />""")
     }
     ```

  2. Register the SSE endpoint:
     ```fsharp
     app.MapGet("/games/{gameId}/sse", gameFragmentHandler) |> ignore
     ```

  3. Write integration tests for the SSE handler:

     **Test 1 -- XTurn renders interactive controls**:
     ```fsharp
     testCase "SSE fragments in XTurn include move button" (fun () ->
         use server = createTestServer()
         use client = server.CreateClient()

         // Game starts in XTurn
         let response = client.GetAsync("/games/game1/sse").Result
         let body = response.Content.ReadAsStringAsync().Result
         Expect.stringContains body "Make Move" "XTurn should show move button"
         Expect.stringContains body "move-btn" "XTurn should have move button element"
     )
     ```

     **Test 2 -- Won renders read-only display**:
     ```fsharp
     testCase "SSE fragments in Won state show game over" (fun () ->
         use server = createTestServer()
         use client = server.CreateClient()

         // Advance game to Won state
         // ... POST moves ...

         let response = client.GetAsync("/games/game1/sse").Result
         let body = response.Content.ReadAsStringAsync().Result
         Expect.stringContains body "Game Over" "Won state should show game over"
         Expect.isFalse (body.Contains("Make Move")) "Won state should not show move button"
     )
     ```

     **Test 3 -- No affordance map renders all controls (permissive default)**:
     ```fsharp
     testCase "SSE fragments without map show all controls" (fun () ->
         // Test with affordanceMap = None
         let affordances = affordancesFor "/games/{gameId}" "XTurn" None
         Expect.isTrue affordances.CanPost "Permissive default should allow POST"
         // This means the move button would render -- correct behavior
     )
     ```

  4. Update `Frank.TicTacToe.Sample.fsproj` compile order if new files were added.

  5. Run tests: `dotnet test test/Frank.TicTacToe.Tests/`

- **Files**:
  - `sample/Frank.TicTacToe.Sample/SseHandlers.fs` (NEW, ~50-80 lines)
  - `sample/Frank.TicTacToe.Sample/Program.fs` (MODIFY to register SSE endpoint)
  - `sample/Frank.TicTacToe.Sample/Frank.TicTacToe.Sample.fsproj` (MODIFY compile order)
  - `test/Frank.TicTacToe.Tests/DatastarAffordanceTests.fs` (NEW, ~100-150 lines)
- **Notes**:
  - The SSE handler code is illustrative. The exact Datastar SSE API (`ServerSentEventGenerator.create`, `MergeFragment`) must match the current Frank.Datastar API. Read `src/Frank.Datastar/ServerSentEventGenerator.fs` and `src/Frank.Datastar/Frank.Datastar.fs` for the exact API.
  - The `ctx.Items.["statechart:stateKey"]` key must match what the statechart middleware sets. Verify the exact key name in `src/Frank.Statecharts/Middleware.fs`.
  - Loading the affordance map at startup can use `AffordanceHelper.loadFromJson` (if implemented in WP10) or manual JSON deserialization from the embedded resource.
  - SSE testing with TestHost requires reading the response as a stream. The test may need to handle the SSE protocol (lines separated by `\n\n`). Consider using a simple string search rather than full SSE parsing for test assertions.

---

## Test Strategy

- **Integration tests**: Full application lifecycle via TestHost -- build, start, send requests, verify response headers and bodies.
- **CLI pipeline tests**: Run `frank-cli extract`, `generate`, and verify outputs. Can be automated as bash scripts or F# tests that invoke the CLI.
- **Datastar tests**: SSE fragment rendering verified via response body string matching.
- **Test framework**: Expecto + Microsoft.AspNetCore.TestHost.
- **Test project**: `test/Frank.TicTacToe.Tests/` (NEW, targeting net10.0).
- **Commands**:
  ```bash
  # Build everything
  dotnet build Frank.sln

  # Run CLI pipeline
  frank-cli extract --project sample/Frank.TicTacToe.Sample/Frank.TicTacToe.Sample.fsproj
  frank-cli generate --project sample/Frank.TicTacToe.Sample/Frank.TicTacToe.Sample.fsproj --format alps --base-uri https://example.com/alps/games
  frank-cli generate --project sample/Frank.TicTacToe.Sample/Frank.TicTacToe.Sample.fsproj --format affordance-map --base-uri https://example.com/alps/games

  # Rebuild with embedded resource
  dotnet build sample/Frank.TicTacToe.Sample/Frank.TicTacToe.Sample.fsproj

  # Run integration tests
  dotnet test test/Frank.TicTacToe.Tests/
  ```

- **Coverage targets**: All 7 acceptance scenarios from spec User Stories 1, 2, 3, 5, 6.

---

## Risks & Mitigations

| Risk | Mitigation |
|------|------------|
| Chicken-and-egg: sample needs affordance map embedded, but map is generated from the sample source | Run CLI pipeline first (generates map to `obj/`), then rebuild (embeds map), then run tests. Document this two-step build process. |
| TestHost setup complexity (multiple middlewares, DI, state management) | Reuse patterns from `test/Frank.Statecharts.Tests/StatefulResourceTests.fs` which already sets up TestHost with statechart middleware. |
| SSE response testing is non-trivial (streaming protocol) | Use simple string matching on the full response body rather than parsing SSE events. For more robust testing, use a helper that buffers the SSE stream. |
| State advancement requires multiple POST requests with correct event payloads | Copy the exact event submission pattern from `StatefulResourceTests.fs`. The test already advances the game through multiple states. |
| The unified CLI pipeline (extract + generate) may not be fully working yet when WP13 starts | WP13 depends on WP07-WP10 completion. If blockers are found, file issues for the specific WP that owns the broken functionality. |
| Frank.Affordances project does not exist yet (created in WP07) | This WP13 cannot start until WP07 is complete. The dependency chain is enforced by the spec-kitty pipeline. |

---

## Review Guidance

- **This is the end-to-end validation WP**: every spec requirement should be exercised. Review against the full acceptance scenario list in `spec.md`.
- Verify the sample builds and runs standalone with `dotnet run`.
- Verify the CLI pipeline produces correct outputs (unified extraction, ALPS, affordance map).
- Verify the runtime middleware injects correct headers that change per state.
- Verify the Datastar SSE handler renders different fragments based on state.
- Verify the health endpoint (plain resource) does not get state-dependent affordance injection.
- Verify graceful degradation when the affordance map is not embedded.
- Run `dotnet build Frank.sln` to verify the full solution builds.
- Run `dotnet test test/Frank.TicTacToe.Tests/` to verify all integration tests pass.
- Cross-check: existing statechart tests in `test/Frank.Statecharts.Tests/` still pass.

---

## Activity Log

> **CRITICAL**: Activity log entries MUST be in chronological order (oldest first, newest last).

### How to Add Activity Log Entries

**When adding an entry**:
1. Scroll to the bottom of this file (Activity Log section below "Valid lanes")
2. **APPEND the new entry at the END** (do NOT prepend or insert in middle)
3. Use exact format: `- YYYY-MM-DDTHH:MM:SSZ -- agent_id -- lane=<lane> -- <action>`
4. Timestamp MUST be current time in UTC (check with `date -u "+%Y-%m-%dT%H:%M:%SZ"`)
5. Lane MUST match the frontmatter `lane:` field exactly
6. Agent ID should identify who made the change (claude-sonnet-4-5, codex, etc.)

**Format**:
```
- YYYY-MM-DDTHH:MM:SSZ -- <agent_id> -- lane=<lane> -- <brief action description>
```

**Valid lanes**: `planned`, `doing`, `for_review`, `done`

**Initial entry**:
- 2026-03-19T02:15:00Z -- system -- lane=planned -- Prompt created.
