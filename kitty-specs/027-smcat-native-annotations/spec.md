# Feature Specification: smcat Native Annotations and Generator Fidelity

**Feature Branch**: `027-smcat-native-annotations`
**Created**: 2026-03-18
**Status**: Draft
**Input**: Complete the remainder of issue #113. Parser and Generator follow WSD too closely without the benefits of being focused on the smcat format. Use smcat-specific annotations, not WSD annotations. Ensure full fidelity on the generator side. Leverage the DU for formats as much as possible.
**Closes**: #113

## Background

The smcat parser, generator, and serializer were migrated to the shared `StatechartDocument` AST (spec 022), but the implementation mirrors the WSD pattern too closely. The generator creates synthetic `"initial"` and `"final"` string names instead of using typed `StateKind` values. Generated elements carry empty `Annotations = []` lists instead of smcat-specific metadata. The `SmcatMeta` discriminated union has only three cases (`SmcatColor`, `SmcatStateLabel`, `SmcatActivity`) and doesn't capture the full richness of smcat semantics. The result is a generator that produces a correct but impoverished AST — one that loses information during generation and can't guarantee round-trip fidelity through the serializer.

## User Scenarios & Testing

### User Story 1 - Generator Produces Typed smcat AST (Priority: P1)

A developer using the Frank statechart system generates an smcat document from `StateMachineMetadata`. The resulting `StatechartDocument` contains `StateNode` entries with correct `StateKind` values and `SmcatAnnotation` metadata on every state and transition, enabling the serializer to produce full-fidelity smcat text without reverse-engineering intent from string names.

**Why this priority**: The generator is the primary entry point for producing smcat output from Frank's runtime metadata. Without typed annotations, the AST is an incomplete intermediate representation that forces downstream consumers to guess semantics from conventions.

**Independent Test**: Generate a `StatechartDocument` from a `StateMachineMetadata` with initial, regular, and final states. Verify every `StateNode` carries `SmcatAnnotation(SmcatStateType(...))` and every `TransitionElement` carries `SmcatAnnotation(SmcatTransitionKind ...)` with the correct semantic role.

**Acceptance Scenarios**:

1. **Given** a `StateMachineMetadata` with an initial state key and state handler map, **When** the smcat generator produces a `StatechartDocument`, **Then** the initial state node has `Kind = Initial` and `SmcatAnnotation(SmcatStateType(Initial, Explicit))`, not a string-named `"initial"` node with `Kind = Regular`.
2. **Given** a `StateMachineMetadata` with a final state, **When** the smcat generator produces a `StatechartDocument`, **Then** the final state node has `Kind = Final` and `SmcatAnnotation(SmcatStateType(Final, Explicit))`, and the transition to it carries `SmcatAnnotation(SmcatTransitionKind FinalTransition)`.
3. **Given** a `StateMachineMetadata` with self-transition handlers, **When** the smcat generator produces a `StatechartDocument`, **Then** each self-transition carries `SmcatAnnotation(SmcatTransitionKind SelfTransition)`.

---

### User Story 2 - Serializer Emits Full-Fidelity smcat Text (Priority: P1)

A developer serializes a `StatechartDocument` (produced by the generator or the parser) to smcat text. The serializer consumes `SmcatAnnotation` metadata to decide whether to emit explicit `[type="..."]` attributes, producing output that faithfully represents the AST's semantic content.

**Why this priority**: Without annotation-aware serialization, the generator's richer AST is wasted — the serializer would strip the additional metadata and produce the same output as before.

**Independent Test**: Serialize a `StatechartDocument` containing states with `SmcatStateType(Initial, Explicit)` and `SmcatStateType(Regular, Inferred)`. Verify the explicit type emits `[type="initial"]` and the inferred type does not emit a type attribute.

**Acceptance Scenarios**:

1. **Given** a `StateNode` with `SmcatAnnotation(SmcatStateType(Initial, Explicit))`, **When** serialized, **Then** the output includes `[type="initial"]` as an attribute on that state.
2. **Given** a `StateNode` with `SmcatAnnotation(SmcatStateType(Regular, Inferred))`, **When** serialized, **Then** no `type` attribute is emitted (the type is conveyed by the state's name or defaults to regular).
3. **Given** a `StateNode` with `SmcatAnnotation(SmcatColor "red")` and `SmcatAnnotation(SmcatStateType(Choice, Explicit))`, **When** serialized, **Then** the output includes `[color="red" type="choice"]` with both attributes present.

---

### User Story 3 - Parser Preserves Type Origin (Priority: P1)

A developer parses smcat text containing both explicitly typed states (`[type="initial"]`) and convention-named states (named `"initial"` without a type attribute). The parser produces `SmcatAnnotation(SmcatStateType(...))` on every state, distinguishing explicit from inferred type origins.

**Why this priority**: Round-trip fidelity depends on the parser recording *how* the type was determined. Without this, the serializer cannot reconstruct the original smcat syntax.

**Independent Test**: Parse `initial [type="initial"]; idle;` and verify the first state has `SmcatStateType(Initial, Explicit)` and the second has `SmcatStateType(Regular, Inferred)`.

**Acceptance Scenarios**:

1. **Given** smcat text with `myState [type="initial"];`, **When** parsed, **Then** the resulting `StateNode` has `Kind = Initial` and `Annotations` contains `SmcatAnnotation(SmcatStateType(Initial, Explicit))`.
2. **Given** smcat text with `initial;` (no explicit type attribute), **When** parsed, **Then** the resulting `StateNode` has `Kind = Initial` and `Annotations` contains `SmcatAnnotation(SmcatStateType(Initial, Inferred))`.
3. **Given** smcat text with `idle;` (a regular state with no type attribute or naming convention), **When** parsed, **Then** the resulting `StateNode` has `Kind = Regular` and `Annotations` contains `SmcatAnnotation(SmcatStateType(Regular, Inferred))`.

---

### User Story 4 - Round-Trip Fidelity (Priority: P2)

A developer parses smcat text, serializes the resulting AST back to smcat, and parses the output again. The two ASTs are structurally equivalent, proving that no information is lost in the parse-serialize cycle.

**Why this priority**: Round-trip fidelity is the definitive test that the annotation system works end-to-end. It depends on stories 1-3 being complete.

**Independent Test**: Parse a representative smcat document with explicit types, inferred types, colors, labels, activities, composite states, and various transition kinds. Serialize. Parse again. Compare ASTs for structural equality.

**Acceptance Scenarios**:

1. **Given** smcat text with a mix of explicit and inferred state types, **When** parsed then serialized then parsed again, **Then** the two `StatechartDocument` ASTs are structurally equal (same `StateKind`, same annotations, same transitions).
2. **Given** smcat text with composite states, activities, colors, and labels, **When** round-tripped through parse-serialize-parse, **Then** all `SmcatAnnotation` metadata is preserved.
3. **Given** smcat text with all five `SmcatTransitionKind` variants representable in the format, **When** round-tripped, **Then** transition annotations are preserved (noting that `SmcatTransitionKind` on parsed transitions is inferred by the parser from structure, not from smcat syntax).

---

### User Story 5 - Expanded SmcatMeta DU (Priority: P1)

The shared AST's `SmcatMeta` discriminated union is expanded with new cases and the misleading `SmcatActivity` case is renamed, so that the DU accurately models smcat-specific metadata.

**Why this priority**: The expanded DU is the foundation that all other stories depend on. Without the right type definitions, generator, parser, and serializer changes cannot be implemented.

**Independent Test**: Build the project across all target frameworks. Verify the new DU cases compile and are usable from all consuming modules.

**Acceptance Scenarios**:

1. **Given** the updated `SmcatMeta` DU, **When** the project is built, **Then** it compiles successfully across net8.0, net9.0, and net10.0.
2. **Given** a `SmcatTransitionKind` value, **When** pattern-matched, **Then** all five cases (InitialTransition, FinalTransition, SelfTransition, ExternalTransition, InternalTransition) are exhaustively covered.
3. **Given** the renamed `SmcatCustomAttribute` case, **When** used in parser and serializer, **Then** it replaces all former `SmcatActivity` usage with no behavioral change for non-standard attributes.

---

### Edge Cases

- What happens when a state has both naming-convention inference AND an explicit `[type="..."]` attribute that disagrees (e.g., state named `"initial"` with `[type="final"]`)? The explicit attribute takes precedence (existing behavior in `inferStateType`), and the annotation records `Explicit`.
- What happens when the generator produces a state with `Kind = Initial` but no smcat annotation? The serializer should handle this gracefully by falling back to emitting the state kind as a `[type="..."]` attribute.
- What happens when a `StatechartDocument` from a non-smcat source (e.g., SCXML parser) is serialized to smcat? States without `SmcatAnnotation` entries should serialize using `StateNode.Kind` to infer the appropriate smcat type attribute.
- What happens when `SmcatTransitionKind` is absent from a transition's annotations? The serializer treats it as a regular external transition (current behavior).

## Requirements

### Functional Requirements

- **FR-001**: The `SmcatMeta` DU MUST include a `SmcatStateType of kind: StateKind * origin: SmcatTypeOrigin` case for carrying state type metadata with explicit/inferred origin tracking.
- **FR-002**: The shared AST MUST define `SmcatTypeOrigin = Explicit | Inferred` as a discriminated union for type origin tracking.
- **FR-003**: The `SmcatMeta` DU MUST include a `SmcatTransitionKind` case wrapping a `SmcatTransitionKind` DU with cases: `InitialTransition`, `FinalTransition`, `SelfTransition`, `ExternalTransition`, `InternalTransition`.
- **FR-004**: The `SmcatActivity` case MUST be renamed to `SmcatCustomAttribute of key: string * value: string` with no change in semantics for non-standard attribute storage.
- **FR-005**: The smcat generator MUST produce `StateNode` entries with `Kind` set to the appropriate `StateKind` value (not `Regular` for all states).
- **FR-006**: The smcat generator MUST annotate every state node with `SmcatAnnotation(SmcatStateType(...))` carrying the state's kind and `Explicit` origin.
- **FR-007**: The smcat generator MUST annotate every transition with `SmcatAnnotation(SmcatTransitionKind ...)` matching its semantic role.
- **FR-008**: The smcat generator MUST NOT create synthetic string-named `"initial"` or `"final"` states; it MUST use typed `StateKind.Initial` and `StateKind.Final` on proper `StateNode` entries.
- **FR-009**: The smcat serializer MUST emit `[type="..."]` attributes for states whose `SmcatStateType` annotation has `origin = Explicit`.
- **FR-010**: The smcat serializer MUST NOT emit `[type="..."]` attributes for states whose `SmcatStateType` annotation has `origin = Inferred` (the type is conveyed by naming convention or is the default `Regular`).
- **FR-011**: The smcat serializer MUST handle `StatechartDocument` nodes that lack `SmcatAnnotation` entries by falling back to `StateNode.Kind` for type determination.
- **FR-012**: The smcat parser MUST store `SmcatAnnotation(SmcatStateType(kind, Explicit))` when a `[type="..."]` attribute is present in the source.
- **FR-013**: The smcat parser MUST store `SmcatAnnotation(SmcatStateType(kind, Inferred))` when the state type is determined by naming convention or defaults to `Regular`.
- **FR-014**: The smcat parser MUST continue to consume the `type` attribute for `StateKind` inference (existing behavior) while also storing it as an annotation (new behavior).
- **FR-015**: Round-trip fidelity MUST be preserved: parsing smcat text, serializing the AST, and parsing again MUST produce structurally equivalent `StatechartDocument` values.
- **FR-016**: All changes MUST compile across net8.0, net9.0, and net10.0 target frameworks.
- **FR-017**: All existing smcat tests MUST continue to pass after updating to use the new annotation types.

### Key Entities

- **SmcatTypeOrigin**: Two-case DU (`Explicit | Inferred`) tracking whether a state's type was declared via attribute or inferred from naming convention.
- **SmcatTransitionKind**: Five-case DU (`InitialTransition | FinalTransition | SelfTransition | ExternalTransition | InternalTransition`) capturing the semantic role of each transition in smcat.
- **SmcatMeta (expanded)**: Format-specific annotation DU carrying `SmcatColor`, `SmcatStateLabel`, `SmcatCustomAttribute`, `SmcatStateType`, and `SmcatTransitionKind`.

## Success Criteria

### Measurable Outcomes

- **SC-001**: Every `StateNode` produced by the smcat generator carries at least one `SmcatAnnotation` — zero nodes with empty annotation lists.
- **SC-002**: Every `TransitionElement` produced by the smcat generator carries a `SmcatTransitionKind` annotation — zero transitions with empty annotation lists.
- **SC-003**: Round-trip test (parse → serialize → parse) produces structurally equivalent ASTs for all test fixtures.
- **SC-004**: The smcat generator produces zero string-named `"initial"` or `"final"` state identifiers — all initial/final semantics conveyed through `StateKind` and annotations.
- **SC-005**: `dotnet build` and `dotnet test` pass across all target frameworks (net8.0, net9.0, net10.0) with zero regressions.
- **SC-006**: Exhaustive pattern matching on `SmcatTransitionKind` compiles without warnings — all five cases handled in every match expression.

## Assumptions

- The `SmcatTransitionKind` annotation on parsed transitions is inferred by the parser from structural analysis (source/target names, self-loop detection), not from smcat syntax — smcat has no explicit transition type syntax.
- The generator uses `Explicit` origin for all generated state types because generated states are intentionally typed, not convention-named.
- The cross-format validator (spec 021) and pipeline (spec 025) will consume the richer annotations without modification, since they work at the `StatechartDocument` level and are annotation-agnostic.
- Renaming `SmcatActivity` to `SmcatCustomAttribute` is a binary-breaking change within the `Frank.Statecharts` assembly but does not affect the public API (all smcat modules are `internal`).
