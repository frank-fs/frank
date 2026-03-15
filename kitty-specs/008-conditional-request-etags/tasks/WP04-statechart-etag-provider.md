---
work_package_id: WP04
title: StatechartETagProvider
lane: "for_review"
dependencies:
- WP01
- WP02
subtasks: [T018, T019, T020, T021, T022]
history:
- timestamp: '2026-03-07T00:00:00Z'
  lane: planned
  agent: system
  shell_pid: ''
  action: Prompt generated via /spec-kitty.tasks
requirement_refs: [FR-002, FR-008, FR-010, FR-015]
---

# Work Package Prompt: WP04 -- StatechartETagProvider

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
spec-kitty implement WP04 --base WP02
```

Depends on WP01 (IETagProvider, ETagFormat) and WP02 (ETagCache for invalidation). Can run in parallel with WP03.

---

## Objectives & Success Criteria

- Implement `StatechartETagProvider<'State, 'Context>` as the default `IETagProvider` for statechart-backed resources
- Hash `('State * 'Context)` pairs using SHA-256 truncated to 128 bits via user-supplied context serializer
- Implement `StatechartETagProviderFactory` as `IETagProviderFactory` for statechart endpoints
- Cache invalidation is middleware-driven: after a 2xx mutation response, the middleware invalidates the `ETagCache` entry (no store-level subscription)
- Provide `AddStatechartETagProvider<'State, 'Context>` DI registration helper
- ETags are deterministic: same `('State * 'Context)` always produces the same ETag
- ETags change when state transitions occur

---

## Context & Constraints

**Reference documents**:
- `kitty-specs/008-conditional-request-etags/research.md` -- Decision 1: Hashing Strategy, Decision 5: StatechartETagProvider Integration
- `kitty-specs/008-conditional-request-etags/data-model.md` -- StatechartETagProvider entity
- `src/Frank.Statecharts/Store.fs` -- `IStateMachineStore<'S,'C>` interface
- `src/Frank.Statecharts/StatefulResourceBuilder.fs` -- `StateMachineMetadata` in endpoint metadata

**Key constraints**:
- New file: `src/Frank.Statecharts/StatechartETagProvider.fs` -- added to Frank.Statecharts.fsproj
- Depends on Frank core types (`IETagProvider`, `IETagProviderFactory`, `ETagFormat`, `ETagCache`) from WP01/WP02
- Depends on Frank.Statecharts types (`IStateMachineStore`, `StateMachineMetadata`) -- already exist
- `contextSerializer: 'Context -> byte[]` is provided by the developer at registration time
- State serialization: `System.Text.Encoding.UTF8.GetBytes(string state)` for DU case names
- SHA-256 via `System.Security.Cryptography.SHA256` (framework built-in)
- No new NuGet dependencies
- No subscription lifecycle to manage -- cache invalidation is handled by the middleware (WP03)

---

## Subtasks & Detailed Guidance

### Subtask T018 -- Create `StatechartETagProvider.fs` implementing `IETagProvider`

**Purpose**: Default ETag provider that reads state from `IStateMachineStore` and produces deterministic ETags.

**Steps**:
1. Create `src/Frank.Statecharts/StatechartETagProvider.fs`
2. Add to `Frank.Statecharts.fsproj` in `<Compile>` list (after existing files, before any extensions)
3. Define the provider:

```fsharp
namespace Frank.Statecharts

open System
open System.Threading.Tasks
open System.Security.Cryptography
open Frank

type StatechartETagProvider<'State, 'Context when 'State : equality>
    (store: IStateMachineStore<'State, 'Context>,
     contextSerializer: 'Context -> byte[],
     cache: ETagCache) =

    let computeETagFromState (state: 'State) (context: 'Context) : string =
        let stateBytes = System.Text.Encoding.UTF8.GetBytes(string state)
        let contextBytes = contextSerializer context
        let combined = Array.append stateBytes contextBytes
        use sha256 = SHA256.Create()
        let hash = sha256.ComputeHash(combined)
        let hex =
            hash
            |> Array.take 16
            |> Array.map (fun b -> b.ToString("x2"))
            |> String.concat ""
        ETagFormat.quote hex

    interface IETagProvider with
        member _.ComputeETag(instanceId: string) : Task<string option> = task {
            let! stateOpt = store.GetState(instanceId)
            match stateOpt with
            | Some(state, context) ->
                let etag = computeETagFromState state context
                return Some etag
            | None ->
                return None
        }
```

**Files**: `src/Frank.Statecharts/StatechartETagProvider.fs`, `src/Frank.Statecharts/Frank.Statecharts.fsproj` (modified)
**Notes**:
- The provider is generic `<'State, 'Context>` but implements the non-generic `IETagProvider`
- `string state` for DU cases produces the case name (e.g., "XTurn") -- stable across F# versions
- `contextSerializer` is the developer's responsibility -- they know how to serialize their context type
- `SHA256.Create()` is in a `use` binding for proper disposal
- `ETagFormat.quote` wraps the hex string in quotes for RFC 9110 compliance

### Subtask T019 -- Verify SHA-256 hashing produces deterministic output

**Purpose**: Ensure the hashing pipeline is deterministic and produces correct ETag format.

**Steps**:
1. This is primarily a verification/test subtask. Write unit tests that confirm:
   - Same `('State * 'Context)` always produces the same ETag
   - Different state or context produces different ETags
   - Output is 32 hex characters inside double quotes
   - `string state` for DU cases is stable (test with actual DU type)

2. Add tests in `test/Frank.Statecharts.Tests/` (or extend existing test project):

```fsharp
// Example DU for testing
type TestState = Active | Completed
type TestContext = { Version: int; Data: string }

let testSerializer (ctx: TestContext) =
    System.Text.Encoding.UTF8.GetBytes(sprintf "%d|%s" ctx.Version ctx.Data)

// Test: same state+context -> same ETag
// Test: Active vs Completed -> different ETags
// Test: same state, different context -> different ETags
```

**Files**: Test file in Frank.Statecharts.Tests
**Notes**:
- `string Active` should produce `"Active"` -- verify this in tests
- The ETag format is `"<32 hex chars>"` -- verify length and character set
- Do NOT depend on specific hash values (they are implementation details) -- only test determinism and uniqueness

### Subtask T020 -- Implement IETagProviderFactory for statechart resources

**Purpose**: Factory that resolves `StatechartETagProvider` instances for endpoints with `StateMachineMetadata`.

**Steps**:
1. In `StatechartETagProvider.fs`, define the factory:

```fsharp
open Microsoft.AspNetCore.Http

/// Factory that creates StatechartETagProvider instances for statechart endpoints.
/// Discovers endpoints via StateMachineMetadata in endpoint metadata.
type StatechartETagProviderFactory<'State, 'Context when 'State : equality>
    (store: IStateMachineStore<'State, 'Context>,
     contextSerializer: 'Context -> byte[],
     cache: ETagCache) =

    // Reuse a single provider instance (it's stateless aside from injected deps)
    let provider = StatechartETagProvider<'State, 'Context>(store, contextSerializer, cache)

    interface IETagProviderFactory with
        member _.CreateProvider(endpoint: Routing.Endpoint) : IETagProvider option =
            // Check if the endpoint has StateMachineMetadata
            let metadata = endpoint.Metadata.GetMetadata<StateMachineMetadata>()
            if isNull (box metadata) then
                None
            else
                Some (provider :> IETagProvider)
```

**Files**: `src/Frank.Statecharts/StatechartETagProvider.fs`
**Notes**:
- The factory checks for `StateMachineMetadata` (from Frank.Statecharts) on the endpoint
- A single provider instance is reused (the provider delegates to the store, which handles per-instance state)
- `isNull (box metadata)` handles the case where `StateMachineMetadata` is a value type or record -- box to check null
- If the endpoint is not a statechart resource, returns `None` (middleware skips it)

### Subtask T021 -- Implement middleware-driven cache invalidation

**Purpose**: When statechart state transitions occur via mutation requests, invalidate the corresponding ETag cache entry so the next request computes a fresh ETag.

**Note**: `IStateMachineStore.Subscribe` is per-instance only (`instanceId: string -> observer: IObserver<'State * 'Context> -> IDisposable`) and cannot be used for global cache invalidation. Instead, cache invalidation is driven by the middleware itself (consistent with T015 in WP03).

**Steps**:
1. Cache invalidation is handled by the `ConditionalRequestMiddleware` (WP03), not by the provider. After the handler returns a 2xx response for a mutation (POST/PUT/DELETE), the middleware sends an `InvalidateETag` message to the `ETagCache`. The `StatechartETagProvider` itself is stateless -- it computes ETags on demand and does not manage subscriptions.

2. No subscription mechanism is needed on `StatechartETagProvider`. The provider's responsibility is limited to computing ETags from the current state via `IStateMachineStore.GetState`.

**Files**: `src/Frank.Statecharts/StatechartETagProvider.fs` (no subscription code needed)
**Notes**:
- The middleware (WP03 T015) already describes the pattern: after observing a 2xx response from the handler, invalidate the cache entry
- This avoids the complexity of managing per-instance subscriptions and their lifecycle
- The provider remains simple: read state, hash it, return ETag
- No `IDisposable` needed on the provider for subscription cleanup (Constitution principle VI still satisfied -- nothing to dispose)

### Subtask T022 -- Add DI registration helper

**Purpose**: Provide `AddStatechartETagProvider<'State, 'Context>` for developer-facing registration.

**Steps**:
1. Add extension method to `IServiceCollection`:

```fsharp
open Microsoft.Extensions.DependencyInjection
open Microsoft.Extensions.Logging

type IServiceCollection with
    /// Register the StatechartETagProvider as the IETagProviderFactory for
    /// statechart-backed resources. Requires AddETagCache() to be called first.
    member services.AddStatechartETagProvider<'State, 'Context when 'State : equality>
        (contextSerializer: 'Context -> byte[]) : IServiceCollection =

        services.AddSingleton<IETagProviderFactory>(fun sp ->
            let store = sp.GetRequiredService<IStateMachineStore<'State, 'Context>>()
            let cache = sp.GetRequiredService<ETagCache>()
            StatechartETagProviderFactory<'State, 'Context>(store, contextSerializer, cache)
            :> IETagProviderFactory)
```

**Files**: `src/Frank.Statecharts/StatechartETagProvider.fs`
**Notes**:
- `AddStatechartETagProvider` registers `IETagProviderFactory` as a singleton
- The `contextSerializer` is captured in the closure and passed to the factory
- Requires `IStateMachineStore<'State, 'Context>` and `ETagCache` to already be registered in DI
- If the developer uses multiple statechart resource types, they would need a composite factory (future enhancement; single-type is sufficient for initial implementation)
- Usage: `services.AddStatechartETagProvider<GameState, GameContext>(fun ctx -> ...)`

---

## Test Strategy

- Run `dotnet build src/Frank.Statecharts/` to verify compilation
- Run tests in Frank.Statecharts.Tests for deterministic hashing and provider behavior
- Run `dotnet build Frank.sln` to verify solution-level build succeeds

---

## Risks & Mitigations

| Risk | Mitigation |
|------|-----------|
| `string state` representation unstable for complex DU cases | Test with actual DU types; document that state types should have clean `ToString()` |
| Generic type erasure at metadata/DI level | Factory pattern hides generics; middleware only sees `IETagProvider` |
| Multiple statechart resource types need separate providers | Document single-type limitation; composite factory as future enhancement |
| N/A (no subscriptions) | Cache invalidation is middleware-driven, no subscription lifecycle to manage |
| Context serializer produces non-deterministic output | Document requirement for deterministic serialization; test with known inputs |

---

## Review Guidance

- Verify `StatechartETagProvider.fs` is in `Frank.Statecharts.fsproj` after existing source files
- Verify SHA-256 uses `use` binding for disposal
- Verify truncation is 16 bytes (128 bits), not 16 characters
- Verify `computeETagFromState` produces hex-encoded output with `ETagFormat.quote`
- Verify `StatechartETagProviderFactory` checks for `StateMachineMetadata` on endpoint
- Verify cache invalidation is middleware-driven (no store-level subscriptions in provider)
- Verify DI registration resolves `IStateMachineStore` and `ETagCache` from service provider
- Run `dotnet build Frank.sln` and relevant tests to confirm green

---

## Activity Log

- 2026-03-07T00:00:00Z -- system -- lane=planned -- Prompt created.
- 2026-03-15T19:37:48Z – unknown – lane=for_review – Moved to for_review
