---
source: kitty-specs/017-wsd-generator-cross-validator
status: complete
type: spec
---

# Feature Specification: WSD Generator and Cross-Format Validator

**Feature Branch**: `017-wsd-generator-cross-validator`
**Created**: 2026-03-15
**Status**: Done
**GitHub Issue**: #91
**Dependencies**: #90 (WSD Parser — provides AST types and enables roundtrip tests), Frank.Statecharts (#87) core runtime, Shared Statechart AST (spec 020) defines the unified AST
**Parallel with**: #97 (ALPS), #98 (SCXML), #100 (smcat) — each owns its own generator
**Parent issue**: #57 (pipeline architecture, WSD mapping rules, examples)
**Location**: Internal to `src/Frank.Statecharts/` (WSD generator namespace), tests in `test/Frank.Statecharts.Tests/`
**Note**: The cross-format validator has been carved out into spec 021 (cross-format validator, issue #112). This spec covers only the WSD generator.

## Clarifications

### Session 2026-03-15

- Q: Should the WSD generator infer arrow styles from HTTP method semantics (GET = dashed/safe, POST = solid/unsafe), depend on ALPS transition type metadata from #97, or use defaults? → A: Use a default arrow style for all transitions. ALPS enrichment comes later when #97 lands.
- Q: What is the scope of the cross-format validator given that other format generators (#97 ALPS, #98 SCXML, #100 smcat) do not exist yet? → A: Define the full validation framework AND implement all cross-format checks. The validator skips checks gracefully when a format's parser/generator is not available yet. All format parsers will share a single AST (defined in a separate spec). The cross-format validator validates against this shared AST.
- Q: Is runtime serving of WSD at `/_statecharts/{resourceName}.wsd` in scope? → A: Out of scope. This spec covers only the pure generator function (`StateMachineMetadata -> WSD text`) and the cross-format validator. Runtime serving is handled by `frank-cli compile` (#94). Each parser/generator spec adds its validations to the cross-format validator.
- Q: How are transitions represented in the WSD output given that `StateHandlerMap` maps states to HTTP method handlers, not transitions? → A: The generator reads from `StateHandlerMap` which provides HTTP method handlers per state, not transition targets. The WSD output is a state-capability diagram: each state emits a message per HTTP method handler, with a synthetic "Resource" participant (named after the resource from `GenerateOptions`) as the receiver.

## Background

Web Sequence Diagrams (WSD) is a text-based notation for describing interactions between participants. The WSD Parser (spec #90, issue #90) provides the lexer, parser, and typed AST for reading WSD text into structured data. This feature builds the reverse direction: generating WSD text from compiled `StateMachineMetadata` that lives on endpoint metadata at build time.

The generator reads the same `StateMachineMetadata` that middleware uses at runtime. At build time, `frank-cli` loads the compiled assembly, reads endpoint metadata, and generates WSD output. The generator constructs a WSD AST (reusing the types from #90) from `StateMachineMetadata` and serializes it to text.

The cross-format validator has been carved out into a separate spec that depends on all format parsers/generators being available.

## User Scenarios & Testing

### User Story 1 - Generate WSD Text from a Stateful Resource (Priority: P1)

A developer has defined a `statefulResource` in Frank with states, transitions, and guards. They run `frank-cli compile` (or call the generator function directly) and receive valid WSD text that accurately represents the state machine: participants correspond to states, messages correspond to transitions, and the diagram is parseable back through the WSD parser.

**Why this priority**: This is the core value proposition -- without a correct generator, the WSD-to-spec roundtrip is incomplete, and developers cannot visualize their state machines as sequence diagrams.

**Independent Test**: Define a simple stateful resource (e.g., a 3-state turnstile: Locked, Unlocked, Broken), generate WSD text, parse it back through the WSD parser, and verify the resulting AST contains the expected participants, messages, and structure.

**Acceptance Scenarios**:

1. **Given** a `StateMachineMetadata` with three states (Locked, Unlocked, Broken) and transitions between them, **When** the generator produces WSD text, **Then** the output contains `participant` declarations for each state and messages for each transition with labels matching event names.
2. **Given** a `StateMachineMetadata` with an initial state of "Locked", **When** the generator produces WSD text, **Then** the initial state appears as the first participant in the diagram.
3. **Given** generated WSD text, **When** parsed back through the WSD parser from #90, **Then** the parser produces a valid AST with no errors.
4. **Given** a `StateMachineMetadata` with no transitions (single terminal state), **When** the generator produces WSD text, **Then** the output contains a single participant declaration and no messages.

---

### User Story 2 - Roundtrip Fidelity: Parse then Generate (Priority: P1)

A developer writes a WSD diagram, parses it into metadata, and then generates WSD back. The generated output preserves the essential semantics: same participants, same transitions, same guard annotations. Cosmetic differences (whitespace, ordering of declarations) are acceptable.

**Why this priority**: Roundtrip fidelity is the key quality signal. If parse-then-generate loses information, neither direction can be trusted.

**Independent Test**: Parse Amundsen's onboarding WSD example, convert the AST to `StateMachineMetadata` (or the shared AST), generate WSD back, re-parse the generated output, and compare the two ASTs structurally.

**Acceptance Scenarios**:

1. **Given** a WSD text parsed into an AST and then generated back, **When** the generated text is re-parsed, **Then** the two ASTs have the same set of participants (by name), the same set of messages (by sender, receiver, and label), and the same guard annotations.
2. **Given** a WSD with guard annotations (`note over X: [guard: role=PlayerX]`), **When** roundtripped through parse-generate-parse, **Then** the guard annotations are preserved with the same key-value pairs.
3. **Given** a WSD with multiple arrow types, **When** roundtripped, **Then** the default arrow style is used in the generated output (since ALPS enrichment is deferred), and a warning or note indicates that arrow style differentiation requires ALPS metadata.

---

### User Story 3 - Generate Guard Annotations in WSD Output (Priority: P2)

A developer's state machine has named guards with predicates. The WSD generator emits `note over` annotations with guard syntax so that the guard information is preserved in the WSD output and can be parsed back.

**Why this priority**: Guards are essential for access control in Frank.Statecharts. Without guard annotations in WSD output, the generated diagrams lose critical security information.

**Independent Test**: Define a stateful resource with guards (e.g., role-based guards on transitions), generate WSD, and verify the output contains `note over` elements with `[guard: ...]` syntax matching the guard definitions.

**Acceptance Scenarios**:

1. **Given** a `StateMachineMetadata` with a guard named "role" on a transition, **When** WSD is generated, **Then** the output contains a `note over` annotation with `[guard: role=...]` syntax adjacent to the corresponding transition message.
2. **Given** a `StateMachineMetadata` with multiple guards on a single transition, **When** WSD is generated, **Then** the guard annotation contains all key-value pairs in a single `[guard: key1=value1, key2=value2]` note.
3. **Given** a `StateMachineMetadata` with no guards, **When** WSD is generated, **Then** no `note over` annotations with guard syntax appear in the output.

---

### Edge Cases

- State machines with a single state and no transitions (degenerate case) produce valid WSD with one participant and no messages
- State machines with self-transitions (state transitions to itself): note that this is a state-capability diagram (listing HTTP method handlers per state), not a transition graph. Self-transitions are not explicitly modeled; each state's handlers produce messages to the synthetic Resource participant
- State names containing special characters (spaces, hyphens) are quoted or escaped in WSD participant declarations
- Guard names with special characters are properly escaped in `[guard: ...]` syntax
- Very large state machines (20+ states, 50+ transitions) generate WSD without performance degradation
- `StateMachineMetadata` with boxed `Machine: obj` that cannot be unboxed to a known type produces a clear error, not a runtime exception
- Duplicate state names across different state machines in the same assembly are disambiguated by resource name
- The generator handles `StateHandlerMap` entries where the handler list is empty (state exists but has no handlers)
- Generated WSD text uses consistent line endings (Unix `\n`)

## Requirements

### Functional Requirements

#### WSD Generator

- **FR-001**: System MUST accept `StateMachineMetadata` as input and produce syntactically valid WSD text as output
- **FR-002**: System MUST construct a WSD AST (reusing the types defined in #90) as an intermediate representation before serializing to text
- **FR-003**: System MUST emit a `participant` declaration for each state in the state machine, using the state's string representation as the participant name
- **FR-004**: System MUST emit the initial state as the first participant in the generated WSD output
- **FR-005**: System MUST emit a message (arrow) for each HTTP method handler in each state, with a synthetic "Resource" participant (named after the resource from `GenerateOptions`) as the receiver, and the label set to the HTTP method name
- **FR-006**: System MUST use a single default arrow style (`->`, solid forward) for all transitions, since ALPS transition type enrichment is deferred
- **FR-007**: System MUST emit `note over` annotations with `[guard: key=value]` syntax for transitions that have associated guards, preserving guard names as keys
- **FR-008**: System MUST produce WSD output that can be parsed back through the WSD parser from #90 without errors (roundtrip compatibility)
- **FR-009**: System MUST emit a `title` directive using the resource name from the `StateMachineMetadata`
- **FR-010**: System MUST extract state and transition information from the `StateMachineMetadata` boxed `Machine` field, regardless of the concrete state machine type used at definition time
- **FR-011**: System MUST produce a clear, structured error when `StateMachineMetadata` cannot be interpreted (e.g., unrecognized boxed type)

*Cross-format validator requirements have been moved to a separate spec.*

### Key Entities

- **WsdGenerator**: Pure function that accepts `StateMachineMetadata` and produces a `Diagram` AST (from #90's types), then serializes it to WSD text
- **WsdSerializer**: Converts a `Diagram` AST to WSD text string, handling formatting, line endings, and escaping
*Cross-format validator entities (ValidationReport, ValidationFailure, ValidationCheck, FormatArtifact) have been moved to the cross-format validator spec.*

## Success Criteria

### Measurable Outcomes

- **SC-001**: Generator produces valid WSD text for the tic-tac-toe state machine example, and the output parses back through the WSD parser (#90) with zero errors
- **SC-002**: Roundtrip test (parse WSD -> generate WSD -> re-parse) preserves all participants, messages, and guard annotations between the original and roundtripped ASTs
- **SC-003**: Generator handles state machines with 1 to 20+ states without errors or measurable performance degradation
*Cross-format validator success criteria (SC-004 through SC-006) have been moved to the cross-format validator spec.*
- **SC-007**: Library compiles and all tests pass across all supported target platforms
- **SC-008**: Generator is a pure function with no side effects, suitable for both build-time CLI invocation and potential future runtime use

## Assumptions

- The WSD AST types from #90 are stable and available for reuse by the generator
- `StateMachineMetadata` contains sufficient information (via reflection on the boxed `Machine: obj`) to reconstruct states, transitions, and guards
- The shared AST for all format parsers is defined in spec 020
- Guard names in `StateMachineMetadata` correspond to the key names used in WSD `[guard: ...]` annotations
- Arrow style differentiation (solid vs. dashed) is deferred to ALPS enrichment (#97); this spec uses solid forward arrows for all transitions


## Research

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
