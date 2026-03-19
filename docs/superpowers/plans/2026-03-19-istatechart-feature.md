# IStatechartFeature Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Replace all `HttpContext.Items` string conventions for statechart state with a typed `IStatechartFeature` interface hierarchy on `HttpContext.Features`.

**Architecture:** Two-level interface hierarchy (`IStatechartFeature` non-generic base + `IStatechartFeature<'S,'C>` generic derived) with dual registration on `HttpContext.Features`. Extension methods provide the public API. All consumers migrate from Items to Features.

**Tech Stack:** F# 8.0+, ASP.NET Core (`HttpContext.Features`, `IFeatureCollection`), Expecto (tests)

**Spec:** `docs/superpowers/specs/2026-03-19-istatechart-feature-design.md`
**Issue:** #127

---

## File Map

| File | Action | Responsibility |
|------|--------|----------------|
| `src/Frank.Statecharts/StatechartFeature.fs` | Create | Interface definitions + extension methods |
| `src/Frank.Statecharts/Frank.Statecharts.fsproj` | Modify | Add `StatechartFeature.fs` to compile order |
| `src/Frank.Statecharts/StatefulResourceBuilder.fs` | Modify | Replace Items writes/reads with feature |
| `src/Frank.Statecharts/Affordances/AffordanceMap.fs` | Modify | Remove `StateKeyItemsKey` |
| `src/Frank.Statecharts/Affordances/AffordanceMiddleware.fs` | Modify | Read from `IStatechartFeature` |
| `src/Frank.Affordances/AffordanceMap.fs` | Modify | Remove `StateKeyItemsKey` |
| `src/Frank.Affordances/AffordanceMiddleware.fs` | Modify | Read from `IStatechartFeature` |
| `sample/Frank.TicTacToe.Sample/Program.fs` | Modify | Remove Items bridge, update comments |
| `test/Frank.Statecharts.Tests/Affordances/AffordanceMiddlewareTests.fs` | Modify | Use `SetStatechartState` |
| `test/Frank.Affordances.Tests/AffordanceMiddlewareTests.fs` | Modify | Use `SetStatechartState` |
| `test/Frank.TicTacToe.Tests/AffordanceIntegrationTests.fs` | Modify | Use feature in handlers + bridge middleware |
| `test/Frank.Statecharts.Tests/StatefulResourceTests.fs` | Modify | Use typed feature in `handlePlayingPost`/`handlePlayingDelete` |

---

### Task 1: Add IStatechartFeature interfaces and extension methods

**Files:**
- Create: `src/Frank.Statecharts/StatechartFeature.fs`
- Modify: `src/Frank.Statecharts/Frank.Statecharts.fsproj` (add to compile order)

- [ ] **Step 1: Create `StatechartFeature.fs` with interfaces and extensions**

```fsharp
namespace Frank.Statecharts

open Microsoft.AspNetCore.Http

/// Non-generic base: readable by type-agnostic middleware (e.g. AffordanceMiddleware).
type IStatechartFeature =
    abstract StateKey: string option
    abstract InstanceId: string option

/// Generic derived: readable by typed closures in StatefulResourceBuilder.
/// Eliminates boxing — state and context are stored as their concrete types.
type IStatechartFeature<'S, 'C> =
    inherit IStatechartFeature
    abstract State: 'S option
    abstract Context: 'C option

/// Extension methods for reading/writing statechart state via HttpContext.Features.
[<AutoOpen>]
module HttpContextStatechartExtensions =
    type HttpContext with
        member ctx.GetStatechartFeature() : IStatechartFeature option =
            match ctx.Features.Get<IStatechartFeature>() with
            | null -> None
            | f -> Some f

        member ctx.GetStatechartFeature<'S, 'C>() : IStatechartFeature<'S, 'C> option =
            match ctx.Features.Get<IStatechartFeature<'S, 'C>>() with
            | null -> None
            | f -> Some f

        member ctx.SetStatechartState<'S, 'C>(stateKey: string, state: 'S, context: 'C, ?instanceId: string) =
            let feature =
                { new IStatechartFeature<'S, 'C> with
                    member _.StateKey = Some stateKey
                    member _.InstanceId = instanceId
                    member _.State = Some state
                    member _.Context = Some context }
            // Dual registration: same object, two type keys (standard ASP.NET Core pattern)
            ctx.Features.Set<IStatechartFeature>(feature)
            ctx.Features.Set<IStatechartFeature<'S, 'C>>(feature)
```

- [ ] **Step 2: Add `StatechartFeature.fs` to fsproj compile order**

The file must appear before `StatefulResourceBuilder.fs` (which will consume it) and before `Affordances/AffordanceMiddleware.fs`. Find the existing compile order and insert it early — after the AST/core types but before the builder.

Check `Frank.Statecharts.fsproj` for the current `<Compile Include="..." />` order and insert `<Compile Include="StatechartFeature.fs" />` just before `StatefulResourceBuilder.fs`.

- [ ] **Step 3: Verify it compiles**

Run: `dotnet build src/Frank.Statecharts/`
Expected: Build succeeds. New types available in `Frank.Statecharts` namespace.

- [ ] **Step 4: Commit**

```bash
git add src/Frank.Statecharts/StatechartFeature.fs src/Frank.Statecharts/Frank.Statecharts.fsproj
git commit -m "feat: add IStatechartFeature interface hierarchy and extension methods

Closes #127 (partial)"
```

---

### Task 2: Migrate StatefulResourceBuilder producer (getCurrentStateKey)

**Files:**
- Modify: `src/Frank.Statecharts/StatefulResourceBuilder.fs:192-208`

- [ ] **Step 1: Update `getCurrentStateKey` closure to use `SetStatechartState`**

Replace the two `ctx.Items` writes with a single `SetStatechartState` call. In the closure at lines 192-208:

Before (lines 200-207):
```fsharp
match result with
| Some(state, context) ->
    ctx.Items[StateMachineContext.stateKey] <- box state
    ctx.Items[StateMachineContext.contextKey] <- box context
    return stateKey state
| None ->
    ctx.Items[StateMachineContext.stateKey] <- box machine.Initial
    ctx.Items[StateMachineContext.contextKey] <- box machine.InitialContext
    return initialStateKey
```

After:
```fsharp
match result with
| Some(state, context) ->
    ctx.SetStatechartState(stateKey state, state, context, instanceId)
    return stateKey state
| None ->
    ctx.SetStatechartState(initialStateKey, machine.Initial, machine.InitialContext, instanceId)
    return initialStateKey
```

- [ ] **Step 2: Verify it compiles**

Run: `dotnet build src/Frank.Statecharts/`
Expected: Build succeeds.

- [ ] **Step 3: Commit**

```bash
git add src/Frank.Statecharts/StatefulResourceBuilder.fs
git commit -m "refactor: getCurrentStateKey sets typed IStatechartFeature instead of Items"
```

---

### Task 3: Migrate StatefulResourceBuilder consumers (guards + transitions)

**Files:**
- Modify: `src/Frank.Statecharts/StatefulResourceBuilder.fs:224-226, 241-243, 270-272`

- [ ] **Step 1: Update `evaluateGuards` closure (lines ~224-226)**

Before:
```fsharp
let state = ctx.Items[StateMachineContext.stateKey] :?> 'S
let context = ctx.Items[StateMachineContext.contextKey] :?> 'C
```

After:
```fsharp
let feature = ctx.Features.Get<IStatechartFeature<'S, 'C>>()
let state = feature.State.Value
let context = feature.Context.Value
```

- [ ] **Step 2: Update `evaluateEventGuards` closure (lines ~241-243) — same pattern**

- [ ] **Step 3: Update `executeTransition` closure (lines ~270-272) — same pattern**

Note: Do NOT remove `StateMachineContext.stateKey` and `StateMachineContext.contextKey` yet — `StatefulResourceTests.fs` still references them. Those constants are removed in Task 7 after the test migration.

- [ ] **Step 4: Update doc comments in `StateMachineMetadata` and `StateMachineContext`**

Update the doc comments that still reference "HttpContext.Items" for state/context:
- Line 77: `/// Resolve state from store, cache in HttpContext.Items, return state key string.` → `/// Resolve state from store, set IStatechartFeature on HttpContext.Features, return state key string.`
- Line 79: `/// Evaluate access-control guards using cached state from HttpContext.Items (pre-handler).` → `/// Evaluate access-control guards using state from IStatechartFeature (pre-handler).`
- Line 83: Update to clarify that events still use Items but state/context now use Features.
- Line 192 comment: `// Closure: resolve state from store, cache typed values in HttpContext.Items` → `// Closure: resolve state from store, set typed values on IStatechartFeature`
- Line 41 (`StateMachineContext` module doc): Narrow to `/// Helpers for communicating events between handlers and middleware via HttpContext.Items.`

- [ ] **Step 5: Verify it compiles**

Run: `dotnet build src/Frank.Statecharts/`
Expected: Build succeeds.

- [ ] **Step 6: Commit**

```bash
git add src/Frank.Statecharts/StatefulResourceBuilder.fs
git commit -m "refactor: statechart closures read typed IStatechartFeature instead of Items"
```

---

### Task 4: Migrate AffordanceMiddleware (both copies)

**Files:**
- Modify: `src/Frank.Statecharts/Affordances/AffordanceMiddleware.fs:28-31`
- Modify: `src/Frank.Affordances/AffordanceMiddleware.fs:28-31`

- [ ] **Step 1: Update `Frank.Statecharts` copy**

In `src/Frank.Statecharts/Affordances/AffordanceMiddleware.fs`, replace lines 28-31:

Before:
```fsharp
let stateKey =
    match ctx.Items.TryGetValue(AffordanceMap.StateKeyItemsKey) with
    | true, (:? string as key) -> key
    | _ -> null
```

After:
```fsharp
let stateKey =
    match ctx.Features.Get<IStatechartFeature>() with
    | null -> null
    | f ->
        match f.StateKey with
        | Some key -> key
        | None -> null
```

Also update the doc comment on the type (line 10) to say "from HttpContext.Features" instead of "from HttpContext.Items".

- [ ] **Step 2: Update `Frank.Affordances` copy — identical change**

In `src/Frank.Affordances/AffordanceMiddleware.fs`, apply the same code replacement as Step 1.

Both copies use `namespace Frank.Affordances` but `IStatechartFeature` is in the `Frank.Statecharts` namespace. Add `open Frank.Statecharts` to the imports of BOTH files. The `Frank.Affordances` project already has a `<ProjectReference>` to `Frank.Statecharts`, so the type is accessible.

- [ ] **Step 3: Verify both projects compile**

Run: `dotnet build src/Frank.Statecharts/ && dotnet build src/Frank.Affordances/`
Expected: Both build successfully.

- [ ] **Step 4: Commit**

```bash
git add src/Frank.Statecharts/Affordances/AffordanceMiddleware.fs src/Frank.Affordances/AffordanceMiddleware.fs
git commit -m "refactor: AffordanceMiddleware reads IStatechartFeature instead of Items"
```

---

### Task 5: Migrate AffordanceMiddleware tests (both copies)

**Files:**
- Modify: `test/Frank.Statecharts.Tests/Affordances/AffordanceMiddlewareTests.fs:55-56, 113, 150, 203, 236`
- Modify: `test/Frank.Affordances.Tests/AffordanceMiddlewareTests.fs:55-56, 113, 150, 203, 236`

- [ ] **Step 1: Update `Frank.Statecharts.Tests` copy**

The `buildTestServer` helper (line 46-80) takes a `stateKeySetter: HttpContext -> unit` callback. The test call sites pass lambdas like:
```fsharp
fun ctx -> ctx.Items.[AffordanceMap.StateKeyItemsKey] <- "XTurn"
```

Replace each call site to use the extension method instead:
```fsharp
fun ctx -> ctx.SetStatechartState("XTurn", "XTurn", 0)
```

Note: The test doesn't have real typed state — it just needs the state key string to be set. Use simple string/int types for state/context since only the state key is read by `AffordanceMiddleware` (via the non-generic `IStatechartFeature` base).

Update the `buildTestServer` comment (line 55) from "Simulate statechart middleware by setting HttpContext.Items" to "Simulate statechart middleware by setting IStatechartFeature".

Four call sites to update:
- Line 113: `"XTurn"` → `ctx.SetStatechartState("XTurn", "XTurn", 0)`
- Line 150: `"Won"` → `ctx.SetStatechartState("Won", "Won", 0)`
- Line 203: `"SomeState"` → `ctx.SetStatechartState("SomeState", "SomeState", 0)`
- Line 236: `"UnknownState"` → `ctx.SetStatechartState("UnknownState", "UnknownState", 0)`

Add `open Frank.Statecharts` to the imports of BOTH test files. Both test projects already reference `Frank.Statecharts` (the `Frank.Affordances.Tests.fsproj` references `Frank.Statecharts.fsproj` directly).

- [ ] **Step 2: Update `Frank.Affordances.Tests` copy — same changes**

- [ ] **Step 3: Run tests**

Run: `dotnet test test/Frank.Statecharts.Tests/ --filter "AffordanceMiddleware" && dotnet test test/Frank.Affordances.Tests/ --filter "AffordanceMiddleware"`
Expected: All affordance middleware tests pass.

- [ ] **Step 4: Commit**

```bash
git add test/Frank.Statecharts.Tests/Affordances/AffordanceMiddlewareTests.fs test/Frank.Affordances.Tests/AffordanceMiddlewareTests.fs
git commit -m "test: migrate AffordanceMiddleware tests to IStatechartFeature"
```

---

### Task 6: Migrate StatefulResourceTests (handler reads)

**Files:**
- Modify: `test/Frank.Statecharts.Tests/StatefulResourceTests.fs:991, 1002`

- [ ] **Step 1: Update `handlePlayingPost` (line 991)**

Before:
```fsharp
let state = ctx.Items.[StateMachineContext.stateKey] :?> HierarchicalGameState
```

After:
```fsharp
let feature = ctx.Features.Get<IStatechartFeature<HierarchicalGameState, HierarchicalContext>>()
let state = feature.State.Value
```

You will need to add `open Microsoft.AspNetCore.Http` if not already present (for `Features`).

- [ ] **Step 2: Update `handlePlayingDelete` (line 1002) — same pattern**

- [ ] **Step 3: Now remove `StateMachineContext.stateKey` and `StateMachineContext.contextKey`**

All consumers have been migrated. In `src/Frank.Statecharts/StatefulResourceBuilder.fs`, `StateMachineContext` module (lines 43-45), remove:
```fsharp
let internal stateKey = "Frank.Statecharts.State"
let internal contextKey = "Frank.Statecharts.Context"
```

Keep `eventKey`, `setEvent`, and `tryGetEvent` — those are unchanged.

- [ ] **Step 4: Verify it compiles**

Run: `dotnet build src/Frank.Statecharts/ && dotnet build test/Frank.Statecharts.Tests/`
Expected: Both build successfully — no remaining references to removed constants.

- [ ] **Step 5: Run tests**

Run: `dotnet test test/Frank.Statecharts.Tests/`
Expected: All tests pass (including hierarchical game tests).

- [ ] **Step 6: Commit**

```bash
git add src/Frank.Statecharts/StatefulResourceBuilder.fs test/Frank.Statecharts.Tests/StatefulResourceTests.fs
git commit -m "refactor: migrate StatefulResourceTests to typed IStatechartFeature, remove old constants"
```

---

### Task 7: Migrate TicTacToe sample and integration tests

**Files:**
- Modify: `sample/Frank.TicTacToe.Sample/Program.fs:12, 37-52`
- Modify: `test/Frank.TicTacToe.Tests/AffordanceIntegrationTests.fs:120, 140, 167-182`

- [ ] **Step 1: Update TicTacToe sample `resolveStateKey` middleware**

In `sample/Frank.TicTacToe.Sample/Program.fs`:

Remove the Items write (line 48):
```fsharp
ctx.Items.[AffordanceMap.StateKeyItemsKey] <- stateKey
```

The feature is already set by `metadata.GetCurrentStateKey` (which calls `getCurrentStateKey`, which now calls `SetStatechartState`). The bridge middleware still needs to call `GetCurrentStateKey` to trigger the store read, but doesn't need to relay the result.

Replace lines 37-52 with:
```fsharp
let resolveStateKey (app: IApplicationBuilder) =
    app.Use(Func<HttpContext, Func<Task>, Task>(fun ctx next ->
        task {
            let endpoint = ctx.GetEndpoint()

            if not (isNull endpoint) then
                let metadata = endpoint.Metadata.GetMetadata<StateMachineMetadata>()

                if not (obj.ReferenceEquals(metadata, null)) then
                    let instanceId = metadata.ResolveInstanceId ctx
                    let! _stateKey = metadata.GetCurrentStateKey ctx.RequestServices ctx instanceId
                    ()

            do! next.Invoke()
        }
        :> Task))
```

Update the module doc comment (line 12) from `ctx.Items["statechart.stateKey"]` to `IStatechartFeature`.

Update the pipeline comment (line 141) similarly.

Remove `open Frank.Affordances` if `AffordanceMap.StateKeyItemsKey` was the only reference from that namespace. Check first — `useAffordances` uses `AffordanceMap` types so the import is still needed.

- [ ] **Step 2: Update integration test handlers**

In `test/Frank.TicTacToe.Tests/AffordanceIntegrationTests.fs`:

**`getGameState` handler (line 120):** Remove `ctx.Items.[AffordanceMap.StateKeyItemsKey] <- key`. The state key is already set by `resolveStateKeyMiddleware` (which runs before the handler via the pipeline). The handler doesn't need to set it again.

**`handleMove` handler (line 140):** Same — remove `ctx.Items.[AffordanceMap.StateKeyItemsKey] <- key`.

**`resolveStateKeyMiddleware` (line 178):** Remove `ctx.Items.[AffordanceMap.StateKeyItemsKey] <- stateKey`. Same reasoning as sample — `GetCurrentStateKey` now sets the feature.

- [ ] **Step 3: Verify the solution compiles end-to-end**

Run: `dotnet build`
Expected: Entire solution builds. No remaining references to `StateKeyItemsKey` or `StateMachineContext.stateKey`/`contextKey` in source code.

- [ ] **Step 4: Run all tests**

Run: `dotnet test test/Frank.Statecharts.Tests/ && dotnet test test/Frank.Affordances.Tests/ && dotnet test test/Frank.TicTacToe.Tests/`
Expected: All tests pass.

- [ ] **Step 5: Commit**

```bash
git add sample/Frank.TicTacToe.Sample/Program.fs test/Frank.TicTacToe.Tests/AffordanceIntegrationTests.fs
git commit -m "refactor: migrate TicTacToe sample and integration tests to IStatechartFeature"
```

---

### Task 8: Remove StateKeyItemsKey and final verification

- [ ] **Step 1: Remove `StateKeyItemsKey` from both AffordanceMap copies**

In `src/Frank.Statecharts/Affordances/AffordanceMap.fs`, remove:
```fsharp
/// HttpContext.Items key convention for the current statechart state key.
/// The statechart middleware stores the resolved state key at this key.
[<Literal>]
let StateKeyItemsKey = "statechart.stateKey"
```

In `src/Frank.Affordances/AffordanceMap.fs`, remove the same lines.

All consumers have been migrated — this removal should compile cleanly.

- [ ] **Step 2: Grep for any remaining Items references**

Run: `grep -rn "StateKeyItemsKey\|statechart\.stateKey\|StateMachineContext\.stateKey\|StateMachineContext\.contextKey" src/ test/ sample/ --include="*.fs"`
Expected: No matches in source files (only spec/plan docs may reference old pattern).

- [ ] **Step 3: Full solution build**

Run: `dotnet build`
Expected: Clean build, no warnings related to this change.

- [ ] **Step 4: Full test suite**

Run: `dotnet test`
Expected: All tests pass.

- [ ] **Step 5: Commit removals and verify clean state**

```bash
git add src/Frank.Statecharts/Affordances/AffordanceMap.fs src/Frank.Affordances/AffordanceMap.fs
git commit -m "refactor: remove AffordanceMap.StateKeyItemsKey (replaced by IStatechartFeature)"
```

Run: `git status`
Expected: Clean working tree (all changes committed).
