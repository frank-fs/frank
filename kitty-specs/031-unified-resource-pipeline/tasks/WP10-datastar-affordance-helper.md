---
work_package_id: WP10
title: Datastar Affordance-Driven Fragments
lane: "done"
dependencies:
- WP06
base_branch: 031-unified-resource-pipeline-WP06
base_commit: 9f5d3ce27a5712da0e0822e1e9310a7eb3eadb9f
created_at: '2026-03-19T04:06:23.256094+00:00'
subtasks:
- T057
- T058
- T059
- T060
- T061
phase: Phase 3 - Integration
assignee: ''
agent: "claude-opus"
shell_pid: "28806"
review_status: "approved"
reviewed_by: "Ryan Riley"
history:
- timestamp: '2026-03-19T02:15:00Z'
  lane: planned
  agent: system
  shell_pid: ''
  action: Prompt generated via /spec-kitty.tasks
requirement_refs:
- FR-022
- FR-023
- FR-024
---

# Work Package Prompt: WP10 -- Datastar Affordance-Driven Fragments

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
Use language identifiers in code blocks: ````fsharp`, ````json`

---

## Implementation Command

Depends on WP06 (affordance map generation). Can run in parallel with WP07-WP09.

```bash
spec-kitty implement WP10 --base WP06
```

---

## Objectives & Success Criteria

1. Create an `AffordanceHelper.fs` module in `Frank.Datastar` that provides a pure function `affordancesFor` which looks up available actions for a given `(routeTemplate, stateKey)` pair from an affordance map.
2. Define an `AffordanceResult` record type with `AllowedMethods`, `LinkRelations`, and convenience booleans (`CanPost`, `CanPut`, `CanDelete`, `CanPatch`).
3. When the affordance map is not loaded or the key is not found, return a permissive default -- all methods available -- per FR-024.
4. Register `AffordanceHelper.fs` in the `Frank.Datastar.fsproj` compile order.
5. Write tests verifying correct affordance lookups for tic-tac-toe game states and permissive default behavior.

**Success**: A Datastar SSE handler can call `affordancesFor "/games/{gameId}" "XTurn" map` and receive an `AffordanceResult` indicating `CanPost = true`, while calling with `"Won"` returns `CanPost = false`. When no map is provided, all methods are available.

---

## Context & Constraints

- **Spec**: `kitty-specs/031-unified-resource-pipeline/spec.md` -- User Story 6 (FR-022, FR-023, FR-024)
- **Plan**: `kitty-specs/031-unified-resource-pipeline/plan.md` -- Datastar helper section, project structure showing `AffordanceHelper.fs` in `src/Frank.Datastar/`
- **Data Model**: `kitty-specs/031-unified-resource-pipeline/data-model.md` -- `AffordanceMapEntry`, `AffordanceLinkRelation` types
- **Contracts**: `kitty-specs/031-unified-resource-pipeline/contracts/affordance-map-schema.json` -- affordance map JSON schema (keys are `{routeTemplate}|{stateKey}`)
- **Research**: `kitty-specs/031-unified-resource-pipeline/research.md` -- R3 (affordance map key design: composite `routeTemplate|stateKey` with `|` separator, `*` wildcard for stateless)

**Key design decisions**:
- The helper is a **standalone pure function** -- not a middleware, not a CE. Developers call it inside their existing Datastar SSE handlers to conditionally decide which HTML fragments to stream.
- The affordance map types are defined **locally in Frank.Datastar** (or passed as a generic parameter) -- no project reference to `Frank.Affordances` or `Frank.Cli.Core`. The map is deserialized from JSON at startup by the consuming application and passed into the helper.
- The `|` separator in composite keys is not valid in route templates or DU case names, making it safe for splitting.
- Permissive default (all methods available) is critical: if the map is missing or a key is not found, the UI must not hide controls. Hiding controls on missing data would make the app non-functional.

**Existing Frank.Datastar structure** (from `src/Frank.Datastar/Frank.Datastar.fsproj`):
```
Consts.fs
Types.fs
ServerSentEvent.fs
SseDataLineWriter.fs
SseDataLineStream.fs
ServerSentEventGenerator.fs
Frank.Datastar.fs
```

---

## Subtasks & Detailed Guidance

### Subtask T057 -- Create `AffordanceHelper.fs` in `Frank.Datastar`

- **Purpose**: Provide the core `affordancesFor` function that Datastar SSE handlers call to determine what controls to render for a given resource state. This is the bridge between the compile-time affordance map and runtime Datastar fragment decisions.

- **Steps**:
  1. Create `src/Frank.Datastar/AffordanceHelper.fs`.
  2. Module declaration: `module Frank.Datastar.AffordanceHelper`
  3. Define the affordance map types locally (do NOT add a project reference to `Frank.Affordances`):

     ```fsharp
     /// A link relation entry from the affordance map.
     type AffordanceLinkRelation =
         { Rel: string
           Href: string
           Method: string
           Title: string option }

     /// A single entry in the affordance map.
     type AffordanceMapEntry =
         { AllowedMethods: string list
           LinkRelations: AffordanceLinkRelation list
           ProfileUrl: string option }

     /// The affordance map: keyed by composite "{routeTemplate}|{stateKey}" strings.
     type AffordanceMap =
         { Version: string
           BaseUri: string
           Entries: Map<string, AffordanceMapEntry> }
     ```

  4. Implement the composite key builder:

     ```fsharp
     /// Build the composite key used to look up entries in the affordance map.
     let private compositeKey (routeTemplate: string) (stateKey: string) : string =
         $"{routeTemplate}|{stateKey}"
     ```

  5. Implement `affordancesFor`:

     ```fsharp
     /// Look up the available affordances for a given route template and state key.
     /// Returns AffordanceResult with available methods and convenience booleans.
     /// If the map is None or the key is not found, returns a permissive default.
     let affordancesFor
         (routeTemplate: string)
         (stateKey: string)
         (map: AffordanceMap option)
         : AffordanceResult =
         match map with
         | None -> permissiveDefault
         | Some m ->
             let key = compositeKey routeTemplate stateKey
             match Map.tryFind key m.Entries with
             | Some entry -> entryToResult entry
             | None ->
                 // Try wildcard key for stateless resources
                 let wildcardKey = compositeKey routeTemplate "*"
                 match Map.tryFind wildcardKey m.Entries with
                 | Some entry -> entryToResult entry
                 | None -> permissiveDefault
     ```

  6. The lookup strategy has three tiers: (1) exact `route|state` match, (2) wildcard `route|*` match (for plain resources), (3) permissive default (all methods). This handles stateful, stateless, and missing-map cases.

- **Files**: `src/Frank.Datastar/AffordanceHelper.fs` (NEW, ~120-150 lines)
- **Parallel?**: T057, T058, T059 are sequential (they build on each other within the same file).
- **Notes**:
  - The types defined here are intentionally duplicated from the affordance map schema -- they are the Datastar-side view of the map. This avoids coupling Frank.Datastar to Frank.Affordances or Frank.Cli.Core.
  - If in the future the types need to be shared, they can be extracted to a lightweight `Frank.Affordances.Abstractions` package. For now, local types are simpler.
  - The `AffordanceMap` type uses F# `Map<string, AffordanceMapEntry>` rather than `Dictionary` because this is a read-only lookup loaded once at startup. `Map` is immutable and thread-safe.

### Subtask T058 -- Define `AffordanceResult` record with convenience booleans

- **Purpose**: Provide a developer-friendly return type that enables pattern matching and conditional rendering in Datastar handlers without requiring the developer to search through method lists.

- **Steps**:
  1. In `AffordanceHelper.fs`, define the result type:

     ```fsharp
     /// Result of an affordance lookup. Provides both raw data and convenience booleans.
     type AffordanceResult =
         { AllowedMethods: string list
           LinkRelations: AffordanceLinkRelation list
           CanGet: bool
           CanPost: bool
           CanPut: bool
           CanDelete: bool
           CanPatch: bool }
     ```

  2. Implement the permissive default:

     ```fsharp
     /// Default when no affordance map is loaded or key not found.
     /// All methods are permitted -- never hide controls when data is missing.
     let private permissiveDefault : AffordanceResult =
         { AllowedMethods = [ "GET"; "POST"; "PUT"; "DELETE"; "PATCH" ]
           LinkRelations = []
           CanGet = true
           CanPost = true
           CanPut = true
           CanDelete = true
           CanPatch = true }
     ```

  3. Implement the entry-to-result converter:

     ```fsharp
     /// Convert an AffordanceMapEntry to an AffordanceResult with computed booleans.
     let private entryToResult (entry: AffordanceMapEntry) : AffordanceResult =
         let methods = entry.AllowedMethods |> List.map (fun m -> m.ToUpperInvariant())
         { AllowedMethods = entry.AllowedMethods
           LinkRelations = entry.LinkRelations
           CanGet = List.contains "GET" methods
           CanPost = List.contains "POST" methods
           CanPut = List.contains "PUT" methods
           CanDelete = List.contains "DELETE" methods
           CanPatch = List.contains "PATCH" methods }
     ```

  4. The convenience booleans are case-insensitive by normalizing to uppercase before comparison. This handles edge cases where the affordance map uses lowercase method names.

- **Files**: `src/Frank.Datastar/AffordanceHelper.fs` (same file as T057)
- **Notes**:
  - The `AffordanceResult` type is designed for use in Datastar view functions. A handler would write:
    ```fsharp
    let affordances = AffordanceHelper.affordancesFor "/games/{gameId}" stateKey map
    if affordances.CanPost then
        // Stream the "Make Move" button fragment
    else
        // Stream read-only display
    ```
  - The `LinkRelations` field is included for advanced use (e.g., rendering link targets in the HTML), but most handlers will just use the `Can*` booleans.
  - HEAD and OPTIONS are not included as convenience booleans -- they are infrastructure methods, not user-facing actions.

### Subtask T059 -- Permissive default when affordance map is not loaded

- **Purpose**: Ensure the helper never causes controls to disappear when the affordance map is unavailable. Per FR-024, the default is permissive (all methods available).

- **Steps**:
  1. Verify that `affordancesFor` returns `permissiveDefault` in all three scenarios:
     - `map` parameter is `None`
     - `map` is `Some` but the exact composite key is not found AND the wildcard key is not found
     - `map` is `Some` but has zero entries

  2. Document the rationale clearly in the module:
     ```fsharp
     // DESIGN: Permissive default (FR-024)
     //
     // When the affordance map is not loaded or a key is not found, ALL methods
     // are reported as available. This ensures that:
     //   1. Applications work without an affordance map (graceful degradation)
     //   2. New states added to code but not yet in the map still show controls
     //   3. The helper never causes a "broken" UI by hiding all actions
     //
     // The only way to restrict methods is to have them explicitly listed in the
     // affordance map. Absence = permissive. Presence = authoritative.
     ```

  3. The `LinkRelations` in the permissive default is an empty list -- we cannot fabricate link relations without the ALPS profile data. This is acceptable because:
     - The UI still shows all controls (buttons, forms)
     - Link relation data is for progressive enhancement (agent-readable hints), not for basic UI rendering

- **Files**: `src/Frank.Datastar/AffordanceHelper.fs` (same file)
- **Notes**:
  - This is a deliberate design choice. The alternative (restrictive default: hide everything when map is missing) would make the application non-functional until the affordance map is generated and embedded. That creates a chicken-and-egg problem during development.
  - Edge case: a state key of empty string `""` should be treated as missing and fall through to the wildcard or permissive default. Normalize empty strings to trigger the wildcard lookup.

### Subtask T060 -- Add `AffordanceHelper.fs` to `Frank.Datastar.fsproj`

- **Purpose**: Register the new file in the F# compile order so it is included in the build.

- **Steps**:
  1. Open `src/Frank.Datastar/Frank.Datastar.fsproj`.
  2. Add `AffordanceHelper.fs` to the `<Compile>` item group. Place it **after** `Types.fs` and **before** `ServerSentEvent.fs`. The helper defines its own types (no dependency on other Frank.Datastar modules), so it should be early in the compile order in case future modules reference it.

     Updated compile order:
     ```xml
     <ItemGroup>
       <Compile Include="Consts.fs" />
       <Compile Include="Types.fs" />
       <Compile Include="AffordanceHelper.fs" />
       <Compile Include="ServerSentEvent.fs" />
       <Compile Include="SseDataLineWriter.fs" />
       <Compile Include="SseDataLineStream.fs" />
       <Compile Include="ServerSentEventGenerator.fs" />
       <Compile Include="Frank.Datastar.fs" />
     </ItemGroup>
     ```

  3. Verify the build: `dotnet build src/Frank.Datastar/Frank.Datastar.fsproj`

  4. Confirm no new `<ProjectReference>` entries are needed. The helper defines all types locally and has no dependencies on `Frank.Affordances` or `Frank.Cli.Core`. The only existing project reference is to `Frank.fsproj` (core), which remains unchanged.

- **Files**: `src/Frank.Datastar/Frank.Datastar.fsproj`
- **Notes**:
  - Do NOT add a NuGet reference to MessagePack or any serialization library. The affordance map is loaded by the consuming application (e.g., via `System.Text.Json` deserialization of the affordance-map.json) and passed to the helper as an `AffordanceMap` record. Deserialization is the application's responsibility, not the helper's.
  - Consider adding a `loadFromJson` convenience function that uses `System.Text.Json` to deserialize the JSON affordance map into the `AffordanceMap` type. This avoids requiring the developer to write deserialization code. If included, ensure `System.Text.Json` is available (it is, since Frank targets .NET 8+).

### Subtask T061 -- Write tests for affordance helper

- **Purpose**: Verify the helper returns correct affordance data for various tic-tac-toe game states, handles missing keys gracefully, and always falls back to the permissive default.

- **Steps**:
  1. Determine the test location. Options:
     - Add tests to `test/Frank.Datastar.Tests/` if it exists
     - Create a new test file in an appropriate test project
     - Check for existing Datastar tests: `sample/Frank.Datastar.Tests/`

  2. Create the test file (e.g., `test/Frank.Datastar.Tests/AffordanceHelperTests.fs` or add to the existing Datastar test project).

  3. Build a mock affordance map representing the tic-tac-toe game:

     ```fsharp
     let ticTacToeMap : AffordanceMap =
         { Version = "1.0"
           BaseUri = "https://example.com/alps"
           Entries =
               Map.ofList [
                   "/games/{gameId}|XTurn",
                   { AllowedMethods = [ "GET"; "POST" ]
                     LinkRelations = [
                         { Rel = "https://example.com/alps/games#makeMove"
                           Href = "/games/{gameId}"
                           Method = "POST"
                           Title = Some "Make Move" }
                     ]
                     ProfileUrl = Some "https://example.com/alps/games" }

                   "/games/{gameId}|OTurn",
                   { AllowedMethods = [ "GET"; "POST" ]
                     LinkRelations = [
                         { Rel = "https://example.com/alps/games#makeMove"
                           Href = "/games/{gameId}"
                           Method = "POST"
                           Title = Some "Make Move" }
                     ]
                     ProfileUrl = Some "https://example.com/alps/games" }

                   "/games/{gameId}|Won",
                   { AllowedMethods = [ "GET" ]
                     LinkRelations = []
                     ProfileUrl = Some "https://example.com/alps/games" }

                   "/games/{gameId}|Draw",
                   { AllowedMethods = [ "GET" ]
                     LinkRelations = []
                     ProfileUrl = Some "https://example.com/alps/games" }
               ] }
     ```

  4. Write the following test cases:

     **Test 1 -- XTurn state returns GET and POST**:
     ```fsharp
     let result = AffordanceHelper.affordancesFor "/games/{gameId}" "XTurn" (Some ticTacToeMap)
     Expect.isTrue result.CanGet "XTurn should allow GET"
     Expect.isTrue result.CanPost "XTurn should allow POST"
     Expect.isFalse result.CanPut "XTurn should not allow PUT"
     Expect.isFalse result.CanDelete "XTurn should not allow DELETE"
     Expect.equal result.AllowedMethods [ "GET"; "POST" ] "XTurn methods"
     ```

     **Test 2 -- Won state returns GET only**:
     ```fsharp
     let result = AffordanceHelper.affordancesFor "/games/{gameId}" "Won" (Some ticTacToeMap)
     Expect.isTrue result.CanGet "Won should allow GET"
     Expect.isFalse result.CanPost "Won should not allow POST"
     Expect.equal result.LinkRelations [] "Won should have no transition links"
     ```

     **Test 3 -- OTurn state has link relations**:
     ```fsharp
     let result = AffordanceHelper.affordancesFor "/games/{gameId}" "OTurn" (Some ticTacToeMap)
     Expect.equal result.LinkRelations.Length 1 "OTurn should have 1 link relation"
     Expect.equal result.LinkRelations.[0].Rel "https://example.com/alps/games#makeMove" "Link rel"
     Expect.equal result.LinkRelations.[0].Method "POST" "Link method"
     ```

     **Test 4 -- Missing state key returns permissive default**:
     ```fsharp
     let result = AffordanceHelper.affordancesFor "/games/{gameId}" "UnknownState" (Some ticTacToeMap)
     Expect.isTrue result.CanGet "Unknown state should allow GET (permissive)"
     Expect.isTrue result.CanPost "Unknown state should allow POST (permissive)"
     Expect.isTrue result.CanPut "Unknown state should allow PUT (permissive)"
     Expect.isTrue result.CanDelete "Unknown state should allow DELETE (permissive)"
     Expect.isTrue result.CanPatch "Unknown state should allow PATCH (permissive)"
     ```

     **Test 5 -- No map (None) returns permissive default**:
     ```fsharp
     let result = AffordanceHelper.affordancesFor "/games/{gameId}" "XTurn" None
     Expect.isTrue result.CanPost "No map should allow POST (permissive)"
     Expect.isTrue result.CanDelete "No map should allow DELETE (permissive)"
     ```

     **Test 6 -- Wildcard key for stateless resource**:
     ```fsharp
     let statelessMap =
         { ticTacToeMap with
             Entries =
                 Map.ofList [
                     "/health|*",
                     { AllowedMethods = [ "GET" ]
                       LinkRelations = []
                       ProfileUrl = None }
                 ] }
     let result = AffordanceHelper.affordancesFor "/health" "anything" (Some statelessMap)
     // Should NOT match exact key "/health|anything", but SHOULD match wildcard "/health|*"
     Expect.isTrue result.CanGet "Wildcard should resolve GET"
     Expect.isFalse result.CanPost "Wildcard should not allow POST"
     ```

     **Test 7 -- Empty string state key falls to wildcard or permissive**:
     ```fsharp
     let result = AffordanceHelper.affordancesFor "/games/{gameId}" "" (Some ticTacToeMap)
     // Empty state key should not match any entry, should fall to permissive default
     Expect.isTrue result.CanPost "Empty state key should be permissive"
     ```

     **Test 8 -- Case-insensitive method matching**:
     ```fsharp
     let lowercaseMap =
         { ticTacToeMap with
             Entries =
                 Map.ofList [
                     "/items|*",
                     { AllowedMethods = [ "get"; "post" ]
                       LinkRelations = []
                       ProfileUrl = None }
                 ] }
     let result = AffordanceHelper.affordancesFor "/items" "*" (Some lowercaseMap)
     Expect.isTrue result.CanGet "Lowercase 'get' should set CanGet"
     Expect.isTrue result.CanPost "Lowercase 'post' should set CanPost"
     ```

  5. Run tests: `dotnet test <test-project-path>`

- **Files**: Test file in appropriate test project (NEW, ~150-200 lines)
- **Notes**:
  - The test uses the same tic-tac-toe domain types from `test/Frank.Statecharts.Tests/StatefulResourceTests.fs` as reference for realistic test data, but does NOT depend on those types -- the affordance map is just strings.
  - Consider also testing `loadFromJson` if implemented in T060 -- round-trip a JSON affordance map through serialization and verify lookups work.
  - Edge case: route template with special characters (e.g., `/api/v2/games/{gameId:guid}`) should work since the `|` separator is not valid in route templates.

---

## Test Strategy

- **Unit tests**: All tests are pure unit tests -- no HTTP server, no middleware, no I/O. The `affordancesFor` function is pure (Map lookup + record construction).
- **Test framework**: Use Expecto (matching existing Frank test conventions).
- **Test data**: Mock `AffordanceMap` records constructed inline in the test module. No fixtures or external files needed.
- **Commands**:
  ```bash
  dotnet build src/Frank.Datastar/Frank.Datastar.fsproj
  dotnet test <test-project-path>
  ```
- **Coverage targets**: All three lookup tiers (exact match, wildcard match, permissive default), all five `Can*` booleans, empty/None edge cases.

---

## Risks & Mitigations

| Risk | Mitigation |
|------|------------|
| Type duplication between `Frank.Datastar.AffordanceHelper` and `Frank.Affordances.AffordanceMap` leads to drift | Types are simple records with string/list fields. The affordance map JSON schema (`contracts/affordance-map-schema.json`) is the source of truth. Add a comment in both locations referencing the schema. |
| `System.Text.Json` deserialization of the affordance map JSON may not handle F# records directly | Use `JsonSerializerOptions` with `JsonFSharpConverter` from `FSharp.SystemTextJson` if available, or use manual `JsonDocument` parsing (matching existing CLI patterns). |
| Developers forget to pass the affordance map to the helper | Document in the module comment that `None` gives permissive defaults -- the app works without the map, it just doesn't restrict controls. |
| The `|` separator collides with a route template character | The `|` character is not valid in RFC 6570 URI templates or ASP.NET Core route templates. Safe to use. |

---

## Review Guidance

- Verify the `AffordanceHelper.fs` module is self-contained with no project references to `Frank.Affordances` or `Frank.Cli.Core`.
- Verify the permissive default returns ALL methods as available and an empty `LinkRelations` list.
- Verify the three-tier lookup: exact key, wildcard key, permissive default.
- Verify the `CanPost`/`CanPut`/`CanDelete`/`CanPatch` booleans are case-insensitive.
- Verify the compile order in `Frank.Datastar.fsproj` places `AffordanceHelper.fs` correctly.
- Verify all 8 test cases cover the documented edge cases.
- Run `dotnet build src/Frank.Datastar/Frank.Datastar.fsproj` to verify clean build.
- Cross-check: existing Datastar tests still pass after adding the new file.

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
- 2026-03-19T04:06:23Z – claude-opus – shell_pid=26096 – lane=doing – Assigned agent via workflow command
- 2026-03-19T04:13:16Z – claude-opus – shell_pid=26096 – lane=for_review – Ready for review: AffordanceHelper.fs with types, 3-tier lookup, permissive defaults, and 8 test cases
- 2026-03-19T04:25:24Z – claude-opus – shell_pid=28806 – lane=doing – Started review via workflow command
- 2026-03-19T04:26:02Z – claude-opus – shell_pid=28806 – lane=done – Review passed: Self-contained AffordanceHelper with correct 3-tier lookup, permissive defaults, case-insensitive methods, proper compile order, and 8 comprehensive tests
