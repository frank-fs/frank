---
source: "github issue #286"
title: "TransitionAlgebra record type + RuntimeInterpreter"
milestone: "v7.4.0"
state: "OPEN"
type: spec
---

# TransitionAlgebra record type + RuntimeInterpreter

> Extracted from [frank-fs/frank#286](https://github.com/frank-fs/frank/issues/286)

**Parent:** #257

## Scope

Two phases landed on master (commit e4a9c99a): `TransitionAlgebra<'r>`, `RuntimeInterpreter`, `TransitionProgram.fromTransition`, and equivalence tests. But the RuntimeInterpreter flattens Fork into a no-op and returns flat `string list` fields — the hierarchy is lost in the output.

This issue is now: **make the RuntimeInterpreter produce tree-shaped output that preserves Harel AND-state parallelism.**

## What exists (on master)

- `TransitionAlgebra<'r>` in `Frank.Statecharts.Core/Types.fs` — correct, no changes needed
- `TransitionProgram.fromTransition` in `Hierarchy.fs` — correct, no changes needed
- `createInterpreter` + `runProgram` in `Hierarchy.fs` — **Fork is a no-op, Bind appends flat lists**
- `HierarchicalTransitionResult` — **has flat `ExitedStates: string list` / `EnteredStates: string list`**
- `TransitionEvent` in `StatefulResourceBuilder.fs` — **has flat `ExitedStates` / `EnteredStates`**
- All tests in `HierarchyAlgebraTests.fs` — **assert flat list equality**

## Types to add (Frank.Statecharts.Core/Types.fs, after TransitionAlgebra)

```fsharp
/// Individual statechart transition operation.
/// Named Transition* not Trace* — these are runtime types fundamental to
/// representing Harel AND-state parallelism, not just observation/logging.
type TransitionOp =
    | Exited of string
    | Entered of string
    | HistoryRecorded of string
    | HistoryRestored of string * HistoryKind

/// Tree-shaped record of transition execution.
/// Preserves the parallel structure of AND-state regions (Par)
/// and sequential composition (Seq). NO companion module. NO flatten functions.
type TransitionStep =
    | Leaf of TransitionOp
    | Seq of TransitionStep list
    | Par of TransitionStep list
```

## Semantic changes (all settled 2026-04-11)

1. **`enterState` returns `(ActiveStateConfiguration * TransitionStep)`** — no parallel function, no duplication. One function, returns the tree.
2. **Enter stops at AND composites.** Returns `Leaf (Entered stateId)` for AND states. Fork does the region entry.
3. **Fork enters regions** via `enterState`, producing `Par` with one subtree per region. Each region starts from the same base config (AND regions are disjoint).
4. **`HierarchicalTransitionResult` drops `ExitedStates`/`EnteredStates`** — only `Configuration`, `Steps: TransitionStep`, `HistoryRecord`.
5. **`TransitionEvent` drops flat fields** — gains `Steps: TransitionStep`.
6. **`transition` delegates to `TransitionProgram.fromTransition` + `runProgram`** — eliminates the duplicated 70-line function.
7. **ALL tests assert tree shape** — zero flat list assertions anywhere. A wrong tree producing correct flat lists is exactly how the no-op Fork survived undetected.

## What stays unchanged

- `TransitionAlgebra<'r>` type — correct as-is
- `TransitionProgram.fromTransition` — correct as-is
- Hand-written algebra test programs — correct, just need assertion updates
- Monad law tests — correct, just need updated destructuring

## Files that must change

- `src/Frank.Statecharts.Core/Types.fs` — add `TransitionOp`, `TransitionStep`
- `src/Frank.Statecharts/Hierarchy.fs` — `enterState` signature, `RuntimeStep`, `HierarchicalTransitionResult`, `createInterpreter`, `runProgram`, `transition`, `enterWithHistory` chain
- `src/Frank.Statecharts/StatefulResourceBuilder.fs` — `TransitionEvent`, 3 construction sites
- `test/Frank.Statecharts.Tests/HierarchyAlgebraTests.fs` — ALL tests
- `test/Frank.Statecharts.Tests/HierarchyTests.fs` — ALL transition tests
- `sample/Frank.OrderFulfillment.Sample/Program.fs` — logging reads `Steps`

## Known tricky spots

- `enterWithHistory`, `enterInitialChild`, `restoreShallowHistory`, `restoreDeepHistory` must also return `TransitionStep` since they call `enterState`
- RestoreHistory handler needs the tree from `enterWithHistory`, not a flat diff
- Test setup calls (`HierarchicalRuntime.enterState ...`) must destructure the tuple — use `fst` or `let (config, _) = ...`

## Anti-patterns — MUST NOT appear in the implementation

These five patterns have been repeatedly smuggled in by agents and must be explicitly rejected:

1. **`TransitionStep.flatten: TransitionStep -> TransitionOp list`** — no flatten functions of any kind
2. **`TransitionStep.enteredStates: TransitionStep -> string list`** — no flat extraction helpers
3. **`ExitedStates: string list` field on any result type** — no flat list fields alongside or instead of the tree
4. **`runStep` or helper that returns `string list` from tree input** — no convenience functions that discard structure
5. **Keeping flat fields "for production consumers" while tests use tree** — the flat fields ARE the problem. If a consumer needs a flat view, that's the consumer's problem — don't provide the escape hatch

Watch for: `List.collect`, `List.choose (function Entered -> ...)`, any function returning `string list` from `TransitionStep` input.

## Acceptance Criteria

**AC-1**: `TransitionOp` and `TransitionStep` types exist in `Frank.Statecharts.Core/Types.fs` with no companion module and no flatten functions.

**AC-2**: `enterState` returns `(ActiveStateConfiguration * TransitionStep)`. Enter stops at AND composites (returns `Leaf (Entered id)`). Fork enters each region via `enterState`, producing a `Par` node.

**AC-3**: `HierarchicalTransitionResult` has `Steps: TransitionStep` and NO `ExitedStates`/`EnteredStates` fields. `TransitionEvent` likewise.

**AC-4**: ALL tests assert tree shape — `Seq`, `Par`, and `Leaf` nodes. Zero `string list` equality assertions for entered/exited states. A test that could pass with a flat implementation is not a valid test.

**AC-5**: `HierarchicalRuntime.transition` delegates to `TransitionProgram.fromTransition` + `runProgram`. The duplicated 70-line implementation is removed.

**AC-6**: All existing test scenarios (XOR transition, AND-state entry, history shallow/deep, self-transition) pass with tree-shaped assertions. Build + test + Frank.Tests + fantomas all green.

## Dependencies

- None — this is the root of the #257 dependency chain

## Design Decisions (resolved 2026-04-04, refined 2026-04-11)

Unchanged from comments. See comment history for Decision 1a-1c, 2, 8.

Closes nothing independently — all child issues together close #257.
