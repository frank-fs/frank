---
work_package_id: WP03
title: FCS Caching & Unified State Persistence
lane: "done"
dependencies: [WP01]
base_branch: 031-unified-resource-pipeline-WP01
base_commit: bd0722e27d0fa12e9ab2ad5cdbbcb7b8e6d5fed6
created_at: '2026-03-19T03:24:54.775727+00:00'
subtasks:
- T013
- T014
- T015
- T016
- T017
- T018
phase: Phase 1 - Core Pipeline
assignee: ''
agent: "claude-opus-wp03-review"
shell_pid: "19979"
review_status: "approved"
reviewed_by: "Ryan Riley"
history:
- timestamp: '2026-03-19T02:15:00Z'
  lane: planned
  agent: system
  shell_pid: ''
  action: Prompt generated via /spec-kitty.tasks
requirement_refs:
- FR-005
- FR-006
- FR-025
- FR-026
- FR-027
- FR-028
---

# Work Package Prompt: WP03 -- FCS Caching & Unified State Persistence

## IMPORTANT: Review Feedback Status

**Read this first if you are implementing this task!**

- **Has review feedback?**: Check the `review_status` field above. If it says `has_feedback`, scroll to the **Review Feedback** section immediately (right below this notice).
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
Use language identifiers in code blocks: ````fsharp`, ````xml`, ````bash`

---

## Implementation Command

Depends on WP01 (shares types only, does NOT depend on WP02):

```bash
spec-kitty implement WP03 --base WP01
```

---

## NOTE: Parallel with WP02

WP03 can run **in parallel** with WP02. Both depend only on WP01 (types). WP03 implements cache read/write for `UnifiedExtractionState`; WP02 implements the extractor that produces it. They converge in WP04 which wires them together.

---

## Objectives & Success Criteria

1. Create `UnifiedCache.fs` module that serializes/deserializes `UnifiedExtractionState` to/from MessagePack binary.
2. Implement source hash computation by hashing all `.fs` source files in the project.
3. Implement staleness detection by comparing the source hash in the cache against the current source files.
4. Implement cache read path: load from `obj/frank-cli/unified-state.bin`, deserialize, return if fresh.
5. Implement cache write path: serialize and write after extraction.
6. Implement `--force` flag to bypass cache.

**Success**: `UnifiedCache.tryLoad` returns `Some state` when the cache exists and source hash matches, `None` when stale or missing. `UnifiedCache.save` writes the binary cache. Sequential commands (extract then generate) skip FCS on the second call when source is unchanged.

---

## Context & Constraints

- **Spec**: `kitty-specs/031-unified-resource-pipeline/spec.md` (FR-005, FR-025 through FR-028, User Story 7)
- **Plan**: `kitty-specs/031-unified-resource-pipeline/plan.md` (cache file location: `obj/frank-cli/unified-state.bin`)
- **Research**: `kitty-specs/031-unified-resource-pipeline/research.md` (R1: MessagePack decision)
- **Data Model**: `kitty-specs/031-unified-resource-pipeline/data-model.md` (UnifiedExtractionState with SourceHash, ToolVersion)
- **Existing cache pattern**: `src/Frank.Cli.Core/State/ExtractionState.fs` uses JSON (`state.json`). The new cache uses binary (`unified-state.bin`). The old format is still read by semantic subcommands until they're migrated.
- **Existing staleness checker**: `src/Frank.Cli.Core/Commands/StalenessChecker.fs` -- check if it has reusable hash computation logic.
- **Key constraints**:
  - Cache format MUST be forward-compatible (FR-028). Include `ToolVersion` in the state. If the cached version doesn't match the current CLI version, treat as stale and re-extract.
  - Cache path: `obj/frank-cli/unified-state.bin` (relative to project directory).
  - MessagePack options must match exactly between write and read (same resolver composition).
  - Source hash MUST include all `.fs` files in the project (not just modified ones) to detect additions, deletions, and modifications.

---

## Subtasks & Detailed Guidance

### Subtask T013 -- Create `UnifiedCache.fs` with MessagePack serialization

- **Purpose**: Implement the binary serialization module that reads and writes `UnifiedExtractionState`.
- **Steps**:
  1. Create `src/Frank.Cli.Core/Unified/UnifiedCache.fs`.
  2. Module: `module Frank.Cli.Core.Unified.UnifiedCache`.
  3. Define MessagePack options (must be shared between read and write):

```fsharp
module Frank.Cli.Core.Unified.UnifiedCache

open System
open System.IO
open MessagePack
open MessagePack.Resolvers
open MessagePack.FSharp

/// MessagePack serialization options for F# types.
/// FSharpResolver handles DUs, options, lists, maps.
/// ContractlessStandardResolver handles public records without attributes.
let private msgpackOptions =
    MessagePackSerializerOptions
        .Standard
        .WithResolver(
            CompositeResolver.Create(
                FSharpResolver.Instance,
                ContractlessStandardResolver.Instance))
```

  4. Implement `save`:

```fsharp
/// Cache file path relative to the project directory.
let cacheFileName = "unified-state.bin"

/// Compute the full cache path for a project.
let cachePath (projectDir: string) : string =
    Path.Combine(projectDir, "obj", "frank-cli", cacheFileName)

/// Serialize and write UnifiedExtractionState to binary cache file.
let save (path: string) (state: UnifiedExtractionState) : Result<unit, string> =
    try
        let dir = Path.GetDirectoryName(path)
        if not (Directory.Exists(dir)) then
            Directory.CreateDirectory(dir) |> ignore

        let bytes = MessagePackSerializer.Serialize(state, msgpackOptions)
        File.WriteAllBytes(path, bytes)
        Ok ()
    with ex ->
        Error $"Failed to write cache: {ex.Message}"
```

  5. Implement `load`:

```fsharp
/// Deserialize UnifiedExtractionState from binary cache file.
/// Returns None if file doesn't exist.
/// Returns Error if deserialization fails.
let load (path: string) : Result<UnifiedExtractionState option, string> =
    try
        if not (File.Exists(path)) then
            Ok None
        else
            let bytes = File.ReadAllBytes(path)
            let state = MessagePackSerializer.Deserialize<UnifiedExtractionState>(bytes, msgpackOptions)
            Ok (Some state)
    with ex ->
        Error $"Failed to read cache: {ex.Message}"
```

- **Files**: `src/Frank.Cli.Core/Unified/UnifiedCache.fs` (NEW)
- **Notes**:
  - `File.WriteAllBytes` is atomic enough for our purposes (single writer). If concurrent CLI instances are a concern, use a temp file + rename pattern. Not needed for Phase 1.
  - The `load` function returns `Result<option, string>` to distinguish three cases: (1) `Ok None` = file doesn't exist, (2) `Ok (Some state)` = loaded successfully, (3) `Error msg` = file exists but is corrupt/incompatible.
  - If MessagePack deserialization fails due to schema evolution (new fields added), it will throw. The `with ex` handler converts this to `Error`, which triggers re-extraction.

### Subtask T014 -- Implement source hash computation

- **Purpose**: Compute a deterministic hash of all `.fs` source files in the project for staleness detection.
- **Steps**:
  1. Add a hash computation function:

```fsharp
open System.Security.Cryptography

/// Compute a SHA256 hash of all .fs source files in a project directory.
/// Files are sorted by path to ensure deterministic ordering.
/// The hash covers file content, not timestamps (content-based staleness).
let computeSourceHash (projectDir: string) : string =
    use sha256 = SHA256.Create()

    // Find all .fs files in the project directory (excluding obj/ and bin/)
    let sourceFiles =
        Directory.GetFiles(projectDir, "*.fs", SearchOption.AllDirectories)
        |> Array.filter (fun f ->
            let rel = Path.GetRelativePath(projectDir, f)
            not (rel.StartsWith("obj" + string Path.DirectorySeparatorChar)) &&
            not (rel.StartsWith("bin" + string Path.DirectorySeparatorChar)) &&
            not (rel.StartsWith("obj/")) &&
            not (rel.StartsWith("bin/")))
        |> Array.sort  // Deterministic ordering

    // Hash each file's content in sequence
    for file in sourceFiles do
        let content = File.ReadAllBytes(file)
        sha256.TransformBlock(content, 0, content.Length, null, 0) |> ignore

    // Also hash the .fsproj file itself (compile order changes matter)
    let fsprojFiles = Directory.GetFiles(projectDir, "*.fsproj")
    for fsproj in fsprojFiles do
        let content = File.ReadAllBytes(fsproj)
        sha256.TransformBlock(content, 0, content.Length, null, 0) |> ignore

    sha256.TransformFinalBlock(Array.empty, 0, 0) |> ignore
    Convert.ToHexString(sha256.Hash).ToLowerInvariant()
```

  2. Alternative approach using `IncrementalHash`:

```fsharp
let computeSourceHash (projectDir: string) : string =
    use hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256)

    let sourceFiles =
        Directory.GetFiles(projectDir, "*.fs", SearchOption.AllDirectories)
        |> Array.filter (fun f ->
            let rel = Path.GetRelativePath(projectDir, f)
            not (rel.StartsWith("obj" + string Path.DirectorySeparatorChar)) &&
            not (rel.StartsWith("bin" + string Path.DirectorySeparatorChar)) &&
            not (rel.StartsWith("obj/")) &&
            not (rel.StartsWith("bin/")))
        |> Array.sort

    for file in sourceFiles do
        let content = File.ReadAllBytes(file)
        hash.AppendData(content)

    let fsprojFiles = Directory.GetFiles(projectDir, "*.fsproj")
    for fsproj in fsprojFiles do
        let content = File.ReadAllBytes(fsproj)
        hash.AppendData(content)

    let hashBytes = hash.GetHashAndReset()
    Convert.ToHexString(hashBytes).ToLowerInvariant()
```

  3. Use whichever approach compiles cleanly with the target frameworks. `IncrementalHash` is available on all three targets.

- **Files**: `src/Frank.Cli.Core/Unified/UnifiedCache.fs` (continued)
- **Notes**:
  - Content-based hashing (not timestamp-based) ensures that a `git checkout` that changes timestamps but not content doesn't invalidate the cache.
  - Including the `.fsproj` file catches compile order changes (adding/removing/reordering `<Compile>` entries).
  - Excluding `obj/` and `bin/` prevents generated files (assembly info, etc.) from invalidating the cache.
  - The hash is deterministic: same files with same content always produce the same hash, regardless of platform.
  - For large projects (50+ files), this reads all files into memory. For a typical F# project, this is <10MB total and completes in milliseconds. Not a concern.

### Subtask T015 -- Implement staleness detection

- **Purpose**: Compare the cached source hash against the current source files to determine if the cache is fresh.
- **Steps**:
  1. Implement staleness check:

```fsharp
/// CLI version for cache compatibility checking.
/// Update this when UnifiedExtractionState schema changes.
let currentToolVersion = "7.1.0"

/// Check if a cached state is still fresh.
/// Returns true if the cache is stale and re-extraction is needed.
type StalenessReason =
    | CacheNotFound
    | SourceHashMismatch of cached: string * current: string
    | ToolVersionMismatch of cached: string * current: string
    | ForcedRefresh
    | CacheCorrupt of message: string

/// Check whether the cache is fresh for the given project.
let checkStaleness
    (projectDir: string)
    (force: bool)
    (cached: Result<UnifiedExtractionState option, string>)
    : Result<UnifiedExtractionState, StalenessReason> =

    if force then
        Error ForcedRefresh
    else
        match cached with
        | Error msg ->
            Error (CacheCorrupt msg)
        | Ok None ->
            Error CacheNotFound
        | Ok (Some state) ->
            // Check tool version compatibility
            if state.ToolVersion <> currentToolVersion then
                Error (ToolVersionMismatch(state.ToolVersion, currentToolVersion))
            else
                // Check source hash
                let currentHash = computeSourceHash projectDir
                if state.SourceHash <> currentHash then
                    Error (SourceHashMismatch(state.SourceHash, currentHash))
                else
                    Ok state
```

  2. The caller will use this:

```fsharp
// In the command that needs unified state:
let projectDir = Path.GetDirectoryName(projectPath)
let cacheFile = UnifiedCache.cachePath projectDir
let cached = UnifiedCache.load cacheFile
match UnifiedCache.checkStaleness projectDir force cached with
| Ok state ->
    // Use cached state -- skip FCS
    state
| Error reason ->
    // Log reason, run FCS extraction, write cache
    let! resources = UnifiedExtractor.extract projectPath
    // ...
```

- **Files**: `src/Frank.Cli.Core/Unified/UnifiedCache.fs` (continued)
- **Notes**:
  - `StalenessReason` is a DU for structured reporting. The CLI can display different messages based on why re-extraction is needed.
  - `currentToolVersion` should match the CLI's version. For now, hardcode. In the future, this could be derived from the assembly version.
  - Tool version mismatch forces re-extraction. This is FR-028: "newer CLI versions MUST be able to detect incompatibility and re-extract."
  - The staleness check computes the source hash even when checking freshness. This is fast (milliseconds) compared to FCS analysis (seconds). Acceptable trade-off.

### Subtask T016 -- Implement cache read path

- **Purpose**: Provide a high-level function that loads the cache, checks freshness, and returns the state if fresh.
- **Steps**:
  1. Implement `tryLoadFresh`:

```fsharp
/// Try to load a fresh cached state. Returns None if cache is stale or missing.
/// Logs the staleness reason for diagnostics.
let tryLoadFresh
    (projectDir: string)
    (force: bool)
    : UnifiedExtractionState option * StalenessReason option =

    let cacheFile = cachePath projectDir
    let cached = load cacheFile

    match checkStaleness projectDir force cached with
    | Ok state ->
        (Some state, None)
    | Error reason ->
        (None, Some reason)
```

  2. The return type is a tuple `(state option, reason option)` so the caller can log the reason when re-extracting.

  3. Alternative: return a `Result`:

```fsharp
/// Try to load a fresh cached state.
/// Returns Ok(state) if cache is fresh, Error(reason) if stale/missing.
let tryLoadFresh (projectDir: string) (force: bool) : Result<UnifiedExtractionState, StalenessReason> =
    let cacheFile = cachePath projectDir
    let cached = load cacheFile
    checkStaleness projectDir force cached
```

  Use the `Result` approach -- it's more idiomatic F# and composes with `Result.bind`.

- **Files**: `src/Frank.Cli.Core/Unified/UnifiedCache.fs` (continued)
- **Notes**:
  - The `tryLoadFresh` function is the public API for commands. They call it first; if `Error`, they run extraction and save.

### Subtask T017 -- Implement cache write path

- **Purpose**: After FCS extraction, serialize the `UnifiedExtractionState` and write it to the cache file.
- **Steps**:
  1. Implement `saveExtractionState`:

```fsharp
/// Save unified extraction state to cache and return the state.
/// Creates the obj/frank-cli/ directory if needed.
let saveExtractionState
    (projectDir: string)
    (resources: UnifiedResource list)
    (baseUri: string)
    (vocabularies: string list)
    : Result<UnifiedExtractionState, string> =

    let sourceHash = computeSourceHash projectDir
    let state : UnifiedExtractionState =
        { Resources = resources
          SourceHash = sourceHash
          BaseUri = baseUri
          Vocabularies = vocabularies
          ExtractedAt = DateTimeOffset.UtcNow
          ToolVersion = currentToolVersion }

    let cacheFile = cachePath projectDir
    match save cacheFile state with
    | Ok () -> Ok state
    | Error msg -> Error msg
```

  2. This function is called after the unified extractor completes:

```fsharp
// In the extract command:
let! resources = UnifiedExtractor.extract projectPath
match resources with
| Ok resources ->
    let projectDir = Path.GetDirectoryName(projectPath)
    match UnifiedCache.saveExtractionState projectDir resources baseUri vocabularies with
    | Ok state -> // success -- state is cached and returned
    | Error msg -> // cache write failed, but extraction succeeded -- warn and continue
```

  3. Note: cache write failure should NOT fail the extraction. The extraction result is valid even if caching fails. Log a warning and return the state.

- **Files**: `src/Frank.Cli.Core/Unified/UnifiedCache.fs` (continued)
- **Notes**:
  - `saveExtractionState` computes the source hash at save time, not at extraction time. This ensures the hash reflects the exact state of source files when the cache was written.
  - The `baseUri` and `vocabularies` parameters come from CLI arguments (same as the current `ExtractCommand`).
  - If the `obj/frank-cli/` directory doesn't exist, `save` creates it (see T013).

### Subtask T018 -- Implement `--force` flag support

- **Purpose**: Allow users to bypass the cache and force re-extraction (FR-027).
- **Steps**:
  1. The `--force` flag is passed through the `force: bool` parameter in `tryLoadFresh` and `checkStaleness`. When `force = true`, `checkStaleness` immediately returns `Error ForcedRefresh`.
  2. This is already implemented in T015's `checkStaleness` function.
  3. The CLI wiring (WP04) will parse `--force` from the command line and pass it through.
  4. Write a test that verifies forced refresh bypasses a valid cache:

```fsharp
testCase "force flag bypasses valid cache" <| fun _ ->
    // Set up: create a valid cache file with matching source hash
    let projectDir = setupTempProject ()
    let state = createSampleState (UnifiedCache.computeSourceHash projectDir)
    UnifiedCache.save (UnifiedCache.cachePath projectDir) state |> ignore

    // Without force: cache is fresh
    let result = UnifiedCache.tryLoadFresh projectDir false
    Expect.isOk result "Should load from cache without force"

    // With force: cache is bypassed
    let forced = UnifiedCache.tryLoadFresh projectDir true
    Expect.isError forced "Should bypass cache with force"
    match forced with
    | Error ForcedRefresh -> () // expected
    | other -> failtest $"Expected ForcedRefresh, got {other}"
```

  5. Also add the `--force` entry to the compile list. The entire `UnifiedCache.fs` needs a compile entry in `Frank.Cli.Core.fsproj`:

```xml
<!-- Unified pipeline -->
<Compile Include="Unified/UnifiedModel.fs" />
<Compile Include="Unified/UnifiedCache.fs" />
```

Place `UnifiedCache.fs` after `UnifiedModel.fs` (it depends on `UnifiedExtractionState` from `UnifiedModel`).

- **Files**: `src/Frank.Cli.Core/Unified/UnifiedCache.fs` (completed), `src/Frank.Cli.Core/Frank.Cli.Core.fsproj` (MODIFIED)
- **Notes**:
  - The `StalenessReason.ForcedRefresh` variant lets the CLI display a specific message like "Cache bypassed (--force)" vs "Cache stale (source changed)" vs "No cache found".

---

## Test Strategy

- **Roundtrip tests**: Serialize a `UnifiedExtractionState`, deserialize it, verify all fields match.
- **Source hash tests**:
  - Compute hash, verify it's deterministic (same result on repeated calls).
  - Modify a `.fs` file, verify hash changes.
  - Add a new `.fs` file, verify hash changes.
  - Modify `.fsproj`, verify hash changes.
- **Staleness tests**:
  - Fresh cache: load + check returns `Ok state`.
  - Stale cache (modified source): returns `Error (SourceHashMismatch ...)`.
  - Missing cache: returns `Error CacheNotFound`.
  - Force flag: returns `Error ForcedRefresh` even with valid cache.
  - Tool version mismatch: returns `Error (ToolVersionMismatch ...)`.
- **Integration test**: Extract, cache, read back, compare resources.

```bash
dotnet build
dotnet test
```

---

## Risks & Mitigations

| Risk | Severity | Mitigation |
|------|----------|------------|
| MessagePack fails to deserialize after schema evolution (new fields added to `UnifiedExtractionState`) | Medium | Version check catches this. Tool version mismatch triggers re-extraction. In future, use MessagePack's `[<Key>]` attributes for explicit field ordering if needed. |
| Source hash computation is slow for very large projects | Low | SHA256 of <10MB of source files completes in <100ms. Not a concern for projects with up to 50 resources. |
| Cache file corruption (partial write due to crash) | Low | `File.WriteAllBytes` is near-atomic. Deserialization failure triggers re-extraction. No data loss risk (cache is regenerated). |
| Platform-specific path separator differences in hash computation | Low | `Array.sort` on full paths ensures deterministic ordering. `Path.DirectorySeparatorChar` handles platform differences in the `obj/`/`bin/` filter. |
| `AnalyzedType` contains types that MessagePack cannot serialize | Medium | WP01 T005 validates this. If it fails there, we'll have a DTO layer. By the time WP03 runs, this is resolved. |

---

## Review Guidance

- Verify `msgpackOptions` uses `FSharpResolver` first, then `ContractlessStandardResolver` (order matters for DU handling).
- Verify source hash excludes `obj/` and `bin/` directories.
- Verify source hash includes the `.fsproj` file (compile order changes should invalidate).
- Verify `checkStaleness` checks tool version BEFORE source hash (fail fast on version mismatch).
- Verify `save` creates the `obj/frank-cli/` directory if needed.
- Verify cache write failure does NOT propagate as an extraction error (warn and continue).
- Verify `dotnet build` passes cleanly.

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
- 2026-03-19T03:24:55Z – claude-opus-wp03 – shell_pid=17669 – lane=doing – Assigned agent via workflow command
- 2026-03-19T03:34:30Z – claude-opus-wp03 – shell_pid=17669 – lane=for_review – Ready for review: UnifiedCache with MessagePack serialization, source hash, staleness detection, 17 tests passing
- 2026-03-19T03:35:55Z – claude-opus-wp03-review – shell_pid=19979 – lane=doing – Started review via workflow command
- 2026-03-19T03:38:19Z – claude-opus-wp03-review – shell_pid=19979 – lane=done – Review passed: All subtasks (T013-T018, T018a) correctly implemented. MessagePack uses FSharpResolver+ContractlessStandardResolver in correct order. SHA256 source hash is deterministic, excludes obj/bin, includes .fsproj. All 5 StalenessReason cases handled with tool version checked before source hash (fail fast). Compile order correct (UnifiedCache.fs after UnifiedModel.fs). Build clean with 0 warnings, 17/17 tests pass.
