# Replace HttpContext.Items with typed IStatechartFeature

**Issue:** #127
**Date:** 2026-03-19
**Status:** Approved

## Summary

Replace all string-based `HttpContext.Items` conventions for passing statechart state between middleware components with a strongly-typed `IStatechartFeature` interface hierarchy on `HttpContext.Features`. Eliminates three magic string keys, removes boxing at consumption sites, and follows ASP.NET Core's `IFeatureCollection` pattern.

## Motivation

The current affordance and statechart middleware communicate via three `HttpContext.Items` entries:

| Key | Purpose | Set by | Read by |
|-----|---------|--------|---------|
| `"statechart.stateKey"` | State key string (e.g. `"XTurn"`) | Bridge middleware (external to `getCurrentStateKey`) | `AffordanceMiddleware` |
| `"Frank.Statecharts.State"` | Boxed typed state (`box state`) | `getCurrentStateKey` closure | Guard/transition closures |
| `"Frank.Statecharts.Context"` | Boxed typed context (`box context`) | `getCurrentStateKey` closure | Guard/transition closures |

Problems:
- No IntelliSense — consumers must know magic strings
- No compile-time safety — typos fail silently at runtime
- Not AOT/trimmer friendly — `Items` is `IDictionary<object, object>` with boxing
- Doesn't follow ASP.NET Core conventions for cross-middleware communication
- Boxing at write site, downcast at read site — unnecessary for typed closures

## Design

### Interface hierarchy

```fsharp
/// Non-generic base: readable by type-agnostic middleware (e.g. AffordanceMiddleware).
type IStatechartFeature =
    abstract StateKey: string option
    abstract InstanceId: string option

/// Generic derived: readable by typed closures in StatefulResourceBuilder.
type IStatechartFeature<'S, 'C> =
    inherit IStatechartFeature
    abstract State: 'S option
    abstract Context: 'C option
```

Both interfaces live in the `Frank.Statecharts` assembly. Namespace placement deferred to #142.

### Extension methods

```fsharp
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
            // Dual registration: same object, two type keys
            ctx.Features.Set<IStatechartFeature>(feature)
            ctx.Features.Set<IStatechartFeature<'S, 'C>>(feature)
```

Dual registration follows the standard ASP.NET Core pattern (e.g. `IHttpRequestFeature` / `IHttpRequestBodyDetectionFeature`).

### Producer migration

**`StatefulResourceBuilder.getCurrentStateKey` closure** — primary producer:

Before:
```fsharp
ctx.Items[StateMachineContext.stateKey] <- box state
ctx.Items[StateMachineContext.contextKey] <- box context
return stateKey state
```

After:
```fsharp
ctx.SetStatechartState(stateKey state, state, context, instanceId)
return stateKey state
```

One call replaces two Items insertions. No boxing.

**`resolveStateKey` bridge middleware (TicTacToe sample):**

Before:
```fsharp
let! stateKey = metadata.GetCurrentStateKey ctx.RequestServices ctx instanceId
ctx.Items.[AffordanceMap.StateKeyItemsKey] <- stateKey
```

After:
```fsharp
let! _stateKey = metadata.GetCurrentStateKey ctx.RequestServices ctx instanceId
// Feature already set by getCurrentStateKey — no bridge needed
```

The bridge middleware's Items write is removed. `GetCurrentStateKey` sets the feature as a side effect. The bridge middleware still calls `GetCurrentStateKey` (triggers the store read) but no longer needs to relay the result.

### Consumer migration

**`AffordanceMiddleware.InvokeAsync`** — reads non-generic base:

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

**`StatefulResourceBuilder` closures** (`evaluateGuards`, `evaluateEventGuards`, `executeTransition`) — read generic derived:

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

No downcast. Fully typed.

### Removals

- `AffordanceMap.StateKeyItemsKey` — removed entirely (nothing released, no deprecation needed)
- `StateMachineContext.stateKey` — removed (internal, replaced by feature)
- `StateMachineContext.contextKey` — removed (internal, replaced by feature)

### Unchanged

- `StateMachineContext.setEvent` / `tryGetEvent` — stays in Items. This is the handler-to-middleware event channel, set and read within one request by user code and statechart middleware. Genuinely internal, already well-typed via generics.

## Affected files

| File | Change |
|------|--------|
| `src/Frank.Statecharts/StatefulResourceBuilder.fs` | Add interfaces, extensions; update `getCurrentStateKey`, `evaluateGuards`, `evaluateEventGuards`, `executeTransition`; remove `StateMachineContext.stateKey`/`contextKey` |
| `src/Frank.Statecharts/Affordances/AffordanceMiddleware.fs` | Read from `IStatechartFeature` instead of Items |
| `src/Frank.Statecharts/Affordances/AffordanceMap.fs` | Remove `StateKeyItemsKey` |
| `src/Frank.Affordances/AffordanceMiddleware.fs` | Same as Frank.Statecharts copy |
| `src/Frank.Affordances/AffordanceMap.fs` | Same as Frank.Statecharts copy |
| `sample/Frank.TicTacToe.Sample/Program.fs` | Remove Items write from `resolveStateKey`; update comments |
| `test/Frank.Statecharts.Tests/Affordances/AffordanceMiddlewareTests.fs` | Use `SetStatechartState` instead of Items |
| `test/Frank.Affordances.Tests/AffordanceMiddlewareTests.fs` | Same |
| `test/Frank.TicTacToe.Tests/AffordanceIntegrationTests.fs` | Use `SetStatechartState` instead of Items in both `resolveStateKeyMiddleware` and handler-level writes (`getGameState`, `handleMove`) |

## Expert input

- **David Fowler:** `Features.Get<T>()` is AOT/trimmer safe. Dual registration is standard ASP.NET Core pattern. Items dictionary is "not how we'd design a public API."
- **Don Syme:** Interface hierarchy preserves F# type safety. Generic `IStatechartFeature<'S,'C>` eliminates boxing at consumption — "what DUs are for."
- **Mark Seemann:** Single feature object is a clean data boundary between middleware layers. Pure core (preCompute) unchanged.
- **Dave Thomas (@7sharp9):** One `Features.Get<>()` replaces three `Items.TryGetValue` calls. Cost is visible. Same allocation profile (one object per request vs three Items entries).
