# Feature Specification: WSD Generator and Cross-Format Validator

**Feature Branch**: `017-wsd-generator-cross-validator`
**Created**: 2026-03-15
**Status**: Draft
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
- State machines with self-transitions (state transitions to itself) produce a message where sender and receiver are the same participant
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
- **FR-005**: System MUST emit a message (arrow) for each transition in the state machine, with the label set to the event name that triggers the transition
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
