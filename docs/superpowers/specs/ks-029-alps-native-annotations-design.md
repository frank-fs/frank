---
source: kitty-specs/029-alps-native-annotations
status: complete
type: spec
---

# Feature Specification: ALPS Native Annotations and Full Fidelity

**Feature Branch**: `029-alps-native-annotations`
**Created**: 2026-03-18
**Status**: Draft
**Input**: Complete #115 for ALPS with native annotations for that format with full fidelity and lossless round-tripping. Add ALPS XML parser alongside existing JSON parser.
**Closes**: #115

## Background

The ALPS parser and generator were migrated to the shared `StatechartDocument` AST (spec 019), but the implementation has fidelity gaps. The JSON parser applies state classification heuristics during parsing that interpret ALPS descriptors as states and transitions — correct for the WSD→ALPS pipeline but potentially lossy for arbitrary ALPS documents. The generator reconstructs ALPS JSON from the interpreted AST but may reorder descriptors or lose non-classified descriptors. Additionally, ALPS has two official representations (JSON and XML per the IETF draft), but only JSON is currently supported.

The `AlpsMeta` DU already has 7 cases (`AlpsTransitionType`, `AlpsDescriptorHref`, `AlpsExtension`, `AlpsDocumentation`, `AlpsLink`, `AlpsDataDescriptor`, `AlpsVersion`), providing good annotation coverage. The primary gaps are: (1) JSON round-trip fidelity for arbitrary ALPS documents, (2) missing XML parser, and (3) round-trip tests.

## User Scenarios & Testing

### User Story 1 - JSON Round-Trip Fidelity (Priority: P1)

A developer parses an ALPS JSON document, generates the AST back to ALPS JSON, and parses the output again. The two ASTs are structurally equivalent, proving no information is lost in the JSON round-trip cycle.

**Why this priority**: JSON is the most common ALPS representation. Round-trip fidelity is the proof that the annotation system works end-to-end.

**Independent Test**: Parse Amundsen's onboarding example from RESTFest 2018, generate back, parse again, compare ASTs.

**Acceptance Scenarios**:

1. **Given** an ALPS JSON document with state descriptors, transition descriptors, data descriptors, extensions, links, and documentation, **When** round-tripped through parse-generate-parse, **Then** the two `StatechartDocument` ASTs are structurally equal.
2. **Given** an ALPS JSON document with shared transitions (href-only references), **When** round-tripped, **Then** the shared transition structure is preserved.
3. **Given** an ALPS JSON document with nested descriptor hierarchies, **When** round-tripped, **Then** all descriptors survive with correct parent-child relationships.

---

### User Story 2 - JSON Generator Fidelity Improvements (Priority: P1)

A developer generates ALPS JSON from a `StatechartDocument`. The output preserves descriptor ordering (data descriptors first, state descriptors, shared transitions), documentation at all levels, extensions, links, and guard annotations.

**Why this priority**: The generator must produce semantically correct ALPS JSON that tools like app-state-diagram can consume.

**Independent Test**: Generate ALPS JSON from a hand-crafted AST. Parse the output with an independent JSON parser and verify all properties are present.

**Acceptance Scenarios**:

1. **Given** a `StatechartDocument` with `AlpsDocumentation` annotations at document, state, and transition levels, **When** ALPS JSON is generated, **Then** `doc` objects appear at all three levels.
2. **Given** a `StatechartDocument` with `AlpsExtension` annotations (including guards), **When** ALPS JSON is generated, **Then** `ext` arrays contain all extensions with guard extensions on transitions.
3. **Given** a `StatechartDocument` with `AlpsDataDescriptor` annotations, **When** ALPS JSON is generated, **Then** data descriptors appear as top-level semantic descriptors.

---

### User Story 3 - ALPS XML Parser (Priority: P2)

A developer parses an ALPS XML document into the shared AST, using the same descriptor classification logic as the JSON parser. The resulting `StatechartDocument` is identical to what the JSON parser would produce for the equivalent ALPS document.

**Why this priority**: ALPS XML is the normative representation in the IETF draft. Supporting both representations makes Frank the most complete ALPS implementation. JSON is higher priority since it's more commonly used in practice.

**Independent Test**: Parse the ALPS XML representation of Amundsen's onboarding example. Compare the AST to the JSON parser's output for the equivalent JSON document.

**Acceptance Scenarios**:

1. **Given** an ALPS XML document with `<alps>`, `<descriptor>`, `<doc>`, `<ext>`, `<link>` elements, **When** parsed, **Then** the resulting `StatechartDocument` has the same structure as the JSON parser would produce for the equivalent JSON.
2. **Given** an ALPS XML document with nested descriptors, **When** parsed, **Then** state classification heuristics produce the same states and transitions as the JSON parser.
3. **Given** an ALPS XML document with `<doc>` elements containing formatted text, **When** parsed, **Then** `AlpsDocumentation` annotations preserve both format and value.

---

### User Story 4 - ALPS XML Generator (Priority: P2)

A developer generates ALPS XML from a `StatechartDocument`. The output is valid ALPS XML that other ALPS tools can consume.

**Why this priority**: Completes the XML round-trip alongside the existing JSON generator.

**Independent Test**: Generate ALPS XML from a `StatechartDocument`. Parse the output with the new XML parser. Compare ASTs.

**Acceptance Scenarios**:

1. **Given** a `StatechartDocument` with ALPS annotations, **When** ALPS XML is generated, **Then** the output is well-formed XML with the ALPS structure.
2. **Given** a `StatechartDocument`, **When** round-tripped through generate-XML → parse-XML, **Then** the ASTs are structurally equal.

---

### User Story 5 - Cross-Format Equivalence (Priority: P2)

A developer parses the same ALPS profile in both JSON and XML formats. The resulting ASTs are identical, proving both parsers apply the same classification logic.

**Why this priority**: Validates that the XML parser is a faithful implementation alongside the JSON parser.

**Independent Test**: Parse the same ALPS profile as JSON and XML. Compare the two ASTs.

**Acceptance Scenarios**:

1. **Given** equivalent ALPS JSON and XML documents, **When** both are parsed, **Then** the resulting `StatechartDocument` ASTs are structurally equal (excluding any format-specific annotations).

---

### Edge Cases

- What happens when an ALPS descriptor has no `id` and no `href`? It's an anonymous descriptor — skip it during state classification but preserve it if it has children.
- What happens when `rt` references a descriptor that doesn't exist? Store the `rt` value as-is in `TransitionEdge.Target`. The cross-format validator can flag dangling references separately.
- What happens when the same descriptor `id` appears multiple times in the ALPS document? Per ALPS spec, later definitions override. The parser should use the last definition.
- What happens when ALPS XML uses CDATA in `<doc>` elements? Preserve the text content including any embedded markup.

## Requirements

### Functional Requirements

- **FR-001**: The ALPS JSON parser MUST produce ASTs that round-trip through generate-parse with structural equivalence.
- **FR-002**: The ALPS JSON generator MUST preserve descriptor ordering: data descriptors, state descriptors, shared transitions.
- **FR-003**: The ALPS JSON generator MUST emit `doc` objects at document, state, and transition levels from `AlpsDocumentation` annotations.
- **FR-004**: The ALPS JSON generator MUST emit `ext` arrays from `AlpsExtension` annotations, with guards reconstructed from `TransitionEdge.Guard`.
- **FR-005**: A new ALPS XML parser MUST produce shared `Ast.ParseResult` with `StatechartDocument`, using the same classification heuristics as the JSON parser.
- **FR-006**: The ALPS XML parser MUST handle `<alps>`, `<descriptor>`, `<doc>`, `<ext>`, `<link>` elements with all attributes.
- **FR-007**: A new ALPS XML generator MUST produce well-formed ALPS XML from `StatechartDocument`.
- **FR-008**: The ALPS XML parser and JSON parser MUST produce identical ASTs for equivalent input documents.
- **FR-009**: Round-trip fidelity MUST be preserved for both JSON (parse → generate → parse) and XML (parse → generate → parse).
- **FR-010**: All changes MUST compile across net8.0, net9.0, and net10.0 target frameworks.
- **FR-011**: All existing ALPS tests MUST continue to pass after the changes.

### Key Entities

- **ALPS XML Parser**: New module (`Alps/XmlParser.fs`) using `System.Xml.Linq` for parsing ALPS XML, sharing Pass 2 classification logic with JSON parser.
- **ALPS XML Generator**: New module (`Alps/XmlGenerator.fs`) producing ALPS XML from `StatechartDocument`.
- **AlpsMeta (unchanged)**: Existing 7-case DU is sufficient — no expansion needed.
- **Shared Classification Logic**: The state/transition classification heuristics (`isStateDescriptor`, `collectRtTargets`, etc.) should be factored into a shared module if not already.

## Success Criteria

### Measurable Outcomes

- **SC-001**: Amundsen's onboarding example round-trips through JSON parse → generate → parse with structural equality.
- **SC-002**: ALPS XML parser produces identical ASTs to JSON parser for equivalent input.
- **SC-003**: JSON round-trip tests pass for documents with data descriptors, shared transitions, extensions, links, documentation, and guards.
- **SC-004**: XML round-trip tests pass for the same document set.
- **SC-005**: `dotnet build` and `dotnet test` pass across all target frameworks with zero regressions.

## Assumptions

- The `AlpsMeta` DU's 7 existing cases are sufficient for full fidelity — no new cases needed.
- The shared classification logic (Pass 2) can be extracted into a shared module or kept as private functions duplicated between JSON and XML parsers (expert majority: extract to shared module per constitution Principle VIII — no duplicated logic).
- ALPS XML uses the same descriptor/extension/link/doc structure as JSON, just in XML syntax. The `System.Xml.Linq` dependency is already available from SCXML.
- JSON property ordering in generated output follows ALPS convention (version, doc, descriptor, link, ext) but JSON spec doesn't guarantee order.
- The XML generator produces indented, human-readable XML.


## Research

# Research: ALPS Native Annotations and Full Fidelity

**Feature**: 029-alps-native-annotations
**Date**: 2026-03-18

## R-001: Shared Classification Module

**Decision**: Extract Pass 2 heuristics and intermediate types to `Alps/Classification.fs`.

**Types to extract**: `ParsedDescriptor`, `ParsedExtension`, `ParsedLink` (currently `private` in JsonParser.fs). Make them `internal` in the new module.

**Functions to extract**: `isTransitionTypeStr`, `collectRtTargets`, `isStateDescriptor`, `buildDescriptorIndex`, `resolveRt`, `extractGuard`, `extractParameters`, `toTransitionKind`, `buildStateAnnotations`, `buildTransitionAnnotations`, `resolveDescriptor`, `extractTransitions`, `toStateNode`.

**Rationale**: Constitution Principle VIII prohibits duplicated logic across modules. JSON and XML parsers both need these functions.

## R-002: ALPS XML Structure

**Decision**: ALPS XML mirrors JSON structure closely.

ALPS XML example:
```xml
<alps version="1.0">
  <doc>Generated from onboarding.wsd</doc>
  <descriptor id="identifier" type="semantic"/>
  <descriptor id="home" type="semantic">
    <descriptor href="#startOnboarding"/>
  </descriptor>
  <descriptor id="startOnboarding" type="unsafe" rt="#WIP">
    <descriptor href="#identifier"/>
  </descriptor>
  <link rel="self" href="http://example.com/alps/onboarding"/>
  <ext id="custom" href="http://example.com/ext" value="data"/>
</alps>
```

XML → `ParsedDescriptor` mapping:
- `<descriptor id="..." type="..." href="..." rt="...">` → `ParsedDescriptor` fields
- `<doc format="...">text</doc>` → `DocFormat`, `DocValue`
- `<ext id="..." href="..." value="..."/>` → `ParsedExtension`
- `<link rel="..." href="..."/>` → `ParsedLink`
- Nested `<descriptor>` → `Children`

## R-003: JSON Fidelity Gaps

**Current gaps identified**:
1. **Title duplication**: `rootDocValue` is used as both `Title` and `AlpsDocumentation`. On round-trip, `Title` is emitted as top-level `doc` which is correct. No gap here — verified.
2. **Descriptor ordering**: Generator emits data descriptors first, then states, then shared transitions. If original had different ordering, it changes. This is acceptable — ALPS doesn't define descriptor ordering semantics.
3. **Property ordering in JSON objects**: JSON spec says objects are unordered. Not a fidelity issue.

**Conclusion**: JSON round-trip is already close to lossless. The main test is to verify with Amundsen's onboarding example and other edge cases.

## R-004: F# Project File Updates

New files must be added to `Frank.Statecharts.fsproj` in the correct compilation order:
- `Alps/Classification.fs` — BEFORE `Alps/JsonParser.fs` and `Alps/XmlParser.fs`
- `Alps/XmlParser.fs` — AFTER `Alps/Classification.fs`
- `Alps/XmlGenerator.fs` — AFTER `Alps/Classification.fs`

Similarly for test project `.fsproj`.
