# Research: WSD Generator

**Feature**: 017-wsd-generator-cross-validator
**Date**: 2026-03-15

## R-01: Extracting State Machine Structure from StateMachineMetadata

### Question

How can the WSD generator extract states, transitions, and guards from `StateMachineMetadata`, given that `Machine: obj` is a boxed generic type and `Transition` is an opaque closure?

### Findings

The `StateMachineMetadata` type provides two complementary sources of information:

**1. Direct fields (no reflection needed):**
- `StateHandlerMap: Map<string, (string * RequestDelegate) list>` -- map keys are state names (from `'S.ToString()`), values are HTTP method handlers
- `InitialStateKey: string` -- the initial state's string representation
- `EvaluateGuards` -- a closure, not inspectable

**2. Boxed Machine (reflection required):**
- `Machine: obj` contains a boxed `StateMachine<'S, 'E, 'C>` with:
  - `Initial: 'S` -- the initial state value
  - `Guards: Guard<'S, 'E, 'C> list` -- named guard predicates
  - `StateMetadata: Map<'S, StateInfo>` -- per-state metadata (allowed methods, isFinal, description)
  - `Transition: 'S -> 'E -> 'C -> TransitionResult<'S, 'C>` -- opaque function

**Reflection strategy:**
- Use `machine.GetType()` to get the runtime type
- Check if the generic type definition matches `StateMachine<_,_,_>` using `GetGenericTypeDefinition()`
- Access fields via F# reflection (`FSharp.Reflection.FSharpType`, `FSharpValue`) or standard .NET reflection
- Since `StateMachine` is an F# record, use `FSharpValue.GetRecordFields(machine)` to extract field values
- Field ordering matches declaration order in the record type

**Transition graph limitation:**
The `Transition` function is a closure -- it cannot be inspected to enumerate all possible (source, event, target) triples. The generator can only infer:
- Which states exist (from `StateHandlerMap` keys or `StateMetadata` keys)
- Which HTTP methods are available per state (from `StateHandlerMap` values)
- Which guards exist (from `Guards` list, but guards are machine-wide, not per-transition)

This means the generated WSD shows state capabilities, not the full transition graph. This is acceptable for the current spec scope. A richer transition graph would require either:
- Explicit transition table metadata (a new field on `StateMachine`)
- The shared AST from spec 020 populated by an external tool

### Decision

Use the direct `StateHandlerMap` and `InitialStateKey` fields as the primary data source. Use reflection on the boxed `Machine` only to extract `Guards` (for guard annotations) and `StateMetadata` (for state descriptions and final-state markers). Do not attempt to reverse-engineer the `Transition` function.

### Alternatives Considered

1. **Add a `TransitionTable` field to `StateMachine`** -- rejected because it requires a breaking change to the core type, which is out of scope for this spec
2. **Execute the `Transition` function with all state/event combinations** -- rejected because (a) we don't know all possible event values without enumerating the DU, (b) the function requires a `'Context` value, (c) it may have side effects via guards
3. **Parse the F# source code** -- rejected as completely impractical for a runtime tool

## R-02: WSD Serialization Format

### Question

What formatting conventions should the WSD serializer follow to produce clean, human-readable output that is also parseable by the existing WSD parser?

### Findings

The existing WSD parser (from #90) accepts the following syntax:

```
title <text>
participant <name>
participant <name> as <alias>
<sender>-><receiver>: <label>
<sender>--><receiver>: <label>
note over <participant>: <text>
note over <participant>: [guard: key=value, key2=value2]
```

**Formatting decisions:**
- Use Unix line endings (`\n`) per spec edge case requirement
- `title` directive first, followed by blank line
- `participant` declarations next, one per line, followed by blank line
- Messages and notes in sequence, separated by single newlines
- Guard annotations as `note over` immediately after the transition message they annotate
- No `autonumber` directive (not applicable to generated output)
- No grouping blocks (the generator produces flat sequences; grouping is a WSD-specific visual concept)
- Participant names that contain spaces or special characters should be quoted with double quotes

**Escaping rules from the lexer:**
- String literals use double quotes: `"name with spaces"`
- Escaped quotes within strings: `\"`
- Identifiers can contain alphanumeric, underscore, and hyphen characters
- Anything else needs quoting

### Decision

Implement a simple serializer that:
1. Emits `title <resourceName>\n\n`
2. Emits `participant <stateName>\n` for each state, initial state first
3. Emits a blank line separator
4. Emits messages and guard notes for each state's handlers
5. Quotes participant names containing non-identifier characters
6. Uses `\n` line endings throughout

### Alternatives Considered

1. **Pretty-printing with configurable indentation** -- rejected; unnecessary complexity for a first version. The output is flat (no groups).
2. **Exact-match formatting to original WSD input** -- rejected; the spec explicitly allows cosmetic differences. Semantic equivalence is the bar.

## R-03: Guard Extraction and Annotation

### Question

How should the generator extract guard information from `StateMachine<'S,'E,'C>.Guards` and emit it as WSD `[guard: ...]` annotations?

### Findings

The `Guard<'S,'E,'C>` type has:
- `Name: string` -- the guard's name (e.g., "role", "auth")
- `Predicate: GuardContext<'S,'E,'C> -> GuardResult` -- opaque function

The guard name is the key used in `[guard: name=...]` syntax. The predicate is opaque and cannot provide the value portion. The generator can emit the guard name as the key but has no runtime-inspectable value.

**Options for the value field:**
- Use the guard name only: `[guard: role]` (missing `=value`, will trigger guard parser error)
- Use the guard name as both key and value: `[guard: role=role]` (redundant but syntactically valid)
- Use a sentinel value: `[guard: role=*]` (indicates "any value", syntactically valid)
- Omit the value: `[guard: role=]` (empty value triggers guard parser warning)

### Decision

Use the guard's `Name` as the key and `"*"` (wildcard) as the value: `[guard: role=*]`. This is syntactically valid (no parser errors), clearly indicates that the actual guard predicate is opaque, and the wildcard convention is recognizable. If multiple guards exist, combine them: `[guard: role=*, auth=*]`.

Guard annotations are emitted as machine-wide notes (since guards in `StateMachine` are not per-transition but apply to all transitions). They are placed after the participant declarations and before the messages, as a `note over <initialState>` to associate them with the state machine's entry point.

### Alternatives Considered

1. **Per-transition guard notes** -- not possible because `StateMachine.Guards` is a flat list, not keyed by transition
2. **Omit guards entirely** -- rejected because FR-007 requires guard annotation emission
3. **Use guard predicate return type as value** -- rejected because the predicate requires a `GuardContext` to evaluate

## R-04: Handling Degenerate and Edge Cases

### Question

How should the generator handle edge cases listed in the spec?

### Findings

| Edge Case | Strategy |
|-----------|----------|
| Single state, no transitions | Emit one participant, no messages. Valid WSD. |
| Self-transitions | Emit message where sender = receiver = same participant name |
| Special characters in state names | Quote with double quotes if name contains non-identifier chars |
| Guard names with special characters | Escape within `[guard: ...]` syntax (follow guard parser conventions) |
| Large state machines (20+ states) | No special handling needed; StringBuilder serialization scales linearly |
| Unrecognized boxed Machine type | Return `GeneratorError.UnrecognizedMachineType` with the actual runtime type name |
| Empty StateHandlerMap | Valid: emit title only, no participants or messages |
| State with empty handler list | Emit participant for the state but no messages from it |
| Consistent line endings | Use `\n` throughout; StringBuilder.AppendLine replaced with explicit `\n` |

### Decision

Handle all edge cases as documented above. The generator returns `Result<Diagram, GeneratorError>` to communicate structured errors for the unrecognized-type case. All other cases produce valid (possibly minimal) WSD output.
