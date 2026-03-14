---
work_package_id: WP01
title: Core ETag Types & Interface
lane: done
dependencies: []
base_branch: master
base_commit: c08cbedce71702e173699650bcc6a50b87fa9ff3
created_at: '2026-03-08T17:30:59.293617+00:00'
subtasks: [T001, T002, T003, T004, T005]
shell_pid: "98096"
agent: "claude-opus"
history:
- timestamp: '2026-03-07T00:00:00Z'
  lane: planned
  agent: system
  shell_pid: ''
  action: Prompt generated via /spec-kitty.tasks
requirement_refs: [FR-007, FR-011, FR-012, FR-013, FR-014]
---

# Work Package Prompt: WP01 -- Core ETag Types & Interface

## Review Feedback Status

**Read this first if you are implementing this task!**

- **Has review feedback?**: Check the `review_status` field above. If it says `has_feedback`, scroll to the **Review Feedback** section immediately.
- **You must address all feedback** before your work is complete.
- **Mark as acknowledged**: When you understand the feedback and begin addressing it, update `review_status: acknowledged` in the frontmatter.

---

## Review Feedback

> **Populated by `/spec-kitty.review`** -- Reviewers add detailed feedback here when work needs changes.

*[This section is empty initially.]*

---

## Implementation Command

```bash
spec-kitty implement WP01
```

No dependencies -- this is the starting package.

---

## Objectives & Success Criteria

- Define `IETagProvider` non-generic interface for computing ETags from resource instance IDs
- Define `IETagProviderFactory` for resolving providers per endpoint
- Define `ETagMetadata` sealed class as endpoint metadata marker
- Implement `ETagFormat` module with RFC 9110-compliant strong ETag formatting
- Implement `ETagComparison` module with strong comparison, header parsing, and wildcard matching
- All types compile in `src/Frank/` with multi-target net8.0;net9.0;net10.0
- Unit tests pass for ETagFormat and ETagComparison logic

---

## Context & Constraints

**Reference documents**:
- `kitty-specs/008-conditional-request-etags/spec.md` -- Feature specification (FR-001 through FR-015)
- `kitty-specs/008-conditional-request-etags/data-model.md` -- Entity definitions and F# type signatures
- `kitty-specs/008-conditional-request-etags/research.md` -- Hashing strategy, RFC compliance, format decisions

**Key constraints**:
- All new code lives in `src/Frank/ETag.fs` -- a single new file in the existing Frank core library
- No new NuGet dependencies; use only `System.Security.Cryptography.SHA256` from the framework
- Strong ETags only: `"<32 hex chars>"` format (no weak ETags)
- `IETagProvider` is non-generic at the consumption site (middleware does not know `'State`/`'Context` types)
- SHA-256 truncated to 128 bits (first 16 bytes, 32 hex characters)
- Follow existing Frank.fsproj patterns for file ordering
- Tests go in `test/Frank.Tests/ETagTests.fs`

---

## Subtasks & Detailed Guidance

### Subtask T001 -- Create `ETag.fs` with `IETagProvider` and `IETagProviderFactory` interfaces

**Purpose**: Define the core abstractions that the middleware and providers depend on.

**Steps**:
1. Create `src/Frank/ETag.fs`
2. Use namespace `Frank`
3. Define the following interfaces:

```fsharp
namespace Frank

open System.Threading.Tasks
open Microsoft.AspNetCore.Http

/// Non-generic interface consumed by the middleware.
/// The middleware does not know the state/context types --
/// it only needs an ETag string for a given resource instance.
type IETagProvider =
    /// Compute the current ETag for a resource instance.
    /// Returns None if the resource has no state (e.g., deleted or not yet created).
    abstract ComputeETag: instanceId: string -> Task<string option>

/// Factory abstraction that creates IETagProvider instances for specific endpoints.
/// Enables the middleware to resolve the correct provider without generic type leakage.
type IETagProviderFactory =
    /// Create a provider for the given endpoint, or None if the endpoint
    /// does not participate in conditional request handling.
    abstract CreateProvider: endpoint: Routing.Endpoint -> IETagProvider option
```

4. Add `ETag.fs` to `Frank.fsproj` in the `<Compile>` list BEFORE `Builder.fs` (types must be available to other modules)

**Files**: `src/Frank/ETag.fs`, `src/Frank/Frank.fsproj` (modified)
**Notes**: The `IETagProvider` interface uses `Task<string option>` (not `Async`) to match ASP.NET Core conventions. The returned string must be a fully-formatted strong ETag (including quotes), e.g., `"a1b2c3..."`.

### Subtask T002 -- Define `ETagMetadata` sealed class

**Purpose**: Endpoint metadata marker that tells the middleware a resource participates in conditional request handling.

**Steps**:
1. In `src/Frank/ETag.fs`, after the interfaces, define:

```fsharp
/// Endpoint metadata marker indicating a resource participates in
/// conditional request handling. Attached during resource building.
/// The middleware checks for this marker to skip non-participating resources.
[<Sealed>]
type ETagMetadata(providerKey: string, instanceIdResolver: HttpContext -> string) =
    /// Key used to look up the IETagProvider in DI or a provider registry.
    member _.ProviderKey = providerKey

    /// Extracts the resource instance ID from the request context.
    /// Typically reads from route values, e.g.:
    ///   fun ctx -> ctx.GetRouteValue("gameId") |> string
    member _.ResolveInstanceId = instanceIdResolver
```

**Files**: `src/Frank/ETag.fs`
**Notes**:
- `[<Sealed>]` prevents inheritance (this is a data class, not an extension point)
- `ProviderKey` is a string identifier, not a type -- avoids generic leakage into metadata
- `ResolveInstanceId` uses the same `HttpContext -> string` pattern as `StateMachineMetadata` in Frank.Statecharts
- The middleware discovers this via `endpoint.Metadata.GetMetadata<ETagMetadata>()`

### Subtask T003 -- Implement `ETagFormat` module

**Purpose**: Pure functions for RFC 9110-compliant strong ETag formatting and SHA-256 hashing.

**Steps**:
1. In `src/Frank/ETag.fs`, define the `ETagFormat` module:

```fsharp
open System
open System.Security.Cryptography

/// Pure functions for RFC 9110 Section 8.8.3 ETag formatting.
/// Strong ETags only: "opaque-tag" format with double quotes.
module ETagFormat =
    /// Wrap a raw hash hex string in double quotes for strong ETag format.
    /// "a1b2c3" -> "\"a1b2c3\""
    let quote (raw: string) : string =
        "\"" + raw + "\""

    /// Remove surrounding double quotes from an ETag value.
    /// "\"a1b2c3\"" -> "a1b2c3"
    let unquote (etag: string) : string =
        if etag.Length >= 2 && etag.[0] = '"' && etag.[etag.Length - 1] = '"' then
            etag.Substring(1, etag.Length - 2)
        else
            etag

    /// Check if an ETag value uses the weak prefix W/"..."
    let isWeak (etag: string) : bool =
        etag.StartsWith("W/\"", StringComparison.Ordinal)

    /// Compute a strong ETag from raw bytes using SHA-256, truncated to 128 bits.
    /// Returns a fully-formatted ETag string including quotes.
    /// Example: "a1b2c3d4e5f67890a1b2c3d4e5f67890"
    let computeFromBytes (data: byte[]) : string =
        use sha256 = SHA256.Create()
        let hash = sha256.ComputeHash(data)
        let hex =
            hash
            |> Array.take 16
            |> Array.map (fun b -> b.ToString("x2"))
            |> String.concat ""
        quote hex
```

**Files**: `src/Frank/ETag.fs`
**Notes**:
- `SHA256.Create()` is wrapped in `use` for proper disposal (Constitution principle VI)
- Truncation to 16 bytes (128 bits) gives birthday bound ~2^64 -- astronomically unlikely collisions
- Hex encoding uses lowercase (`x2`) for consistent output
- `computeFromBytes` returns the fully-quoted ETag -- callers do not need to call `quote` separately
- `unquote` uses `Substring` rather than slice syntax for .NET 8 compatibility

### Subtask T004 -- Implement `ETagComparison` module

**Purpose**: Pure functions for RFC 9110 ETag comparison and conditional header parsing.

**Steps**:
1. In `src/Frank/ETag.fs`, after `ETagFormat`, define:

```fsharp
/// Pure functions for RFC 9110 ETag comparison semantics.
/// Used by ConditionalRequestMiddleware for If-None-Match and If-Match evaluation.
module ETagComparison =
    /// Strong comparison (RFC 9110 Section 8.8.3.2):
    /// Two ETags match if neither is weak and their opaque-tags are identical.
    /// Since we only produce strong ETags, this is a character-by-character comparison.
    let strongMatch (etag1: string) (etag2: string) : bool =
        not (ETagFormat.isWeak etag1)
        && not (ETagFormat.isWeak etag2)
        && String.Equals(etag1, etag2, StringComparison.Ordinal)

    /// Parse a comma-separated ETag header value into individual ETag strings.
    /// Handles whitespace trimming per RFC 9110.
    /// Returns ["*"] for wildcard, or a list of quoted ETag values.
    let parseETagList (headerValue: string) : string list =
        if String.IsNullOrWhiteSpace(headerValue) then
            []
        elif headerValue.Trim() = "*" then
            ["*"]
        else
            headerValue.Split(',')
            |> Array.map (fun s -> s.Trim())
            |> Array.filter (fun s -> s.Length > 0)
            |> Array.toList

    /// Parse an If-None-Match header value.
    let parseIfNoneMatch (headerValue: string) : string list =
        parseETagList headerValue

    /// Parse an If-Match header value.
    let parseIfMatch (headerValue: string) : string list =
        parseETagList headerValue

    /// Check if any ETag in the header list matches the current ETag.
    /// Handles wildcard "*" (matches any existing resource -- currentETag is Some).
    /// Returns true if any match is found.
    let anyMatch (currentETag: string option) (headerETags: string list) : bool =
        match currentETag with
        | None -> false  // No current ETag means resource doesn't exist; nothing matches
        | Some current ->
            headerETags
            |> List.exists (fun headerETag ->
                if headerETag = "*" then true
                else strongMatch current headerETag)
```

**Files**: `src/Frank/ETag.fs`
**Notes**:
- `parseETagList` is the shared implementation; `parseIfNoneMatch` and `parseIfMatch` are named wrappers for clarity at call sites
- Wildcard `*` is returned as a list element `["*"]` so `anyMatch` can check it uniformly
- `anyMatch` returns `false` when `currentETag` is `None` -- a non-existent resource does not match anything (important for `If-None-Match: *` on non-existent resources)
- Strong comparison uses `StringComparison.Ordinal` (case-sensitive, no culture)
- Empty/whitespace header values parse to empty list (no match)

### Subtask T005 -- Create `ETagTests.fs` with unit tests

**Purpose**: Validate ETagFormat and ETagComparison modules with comprehensive unit tests.

**Steps**:
1. Create `test/Frank.Tests/ETagTests.fs`
2. Add to `Frank.Tests.fsproj` compilation order (before `Program.fs`)
3. Write Expecto tests covering:

**a. ETagFormat tests**:
- `quote` wraps raw string in double quotes
- `unquote` removes surrounding quotes
- `unquote` handles strings without quotes (returns unchanged)
- `isWeak` detects `W/"..."` prefix
- `isWeak` returns false for strong ETags
- `computeFromBytes` returns quoted 32-char hex string
- `computeFromBytes` is deterministic (same input -> same output)
- `computeFromBytes` produces different output for different input

**b. ETagComparison tests**:
- `strongMatch` matches identical strong ETags
- `strongMatch` rejects different ETags
- `strongMatch` rejects weak ETags (even if opaque-tags match)
- `parseIfNoneMatch` parses single ETag
- `parseIfNoneMatch` parses comma-separated ETags
- `parseIfNoneMatch` handles whitespace around commas
- `parseIfNoneMatch` returns `["*"]` for wildcard
- `parseIfNoneMatch` returns empty list for empty/whitespace input
- `anyMatch` returns true when current ETag is in list
- `anyMatch` returns false when current ETag is not in list
- `anyMatch` returns true for wildcard when resource exists
- `anyMatch` returns false for wildcard when resource does not exist (None)
- `anyMatch` returns false for empty header list

**Example test structure**:

```fsharp
module ETagTests

open Expecto
open Frank

[<Tests>]
let etagFormatTests =
    testList "ETagFormat" [
        test "quote wraps in double quotes" {
            Expect.equal (ETagFormat.quote "abc") "\"abc\"" "quoted"
        }
        test "unquote removes surrounding quotes" {
            Expect.equal (ETagFormat.unquote "\"abc\"") "abc" "unquoted"
        }
        test "computeFromBytes is deterministic" {
            let data = System.Text.Encoding.UTF8.GetBytes("hello")
            let etag1 = ETagFormat.computeFromBytes data
            let etag2 = ETagFormat.computeFromBytes data
            Expect.equal etag1 etag2 "same input same output"
        }
        test "computeFromBytes produces 32-char hex in quotes" {
            let data = System.Text.Encoding.UTF8.GetBytes("test")
            let etag = ETagFormat.computeFromBytes data
            Expect.isTrue (etag.StartsWith("\"")) "starts with quote"
            Expect.isTrue (etag.EndsWith("\"")) "ends with quote"
            let inner = ETagFormat.unquote etag
            Expect.equal inner.Length 32 "32 hex chars"
        }
    ]

[<Tests>]
let etagComparisonTests =
    testList "ETagComparison" [
        test "strongMatch matches identical strong ETags" {
            Expect.isTrue (ETagComparison.strongMatch "\"abc\"" "\"abc\"") "match"
        }
        test "strongMatch rejects different ETags" {
            Expect.isFalse (ETagComparison.strongMatch "\"abc\"" "\"def\"") "no match"
        }
        test "anyMatch with wildcard and existing resource" {
            Expect.isTrue (ETagComparison.anyMatch (Some "\"abc\"") ["*"]) "wildcard matches"
        }
        test "anyMatch with wildcard and no resource" {
            Expect.isFalse (ETagComparison.anyMatch None ["*"]) "wildcard no match on None"
        }
    ]
```

**Files**: `test/Frank.Tests/ETagTests.fs`, `test/Frank.Tests/Frank.Tests.fsproj` (modified)
**Validation**: `dotnet test test/Frank.Tests/` passes with all tests green.

---

## Test Strategy

- Run `dotnet build src/Frank/` to verify compilation on all 3 targets
- Run `dotnet test test/Frank.Tests/` to verify all ETag unit tests pass
- Run `dotnet build Frank.sln` to verify solution-level build succeeds

---

## Risks & Mitigations

| Risk | Mitigation |
|------|-----------|
| ETag header parsing edge cases (whitespace, empty, malformed) | Comprehensive test coverage for empty, single, multi, wildcard, whitespace cases |
| SHA-256 determinism across .NET versions | SHA-256 is standardized; output is identical across all .NET runtimes |
| `computeFromBytes` performance | SHA-256 is ~200ns for small inputs; well within <1ms target |
| File ordering in Frank.fsproj | ETag.fs must be before Builder.fs; verify after adding |

---

## Review Guidance

- Verify `ETag.fs` is added to `Frank.fsproj` before `Builder.fs`
- Verify `IETagProvider.ComputeETag` returns `Task<string option>` (not Async)
- Verify `ETagFormat.computeFromBytes` uses `SHA256.Create()` with `use` binding
- Verify truncation is to 16 bytes (128 bits), not 16 hex chars
- Verify `ETagComparison.strongMatch` rejects weak ETags
- Verify `anyMatch` returns false when currentETag is None (even for wildcard)
- Verify test coverage includes all RFC 9110 edge cases
- Run `dotnet build Frank.sln` and `dotnet test` to confirm green

---

## Activity Log

- 2026-03-07T00:00:00Z -- system -- lane=planned -- Prompt created.
- 2026-03-08T17:30:59Z – claude-opus – shell_pid=98096 – lane=doing – Assigned agent via workflow command
- 2026-03-08T17:57:54Z – claude-opus – shell_pid=98096 – lane=for_review – T001-T005 complete: IETagProvider, IETagProviderFactory, ETagMetadata, ETagFormat, ETagComparison. Builds clean.
