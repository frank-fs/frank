# Feature Specification: SCXML Shared AST Migration

**Feature Branch**: `024-scxml-shared-ast-migration`
**Created**: 2026-03-16
**Status**: Draft
**Input**: Issue #114 - SCXML: migrate parser and generator to shared StatechartDocument AST

## User Scenarios & Testing *(mandatory)*

### User Story 1 - Parser Produces Shared AST Directly (Priority: P1)

A developer parsing an SCXML document receives a shared `Ast.ParseResult` containing a `StatechartDocument` directly, without needing to call a separate mapper. The parser handles all SCXML constructs -- states, transitions, history, invoke, data model, parallel regions -- and maps them into the shared AST types, preserving all SCXML-specific data via `ScxmlAnnotation` entries on the relevant AST nodes.

**Why this priority**: The parser is the entry point for all SCXML processing. Until it produces shared AST types, no downstream consumer (cross-format validator, generator, tooling) can work with SCXML artifacts natively. This is the foundational change that enables everything else.

**Independent Test**: Can be fully tested by parsing SCXML strings and asserting on the resulting `StatechartDocument` structure, state nodes, transition edges, annotations, and data entries. Delivers immediate value because any code consuming shared AST types can now accept SCXML input without a mapper.

**Acceptance Scenarios**:

1. **Given** valid SCXML with states and transitions, **When** `parseString` is called, **Then** the result is an `Ast.ParseResult` where `Document.Elements` contains `StateDecl` nodes for each state and `TransitionElement` edges for each transition, with correct identifiers, events, guards, and targets.
2. **Given** SCXML with a `<parallel>` element, **When** parsed, **Then** the resulting `StateNode` has `Kind = Parallel` and contains child `StateNode` entries for each parallel region.
3. **Given** SCXML with `<history>` elements, **When** parsed, **Then** history pseudo-states appear as child `StateNode` entries with `Kind = ShallowHistory` or `Kind = DeepHistory` and carry a `ScxmlAnnotation(ScxmlHistory(...))` annotation preserving the history id and kind.
4. **Given** SCXML with `<invoke>` elements, **When** parsed, **Then** the parent `StateNode` carries `ScxmlAnnotation(ScxmlInvoke(...))` annotations preserving invoke type, src, and id.
5. **Given** SCXML with `<datamodel>/<data>` entries at document and state level, **When** parsed, **Then** all data entries appear in `StatechartDocument.DataEntries` with correct names (mapped from SCXML `id` attribute) and expressions.
6. **Given** SCXML with `initial`, `name`, `datamodel`, and `binding` attributes on the root `<scxml>` element, **When** parsed, **Then** `StatechartDocument.InitialStateId` and `StatechartDocument.Title` are set, and document-level `ScxmlAnnotation` entries preserve the datamodel type and binding attribute.
7. **Given** SCXML with `<transition type="internal">`, **When** parsed, **Then** the `TransitionEdge` carries a `ScxmlAnnotation(ScxmlTransitionType(Internal))` annotation.
8. **Given** SCXML with multi-target transitions (`target="s1 s2 s3"`), **When** parsed, **Then** the `TransitionEdge` carries a `ScxmlAnnotation(ScxmlMultiTarget([...]))` annotation preserving all targets, and `TransitionEdge.Target` is set to the first target.
9. **Given** malformed XML, **When** parsed, **Then** the result contains `ParseFailure` entries (shared AST type) with descriptions and positions, matching the existing error reporting behavior.
10. **Given** SCXML with unknown elements, **When** parsed, **Then** the result contains `ParseWarning` entries (shared AST type) matching existing warning behavior.

---

### User Story 2 - Generator Consumes Shared AST (Priority: P1)

A developer generating SCXML output provides a `StatechartDocument` and receives valid SCXML XML text. The generator reconstructs the full SCXML document structure -- including hierarchy, history, invoke, data model, transition types, multi-target transitions, and document-level attributes -- by reading shared AST nodes and extracting SCXML-specific data from `ScxmlAnnotation` entries.

**Why this priority**: Equal priority with the parser because both must work with the shared AST for the migration to be complete. The generator is the other half of the round-trip: without it, there is no way to produce SCXML output from the shared representation.

**Independent Test**: Can be fully tested by constructing `StatechartDocument` values with appropriate `ScxmlAnnotation` entries and asserting the generated XML contains the expected elements and attributes. Round-trip tests (parse then generate then re-parse) provide the strongest validation.

**Acceptance Scenarios**:

1. **Given** a `StatechartDocument` with `StateDecl` nodes and `TransitionElement` edges, **When** `generate` is called, **Then** valid SCXML XML is produced with `<state>`, `<parallel>`, `<final>` elements and `<transition>` child elements.
2. **Given** a `StatechartDocument` with `StateNode` entries that have children, **When** generated, **Then** the XML reflects the correct hierarchical nesting (compound states contain child state elements).
3. **Given** a `StatechartDocument` with `ScxmlAnnotation(ScxmlHistory(...))` on child `StateNode` entries, **When** generated, **Then** `<history>` elements are produced with correct `id` and `type` attributes.
4. **Given** a `StatechartDocument` with `ScxmlAnnotation(ScxmlInvoke(...))` on `StateNode` entries, **When** generated, **Then** `<invoke>` child elements are produced with correct `type`, `src`, and `id` attributes.
5. **Given** a `StatechartDocument` with `ScxmlAnnotation(ScxmlTransitionType(Internal))` on a `TransitionEdge`, **When** generated, **Then** the `<transition>` element has `type="internal"`.
6. **Given** a `StatechartDocument` with `ScxmlAnnotation(ScxmlMultiTarget(...))` on a `TransitionEdge`, **When** generated, **Then** the `<transition>` element has a space-separated `target` attribute with all targets.
7. **Given** a `StatechartDocument` with document-level `ScxmlAnnotation` entries for datamodel type and binding, **When** generated, **Then** the root `<scxml>` element has the corresponding `datamodel` and `binding` attributes.
8. **Given** a `StatechartDocument` with `DataEntries`, **When** generated, **Then** `<datamodel>/<data>` elements are produced with `id` and `expr` attributes.

---

### User Story 3 - Full Round-Trip Fidelity (Priority: P1)

A developer can parse an SCXML document into the shared AST and then generate it back to SCXML, producing output that is structurally equivalent to the original. All SCXML-specific data -- document attributes, transition types, multi-target transitions, history pseudo-states, invoke elements, data model entries -- survives the round trip without loss.

**Why this priority**: Round-trip fidelity is the ultimate validation that the migration is correct and complete. This was explicitly required: "SCXML -> StatechartDocument -> SCXML produces a correct, complete SCXML document."

**Independent Test**: Can be fully tested by parsing reference SCXML documents, generating output, re-parsing, and comparing the two `StatechartDocument` values for structural equality (after stripping source positions). The existing round-trip tests provide the test patterns.

**Acceptance Scenarios**:

1. **Given** a reference SCXML document with states, transitions, guards, events, and a final state, **When** parsed then generated then re-parsed, **Then** the two `StatechartDocument` values are structurally equal (ignoring source positions).
2. **Given** SCXML with parallel states, history nodes, and invoke elements, **When** round-tripped, **Then** all constructs are preserved including history kind, invoke attributes, and parallel structure.
3. **Given** SCXML with internal transition types, **When** round-tripped, **Then** the transition type is preserved.
4. **Given** SCXML with multi-target transitions, **When** round-tripped, **Then** all targets are preserved.
5. **Given** SCXML with `name`, `datamodel`, and `binding` attributes on the root element, **When** round-tripped, **Then** all document-level attributes are preserved.
6. **Given** SCXML with both document-level and state-level data entries, **When** round-tripped, **Then** all data entries are preserved with correct ids and expressions.
7. **Given** SCXML with history nodes that have default transitions, **When** round-tripped, **Then** the default transition targets are preserved.

---

### User Story 4 - Type Cleanup and Mapper Removal (Priority: P2)

After the parser and generator work directly with the shared AST, the format-specific document types (`ScxmlDocument`, `ScxmlState`, `ScxmlTransition`, `ScxmlParseResult`, `ScxmlStateKind`) and the temporary `Mapper.fs` bridge module are deleted. The `Types.fs` file retains only SCXML-specific types needed for annotation payloads (`ScxmlTransitionType`, `ScxmlHistoryKind`) and any types that have no equivalent in the shared AST.

**Why this priority**: This is cleanup that follows from the core migration. It cannot be done until the parser and generator are fully migrated, but it is essential for the feature to be considered complete -- leaving dead types creates confusion and maintenance burden.

**Independent Test**: Can be tested by verifying the project builds successfully after type and file deletion, confirming no remaining references to deleted types exist, and running the full test suite.

**Acceptance Scenarios**:

1. **Given** the parser and generator use shared AST types, **When** `ScxmlDocument`, `ScxmlState`, `ScxmlTransition`, `ScxmlParseResult`, and `ScxmlStateKind` are deleted from `Types.fs`, **Then** the project builds successfully across all target frameworks.
2. **Given** the parser produces `Ast.ParseResult` directly, **When** `Mapper.fs` is deleted from the project, **Then** the project builds successfully and no code references the mapper module.
3. **Given** types are cleaned up, **When** all tests are run, **Then** all existing test scenarios pass (updated to use shared types).

---

### User Story 5 - Cross-Format Validator Compatibility (Priority: P2)

The cross-format validator works with SCXML artifacts that are now `StatechartDocument` values produced directly by the parser, without needing the mapper as an intermediary. Validation rules that compare SCXML artifacts with other formats (WSD, smcat, ALPS, XState) continue to function correctly.

**Why this priority**: The cross-format validator is a key consumer of parser output. It currently works with `StatechartDocument` values (via the mapper), so the migration should be transparent to it. However, this must be verified.

**Independent Test**: Can be tested by running cross-format validation tests that include SCXML artifacts and confirming they produce the same results as before the migration.

**Acceptance Scenarios**:

1. **Given** an SCXML artifact parsed directly to `StatechartDocument`, **When** used in cross-format state name agreement validation with other format artifacts, **Then** the validation produces correct pass/fail results.
2. **Given** an SCXML artifact and a WSD artifact parsed from equivalent state machines, **When** cross-format validation runs, **Then** no state name disagreements are reported.

---

### Edge Cases

- What happens when SCXML has states with no `id` attribute? The parser assigns an empty string identifier, matching current behavior.
- How does the system handle SCXML with nested data model entries at multiple levels? All data entries are collected into `StatechartDocument.DataEntries` from both document and state scopes.
- What happens when `ScxmlAnnotation` entries are missing from a `StatechartDocument` given to the generator? The generator uses sensible defaults: external transition type, no invoke elements, no history nodes, no datamodel/binding attributes.
- How does the generator handle a `TransitionEdge` with `Target = None` and no `ScxmlMultiTarget` annotation? It produces a targetless `<transition>` element (valid SCXML for internal transitions).
- What happens with history nodes that have default transitions targeting non-existent states? The generator produces the `<transition>` element as-is; semantic validation is not the generator's responsibility.
- How are `StateNode` entries with `Kind = Initial`, `Kind = Choice`, `Kind = ForkJoin`, or `Kind = Terminate` handled by the generator? These state kinds do not map to SCXML elements. The generator treats `Initial` as a regular `<state>`, and `Choice`/`ForkJoin`/`Terminate` are not valid in SCXML and should produce a warning or be silently skipped (matching how the WSD serializer handles non-WSD state kinds).

## Requirements *(mandatory)*

### Functional Requirements

- **FR-001**: Parser `parseString`, `parseReader`, and `parseStream` functions MUST return `Ast.ParseResult` (with `StatechartDocument`) instead of `ScxmlParseResult` (with `ScxmlDocument option`).
- **FR-002**: Parser MUST map `<state>` elements to `StateNode` with `Kind = Regular`, distinguishing compound from simple states by the presence of children in `StateNode.Children`.
- **FR-003**: Parser MUST map `<parallel>` elements to `StateNode` with `Kind = Parallel`.
- **FR-004**: Parser MUST map `<final>` elements to `StateNode` with `Kind = Final`.
- **FR-005**: Parser MUST map `<transition>` elements to `TransitionEdge` with `Source` set to the parent state's identifier, `Target` set to the first target (or `None`), `Event` and `Guard` populated from attributes.
- **FR-006**: Parser MUST preserve the transition `type` attribute (internal/external) as a `ScxmlAnnotation(ScxmlTransitionType(...))` on the `TransitionEdge`.
- **FR-007**: Parser MUST preserve multi-target transitions by setting `TransitionEdge.Target` to the first target and attaching `ScxmlAnnotation(ScxmlMultiTarget(targets))` with the full target list.
- **FR-008**: Parser MUST map `<history>` elements to child `StateNode` entries with `Kind = ShallowHistory` or `Kind = DeepHistory` and a `ScxmlAnnotation(ScxmlHistory(id, kind))` annotation.
- **FR-009**: Parser MUST map `<invoke>` elements to `ScxmlAnnotation(ScxmlInvoke(type, src, id))` annotations on the parent `StateNode`, preserving invoke `id` via the extended `ScxmlInvoke` payload. Note: the `invokeType` field changes from `string` to `string option` because the SCXML `type` attribute is optional (defaults to the platform-specific SCXML processor's default invocation type when omitted).
- **FR-010**: Parser MUST map `<datamodel>/<data>` entries to `Ast.DataEntry` records with `Name` (from SCXML `id`), `Expression`, and `Position`.
- **FR-011**: Parser MUST set `StatechartDocument.Title` from the SCXML `name` attribute and `StatechartDocument.InitialStateId` from the SCXML `initial` attribute (with fallback to first child state).
- **FR-012**: Parser MUST preserve the SCXML `datamodel` attribute as `ScxmlAnnotation(ScxmlDatamodelType(...))` on the `StatechartDocument`.
- **FR-013**: Parser MUST preserve the SCXML `binding` attribute as `ScxmlAnnotation(ScxmlBinding(...))` on the `StatechartDocument`.
- **FR-014**: Parser MUST preserve state-level `initial` attributes as `ScxmlAnnotation(ScxmlInitial(id))` on the `StateNode`.
- **FR-015**: Parser MUST map parse errors to `Ast.ParseFailure` records and parse warnings to `Ast.ParseWarning` records, preserving positions and descriptions.
- **FR-016**: Parser MUST return an empty `StatechartDocument` (no states, no transitions) with error entries when the input is invalid XML or has a non-`<scxml>` root element (matching the `Ast.ParseResult` contract where `Document` is always present).
- **FR-017**: Generator `generate` and `generateTo` functions MUST accept `StatechartDocument` instead of `ScxmlDocument`.
- **FR-018**: Generator MUST reconstruct hierarchical XML from `StateNode.Children`, producing nested `<state>`, `<parallel>`, and `<final>` elements.
- **FR-019**: Generator MUST reconstruct `<history>` elements from child `StateNode` entries with `Kind = ShallowHistory` or `Kind = DeepHistory`, reading `ScxmlAnnotation(ScxmlHistory(...))` for the id and kind.
- **FR-020**: Generator MUST reconstruct `<invoke>` elements from `ScxmlAnnotation(ScxmlInvoke(...))` annotations on `StateNode` entries.
- **FR-021**: Generator MUST reconstruct `<transition type="internal">` from `ScxmlAnnotation(ScxmlTransitionType(Internal))` on `TransitionEdge` entries, defaulting to external when no annotation is present.
- **FR-022**: Generator MUST reconstruct multi-target `target` attributes from `ScxmlAnnotation(ScxmlMultiTarget(...))` annotations on `TransitionEdge` entries, falling back to `TransitionEdge.Target` when no annotation is present.
- **FR-023**: Generator MUST reconstruct `datamodel` and `binding` attributes on the root `<scxml>` element from document-level `ScxmlAnnotation` entries.
- **FR-024**: Generator MUST produce `<datamodel>/<data>` elements from `StatechartDocument.DataEntries`.
- **FR-025**: The format-specific types `ScxmlDocument`, `ScxmlState`, `ScxmlTransition`, `ScxmlParseResult`, and `ScxmlStateKind` MUST be deleted from `Types.fs`.
- **FR-026**: The `Mapper.fs` file MUST be deleted from the project.
- **FR-027**: `Types.fs` MUST retain `ScxmlTransitionType` (Internal/External), `ScxmlHistoryKind` (Shallow/Deep), `SourcePosition` (if still needed by parser internals), and any parse-internal helper types.
- **FR-028**: New `ScxmlMeta` cases MUST be added to the shared AST's `ScxmlMeta` discriminated union: `ScxmlTransitionType`, `ScxmlMultiTarget`, `ScxmlDatamodelType`, `ScxmlBinding`, `ScxmlInitial`, and `ScxmlInvokeId` (or extending `ScxmlInvoke` to carry the id).
- **FR-029**: All existing SCXML tests MUST pass after being updated to use shared AST types.
- **FR-030**: The project MUST build successfully across net8.0, net9.0, and net10.0 target frameworks.
- **FR-031**: The cross-format validator MUST work with SCXML-produced `StatechartDocument` artifacts without the mapper.

### Key Entities

- **StatechartDocument**: The shared root AST node representing a parsed statechart. After migration, this is the sole output type of the SCXML parser and the sole input type of the SCXML generator.
- **StateNode**: Shared AST type representing a state. Replaces `ScxmlState`. Carries SCXML-specific data via `Annotations`.
- **TransitionEdge**: Shared AST type representing a transition. Replaces `ScxmlTransition`. Carries SCXML-specific data (transition type, multi-target) via `Annotations`.
- **ScxmlMeta**: The discriminated union within the shared AST's `Annotation` system that carries SCXML-specific data. Extended with new cases for full round-trip fidelity.
- **ParseResult**: Shared AST parse result type. Replaces `ScxmlParseResult`. Contains `Document` (always present), `Errors`, and `Warnings`.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: All three parser entry points (`parseString`, `parseReader`, `parseStream`) return `Ast.ParseResult` with `StatechartDocument` -- verified by type signatures and compilation.
- **SC-002**: Both generator entry points (`generate`, `generateTo`) accept `StatechartDocument` -- verified by type signatures and compilation.
- **SC-003**: Round-trip fidelity: parsing any valid SCXML document, generating it back, and re-parsing produces a structurally equal `StatechartDocument` (ignoring source positions) -- verified by round-trip tests covering at least 8 SCXML documents of varying complexity.
- **SC-004**: Zero references to `ScxmlDocument`, `ScxmlState`, `ScxmlTransition`, `ScxmlParseResult`, or `ScxmlStateKind` remain in the codebase -- verified by text search.
- **SC-005**: The `Mapper.fs` file does not exist in the project -- verified by file system check and project file inspection.
- **SC-006**: All existing SCXML test scenarios pass (updated to use shared types) -- verified by `dotnet test` with zero failures.
- **SC-007**: The project builds with zero errors across all three target frameworks (net8.0, net9.0, net10.0) -- verified by `dotnet build`.
- **SC-008**: Cross-format validator tests that involve SCXML artifacts pass without the mapper -- verified by `dotnet test` on the validation test suite.

## Assumptions

- The WSD parser/serializer migration (spec 020) has already landed and the shared AST types in `Frank.Statecharts.Ast` are stable.
- The existing `ScxmlMeta` type in the shared AST (`ScxmlInvoke`, `ScxmlHistory`, `ScxmlNamespace`) can be extended with new cases without breaking existing consumers, since the type is within the same project.
- `ParseResult.Document` is always present (best-effort) per the shared AST contract, unlike `ScxmlParseResult.Document` which was `option`. On parse failure, the parser returns an empty `StatechartDocument` with error entries.
- State-scoped data entries in SCXML are flattened into `StatechartDocument.DataEntries` (matching the existing mapper behavior). If state-scoped data placement is needed for round-trip fidelity, `ScxmlAnnotation` entries on `StateNode` can carry data entry references.
- The `SourcePosition` struct in `Types.fs` (SCXML-specific) may be retained for internal parser use, but the parser converts to `Ast.SourcePosition` before returning results.
- History nodes' default transitions are preserved as `ScxmlAnnotation` data on the history `StateNode`, since `StateNode` does not have a transitions list (transitions are separate `TransitionElement` entries in `StatechartDocument.Elements`).

## Dependencies

- **Spec 020 (#111)**: Shared AST types must be available and stable.
- **Spec 018 (#98)**: The existing SCXML parser and generator implementation (the code being migrated).

## Risks

- **State-scoped data model scope loss**: The shared AST's `DataEntries` is a flat list on `StatechartDocument`. SCXML allows `<datamodel>` at state level. If state-scoped data placement must round-trip, additional annotation work is needed. Mitigation: the existing mapper already flattens data entries; if state-level placement is needed, add `ScxmlAnnotation(ScxmlStateData(stateId, entries))` annotations.
- **History default transition representation**: The shared AST separates states and transitions into different `StatechartElement` entries. SCXML `<history>` elements can contain a `<transition>` child for the default target. The default transition must be preserved via annotation rather than as a separate `TransitionElement`. Mitigation: store default transition target in the `ScxmlHistory` annotation payload.
- **Non-SCXML state kinds in generator**: If a `StatechartDocument` contains `StateNode` entries with kinds that have no SCXML equivalent (`Choice`, `ForkJoin`, `Terminate`), the generator must handle them gracefully. Mitigation: skip or map to `<state>` with a warning.
