---
source: kitty-specs/028-scxml-native-annotations
status: complete
type: spec
---

# Feature Specification: SCXML Native Annotations and Generator Fidelity

**Feature Branch**: `028-scxml-native-annotations`
**Created**: 2026-03-18
**Status**: Draft
**Input**: Eliminate all lossy transformations in the SCXML parser and generator so that SCXML round-trips with zero information loss. Dual-layer approach: portable activities for cross-format tools, raw XML annotations for format-specific fidelity.
**Closes**: #114

## Clarifications

### Session 2026-03-18

- Q: SCXML allows multiple `<onentry>` blocks on the same state. Store one annotation per block or concatenate? → A: (A) One annotation per block — multiple `ScxmlOnEntry` entries in the `Annotations` list, matching the existing `ScxmlInvoke` pattern. Generator emits one `<onentry>` element per annotation. Preserves exact document structure.

## Background

The SCXML parser and generator were migrated to the shared `StatechartDocument` AST (spec 018), but the implementation silently drops several SCXML features during parsing. `<onentry>` and `<onexit>` executable content blocks are skipped entirely — `StateActivities` is always `None`. `<initial>` child elements are recognized but not parsed. `<data src="...">` attributes are not captured. The namespace origin is not stored despite `ScxmlNamespace` existing in the `ScxmlMeta` DU. The result is a parser that produces a correct but impoverished AST — one that loses information that cannot be recovered during generation.

The generator is already annotation-aware (consuming `ScxmlMultiTarget`, `ScxmlTransitionType`, `ScxmlInvoke`, `ScxmlHistory`, `ScxmlInitial`, `ScxmlDatamodelType`, `ScxmlBinding`), but it cannot emit what the parser didn't capture.

## User Scenarios & Testing

### User Story 1 - Parser Captures Executable Content (Priority: P1)

A developer parses SCXML containing `<onentry>` and `<onexit>` blocks. The resulting `StateNode` has `Activities` populated with action names (portable layer) and `ScxmlAnnotation` entries carrying the raw XML (format-specific layer), enabling both cross-format reasoning and round-trip fidelity.

**Why this priority**: Entry/exit actions are the largest source of information loss. Every SCXML document with executable content currently loses it silently.

**Independent Test**: Parse SCXML with `<onentry><send event="done"/><log expr="hello"/></onentry>`. Verify `Activities.Entry` contains action descriptions AND annotations contain `ScxmlOnEntry` with the raw XML.

**Acceptance Scenarios**:

1. **Given** SCXML with `<onentry>` containing `<send>`, `<log>`, and `<assign>` elements, **When** parsed, **Then** the `StateNode.Activities.Entry` list contains descriptive strings for each action AND `Annotations` contains `ScxmlAnnotation(ScxmlOnEntry(xml))` with the full `<onentry>` XML.
2. **Given** SCXML with `<onexit>` containing executable content, **When** parsed, **Then** the `StateNode.Activities.Exit` list contains descriptive strings AND `Annotations` contains `ScxmlAnnotation(ScxmlOnExit(xml))` with the full `<onexit>` XML.
3. **Given** SCXML with no `<onentry>` or `<onexit>`, **When** parsed, **Then** `Activities` is `None` and no `ScxmlOnEntry`/`ScxmlOnExit` annotations are present.

---

### User Story 2 - Generator Emits Executable Content (Priority: P1)

A developer generates SCXML from a `StatechartDocument` containing `ScxmlOnEntry`/`ScxmlOnExit` annotations. The generated XML includes the full `<onentry>`/`<onexit>` blocks reconstructed from the raw XML annotations.

**Why this priority**: Without generation, the parser's new capture capability is wasted — round-trip fidelity requires both directions.

**Independent Test**: Generate SCXML from a `StatechartDocument` with `ScxmlOnEntry` annotations. Verify the output XML contains `<onentry>` blocks with the original executable content.

**Acceptance Scenarios**:

1. **Given** a `StateNode` with `ScxmlAnnotation(ScxmlOnEntry(xml))`, **When** SCXML is generated, **Then** the output contains the `<onentry>` block with the stored XML content.
2. **Given** a `StateNode` with both `ScxmlOnEntry` and `ScxmlOnExit` annotations, **When** SCXML is generated, **Then** both blocks appear in the correct order within the `<state>` element.
3. **Given** a `StateNode` with no executable content annotations, **When** SCXML is generated, **Then** no `<onentry>` or `<onexit>` blocks are emitted.

---

### User Story 3 - Initial Element Parsing and Generation (Priority: P1)

A developer parses SCXML using `<initial>` child elements (not just the `initial` attribute). The parser correctly captures the `<initial>` element's target transition, and the generator reconstructs the `<initial>` element when the annotation is present.

**Why this priority**: SCXML has two ways to specify initial states — attribute and child element. The parser currently only handles the attribute, silently dropping the element form.

**Independent Test**: Parse SCXML with `<state><initial><transition target="s1"/></initial>...</state>`. Verify the annotation stores the target. Generate back and verify `<initial>` child element appears.

**Acceptance Scenarios**:

1. **Given** SCXML with `<initial><transition target="s1"/></initial>` inside a `<state>`, **When** parsed, **Then** the `StateNode` has `ScxmlAnnotation(ScxmlInitialElement("s1"))`.
2. **Given** a `StateNode` with both `ScxmlInitial` (attribute) and `ScxmlInitialElement` (child), **When** parsed, **Then** the child element takes precedence per W3C spec.
3. **Given** a `StateNode` with `ScxmlAnnotation(ScxmlInitialElement("s1"))`, **When** SCXML is generated, **Then** the output contains `<initial><transition target="s1"/></initial>` inside the state element.

---

### User Story 4 - Data Source Attribute Capture (Priority: P2)

A developer parses SCXML with `<data src="...">` attributes. The parser captures the `src` attribute so the generator can reconstruct it.

**Why this priority**: Less common than executable content but still a source of information loss.

**Independent Test**: Parse SCXML with `<data id="config" src="config.json"/>`. Verify the data entry or annotation preserves the `src` value.

**Acceptance Scenarios**:

1. **Given** SCXML with `<data id="config" src="config.json"/>`, **When** parsed, **Then** the `src` value is captured as `ScxmlAnnotation(ScxmlDataSrc("config", "config.json"))` on the document.
2. **Given** a `StatechartDocument` with a `ScxmlDataSrc` annotation, **When** SCXML is generated, **Then** the output `<data>` element includes `src="config.json"`.

---

### User Story 5 - Namespace Origin Tracking (Priority: P2)

A developer parses SCXML written without the W3C namespace (plain `<scxml>` instead of `<scxml xmlns="http://www.w3.org/2005/07/scxml">`). The parser stores the namespace origin so the generator can reproduce the original form.

**Why this priority**: Minor fidelity issue but prevents unnecessary namespace injection during round-trip.

**Independent Test**: Parse no-namespace SCXML. Generate back. Verify the output does not inject the W3C namespace if the input didn't have it.

**Acceptance Scenarios**:

1. **Given** SCXML with the W3C namespace, **When** parsed and generated, **Then** the output retains the W3C namespace.
2. **Given** SCXML without any namespace, **When** parsed, **Then** `ScxmlAnnotation(ScxmlNamespace(""))` or no namespace annotation is stored, and the generator respects the absence.

---

### User Story 6 - Round-Trip Fidelity (Priority: P1)

A developer parses SCXML, generates the AST back to SCXML, and parses the output again. The two ASTs are structurally equivalent, proving zero information loss.

**Why this priority**: Round-trip fidelity is the definitive proof that all lossy transformations have been eliminated.

**Independent Test**: Parse a comprehensive SCXML document with all features (states, transitions, history, invoke, data, onentry, onexit, initial elements, namespaces). Generate. Parse again. Compare ASTs.

**Acceptance Scenarios**:

1. **Given** SCXML with executable content, history, invoke, data entries, and initial elements, **When** round-tripped through parse-generate-parse, **Then** the two `StatechartDocument` ASTs are structurally equal (excluding source positions).
2. **Given** SCXML with `<onentry>`/`<onexit>` blocks, **When** round-tripped, **Then** the raw XML annotations are preserved.
3. **Given** SCXML with both `initial` attribute and `<initial>` child element forms, **When** round-tripped, **Then** the correct form is preserved for each state.

---

### Edge Cases

- What happens when `<onentry>` contains only whitespace or comments? The parser stores the raw XML as-is (including whitespace). `StateActivities.Entry` is empty since no actions are present.
- What happens when a `<data>` element has both `src` and `expr` attributes? Per W3C spec, `src` and `expr` are mutually exclusive. The parser captures whichever is present; if both appear, `expr` takes precedence (existing behavior) and `src` is stored as annotation.
- What happens when `<initial>` child element has no `<transition>` child? Store annotation with empty target. Generator emits `<initial/>` empty element.
- What happens when a state has multiple `<onentry>` blocks? Each block becomes a separate `ScxmlOnEntry` annotation. The generator emits one `<onentry>` element per annotation, preserving the original structure. `StateActivities.Entry` aggregates actions from all blocks.
- What happens when `StateActivities` is populated but no `ScxmlOnEntry`/`ScxmlOnExit` annotation exists (e.g., from a non-SCXML source)? The generator should emit `<onentry>` with simple `<log>` elements derived from activity names (best-effort).

## Requirements

### Functional Requirements

- **FR-001**: The `ScxmlMeta` DU MUST include `ScxmlOnEntry of xml: string` for storing raw `<onentry>` XML content.
- **FR-002**: The `ScxmlMeta` DU MUST include `ScxmlOnExit of xml: string` for storing raw `<onexit>` XML content.
- **FR-003**: The `ScxmlMeta` DU MUST include `ScxmlInitialElement of targetId: string` for distinguishing `<initial>` child elements from the `initial` attribute.
- **FR-004**: The `ScxmlMeta` DU MUST include `ScxmlDataSrc of name: string * src: string` for capturing `<data src="...">` attributes.
- **FR-005**: The SCXML parser MUST populate `StateNode.Activities` from `<onentry>`/`<onexit>` blocks, extracting action descriptions into `Entry`/`Exit` string lists.
- **FR-006**: The SCXML parser MUST store one `ScxmlAnnotation(ScxmlOnEntry(xml))` per `<onentry>` block encountered. Multiple `<onentry>` blocks on the same state produce multiple annotations (matching the `ScxmlInvoke` pattern).
- **FR-007**: The SCXML parser MUST store one `ScxmlAnnotation(ScxmlOnExit(xml))` per `<onexit>` block encountered. Multiple `<onexit>` blocks on the same state produce multiple annotations.
- **FR-008**: The SCXML parser MUST parse `<initial>` child elements and store `ScxmlAnnotation(ScxmlInitialElement(targetId))`.
- **FR-009**: The SCXML parser MUST capture `<data src="...">` attributes as `ScxmlAnnotation(ScxmlDataSrc(name, src))`.
- **FR-010**: The SCXML parser MUST store `ScxmlAnnotation(ScxmlNamespace(ns))` with the actual namespace string (or empty for no-namespace documents).
- **FR-011**: The SCXML generator MUST emit `<onentry>` blocks from `ScxmlOnEntry` annotations, reconstructing the XML content.
- **FR-012**: The SCXML generator MUST emit `<onexit>` blocks from `ScxmlOnExit` annotations, reconstructing the XML content.
- **FR-013**: The SCXML generator MUST emit `<initial>` child elements from `ScxmlInitialElement` annotations.
- **FR-014**: The SCXML generator MUST emit `src` attributes on `<data>` elements from `ScxmlDataSrc` annotations.
- **FR-015**: The SCXML generator MUST respect namespace origin from `ScxmlNamespace` annotations.
- **FR-016**: Round-trip fidelity MUST be preserved: parsing SCXML, generating XML, and parsing again MUST produce structurally equivalent `StatechartDocument` values (excluding source positions).
- **FR-017**: All changes MUST compile across net8.0, net9.0, and net10.0 target frameworks.
- **FR-018**: All existing SCXML tests MUST continue to pass after the changes.

### Key Entities

- **ScxmlOnEntry / ScxmlOnExit**: New `ScxmlMeta` cases carrying raw XML strings for executable content round-trip.
- **ScxmlInitialElement**: New `ScxmlMeta` case distinguishing `<initial>` child element from `initial` attribute.
- **ScxmlDataSrc**: New `ScxmlMeta` case capturing `<data src="...">` attribute values.
- **StateActivities**: Existing shared AST type, populated from `<onentry>`/`<onexit>` action names (portable layer).

## Success Criteria

### Measurable Outcomes

- **SC-001**: Every `<onentry>` block in parsed SCXML produces both a `StateActivities.Entry` entry AND a `ScxmlOnEntry` annotation — zero silent drops.
- **SC-002**: Every `<onexit>` block in parsed SCXML produces both a `StateActivities.Exit` entry AND a `ScxmlOnExit` annotation — zero silent drops.
- **SC-003**: Round-trip test (parse → generate → parse) produces structurally equivalent ASTs for all test fixtures (excluding positions).
- **SC-004**: `<initial>` child elements survive round-trip — parsed and regenerated correctly.
- **SC-005**: `<data src="...">` attributes survive round-trip — parsed and regenerated correctly.
- **SC-006**: `dotnet build` and `dotnet test` pass across all target frameworks with zero regressions.

## Assumptions

- Executable content XML is stored as raw strings (`XElement.ToString()` on parse, `XElement.Parse()` on generate). No typed AST for executable content — this keeps scope manageable.
- The portable `StateActivities` layer extracts simple action descriptions (e.g., `"send done"`, `"log hello"`, `"assign x"`) from executable content element names and key attributes. Complex nested structures are only preserved in the raw XML annotation.
- The existing `ScxmlNamespace` case in `ScxmlMeta` is already defined — no DU change needed for namespace tracking, only parser/generator updates.
- The existing SCXML round-trip tests use `stripDocPositions` for comparison — this pattern is preserved and extended.
- Cross-format tools (validator, pipeline) can use `StateActivities` to reason about entry/exit actions without understanding SCXML-specific XML content.


## Research

# Research: SCXML Native Annotations and Generator Fidelity

**Feature**: 028-scxml-native-annotations
**Date**: 2026-03-18

## R-001: Raw XML Storage Approach

**Decision**: Store `<onentry>`/`<onexit>` XML content as strings via `XElement.ToString()`.

**Rationale**: SCXML executable content (`<send>`, `<raise>`, `<log>`, `<assign>`, `<if>`, `<foreach>`) is deeply nested XML with format-specific semantics. Building a typed AST for it would be a compiler project. Raw strings preserve 100% of the content with ~20 lines of parser code and ~10 lines of generator code.

**Alternatives considered**:
- Typed executable content AST — rejected: scope explosion, no cross-format value
- Ignore executable content — rejected: user requires zero information loss

## R-002: Multiple Annotations Per Block

**Decision**: One `ScxmlOnEntry` annotation per `<onentry>` element. Multiple blocks → multiple annotations.

**Rationale**: W3C SCXML spec allows multiple `<onentry>` blocks per state (treated sequentially). Concatenating would change document structure. The `Annotations: Annotation list` already supports multiple entries — same pattern as `ScxmlInvoke`.

## R-003: Portable Action Descriptions

**Decision**: Extract simple descriptions from executable content into `StateActivities.Entry`/`Exit`.

**Format**: `"{elementName} {key-attribute-value}"` — e.g.:
- `<send event="done"/>` → `"send done"`
- `<log expr="hello"/>` → `"log hello"`
- `<assign location="x" expr="1"/>` → `"assign x"`
- `<raise event="error"/>` → `"raise error"`
- `<script>` → `"script"`
- `<foreach>` → `"foreach"`

**Rationale**: Cross-format tools need to know entry/exit actions exist without parsing SCXML XML. The description is best-effort — complex nested content is only in the raw XML annotation.

## R-004: Impact Analysis

**ScxmlMeta expansion** (4 new cases added to Ast/Types.fs):
- `ScxmlOnEntry of xml: string`
- `ScxmlOnExit of xml: string`
- `ScxmlInitialElement of targetId: string`
- `ScxmlDataSrc of name: string * src: string`

**Existing ScxmlMeta consumers**: Only `Scxml/Parser.fs` and `Scxml/Generator.fs` pattern-match on `ScxmlMeta`. No cross-module impact. The cross-format validator is annotation-agnostic.

**Parser changes** (Parser.fs, 401 lines):
- `outOfScopeElements` set: remove `onentry`, `onexit` from skipped list
- New `parseExecutableContent` helper: iterate `<onentry>`/`<onexit>` children, extract actions for `StateActivities`, store raw XML as annotation
- `parseState` function: call new helper, populate `Activities` field
- `<initial>` child element: add parsing in `parseState` alongside existing `knownStateChildElements`
- `<data src>`: extend `parseDataEntries` to capture `src` attribute
- Namespace: store `ScxmlNamespace` in `parseDocument`

**Generator changes** (Generator.fs, 206 lines):
- `generateState`: emit `<onentry>`/`<onexit>` from annotations (parse raw XML back to XElement)
- `generateState`: emit `<initial>` child element from `ScxmlInitialElement`
- `generateRoot`: emit `<data src>` from `ScxmlDataSrc` annotations
- `generateRoot`/`buildXDocument`: respect `ScxmlNamespace` for namespace selection
