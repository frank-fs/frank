# Implementation Plan: WSD Generator

**Branch**: `017-wsd-generator-cross-validator` | **Date**: 2026-03-15 | **Spec**: [spec.md](spec.md)
**Input**: Feature specification from `kitty-specs/017-wsd-generator-cross-validator/spec.md`
**Research**: [research.md](research.md) | **Data Model**: [data-model.md](data-model.md) | **Quickstart**: [quickstart.md](quickstart.md)

## Summary

Implement a pure WSD generator function that converts `StateMachineMetadata` (from Frank.Statecharts runtime) into syntactically valid WSD text. The pipeline is `StateMachineMetadata -> Wsd.Types.Diagram -> WSD text`, reusing the existing AST types from #90. The generator extracts states, transitions, and guards from the boxed `Machine: obj` field via reflection/pattern matching on `StateMachine<'S,'E,'C>`, constructs a `Diagram` AST, and serializes it to WSD text. All transitions use the default solid forward arrow style (`->`), with ALPS enrichment deferred to #97. Guard annotations are emitted as `note over` elements with `[guard: key=value]` syntax. The cross-format validator has been carved out to spec 021.

## Technical Context

**Language/Version**: F# 8.0+ targeting .NET 8.0/9.0/10.0 (multi-targeting, matching Frank.Statecharts)
**Primary Dependencies**: None beyond what Frank.Statecharts already has; reuses `Wsd.Types` from #90
**Storage**: N/A (pure function, stateless)
**Testing**: Expecto (matching existing Frank test patterns) in `test/Frank.Statecharts.Tests/`
**Target Platform**: .NET multi-target library (net8.0;net9.0;net10.0)
**Project Type**: Internal modules within existing library project (Frank.Statecharts)
**Performance Goals**: Generate WSD for 20+ state machines without measurable performance degradation (SC-003)
**Constraints**: Internal visibility; no external NuGet dependencies; pure function with no side effects (SC-008)
**Scale/Scope**: State machines typically 3-20 states; must handle 20+ states with 50+ transitions per spec edge cases

## Constitution Check

*GATE: Must pass before Phase 0 research. Re-check after Phase 1 design.*

### I. Resource-Oriented Design -- PASS (Not Directly Applicable)

The WSD generator is a pure data transformation module with no HTTP surface. It reads `StateMachineMetadata` (resource-oriented endpoint metadata) and produces WSD text. It does not introduce any URL patterns, handlers, or routing concepts. No tension with resource-oriented design.

### II. Idiomatic F# -- PASS

- Generator is a pure function: `StateMachineMetadata -> Result<Diagram, GeneratorError>`
- Serializer is a pure function: `Diagram -> string`
- Error cases modeled with discriminated unions (`GeneratorError`)
- Pipeline-friendly composition: `metadata |> WsdGenerator.generate |> Result.map WsdSerializer.serialize`
- No mutable state in the generator; immutable AST construction throughout

### III. Library, Not Framework -- PASS

The generator is a pure function. No framework behavior, no lifecycle management. Consumers call the generator function and receive WSD text or a structured error. The generator makes no decisions about how the output is used (CLI, runtime serving, tests).

### IV. ASP.NET Core Native -- PASS (Not Directly Applicable)

The WSD generator has no ASP.NET Core dependency beyond reading `StateMachineMetadata`, which is an existing type in the assembly. It does not create any new ASP.NET Core abstractions.

### V. Performance Parity -- PASS

- Pure function with no I/O; performance is bounded by AST construction and string serialization
- StringBuilder-based serialization avoids intermediate string allocations
- No external library overhead

### VI. Resource Disposal Discipline -- PASS

The generator is pure: no `IDisposable` resources. Input is a `StateMachineMetadata` record, output is a `string` (via `Result`). StringBuilder used internally is not IDisposable.

### VII. No Silent Exception Swallowing -- PASS

The generator uses `Result<Diagram, GeneratorError>` for expected failures (unrecognized boxed type in `Machine: obj`). No `try/with` catch-all blocks. Unexpected errors propagate naturally.

### VIII. No Duplicated Logic -- PASS

- WSD AST types are reused from `Wsd/Types.fs` (no duplication)
- Guard annotation formatting uses the same `[guard: key=value]` syntax defined by the existing guard parser (no parallel implementation)
- Serialization logic is a single module; no format-specific helpers duplicated across files

## Project Structure

### Documentation (this feature)

```
kitty-specs/017-wsd-generator-cross-validator/
├── spec.md              # Feature specification
├── plan.md              # This file
├── research.md          # Reflection strategy, serialization decisions
├── data-model.md        # Generator function signatures, error types
└── quickstart.md        # Developer quickstart (generate WSD from metadata)
```

### Source Code (repository root)

```
src/
└── Frank.Statecharts/                  # Existing project (from #87)
    ├── Frank.Statecharts.fsproj        # Multi-target: net8.0;net9.0;net10.0
    ├── Wsd/                            # Existing: WSD parser modules (internal)
    │   ├── Types.fs                    # Token, AST DUs (REUSED by generator)
    │   ├── Lexer.fs                    # Tokenizer (used in roundtrip tests)
    │   ├── GuardParser.fs              # Guard annotation parser (used in roundtrip tests)
    │   └── Parser.fs                   # WSD parser (used in roundtrip tests)
    ├── Wsd/
    │   ├── Generator.fs                # NEW: StateMachineMetadata -> Diagram AST
    │   └── Serializer.fs               # NEW: Diagram AST -> WSD text string
    └── ...                             # Other existing modules

test/
└── Frank.Statecharts.Tests/            # Existing test project
    ├── Wsd/                            # Existing: WSD parser test modules
    │   ├── LexerTests.fs               # Existing
    │   ├── ParserTests.fs              # Existing
    │   ├── ...                         # Other existing test modules
    │   ├── GeneratorTests.fs           # NEW: Generator unit tests
    │   ├── SerializerTests.fs          # NEW: Serializer unit tests
    │   └── GeneratorRoundTripTests.fs  # NEW: Parse -> generate -> re-parse roundtrip
    └── Program.fs                      # Expecto entry point (updated with new modules)
```

**Structure Decision**: New source files added within the existing `Wsd/` subdirectory of `Frank.Statecharts`. This follows the pattern established by spec 007 (WSD parser). New `.fs` files must be added to the `.fsproj` `<Compile>` items after `Wsd/Parser.fs` since the generator depends on the AST types and the serializer depends on the generator types. File order: `Wsd/Types.fs` -> `Wsd/Lexer.fs` -> `Wsd/GuardParser.fs` -> `Wsd/Parser.fs` -> `Wsd/Serializer.fs` -> `Wsd/Generator.fs`.

## Parallel Work Analysis

### Dependency Graph

```
Wsd/Types.fs (existing AST types)
    |
    ├── Wsd/Serializer.fs (Diagram -> string; depends only on AST types)
    |       |
    |       └── Wsd/Generator.fs (StateMachineMetadata -> Diagram; uses Serializer for top-level API)
    |
    └── Wsd/Parser.fs (existing; used only in roundtrip tests)
```

### Parallelizable Work

1. **Serializer.fs** depends only on the existing `Wsd.Types` (specifically `Diagram`, `DiagramElement`, `Participant`, `Message`, `Note`, `GuardAnnotation`). It can be developed independently.
2. **Generator.fs** depends on `Wsd.Types` and `StateMachineMetadata` (from `StatefulResourceBuilder.fs`). It constructs a `Diagram` AST. It can be developed in parallel with the serializer since it produces a `Diagram` -- the serializer consumes one.
3. **Roundtrip tests** require both generator and parser to be working and depend on the serializer for text output.

### Recommended Implementation Order

| Phase | Module | Depends On | Can Parallel With |
|-------|--------|------------|-------------------|
| 1a | Wsd/Serializer.fs + SerializerTests.fs | Wsd/Types.fs (existing) | Generator |
| 1b | Wsd/Generator.fs + GeneratorTests.fs | Wsd/Types.fs + StateMachineMetadata (existing) | Serializer |
| 2 | GeneratorRoundTripTests.fs | Generator + Serializer + Parser (all existing/new) | -- |

## Design Decisions

### DD-01: Two-Phase Pipeline (Generator + Serializer)

The generator produces a `Diagram` AST (from `Wsd.Types`), not raw text. A separate serializer converts the `Diagram` to WSD text. This separation enables:
- Testing the generator's AST output independently from text formatting
- Reusing the serializer for any `Diagram` (not just generator output)
- Roundtrip testing by comparing ASTs structurally (parse -> compare AST vs generator -> compare AST)

### DD-02: Reflection-Based Machine Extraction

`StateMachineMetadata.Machine` is `obj` (boxed `StateMachine<'S,'E,'C>`). The generator must extract states and transitions from this boxed value. Strategy:
- Use F# reflection to discover the generic type arguments of the boxed `StateMachine<_,_,_>`
- Extract `Initial: 'S`, `Guards: Guard<'S,'E,'C> list`, and `StateMetadata: Map<'S, StateInfo>`
- Extract transitions by inspecting `StateHandlerMap: Map<string, (string * RequestDelegate) list>` for state names and handler methods
- The `Transition` function itself is opaque (a closure), so the generator infers transitions from `StateHandlerMap` keys (state names) and HTTP methods (event proxies)
- If the boxed type does not match `StateMachine<_,_,_>`, return `GeneratorError.UnrecognizedMachineType`

### DD-03: Default Arrow Style

All transitions use `ArrowStyle.Solid` + `Direction.Forward` (the `->` arrow). The spec explicitly defers arrow style differentiation to ALPS enrichment (#97). This simplifies the generator and avoids speculative design.

### DD-04: Guard Annotation Emission

Guards from `StateMachine<'S,'E,'C>.Guards` are emitted as `note over` elements with `[guard: name=predicate]` syntax, matching the existing guard parser's expected format. Guard notes are placed immediately after the transition message they protect. Multiple guards on the same machine are combined into a single `[guard: key1=value1, key2=value2]` annotation per the spec (FR-007).

### DD-05: State and Transition Discovery from StateMachineMetadata

The generator has two sources of state information:
- `StateMachineMetadata.StateHandlerMap: Map<string, (string * RequestDelegate) list>` -- keys are state names (`.ToString()` of each state), values are handler lists
- `StateMachineMetadata.InitialStateKey: string` -- the initial state name

Transitions are inferred from the handler map: each state has HTTP method handlers, and the method names serve as event labels in the WSD output. The actual `Transition` function is a closure and cannot be inspected. This means the generator produces a WSD diagram showing which HTTP methods are available in each state, not the full state-transition graph.

To extract the full state-transition graph (including target states), the generator must use reflection on the boxed `Machine: obj` to access `StateMachine<'S,'E,'C>` and its `Transition` function. However, since `Transition` is a function (not data), the full graph is not directly available. The generator will:
1. Use `StateHandlerMap` keys as participant names (states)
2. Use HTTP method names as message labels (events)
3. Emit messages between states based on the handler map structure
4. Use `InitialStateKey` to determine participant ordering (initial state first)

### DD-06: Internal Visibility

Both `Generator.fs` and `Serializer.fs` are `internal` to the `Frank.Statecharts` assembly, matching the parser modules. The `InternalsVisibleTo` for `Frank.Statecharts.Tests` already exists in the `.fsproj`.

## Complexity Tracking

No constitution violations requiring justification. All new code lives within an existing project as internal modules. No new NuGet dependencies. No new projects.

## Risks and Mitigations

| Risk | Impact | Mitigation |
|------|--------|------------|
| `StateMachineMetadata.Machine` is boxed and generic -- reflection may be fragile across TFMs | Medium | Test reflection logic against all three target frameworks (net8.0, net9.0, net10.0); use `FSharp.Reflection` for robust generic type inspection |
| Transition targets cannot be extracted from the opaque `Transition` closure | High | Document this limitation clearly; the generator produces a state-capability diagram (which methods are available in which states), not a full state-transition graph. The full graph requires either (a) the shared AST from spec 020, or (b) a new metadata field on `StateMachineMetadata` |
| Serializer output formatting may not exactly match hand-written WSD conventions | Low | Roundtrip tests verify parse-compatibility, not character-exact reproduction; cosmetic differences are acceptable per spec |
| Guard names from `StateMachine<'S,'E,'C>.Guards` may not have meaningful string representations | Low | Guard names are already strings (`Guard.Name: string`); the generator uses them directly |
