# Pipeline Cleanup Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Clean up the extraction pipeline boundary — `loadOrExtract` writes cache on miss and returns a named record; `enrichWithSpecTransitions` separates I/O from pure transformation.

**Architecture:** Two orthogonal changes to `UnifiedExtractor.fs`. Change 1 adds cache-write-on-miss to `loadOrExtract` with a `LoadResult` record return type. Change 2 extracts a pure `applySpecTransitions` function from `enrichWithSpecTransitions`, moving file I/O into the impure `extract` shell. Both changes update callers and tests. Spec files are added to the cache source hash for correctness.

**Tech Stack:** F#, Expecto, ASP.NET Core TestHost, MessagePack

---

### Task 1: Add spec file extensions to `computeSourceHash`

Pre-existing bug: changing a `.smcat`/`.wsd`/`.scxml` file doesn't invalidate the cache because `computeSourceHash` only hashes `.fs` and `.fsproj` files.

**Files:**
- Modify: `src/Frank.Cli.Core/Unified/UnifiedCache.fs:60-86`
- Test: `test/Frank.Cli.Core.Tests/Unified/UnifiedCacheTests.fs`

- [ ] **Step 1: Write failing test — spec file changes invalidate source hash**

Note: `UnifiedCacheTests.fs` uses `setupTempProject()` (creates `.fs` + `.fsproj` files) and its own `cleanup`. Use that helper, not `setupTempDir` from SpecCoExtractionTests.

```fsharp
testCase "spec file changes affect source hash"
<| fun _ ->
    let dir = setupTempProject ()
    try
        let hash1 = UnifiedCache.computeSourceHash dir

        // Add a specs/ directory with a .smcat file
        let specsDir = Path.Combine(dir, "specs")
        Directory.CreateDirectory(specsDir) |> ignore
        File.WriteAllText(Path.Combine(specsDir, "game.smcat"), "XTurn => OTurn;")
        let hash2 = UnifiedCache.computeSourceHash dir

        Expect.notEqual hash2 hash1 "Adding spec file should change hash"

        // Modify the spec file
        File.WriteAllText(Path.Combine(specsDir, "game.smcat"), "XTurn => OTurn: move;")
        let hash3 = UnifiedCache.computeSourceHash dir

        Expect.notEqual hash3 hash2 "Modifying spec file should change hash"
    finally
        cleanup dir
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test test/Frank.Cli.Core.Tests/ --filter "spec file changes affect source hash"`
Expected: FAIL — hashes are equal because spec files aren't included

- [ ] **Step 3: Add spec file extensions to `computeSourceHash`**

In `UnifiedCache.fs`, after the `.fsproj` hashing block (line 83), add:

```fsharp
    // Also hash spec files in specs/ directory (spec changes affect extraction output)
    let specsDir = Path.Combine(projectDir, "specs")

    if Directory.Exists(specsDir) then
        let specExtensions = [| "*.wsd"; "*.smcat"; "*.scxml"; "*.alps.json"; "*.alps.xml" |]

        let specFiles =
            specExtensions
            |> Array.collect (fun ext -> Directory.GetFiles(specsDir, ext))
            |> Array.sort

        for specFile in specFiles do
            let content = File.ReadAllBytes(specFile)
            hash.AppendData(content)
```

- [ ] **Step 4: Run test to verify it passes**

Run: `dotnet test test/Frank.Cli.Core.Tests/ --filter "spec file changes affect source hash"`
Expected: PASS

- [ ] **Step 5: Build the full solution**

Run: `dotnet build Frank.sln`
Expected: Success

- [ ] **Step 6: Commit**

```bash
git add src/Frank.Cli.Core/Unified/UnifiedCache.fs test/Frank.Cli.Core.Tests/Unified/UnifiedCacheTests.fs
git commit -m "fix: include spec files in cache source hash"
```

---

### Task 2: Define `LoadResult` record, refactor `loadOrExtract`, and update all 5 callers

`loadOrExtract` has 5 callers that destructure the tuple. All must be updated atomically so the build stays green.

**Files:**
- Modify: `src/Frank.Cli.Core/Unified/UnifiedExtractor.fs:853-868`
- Modify: `src/Frank.Cli.Core/Commands/ProjectCommand.fs:34,38`
- Modify: `src/Frank.Cli.Core/Commands/ValidateResourcesCommand.fs:75,77`
- Modify: `src/Frank.Cli.Core/Commands/GenerateArtifactsCommand.fs:99,101,119,121`
- Modify: `src/Frank.Cli.Core/Commands/StatechartGenerateCommand.fs:89,91`
- Modify: `src/Frank.Cli.Core/Commands/StatechartExtractCommand.fs:13,14`

- [ ] **Step 1: Define `LoadResult` in `UnifiedExtractor.fs`**

Add just above the `loadOrExtract` function (before line 853):

```fsharp
/// Result of loading or extracting resources.
type LoadResult =
    { Resources: UnifiedResource list
      FromCache: bool }
```

- [ ] **Step 2: Change `loadOrExtract` return type and add cache-write-on-miss**

Replace the current `loadOrExtract` (lines 853-868) with:

```fsharp
/// Load resources from cache if fresh, otherwise extract from source and write cache.
/// Shared by all commands that need resources (generate, validate, project).
/// Cache write failures are logged but do not fail the operation.
let loadOrExtract (projectPath: string) (force: bool) : Async<Result<LoadResult, StatechartError>> =
    async {
        if not (File.Exists projectPath) then
            return Error(FileNotFound projectPath)
        else
            let projectDir = Path.GetDirectoryName(Path.GetFullPath(projectPath))

            match UnifiedCache.tryLoadFresh projectDir force with
            | Ok state ->
                return Ok { Resources = state.Resources; FromCache = true }
            | Error _ ->
                match! extract projectPath with
                | Ok resources ->
                    match UnifiedCache.saveExtractionState projectDir resources "" [] with
                    | Ok _ -> ()
                    | Error msg -> eprintfn "Warning: failed to write cache: %s" msg

                    return Ok { Resources = resources; FromCache = false }
                | Error e -> return Error e
    }
```

Key decisions:
- Pass `""` and `[]` as `baseUri`/`vocabularies` defaults — cache is for extraction results, not projection config
- Log cache write failure via `eprintfn`, return resources regardless
- Return `LoadResult` record

- [ ] **Step 3: Update `ProjectCommand.fs`**

Line 38: Change `| Ok(resources, fromCache) ->` to `| Ok result ->`, then use `result.Resources` and `result.FromCache` throughout.

- [ ] **Step 4: Update `ValidateResourcesCommand.fs`**

Line 77: Change `| Ok(resources, fromCache) ->` to `| Ok result ->`, then use `result.FromCache` and `result.Resources`.

- [ ] **Step 5: Update `GenerateArtifactsCommand.fs`**

Two call sites. Line 101 and line 121: Change `| Ok(resources, fromCache) ->` to `| Ok result ->`, use `result.Resources` and `result.FromCache`.

- [ ] **Step 6: Update `StatechartGenerateCommand.fs`**

Line 91: Change `| Ok(resources, _) ->` to `| Ok result ->`, use `result.Resources`.

- [ ] **Step 7: Update `StatechartExtractCommand.fs`**

Line 14: Change `| Ok(resources, _) ->` to `| Ok result ->`, use `result.Resources`.

- [ ] **Step 8: Build**

Run: `dotnet build Frank.sln`
Expected: Success

- [ ] **Step 9: Run all tests**

Run: `dotnet test Frank.sln --filter "FullyQualifiedName!~Sample"`
Expected: All pass

- [ ] **Step 10: Commit**

```bash
git add src/Frank.Cli.Core/Unified/UnifiedExtractor.fs src/Frank.Cli.Core/Commands/ProjectCommand.fs src/Frank.Cli.Core/Commands/ValidateResourcesCommand.fs src/Frank.Cli.Core/Commands/GenerateArtifactsCommand.fs src/Frank.Cli.Core/Commands/StatechartGenerateCommand.fs src/Frank.Cli.Core/Commands/StatechartExtractCommand.fs
git commit -m "refactor: loadOrExtract returns LoadResult record, writes cache on miss

Update all 5 callers to destructure LoadResult record instead of raw tuple."
```

---

### Task 3: Extract pure `applySpecTransitions` from `enrichWithSpecTransitions`

This is the core purity boundary change. The current `enrichWithSpecTransitions` (lines 762-815) does file I/O + parsing + matching + merging. We extract the pure matching/merging logic into `applySpecTransitions`.

**Files:**
- Modify: `src/Frank.Cli.Core/Unified/UnifiedExtractor.fs:762-815`

- [ ] **Step 1: Write `applySpecTransitions` as a pure function**

Add just before `enrichWithSpecTransitions` (before line 762):

```fsharp
/// Pure function: merge transitions from pre-parsed spec documents into resources.
/// For resources with statecharts that have empty transitions, find a matching
/// spec document by state name overlap and apply its transitions and roles.
let internal applySpecTransitions
    (docs: Frank.Statecharts.Ast.StatechartDocument list)
    (resources: UnifiedResource list)
    : UnifiedResource list =
    if docs.IsEmpty then
        resources
    else
        let docsWithStates =
            docs |> List.map (fun doc -> doc, documentStateNames doc)

        resources
        |> List.map (fun r ->
            match r.Statechart with
            | Some sc when sc.Transitions.IsEmpty ->
                let matchingDoc =
                    docsWithStates
                    |> List.tryPick (fun (doc, docStates) ->
                        if matchesResource docStates r then Some doc else None)

                match matchingDoc with
                | Some doc ->
                    let transitions = TransitionExtractor.extract doc
                    let specRoles = TransitionExtractor.extractRoles doc

                    let mergedRoles =
                        if not sc.Roles.IsEmpty then sc.Roles
                        elif not specRoles.IsEmpty then specRoles
                        else []

                    { r with
                        Statechart =
                            Some
                                { sc with
                                    Transitions = transitions
                                    Roles = mergedRoles } }
                | None -> r
            | _ -> r)
```

- [ ] **Step 2: Rewrite `enrichWithSpecTransitions` to delegate to `applySpecTransitions`**

Replace the current `enrichWithSpecTransitions` (lines 762-815) with:

```fsharp
/// Co-extract transitions from spec files and merge into resources.
/// Returns enriched resources and any parse warnings.
/// This is the impure shell: does file I/O, then delegates to pure applySpecTransitions.
let internal enrichWithSpecTransitions
    (projectDir: string)
    (resources: UnifiedResource list)
    : UnifiedResource list * string list =
    let specFiles = findSpecFiles projectDir

    if specFiles.IsEmpty then
        resources, []
    else
        let parseResults = specFiles |> List.map (fun f -> f, tryParseSpecFile f)

        let parseWarnings =
            parseResults
            |> List.choose (fun (_, r) ->
                match r with
                | Error msg -> Some msg
                | Ok _ -> None)

        let docs =
            parseResults
            |> List.choose (fun (_, r) ->
                match r with
                | Ok doc -> Some doc
                | Error _ -> None)

        applySpecTransitions docs resources, parseWarnings
```

- [ ] **Step 3: Build**

Run: `dotnet build Frank.sln`
Expected: Success

- [ ] **Step 4: Run existing tests to verify behavior is preserved**

Run: `dotnet test test/Frank.Cli.Core.Tests/ --filter "Spec co-extraction"`
Expected: All 10 tests pass (behavior unchanged, just code restructured)

- [ ] **Step 5: Commit**

```bash
git add src/Frank.Cli.Core/Unified/UnifiedExtractor.fs
git commit -m "refactor: extract pure applySpecTransitions from enrichWithSpecTransitions

enrichWithSpecTransitions now does file I/O then delegates to
applySpecTransitions for the pure matching/merging logic."
```

---

### Task 4: Surface spec warnings in `extract` and move I/O to impure shell

Currently `extract` discards spec warnings with `_specWarnings` (line 842). Fix this and restructure the I/O. After this task, `extract` calls `applySpecTransitions` directly with pre-parsed docs. `enrichWithSpecTransitions` remains as a convenience wrapper used by existing tests.

**Files:**
- Modify: `src/Frank.Cli.Core/Unified/UnifiedExtractor.fs:823-851` (the `extract` function)

Note: line numbers refer to the original file. After Task 3 inserts `applySpecTransitions` (~30 lines), these will have shifted. Find the `extract` function by its `let extract (projectPath` signature.

- [ ] **Step 1: Update `extract` to surface spec warnings**

Replace the current `extract` function (lines 823-851) with:

```fsharp
/// Extract unified resource descriptions from an F# project using FCS.
/// Performs a single FCS typecheck and produces both type and behavioral data.
let extract (projectPath: string) : Async<Result<UnifiedResource list, StatechartError>> =
    async {
        if not (File.Exists projectPath) then
            return Error(FileNotFound projectPath)
        else
            match! ProjectLoader.loadProject projectPath with
            | Error msg -> return Error(AssemblyLoadError(projectPath, msg))
            | Ok loaded ->
                // Phase 1: Single-pass syntax walk
                let syntaxFindings = findAllResources loaded.ParsedFiles

                // Phase 2: Single-pass typed AST walk
                let typedResult = analyzeTypedAst loaded.CheckResults

                // Phase 3: Cross-reference and build UnifiedResource records
                let resources = buildUnifiedResources syntaxFindings typedResult

                // Phase 3.5: Co-extract transitions from spec files (I/O boundary)
                let projectDir = Path.GetDirectoryName(Path.GetFullPath(projectPath))
                let specFiles = findSpecFiles projectDir
                let parseResults = specFiles |> List.map (fun f -> f, tryParseSpecFile f)

                for _, result in parseResults do
                    match result with
                    | Error msg -> eprintfn "Warning: %s" msg
                    | Ok _ -> ()

                let docs =
                    parseResults
                    |> List.choose (fun (_, r) ->
                        match r with
                        | Ok doc -> Some doc
                        | Error _ -> None)

                let withTransitions = applySpecTransitions docs resources

                // Phase 4: Associate types with resources
                let withTypes = associateTypes withTransitions typedResult.AnalyzedTypes

                // Phase 5: Compute derived fields
                let enriched = enrichWithDerivedFields typedResult.AnalyzedTypes withTypes

                return Ok enriched
    }
```

Key changes:
- Spec file I/O moved inline (alongside `ProjectLoader.loadProject` — both impure)
- Parse warnings surfaced via `eprintfn` (constitution rule 7)
- Pure `applySpecTransitions` called with pre-parsed docs

- [ ] **Step 2: Build**

Run: `dotnet build Frank.sln`
Expected: Success

- [ ] **Step 3: Run all tests**

Run: `dotnet test Frank.sln --filter "FullyQualifiedName!~Sample"`
Expected: All pass

- [ ] **Step 4: Commit**

```bash
git add src/Frank.Cli.Core/Unified/UnifiedExtractor.fs
git commit -m "refactor: surface spec parse warnings, move I/O to impure shell

extract now does spec file I/O inline alongside ProjectLoader.loadProject,
logs parse warnings via eprintfn, then calls pure applySpecTransitions."
```

---

### Task 5: Add in-memory tests for `applySpecTransitions`

The whole point of the pure extraction: tests can now use in-memory documents without filesystem I/O.

**Files:**
- Modify: `test/Frank.Cli.Core.Tests/Unified/SpecCoExtractionTests.fs`

- [ ] **Step 1: Add `open Frank.Statecharts.Smcat` to test file**

Add after the existing `open Frank.Cli.Core.Unified.UnifiedExtractor` line:

```fsharp
open Frank.Statecharts.Smcat
```

- [ ] **Step 2: Add in-memory test list**

Add a new test list in `SpecCoExtractionTests.fs`:

```fsharp
          testList
              "applySpecTransitions (pure, in-memory)"
              [ testCase "applies transitions from matching document"
                <| fun _ ->
                    let smcat = "XTurn => OTurn: makeMove;\nOTurn => XTurn: makeMove;"

                    let doc =
                        (Frank.Statecharts.Smcat.Parser.parseSmcat smcat).Document

                    let resource = makeResourceWithStates "games" [ "XTurn"; "OTurn" ] []
                    let result = applySpecTransitions [ doc ] [ resource ]

                    match result.[0].Statechart with
                    | Some sc ->
                        Expect.isGreaterThan
                            sc.Transitions.Length
                            0
                            "Should have transitions"

                        let sources = sc.Transitions |> List.map _.Source |> Set.ofList
                        Expect.isTrue (sources.Contains "XTurn") "Should have XTurn source"
                        Expect.isTrue (sources.Contains "OTurn") "Should have OTurn source"
                    | None -> failtest "Should have statechart"

                testCase "does not overwrite existing transitions"
                <| fun _ ->
                    let smcat = "XTurn => OTurn: makeMove;"

                    let doc =
                        (Frank.Statecharts.Smcat.Parser.parseSmcat smcat).Document

                    let existing =
                        { Event = "Existing"
                          Source = "XTurn"
                          Target = "OTurn"
                          Guard = None
                          Constraint = Unrestricted }

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

                    let doc =
                        (Frank.Statecharts.Smcat.Parser.parseSmcat smcat).Document

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

                    let doc =
                        (Frank.Statecharts.Smcat.Parser.parseSmcat smcat).Document

                    let matching = makeResourceWithStates "games" [ "XTurn"; "OTurn" ] []
                    let nonMatching = makeResourceWithStates "tasks" [ "Active"; "Done" ] []
                    let result = applySpecTransitions [ doc ] [ nonMatching; matching ]

                    match result.[0].Statechart with
                    | Some sc -> Expect.isEmpty sc.Transitions "Non-matching unchanged"
                    | None -> failtest "Should have statechart"

                    match result.[1].Statechart with
                    | Some sc ->
                        Expect.isGreaterThan sc.Transitions.Length 0 "Matching gets transitions"
                    | None -> failtest "Should have statechart" ]
```

- [ ] **Step 3: Run new tests**

Run: `dotnet test test/Frank.Cli.Core.Tests/ --filter "applySpecTransitions"`
Expected: All pass

- [ ] **Step 4: Run all spec co-extraction tests**

Run: `dotnet test test/Frank.Cli.Core.Tests/ --filter "Spec co-extraction"`
Expected: All pass (old + new)

- [ ] **Step 5: Commit**

```bash
git add test/Frank.Cli.Core.Tests/Unified/SpecCoExtractionTests.fs
git commit -m "test: add in-memory tests for pure applySpecTransitions"
```

---

### Task 6: Final verification

- [ ] **Step 1: Full build**

Run: `dotnet build Frank.sln`
Expected: Success

- [ ] **Step 2: Full test suite**

Run: `dotnet test Frank.sln --filter "FullyQualifiedName!~Sample"`
Expected: All pass

- [ ] **Step 3: Fantomas format check**

Run: `dotnet fantomas --check src/ test/`
Expected: No formatting issues (or fix any that arise)
