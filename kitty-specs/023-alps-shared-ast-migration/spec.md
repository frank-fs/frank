# Feature Specification: ALPS Shared AST Migration

**Feature Branch**: `023-alps-shared-ast-migration`
**Created**: 2026-03-16
**Status**: Draft
**Dependencies**: #111 (Shared AST, spec 020 -- complete), #97 (ALPS Parser + Generator, spec 011 -- complete)
**Location**: `src/Frank.Statecharts/Alps/` (parser, generator, types), `src/Frank.Statecharts/Ast/Types.fs` (AlpsMeta extensions). Tests in `test/Frank.Statecharts.Tests/Alps/`.
**Input**: Issue #115 -- ALPS: migrate parser and generator to shared StatechartDocument AST

## Clarifications

### Session 2026-03-16

- Q: Should ALPS roundtripping preserve full ALPS JSON fidelity or only statechart-level semantics? -> A: Full fidelity. All ALPS-specific constructs (AlpsLink, AlpsDocumentation, AlpsExtension, pure data descriptors, href references) must be preserved via AlpsMeta annotations on the shared AST. The pipeline should generate valid, complete ALPS JSON from the shared AST -- and potentially enriched JSON if other format artifacts provide additional information.

## Background

The ALPS parser (spec 011, #97) currently produces format-specific types (`AlpsDocument`, `Descriptor`, `AlpsParseError`) and the generator consumes `AlpsDocument`. A separate `Mapper.fs` bridges between these ALPS-specific types and the shared `StatechartDocument` AST (spec 020, #111). This mapper is a workaround -- the target design has both parser and generator working directly with `StatechartDocument`.

The WSD parser/serializer has already been migrated to the shared AST (spec 020) and serves as the reference pattern. The WSD migration eliminated WSD-specific semantic types (`Diagram`, `Participant`, `Message`, etc.) and had the parser produce `StatechartDocument` directly, with format-specific data (arrow styles, note positions) stored in `WsdAnnotation` annotations.

ALPS has the most complex mapping of any format because ALPS descriptors do not map 1:1 to states and transitions. The current `Mapper.toStatechartDocument` applies heuristics to identify which descriptors are states (those with transition-type children or referenced as `rt` targets) versus pure data descriptors (like `position`, `player`). These heuristics must be absorbed into the parser itself.

Additionally, ALPS carries significant format-specific metadata that must survive roundtripping: documentation elements with format attributes, extension elements (guard labels and arbitrary metadata), link elements (rel/href pairs), href-only descriptor references, pure data descriptors, and the ALPS version string. All of this must be preserved through `AlpsMeta` annotations on the shared AST so the generator can reconstruct complete, valid ALPS JSON.

### Current Architecture (to be replaced)

```
ALPS JSON --> JsonParser --> AlpsDocument --> Mapper --> StatechartDocument
StatechartDocument --> Mapper --> AlpsDocument --> JsonGenerator --> ALPS JSON
```

### Target Architecture

```
ALPS JSON --> JsonParser --> StatechartDocument (with AlpsMeta annotations)
StatechartDocument (with AlpsMeta annotations) --> JsonGenerator --> ALPS JSON
```

### ALPS Descriptor-to-AST Mapping Complexity

ALPS descriptors are polymorphic -- the same `descriptor` element can represent a state, a transition, a data parameter, or a reference depending on context:

- **State descriptors**: Top-level semantic descriptors that contain transition-type children or are referenced as `rt` targets. Map to `StateNode`.
- **Transition descriptors**: Children with type safe/unsafe/idempotent. Map to `TransitionEdge` with the parent state as source and `rt` as target.
- **Data descriptors**: Top-level semantic descriptors with no transition children and not referenced as `rt` targets (e.g., `position`, `player`). Must be preserved as annotations for roundtripping.
- **Href-only references**: Children like `{ "href": "#viewGame" }` that reference other descriptors by ID. Must be resolved during parsing and preserved as annotations for roundtripping.
- **Parameter references**: Href-only children of transition descriptors (e.g., `{ "href": "#position" }` inside `makeMove`). Map to `TransitionEdge.Parameters`.

## User Scenarios & Testing

### User Story 1 - Parser Produces Shared AST Directly (Priority: P1)

A developer using the ALPS JSON parser receives a `StatechartDocument` (shared AST) directly, without needing to call a separate mapper. The parser identifies state descriptors, extracts transitions with their guards and parameters, and stores all ALPS-specific metadata in `AlpsMeta` annotations. The parser returns the shared `ParseResult` type with errors and warnings consistent with all other format parsers.

**Why this priority**: This is the core deliverable. Without the parser producing shared AST directly, the mapper workaround remains and the cross-format validator cannot work with ALPS artifacts uniformly.

**Independent Test**: Parse the tic-tac-toe golden file ALPS JSON through the migrated parser and verify the resulting `StatechartDocument` contains the correct states (XTurn, OTurn, Won, Draw, gameState), transitions (makeMove with guards and parameters, viewGame), and ALPS-specific annotations (transition types, documentation, links, data descriptors).

**Acceptance Scenarios**:

1. **Given** the tic-tac-toe ALPS JSON input, **When** the parser produces a `StatechartDocument`, **Then** the document contains `StateNode` elements for XTurn, OTurn, Won, Draw, and gameState (5 states identified by the same heuristics currently in the mapper).
2. **Given** the tic-tac-toe ALPS JSON with `makeMove` descriptors of type `unsafe`, **When** the parser produces transitions, **Then** each `TransitionEdge` carries an `AlpsAnnotation(AlpsTransitionType Unsafe)` annotation and has the correct source (parent state) and target (resolved `rt` value with `#` prefix stripped).
3. **Given** the tic-tac-toe ALPS JSON with `ext` elements containing guard values, **When** the parser produces transitions, **Then** each guarded `TransitionEdge` has its `Guard` field populated (e.g., `Some "role=PlayerX"`, `Some "wins"`, `Some "boardFull"`).
4. **Given** the tic-tac-toe ALPS JSON with href-only `{ "href": "#viewGame" }` references inside state descriptors, **When** the parser resolves these references, **Then** the resolved transition (viewGame, type safe) appears as a `TransitionEdge` from the containing state with an `AlpsAnnotation(AlpsDescriptorHref "#viewGame")` annotation preserving the original reference.
5. **Given** the tic-tac-toe ALPS JSON with `makeMove` descriptors containing `{ "href": "#position" }` and `{ "href": "#player" }` parameter children, **When** the parser produces transitions, **Then** each `TransitionEdge.Parameters` list contains `["position"; "player"]`.
6. **Given** malformed JSON or JSON missing the `alps` root object, **When** the parser attempts to parse, **Then** it returns a `ParseResult` with errors in the `Errors` field (using `ParseFailure` rather than the old `AlpsParseError` type).
7. **Given** the onboarding ALPS JSON input, **When** the parser produces a `StatechartDocument`, **Then** the document contains the correct states (Welcome, CollectEmail, CollectProfile, Review, Complete) and transitions (start, submitEmail, submitProfile, confirmReview, editEmail, editProfile) with correct source/target pairs.

---

### User Story 2 - Extend AlpsMeta for Full-Fidelity Roundtripping (Priority: P1)

The `AlpsMeta` discriminated union in the shared AST (`Ast/Types.fs`) is extended with additional cases to carry all ALPS-specific data that does not map to core statechart concepts. This enables the generator to reconstruct complete ALPS JSON from a `StatechartDocument` without information loss.

**Why this priority**: Without extended annotations, the generator cannot produce valid ALPS JSON that matches the original input. Full-fidelity roundtripping is a stated requirement.

**Independent Test**: Construct a `StatechartDocument` with all AlpsMeta annotation variants populated, then generate ALPS JSON from it, then re-parse the JSON and verify structural equality of the resulting `StatechartDocument`.

**Acceptance Scenarios**:

1. **Given** an ALPS document with `doc` elements on descriptors and the root, **When** parsed to shared AST, **Then** documentation text and format are stored as `AlpsMeta` annotations on the corresponding `StateNode`, `TransitionEdge`, or document-level `Annotations` list, and can be extracted by the generator.
2. **Given** an ALPS document with `link` elements (rel/href pairs), **When** parsed to shared AST, **Then** links are stored as `AlpsMeta` annotations and can be reconstructed by the generator.
3. **Given** an ALPS document with `ext` elements beyond guards (arbitrary id/href/value triplets), **When** parsed to shared AST, **Then** non-guard extensions are stored as `AlpsMeta` annotations and can be reconstructed by the generator.
4. **Given** an ALPS document with pure data descriptors (semantic descriptors with no transition children and not referenced as `rt` targets, e.g., `position`, `player`), **When** parsed to shared AST, **Then** data descriptors are stored as `AlpsMeta` annotations (or `DataEntry` records) so the generator can reconstruct them.
5. **Given** an ALPS document with a `version` string, **When** parsed to shared AST, **Then** the version is stored as an `AlpsMeta` annotation on the document and can be emitted by the generator.

---

### User Story 3 - Generator Consumes Shared AST (Priority: P1)

A developer using the ALPS JSON generator passes a `StatechartDocument` (with ALPS annotations) and receives valid ALPS JSON output. The generator reconstructs the ALPS descriptor hierarchy by reading `StateNode` elements as state descriptors, `TransitionEdge` elements as transition descriptors nested under their source states, and `AlpsMeta` annotations for format-specific metadata (documentation, links, extensions, data descriptors, version).

**Why this priority**: The generator is the output side of the pipeline. It must consume the same shared AST type that the parser produces, completing the migration.

**Independent Test**: Take the `StatechartDocument` produced by parsing the tic-tac-toe golden file, pass it through the generator, then re-parse the generated JSON and verify the resulting `StatechartDocument` is structurally equal to the original.

**Acceptance Scenarios**:

1. **Given** a `StatechartDocument` with `StateNode` elements and `TransitionEdge` elements carrying `AlpsAnnotation(AlpsTransitionType ...)` annotations, **When** the generator produces ALPS JSON, **Then** states become semantic descriptors and transitions become nested child descriptors with the correct `type` (safe/unsafe/idempotent) and `rt` values.
2. **Given** a `StatechartDocument` with `AlpsMeta` annotations for documentation, links, and extensions, **When** the generator produces ALPS JSON, **Then** the output contains properly formatted `doc`, `link`, and `ext` JSON elements.
3. **Given** a `StatechartDocument` with `AlpsMeta` annotations for pure data descriptors, **When** the generator produces ALPS JSON, **Then** data descriptors appear as top-level semantic descriptors in the output (before state descriptors, matching ALPS convention).
4. **Given** a `StatechartDocument` with transitions that have `AlpsAnnotation(AlpsDescriptorHref ...)` annotations, **When** the generator produces ALPS JSON, **Then** the output contains href-only descriptor references (`{ "href": "#viewGame" }`) nested inside the appropriate state descriptor.
5. **Given** a `StatechartDocument` with no ALPS-specific annotations (e.g., produced by a different format parser), **When** the generator produces ALPS JSON, **Then** it uses sensible defaults: `unsafe` for transitions without type annotations, `"1.0"` for version, and omits empty optional fields.
6. **Given** a `StatechartDocument` with transition parameters, **When** the generator produces ALPS JSON, **Then** parameters appear as href-only child descriptors of the transition descriptor (e.g., `{ "href": "#position" }`).

---

### User Story 4 - Delete Format-Specific Types and Mapper (Priority: P1)

The ALPS-specific document types (`AlpsDocument`, `Descriptor`, `AlpsParseError`) and the `Mapper.fs` bridge module are deleted. `Types.fs` is either deleted entirely or reduced to only carry types needed for `AlpsMeta` annotation payloads (if any are needed beyond what is already in `Ast/Types.fs`).

**Why this priority**: Keeping dead types creates confusion and maintenance burden. The deletion is the proof that the migration is complete.

**Independent Test**: After deletion, verify that `dotnet build` succeeds across all target frameworks (net8.0/net9.0/net10.0) and no remaining code references the deleted types.

**Acceptance Scenarios**:

1. **Given** the migration is complete, **When** `AlpsDocument`, `Descriptor`, `DescriptorType`, `AlpsParseError`, `AlpsDocumentation`, `AlpsExtension`, `AlpsLink` types are deleted from `Alps/Types.fs`, **Then** no compiler errors occur because no code references them.
2. **Given** the migration is complete, **When** `Alps/Mapper.fs` is deleted, **Then** no compiler errors occur because no code imports or calls `Mapper.toStatechartDocument` or `Mapper.fromStatechartDocument`.
3. **Given** the deletions, **When** `dotnet build` is run targeting net8.0, net9.0, and net10.0, **Then** the build succeeds with no errors.

---

### User Story 5 - Test Suite Migration (Priority: P1)

All existing ALPS tests are updated to work with the shared AST types. Tests that previously constructed `AlpsDocument` values directly now construct `StatechartDocument` values with appropriate `AlpsMeta` annotations. Tests that previously asserted against `Descriptor` fields now assert against `StateNode`, `TransitionEdge`, and annotation fields. The test coverage is preserved or improved.

**Why this priority**: Tests validate correctness. Without migrated tests, there is no confidence the migration preserved behavior.

**Independent Test**: Run the full ALPS test suite (`dotnet test` on the test project) and verify all tests pass.

**Acceptance Scenarios**:

1. **Given** `JsonParserTests.fs` tests that previously asserted against `AlpsDocument` fields, **When** updated to assert against `StatechartDocument` and `ParseResult` fields, **Then** all parser tests pass with equivalent semantic assertions.
2. **Given** `JsonGeneratorTests.fs` tests that previously constructed `AlpsDocument` values, **When** updated to construct `StatechartDocument` values with `AlpsMeta` annotations, **Then** all generator tests pass and produce equivalent ALPS JSON output.
3. **Given** `RoundTripTests.fs` tests that previously verified `AlpsDocument` roundtripping, **When** updated to verify `StatechartDocument` roundtripping (parse -> generate -> re-parse produces structurally equal `StatechartDocument`), **Then** all roundtrip tests pass.
4. **Given** `MapperTests.fs` tests that verified mapper behavior, **When** the mapper is deleted, **Then** the equivalent assertions are absorbed into the parser tests (since the parser now does what the mapper did) and all pass.
5. **Given** cross-format validator tests that use ALPS artifacts, **When** they construct ALPS `FormatArtifact` values, **Then** they work with `StatechartDocument` directly (no mapper call needed) and all cross-format tests pass.

---

### User Story 6 - Cross-Format Validator Compatibility (Priority: P2)

The cross-format validator works with ALPS artifacts without requiring a mapper step. Since the ALPS parser now produces `StatechartDocument` directly, ALPS artifacts can be passed to the validator alongside WSD, SCXML, smcat, and XState artifacts for cross-format consistency checking.

**Why this priority**: This is a downstream benefit of the migration rather than a core deliverable, but it validates the migration achieved its architectural goal.

**Independent Test**: Create a cross-format validation scenario with ALPS and WSD artifacts for the same tic-tac-toe state machine. Run the validator and verify state name agreement and event name agreement rules execute without errors.

**Acceptance Scenarios**:

1. **Given** an ALPS `FormatArtifact` created by parsing tic-tac-toe ALPS JSON (no mapper step), **When** compared against a WSD `FormatArtifact` with the same states and events, **Then** the cross-format state name agreement and event name agreement rules pass.
2. **Given** an ALPS `FormatArtifact` with states [XTurn, OTurn, Won, Draw, gameState] and a WSD `FormatArtifact` with states [XTurn, OTurn, Won, Draw] (missing gameState), **When** the cross-format validator runs, **Then** it reports the state name mismatch for gameState.

---

### Edge Cases

- Empty ALPS document (no descriptors) produces a valid `StatechartDocument` with empty element lists
- ALPS document with only `version` and no descriptors produces a valid `StatechartDocument` with version stored as annotation
- Descriptor with no `id` (href-only reference at top level) is handled gracefully
- Descriptor with unknown `type` string defaults to semantic (forward compatibility)
- Multiple `ext` elements with `id="guard"` on a single descriptor -- first guard wins (existing behavior preserved)
- Descriptor with external URL in `rt` (e.g., `http://example.com/other`) is preserved as target without `#` stripping
- Deeply nested descriptors (3+ levels) are handled correctly
- Unicode characters in descriptor IDs, documentation values, and extension values
- `StatechartDocument` with no ALPS annotations passed to generator produces valid minimal ALPS JSON with defaults
- Descriptor with both `id` and `href` attributes (unusual but valid ALPS)
- ALPS document with top-level `ext` elements (document-level extensions, not on any descriptor)
- Transition descriptor with no `rt` value produces a `TransitionEdge` with `Target = None`
- State descriptor whose only children are href-only references (e.g., Won with only `{ "href": "#viewGame" }`)

## Requirements

### Functional Requirements

- **FR-001**: System MUST change `parseAlpsJson` to return `ParseResult` (shared AST type with `Document: StatechartDocument`, `Errors: ParseFailure list`, `Warnings: ParseWarning list`) instead of `Result<AlpsDocument, AlpsParseError list>`
- **FR-002**: System MUST apply state identification heuristics during parsing: a top-level semantic descriptor is a state if it contains transition-type children (safe/unsafe/idempotent), is referenced as an `rt` target, or contains href-only references to other descriptors
- **FR-003**: System MUST extract transitions from state descriptors during parsing: each transition-type child descriptor becomes a `TransitionEdge` with source = parent state identifier, target = resolved `rt` value (stripped of `#` prefix), event = child descriptor `id`
- **FR-004**: System MUST resolve href-only descriptor references during parsing by building an index of all descriptors by `id` and looking up the referenced descriptor to determine if it is a transition
- **FR-005**: System MUST extract guard labels from `ext` elements with `id="guard"` during parsing and set them on `TransitionEdge.Guard`
- **FR-006**: System MUST extract parameter references from transition descriptor children during parsing: href-only children of transition descriptors become entries in `TransitionEdge.Parameters` (with `#` prefix stripped from href values)
- **FR-007**: System MUST extend the `AlpsMeta` discriminated union in `Ast/Types.fs` with additional cases to carry: documentation (format and value text), link elements (rel and href), non-guard extension elements (id, href, value), pure data descriptors (id and documentation), and the ALPS version string
- **FR-008**: System MUST store ALPS documentation elements as `AlpsMeta` annotations on the corresponding AST node -- document-level documentation on `StatechartDocument.Annotations`, state documentation on `StateNode.Annotations`, transition documentation on `TransitionEdge.Annotations`
- **FR-009**: System MUST store ALPS link elements as `AlpsMeta` annotations -- document-level links on `StatechartDocument.Annotations`, descriptor-level links on the corresponding `StateNode` or `TransitionEdge`
- **FR-010**: System MUST store pure data descriptors (semantic descriptors that are not states and not transitions) as `AlpsMeta` annotations (specifically `AlpsDataDescriptor`) on `StatechartDocument.Annotations`, preserving their `id` and optional documentation
- **FR-011**: System MUST store the ALPS version string as an `AlpsMeta` annotation on `StatechartDocument.Annotations`
- **FR-012**: System MUST change `generateAlpsJson` to accept `StatechartDocument` instead of `AlpsDocument` and produce valid ALPS JSON by reconstructing the descriptor hierarchy from `StateNode` elements, `TransitionEdge` elements, and `AlpsMeta` annotations
- **FR-013**: System MUST reconstruct descriptor nesting in the generator: `TransitionEdge` elements are grouped by source state and nested as child descriptors under the corresponding state descriptor
- **FR-014**: System MUST reconstruct href-only descriptor references in the generator when `AlpsAnnotation(AlpsDescriptorHref ...)` annotations are present on a transition
- **FR-015**: System MUST reconstruct the ALPS `rt` value in the generator by adding a `#` prefix to `TransitionEdge.Target` for local references
- **FR-016**: System MUST default to `unsafe` descriptor type when generating a transition descriptor that has no `AlpsAnnotation(AlpsTransitionType ...)` annotation
- **FR-017**: System MUST default to ALPS version `"1.0"` when generating and no `AlpsMeta` version annotation is present on the document
- **FR-018**: System MUST delete `AlpsDocument`, `Descriptor`, `DescriptorType`, `AlpsDocumentation`, `AlpsExtension`, `AlpsLink`, `AlpsParseError`, and `AlpsSourcePosition` types from `Alps/Types.fs`
- **FR-019**: System MUST delete `Alps/Mapper.fs` entirely
- **FR-020**: System MUST update all ALPS tests to use shared AST types and pass with equivalent semantic assertions
- **FR-021**: System MUST compile under multi-target (net8.0/net9.0/net10.0) with `dotnet build` after migration
- **FR-022**: System MUST preserve the existing ALPS JSON roundtrip property: parsing ALPS JSON, generating from the resulting `StatechartDocument`, and re-parsing the generated JSON produces a structurally equal `StatechartDocument`

### Key Entities

- **StatechartDocument**: Root AST node (from spec 020). After migration, the ALPS parser populates this directly with states, transitions, and ALPS-specific annotations. The generator reads this to produce ALPS JSON.
- **AlpsMeta**: Extended discriminated union carrying ALPS-specific data that does not map to core statechart concepts. Cases include transition type, descriptor href, extension elements, documentation, links, data descriptors, and version string.
- **StateNode**: Represents an ALPS state descriptor (semantic descriptor identified as a state by the heuristics). Carries ALPS documentation as an annotation.
- **TransitionEdge**: Represents an ALPS transition descriptor (safe/unsafe/idempotent child of a state descriptor). Carries transition type, guard, parameters, documentation, and href-reference annotations.
- **ParseResult**: Uniform result type (from spec 020) wrapping `StatechartDocument` with `ParseFailure` errors and `ParseWarning` warnings. Replaces `Result<AlpsDocument, AlpsParseError list>`.

## Success Criteria

### Measurable Outcomes

- **SC-001**: `parseAlpsJson` returns `ParseResult` with a `StatechartDocument` -- verified by parsing both golden files (tic-tac-toe and onboarding) and asserting the result type is `ParseResult`
- **SC-002**: All state identification produces the same results as the current mapper -- verified by comparing state identifiers extracted from the parser output against the known expected states for each golden file
- **SC-003**: All transition extraction produces the same results as the current mapper -- verified by comparing source/target/event/guard/parameter tuples from the parser output against the known expected transitions for each golden file
- **SC-004**: Full-fidelity ALPS JSON roundtripping works: parse -> generate -> re-parse produces a structurally equal `StatechartDocument` -- verified for both golden files and edge case documents
- **SC-005**: `AlpsDocument`, `Descriptor`, `AlpsParseError`, and `Mapper.fs` are deleted and no code references them -- verified by `dotnet build` succeeding and grep finding zero references to deleted types
- **SC-006**: All existing ALPS tests pass after migration to shared AST types -- verified by `dotnet test` with zero failures in the Alps test modules
- **SC-007**: Multi-target build succeeds -- verified by `dotnet build` passing for net8.0, net9.0, and net10.0
- **SC-008**: Cross-format validator works with ALPS artifacts without a mapper step -- verified by running cross-format validation with an ALPS `FormatArtifact` constructed directly from parser output

## Assumptions

- The `AlpsMeta` discriminated union in `Ast/Types.fs` will be extended with new cases. This is a non-breaking change since `AlpsMeta` is internal to `Frank.Statecharts` and pattern matches on it use wildcard fallbacks in consuming code.
- The state identification heuristics from `Mapper.toStatechartDocument` are correct and complete. The parser will absorb them without modification.
- The `fromStatechartDocument` direction (shared AST to ALPS JSON) is handled by the generator directly reading the shared AST, replacing `Mapper.fromStatechartDocument`.
- Pure data descriptors (like `position`, `player`) that are not states or transitions will be stored either as `DataEntry` records or as `AlpsMeta` annotations on the document. The implementation will choose the approach that best supports roundtripping.
- The existing golden file ALPS JSON documents are sufficient test fixtures. No new golden files are needed.
- Tests that construct `AlpsDocument` / `Descriptor` values inline will be rewritten to construct `StatechartDocument` values with `AlpsMeta` annotations. The test logic (what is verified) remains the same.
- `Alps/Types.fs` may be retained as an empty file or deleted entirely, depending on whether any ALPS-specific helper types are needed beyond `AlpsMeta` cases in the shared AST. If retained, it will only contain types used as payloads inside `AlpsMeta` cases.
