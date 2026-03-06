---
work_package_id: WP03
title: Frank.LinkedData — Middleware & Negotiation
lane: "doing"
dependencies: [WP02]
base_branch: 002-phase1-code-review-fixes-WP02
base_commit: 4ebbaebb6b8f6bcc0bad74b37f685bafb57a1157
created_at: '2026-03-06T19:00:10.737267+00:00'
subtasks:
- T011
- T012
- T013
- T014
- T015
- T026
phase: Phase 1 - Foundation
assignee: ''
agent: "claude-opus"
shell_pid: "89475"
review_status: ''
reviewed_by: ''
history:
- timestamp: '2026-03-06T15:25:00Z'
  lane: planned
  agent: system
  shell_pid: ''
  action: Prompt generated via /spec-kitty.tasks
requirement_refs: [FR-007, FR-008, FR-009, FR-011, FR-015]
---

# Work Package Prompt: WP03 – Frank.LinkedData — Middleware & Negotiation

## ⚠️ IMPORTANT: Review Feedback Status

**Read this first if you are implementing this task!**

- **Has review feedback?**: Check the `review_status` field above. If it says `has_feedback`, scroll to the **Review Feedback** section immediately.
- **You must address all feedback** before your work is complete.

---

## Review Feedback

*[This section is empty initially. Reviewers will populate it if the work is returned from review.]*

---

## Implementation Command

```bash
spec-kitty implement WP03 --base WP02
```

Depends on WP02 — both modify `WebHostBuilderExtensions.fs`, `InstanceProjector.fs`, and `GraphLoader.fs`.

---

## Objectives & Success Criteria

- Middleware logs exceptions via `ILogger` — no silent catch-all handlers (Constitution VII)
- Unrecoverable errors fail fast (throw) — not wrapped in Result
- Accept header parsing uses `MediaTypeHeaderValue` (proper RFC 7231 parsing)
- `Assembly.GetEntryAssembly()` null is handled gracefully
- Cache keys use structural hash of RDF-relevant properties (content-addressable)
- `LinkedDataConfig.loadConfig` uses composed pipelines (no nested match pyramids)
- `GraphLoader.load` uses composed pipelines (no nested match pyramids) — FR-015 split with T015

## Context & Constraints

- **Tracking Issue**: #81 — Tier 1 (silent exception swallowing) + Tier 2 (cache keys, Accept parsing, null assembly, mutable state)
- **Constitution**: Principle VII (No Silent Exception Swallowing), Principle IV (ASP.NET Core Native — use built-in `MediaTypeHeaderValue`)
- **Research**: R3 (structural hash), R4 (MediaTypeHeaderValue), R6 (fail-fast)
- **Clarifications**:
  - Fail-fast for unrecoverable errors — do NOT wrap in Result
  - Result/Option only for expected recoverable outcomes
  - Compose with CE or piped module functions; extract once at top-level
- **Key file**: `src/Frank.LinkedData/WebHostBuilderExtensions.fs` — main target for T011, T012, T013
- **Key file**: `src/Frank.LinkedData/Rdf/InstanceProjector.fs` — T014 cache key fix
- **Key file**: `src/Frank.LinkedData/LinkedDataConfig.fs` — T015 match pyramid fix

## Subtasks & Detailed Guidance

### Subtask T011 – Replace Catch-All Exception Handler with ILogger

- **Purpose**: `linkedDataMiddleware` at line ~129 in `WebHostBuilderExtensions.fs` catches all exceptions with `with | _ ->` and silently falls back to the original JSON response. This violates Constitution VII.
- **Steps**:
  1. Open `src/Frank.LinkedData/WebHostBuilderExtensions.fs`
  2. Locate the `linkedDataMiddleware` function (line ~89-135)
  3. Find the catch-all handler (line ~129): `with | _ -> ...`
  4. Modify the middleware function to accept `ILogger` as a parameter:
     - The middleware is typically wired through `UseLinkedData`/`UseLinkedDataWith` custom operations
     - Resolve `ILogger<LinkedDataMiddleware>` (or similar) from the `IServiceProvider` at middleware registration time
  5. Replace the catch-all with specific exception handling:
     ```fsharp
     with
     | :? InvalidOperationException as ex ->
         // Recoverable: configuration issue, log and fall back
         logger.LogWarning(ex, "LinkedData content negotiation failed for {Path}", ctx.Request.Path)
         // Write original response as fallback
     | ex ->
         // Unrecoverable: let it propagate (fail-fast)
         logger.LogError(ex, "Unhandled exception in LinkedData middleware for {Path}", ctx.Request.Path)
         raise (exn("LinkedData middleware failure", ex))
     ```
  6. Consider which exceptions are truly recoverable (e.g., serialization format issues) vs unrecoverable (e.g., null reference, out of memory)
  7. For recoverable cases: log at Warning level and fall back gracefully
  8. For unrecoverable cases: log at Error level and re-raise or propagate
- **Files**: `src/Frank.LinkedData/WebHostBuilderExtensions.fs`
- **Parallel?**: Yes — independent of T012-T015
- **Notes**: The key insight from the clarification is fail-fast. Do NOT wrap exceptions in `Result`. Let unrecoverable errors surface immediately. Only catch what you can meaningfully recover from.

### Subtask T012 – Replace String.Contains with MediaTypeHeaderValue Parsing

- **Purpose**: `negotiateRdfType` uses `String.Contains("application/ld+json")` which can false-match on edge cases. ASP.NET Core provides proper RFC 7231 media type parsing.
- **Steps**:
  1. Open `src/Frank.LinkedData/WebHostBuilderExtensions.fs`
  2. Locate `negotiateRdfType` (lines ~19-23)
  3. Replace with `MediaTypeHeaderValue` parsing:
     ```fsharp
     open Microsoft.Net.Http.Headers

     let negotiateRdfType (acceptHeader: string) : string option =
         if String.IsNullOrWhiteSpace acceptHeader then None
         else
             let mediaTypes =
                 try MediaTypeHeaderValue.ParseList(Microsoft.Extensions.Primitives.StringValues(acceptHeader))
                 with _ -> System.Collections.Generic.List<MediaTypeHeaderValue>()

             let supported = [
                 "application/ld+json"
                 "text/turtle"
                 "application/rdf+xml"
             ]

             mediaTypes
             |> Seq.sortByDescending (fun mt -> mt.Quality |> Option.ofNullable |> Option.defaultValue 1.0)
             |> Seq.tryPick (fun mt ->
                 supported |> List.tryFind (fun s ->
                     mt.MediaType.Equals(s, StringComparison.OrdinalIgnoreCase)))
     ```
  4. This handles quality factors, case-insensitive matching, and proper media type parsing
  5. Handle malformed Accept headers gracefully (empty list, not an exception)
- **Files**: `src/Frank.LinkedData/WebHostBuilderExtensions.fs`
- **Parallel?**: Yes — independent of T011, T013-T015
- **Notes**: `MediaTypeHeaderValue` is in `Microsoft.Net.Http.Headers` which is part of the ASP.NET Core framework reference (already available). `StringValues` is in `Microsoft.Extensions.Primitives`.

### Subtask T013 – Handle Null Assembly.GetEntryAssembly()

- **Purpose**: `Assembly.GetEntryAssembly()` returns null in test host scenarios. The current code at line ~144 in `WebHostBuilderExtensions.fs` doesn't check for null.
- **Steps**:
  1. Open `src/Frank.LinkedData/WebHostBuilderExtensions.fs`
  2. Locate `Assembly.GetEntryAssembly()` usage (line ~144)
  3. Determine what the assembly is used for (likely finding embedded resources)
  4. Add null handling:
     ```fsharp
     let assembly =
         Assembly.GetEntryAssembly()
         |> Option.ofObj
         |> Option.defaultWith (fun () ->
             // Fallback: use the calling assembly or a well-known assembly
             Assembly.GetExecutingAssembly())
     ```
  5. Alternatively, if the assembly is critical and there's no reasonable fallback:
     ```fsharp
     let assembly =
         match Assembly.GetEntryAssembly() with
         | null -> failwith "Could not determine entry assembly. This may occur in test host scenarios — provide the assembly explicitly."
         | asm -> asm
     ```
  6. Choose based on whether a fallback makes sense for the use case. If loading embedded resources, `GetExecutingAssembly()` may not have them — fail-fast may be better.
- **Files**: `src/Frank.LinkedData/WebHostBuilderExtensions.fs`
- **Parallel?**: Yes — independent of T011-T012, T014-T015
- **Notes**: Per fail-fast clarification, if there's no meaningful fallback, throw with a clear message rather than silently using a wrong assembly.

### Subtask T014 – Replace RuntimeHelpers.GetHashCode with Structural Hash

- **Purpose**: `RuntimeHelpers.GetHashCode` returns identity hash codes that are not unique (collision-prone) and not stable. Need content-addressable cache keys based on RDF-relevant properties.
- **Steps**:
  1. Open `src/Frank.LinkedData/Rdf/InstanceProjector.fs`
  2. Locate cache key generation using `RuntimeHelpers.GetHashCode`
  3. Understand what properties are being cached (the object → RDF triple projection result)
  4. Implement structural hash:
     ```fsharp
     let private computeCacheKey (obj: obj) : int =
         match obj with
         | :? IStructuralEquatable as se ->
             // F# records implement IStructuralEquatable
             se.GetHashCode(StructuralComparisons.StructuralEqualityComparer)
         | _ ->
             // Fallback: hash the JSON serialization of the object
             // or hash specific RDF-relevant properties
             obj.GetHashCode()
     ```
  5. Consider whether `IStructuralEquatable` is sufficient for the types being cached (F# records should implement it)
  6. If the cached types are always F# records, structural equality/hash is already correct
  7. Update the cache dictionary to use this new key
  8. Verify that objects with the same structural content produce the same key
- **Files**: `src/Frank.LinkedData/Rdf/InstanceProjector.fs`
- **Parallel?**: Yes — independent of T011-T013, T015
- **Notes**: For F# records, `obj.GetHashCode()` already uses structural equality. The issue is that `RuntimeHelpers.GetHashCode` specifically bypasses this to use identity hash. Simply using the object's own `GetHashCode()` may be sufficient if the types are F# records.

### Subtask T015 – Replace Nested Match Pyramids in LinkedDataConfig.loadConfig

- **Purpose**: `LinkedDataConfig.loadConfig` has nested `match` statements creating a pyramid. Replace with composed pipelines using CE or piped module functions.
- **Steps**:
  1. Open `src/Frank.LinkedData/LinkedDataConfig.fs`
  2. Locate `loadConfig` function — identify the nested match pyramid
  3. Determine the types involved (likely `Result`, `Option`, or both)
  4. If not already added, add `FsToolkit.ErrorHandling` to `Frank.LinkedData.fsproj`:
     ```xml
     <PackageReference Include="FsToolkit.ErrorHandling" Version="<latest>" />
     ```
  5. Refactor using the appropriate CE:
     ```fsharp
     open FsToolkit.ErrorHandling

     let loadConfig (options: LinkedDataOptions) =
         result {
             let! manifest = loadManifest options.Assembly
             let! ontology = loadOntology manifest
             let! shapes = loadShapes manifest
             let! baseUri = validateBaseUri options.BaseUri
             return {
                 OntologyGraph = ontology
                 ShapesGraph = shapes
                 BaseUri = baseUri
                 Manifest = manifest
             }
         }
     ```
  6. Or use piped `Result.bind` if the chain is simpler:
     ```fsharp
     loadManifest options.Assembly
     |> Result.bind (fun manifest -> ...)
     ```
  7. Extract/unwrap the final `Result` only at the top-level call site (where `loadConfig` is called from middleware setup)
  8. Do NOT use `Result.defaultValue` or force-unwrap mid-function
- **Files**: `src/Frank.LinkedData/LinkedDataConfig.fs`
- **Parallel?**: Yes — independent of T011-T014
- **Notes**: Per clarification, both CE and piped module functions are acceptable. Choose whichever reads more naturally for the specific chain. The key rule is: compose within the body, extract once at the boundary.

### Subtask T026 – Replace Nested Match Pyramids in GraphLoader.load

- **Purpose**: `GraphLoader.load` has nested `match` statements. Replace with composed pipelines using FsToolkit.ErrorHandling CEs. This is the second half of FR-015 (T015 covers `LinkedDataConfig`, T026 covers `GraphLoader`).
- **Steps**:
  1. Open `src/Frank.LinkedData/Rdf/GraphLoader.fs`
  2. Locate the `load` function — identify the nested match pyramid
  3. Determine the wrapper types involved (`Result`, `Option`, or mixed)
  4. FsToolkit.ErrorHandling should already be in `Frank.LinkedData.fsproj` from T015
  5. Refactor using the appropriate CE:
     ```fsharp
     open FsToolkit.ErrorHandling

     let load (assembly: Assembly) =
         result {
             let! manifest = loadManifest assembly
             let! ontologyStream = findResource assembly manifest.OntologyResource
             use reader = new StreamReader(ontologyStream)  // Constitution VI
             let! ontology = parseOntology reader
             let! shapesStream = findResource assembly manifest.ShapesResource
             let! shapes = parseShapes shapesStream
             return { Ontology = ontology; Shapes = shapes; Manifest = manifest }
         }
     ```
  6. Or use piped `Result.bind` / `Option.bind` if simpler
  7. Key rules:
     - Do NOT use `Async.RunSynchronously` mid-function
     - Do NOT use `Option.get` or `Result.defaultValue` mid-function
     - Compose naturally; extract once at the call site
     - If some steps return `Option` and others `Result`, use `Result.requireSome` to unify
- **Files**: `src/Frank.LinkedData/Rdf/GraphLoader.fs`
- **Parallel?**: No — depends on T015 (FsToolkit.ErrorHandling already added)
- **Notes**: WP02 T008 also modifies this file (StreamReader disposal). Since WP03 depends on WP02, the `use` binding from T008 should already be in place. Incorporate it into the CE refactor (the `use reader = ...` line in the example above).

## Risks & Mitigations

- **T011**: Changing error behavior will surface previously-hidden failures. This is intentional — Constitution VII requires visibility. Document in PR description.
- **T012**: `MediaTypeHeaderValue.ParseList` may throw on severely malformed input. Handle with try/catch returning empty list.
- **T014**: Structural hash performance — if `InstanceProjector` is on a hot path, benchmark the change. F# record `GetHashCode()` should be fast.
- **T015**: Adding FsToolkit.ErrorHandling to `Frank.LinkedData` is a new dependency for the library. Acceptable per planning decision, but note it in the PR.

## Review Guidance

- Verify no `with | _ ->` catch-all handlers remain in `WebHostBuilderExtensions.fs`
- Verify `negotiateRdfType` correctly handles: valid Accept, multiple types with quality factors, empty Accept, malformed Accept
- Verify null assembly produces clear error (not NullReferenceException)
- Verify cache key uniqueness: same content → same key, different content → different key
- Verify `loadConfig` has no nested match pyramid; uses CE or piped composition

## Activity Log

- 2026-03-06T15:25:00Z – system – lane=planned – Prompt created.
- 2026-03-06T19:00:10Z – claude-opus – shell_pid=89475 – lane=doing – Assigned agent via workflow command
