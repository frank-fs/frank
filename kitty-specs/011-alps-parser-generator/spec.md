# Feature Specification: ALPS Parser and Generator

**Feature Branch**: `011-alps-parser-generator`
**Created**: 2026-03-15
**Status**: Draft
**GitHub Issue**: #97
**Dependencies**: Frank.Statecharts (#87) core runtime is complete
**Parallel With**: #98 (SCXML), #100 (smcat), #90 (WSD Parser)
**Location**: Internal to `src/Frank.Statecharts/Alps/`, tests in `test/Frank.Statecharts.Tests/`
**Input**: P2 component of the bidirectional spec pipeline (#57). Enables ALPS as both an input format (for LLM-assisted code scaffolding) and an output format (from compiled Frank.Statecharts assemblies).

## Clarifications

### Session 2026-03-15

- Q: Should the ALPS parser live under `src/Frank.Statecharts/Alps/` (like WSD under `Wsd/`) or in a separate project? -> A: Internal to Frank.Statecharts, under `Alps/` sub-directory, following the WSD pattern.
- Q: Is ALPS XML parsing in scope or deferred? -> A: In scope. Both JSON and XML parsers are deliverables.
- Q: Which examples are the "golden files"? -> A: Tic-tac-toe and onboarding examples from #57 research.
- Q: Should CLI wiring (`frank statechart import/generate`) be included? -> A: No. Library-level only; CLI integration belongs in #94.

## Background

ALPS (Application-Level Profile Semantics) is a format for describing the vocabulary and semantic descriptors of an application. It intentionally omits workflow ordering (FAQ A.2), focusing instead on what states and transitions *mean* rather than how they execute. ALPS supports two serializations: JSON and XML.

This feature provides bidirectional ALPS support:
- **Input direction**: Parse ALPS JSON or XML into a typed F# AST, then map descriptors to `StateMachineMetadata` (best-effort, lossy per research decision D-006)
- **Output direction**: Generate ALPS JSON from `StateMachineMetadata` extracted from a running or compiled Frank application

The parser and generator share a common set of AST types, ensuring structural consistency for roundtrip scenarios.

### Known Limitations (from D-006)

- ALPS cannot distinguish PUT from DELETE (both `type="idempotent"`) -- the LLM or developer must specify HTTP method choice
- ALPS has no workflow ordering -- transition ordering must be inferred or specified by the developer
- `rt` (return type) is single-valued -- conditional transitions (e.g., makeMove leading to OTurn, Won, or Draw) require one descriptor per target state
- Guard semantics use `ext` elements only -- extraction is best-effort
- No concept of initial state in ALPS -- must be inferred or annotated

## User Scenarios & Testing

### User Story 1 - Parse ALPS JSON into Typed AST (Priority: P1)

A developer provides an ALPS JSON document describing their application's semantic descriptors and transitions. The parser deserializes it into a typed F# AST that captures descriptor identifiers, types (semantic/safe/unsafe/idempotent), nesting relationships, return types, parameter descriptors, and guard extensions.

**Why this priority**: This is the foundational capability. Without a correct AST from the primary serialization format, no downstream mapping or roundtrip is possible.

**Independent Test**: Parse the tic-tac-toe ALPS JSON golden file, walk the resulting AST, and verify every descriptor has the correct id, type, nested descriptors, `rt` references, and `ext` elements.

**Acceptance Scenarios**:

1. **Given** an ALPS JSON document with semantic descriptors (`"type": "semantic"`), **When** parsed, **Then** the AST contains `Descriptor` nodes with `Semantic` type and correct `id` values.
2. **Given** an ALPS JSON document with transition descriptors (`"type": "unsafe"`), **When** parsed, **Then** the AST contains `Descriptor` nodes with `Unsafe` type, correct `rt` references, and nested parameter descriptors.
3. **Given** an ALPS JSON document with `ext` elements on a transition descriptor, **When** parsed, **Then** the AST contains `Extension` nodes with extracted id and value pairs (e.g., guard labels).
4. **Given** an ALPS JSON document with nested descriptors (state descriptors containing `href` references to transitions), **When** parsed, **Then** the AST captures the nesting hierarchy with both inline descriptors and `href` references resolved.
5. **Given** an ALPS JSON document with a top-level `doc` element, **When** parsed, **Then** the AST captures the documentation content and format.

---

### User Story 2 - Parse ALPS XML into Typed AST (Priority: P1)

A developer provides an ALPS XML document. The parser deserializes it into the same typed F# AST used for JSON parsing, ensuring format-agnostic downstream processing.

**Why this priority**: ALPS supports two official serializations. Both must produce identical AST structures to enable format-agnostic downstream tooling.

**Independent Test**: Parse the tic-tac-toe ALPS example in XML format, compare the resulting AST against the AST from the equivalent JSON document, and verify structural equivalence.

**Acceptance Scenarios**:

1. **Given** an ALPS XML document with `<descriptor>` elements, **When** parsed, **Then** the AST matches the structure produced by parsing the equivalent JSON document.
2. **Given** an ALPS XML document with `<ext>` elements, **When** parsed, **Then** extension data is captured identically to JSON `ext` parsing.
3. **Given** an ALPS XML document with `<doc>` elements containing `format` attributes, **When** parsed, **Then** documentation content and format are captured.
4. **Given** an ALPS XML document with `<link>` elements, **When** parsed, **Then** link relations and hrefs are captured in the AST.

---

### User Story 3 - Map ALPS AST to StateMachineMetadata (Priority: P1)

A developer has parsed an ALPS document (JSON or XML) and wants to extract state machine information for use in the Frank.Statecharts pipeline. The mapper walks the ALPS AST and produces a best-effort `StateMachineMetadata`-compatible representation, documenting information that ALPS cannot express.

**Why this priority**: The mapper is the bridge between ALPS input and the statecharts pipeline. Without it, parsed ALPS documents cannot feed into LLM-assisted code generation.

**Independent Test**: Parse the tic-tac-toe ALPS golden file, map it to statechart metadata, and verify that states (XTurn, OTurn, Won, Draw), transitions (makeMove, viewGame), and guard labels are present in the output.

**Acceptance Scenarios**:

1. **Given** an ALPS AST with semantic descriptors that contain nested transition descriptors, **When** mapped, **Then** the semantic descriptors become states and the nested transitions become available actions per state.
2. **Given** an ALPS AST with `rt` references on transition descriptors, **When** mapped, **Then** the target states are identified from `rt` values.
3. **Given** an ALPS AST with `ext` elements containing guard labels, **When** mapped, **Then** guard label information is extracted and associated with the corresponding transitions.
4. **Given** an ALPS AST with transition types (`safe`, `unsafe`, `idempotent`), **When** mapped, **Then** HTTP method hints are derived: `safe` maps to GET, `unsafe` maps to POST, `idempotent` maps to PUT (with a documented caveat that DELETE is also idempotent).
5. **Given** an ALPS AST where workflow ordering is absent, **When** mapped, **Then** the mapper produces states and transitions without ordering constraints, and documents this gap for the LLM to resolve.

---

### User Story 4 - Generate ALPS JSON from StateMachineMetadata (Priority: P2)

A developer has a running Frank application with stateful resources. The generator reads `StateMachineMetadata` and produces a canonical ALPS JSON document that describes the application's semantic vocabulary, transition types, and guard extensions.

**Why this priority**: The generator enables the output direction of the spec pipeline -- extracting ALPS from a running application for documentation, visualization (app-state-diagram), and verification against the original design.

**Independent Test**: Construct a `StateMachineMetadata` matching the tic-tac-toe state machine, generate ALPS JSON, and verify the output matches the golden file (modulo ordering).

**Acceptance Scenarios**:

1. **Given** `StateMachineMetadata` with multiple states and transitions, **When** generated, **Then** each state becomes a semantic descriptor containing `href` references to its available transitions.
2. **Given** `StateMachineMetadata` with a transition that has multiple target states (conditional), **When** generated, **Then** the generator produces one transition descriptor per target state (per D-006 `rt` single-value limitation), each with the same event name but different `rt` values.
3. **Given** `StateMachineMetadata` with guard labels on transitions, **When** generated, **Then** guard information appears as `ext` elements on the corresponding transition descriptors.
4. **Given** `StateMachineMetadata` with safe (GET) and unsafe (POST) transitions, **When** generated, **Then** the generator produces descriptors with correct ALPS type annotations (`safe`, `unsafe`, `idempotent`).
5. **Given** generated ALPS JSON, **When** validated against the ALPS specification structure, **Then** the output is a well-formed ALPS document with the required `alps` root object and `descriptor` array.

---

### User Story 5 - Roundtrip Consistency (Priority: P2)

A developer parses an ALPS document, maps it to metadata, then generates ALPS JSON from that metadata. The generated output preserves all information that ALPS can express, even though some information is lost during the metadata mapping step.

**Why this priority**: Roundtrip consistency validates that the parser, mapper, and generator work together correctly and that information loss is confined to documented limitations.

**Independent Test**: Parse the tic-tac-toe ALPS golden file, map to metadata, generate ALPS JSON, re-parse the generated JSON, and compare the two ASTs for structural equivalence.

**Acceptance Scenarios**:

1. **Given** the tic-tac-toe ALPS golden file, **When** roundtripped (parse -> map -> generate -> re-parse), **Then** all descriptor ids, types, `rt` references, and `ext` elements are preserved.
2. **Given** the onboarding ALPS golden file, **When** roundtripped, **Then** all semantic descriptors and transition types are preserved.
3. **Given** an ALPS document with information that ALPS can express (descriptor types, `rt`, `ext`), **When** roundtripped, **Then** no expressible information is lost.

---

### User Story 6 - Golden File Validation (Priority: P2)

A developer runs golden file tests that compare generated ALPS JSON output against reference examples from the spec pipeline design. This ensures the generator produces output consistent with the documented ALPS mapping rules.

**Why this priority**: Golden files anchor the generator output to the design intent from #57, preventing silent drift.

**Independent Test**: Generate ALPS JSON from tic-tac-toe and onboarding metadata, compare against stored golden files, and verify exact structural match.

**Acceptance Scenarios**:

1. **Given** `StateMachineMetadata` for the tic-tac-toe example, **When** ALPS JSON is generated, **Then** the output structurally matches the tic-tac-toe ALPS golden file.
2. **Given** `StateMachineMetadata` for the onboarding example, **When** ALPS JSON is generated, **Then** the output structurally matches the onboarding ALPS golden file.

---

### Edge Cases

- Empty ALPS document (no descriptors): parser produces a valid empty AST, generator produces a minimal ALPS document with empty descriptor array
- Descriptor with `href` reference to a non-existent id: parser preserves the reference, mapper reports a warning
- Circular `href` references between descriptors: parser handles without infinite recursion, mapper reports a warning
- ALPS document with only `link` elements and no descriptors: parser captures links, mapper produces empty state machine metadata
- `ext` element with complex or multi-valued content: parser preserves raw value string
- Descriptor with no `type` attribute: defaults to `semantic` per ALPS specification
- Multiple `ext` elements on a single descriptor: all are captured in order
- Unicode characters in descriptor ids, documentation text, and extension values
- ALPS JSON with additional unknown properties: parser ignores unknown fields (forward-compatible)
- ALPS XML with namespace declarations: parser handles the ALPS namespace correctly
- Very large ALPS documents (100+ descriptors): parser handles without performance degradation
- `rt` value referencing an external URL (not a fragment reference): parser preserves the full URL, mapper treats it as an external link (not a local state)

## Requirements

### Functional Requirements

- **FR-001**: System MUST parse ALPS JSON documents into a typed F# AST, capturing descriptors with their id, type, nested descriptors, `href` references, `rt` (return type), `doc` elements, `link` elements, and `ext` (extension) elements
- **FR-002**: System MUST parse ALPS XML documents into the same typed F# AST as JSON parsing, producing structurally identical results for equivalent documents
- **FR-003**: System MUST support all four ALPS descriptor types: `semantic`, `safe`, `unsafe`, and `idempotent`
- **FR-004**: System MUST parse `ext` elements and preserve their id and value content for downstream guard extraction
- **FR-005**: System MUST handle descriptors with `href` references to other descriptors (both local fragment references like `#position` and external URLs)
- **FR-006**: System MUST default untyped descriptors to `semantic` per the ALPS specification
- **FR-007**: System MUST parse `doc` elements with their optional `format` attribute and text content
- **FR-008**: System MUST parse `link` elements with their `rel` and `href` attributes
- **FR-009**: System MUST map ALPS semantic descriptors containing nested transition descriptors to states with available transitions
- **FR-010**: System MUST map ALPS transition types to HTTP method hints: `safe` to GET, `unsafe` to POST, `idempotent` to PUT (with documented caveat that DELETE is also idempotent per D-006)
- **FR-011**: System MUST map `rt` values to target states, handling the single-value limitation by treating each descriptor as one transition-target pair
- **FR-012**: System MUST extract guard labels from `ext` elements with id `"guard"` and associate them with the parent transition descriptor
- **FR-013**: System MUST generate ALPS JSON from `StateMachineMetadata`, producing one semantic descriptor per state and one transition descriptor per state-transition-target triple
- **FR-014**: System MUST generate `ext` elements for guard labels when guard information is present in the metadata
- **FR-015**: System MUST produce well-formed ALPS JSON with the required `alps` root object containing a `descriptor` array
- **FR-016**: System MUST produce structured error results for malformed input (invalid JSON/XML, missing required fields), including a description of what went wrong
- **FR-017**: System MUST handle forward-compatible parsing: unknown JSON properties and XML elements are ignored without error
- **FR-018**: System MUST share AST types between parser and generator to ensure structural consistency

### Key Entities

- **AlpsDocument**: Top-level AST node containing a version (optional), documentation (optional), list of descriptors, list of links, and list of extensions
- **Descriptor**: Core ALPS element with id, type (`Semantic`/`Safe`/`Unsafe`/`Idempotent`), optional `href` reference, optional `rt` (return type), optional documentation, nested child descriptors, and extension elements
- **DescriptorType**: Discriminated union -- `Semantic`, `Safe`, `Unsafe`, `Idempotent`
- **DescriptorReference**: Either an inline `Descriptor` or an `Href` reference string pointing to another descriptor
- **Extension**: ALPS extension element with id, optional href, and value content
- **Documentation**: ALPS `doc` element with optional format (e.g., `"text"`, `"html"`) and text content
- **Link**: ALPS link with `rel` (relation type) and `href` (target URL)
- **AlpsParseResult**: Result type containing either a successfully parsed `AlpsDocument` or a list of parse errors
- **AlpsParseError**: Structured error with description, and for XML errors, optional position (line/column)

## Success Criteria

### Measurable Outcomes

- **SC-001**: Parser correctly handles the tic-tac-toe ALPS golden file in both JSON and XML formats, producing ASTs where every descriptor can be verified by walking the tree and confirming id, type, nested descriptors, `rt` references, and `ext` elements match the input
- **SC-002**: Parser correctly handles the onboarding ALPS golden file, producing an AST with all semantic and transition descriptors preserved
- **SC-003**: Mapper produces state machine metadata from the tic-tac-toe ALPS AST containing all expected states (XTurn, OTurn, Won, Draw), transitions (makeMove, viewGame), and guard labels
- **SC-004**: Generator produces ALPS JSON from tic-tac-toe metadata that structurally matches the golden file (descriptor ids, types, `rt` references, and `ext` elements all present)
- **SC-005**: Roundtrip (parse -> map -> generate -> re-parse) preserves all ALPS-expressible information for both golden file examples
- **SC-006**: Library compiles under multi-target (net8.0/net9.0/net10.0) with `dotnet build`
- **SC-007**: Malformed ALPS input (invalid JSON, invalid XML, missing required fields) produces structured error results with actionable descriptions rather than unhandled exceptions
