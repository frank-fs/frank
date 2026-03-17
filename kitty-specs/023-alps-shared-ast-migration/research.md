# Research Report: 023-alps-shared-ast-migration

**Created**: 2026-03-16
**Feature**: ALPS Shared AST Migration (Issue #115)
**Phase**: Phase 0 Research

## 1. Descriptor-to-State Mapping Heuristics in `Mapper.fs`

**File**: `src/Frank.Statecharts/Alps/Mapper.fs`

The `Mapper.toStatechartDocument` function (line 111) applies a three-part heuristic to determine which top-level descriptors are states, using `isStateDescriptor` (line 76-84):

A top-level semantic descriptor is classified as a **state** if **any** of these conditions hold:
1. **Has transition-type children**: `d.Descriptors |> List.exists (fun child -> isTransitionType child.Type)` -- where transition types are `Safe`, `Unsafe`, or `Idempotent` (line 79)
2. **Is referenced as an `rt` target**: `Set.contains d.Id.Value rtTargets` -- where `rtTargets` is computed by `collectRtTargets` which recursively walks all descriptors collecting resolved `rt` values (lines 59-72)
3. **Has href-only children**: `d.Descriptors |> List.exists (fun child -> child.Href.IsSome && child.Id.IsNone)` -- e.g., `Won` whose only children are `{ "href": "#viewGame" }` references (lines 82-84)

Anything not matching these rules is a **pure data descriptor** (e.g., `position`, `player`).

Supporting functions to absorb into the parser:
- `collectRtTargets` (line 59): Recursively scans all descriptors to find `rt` target IDs
- `buildDescriptorIndex` (line 91): Maps descriptor `id` to `Descriptor` for href resolution
- `resolveDescriptor` (line 116): Resolves href-only descriptors to actual descriptors in the index
- `extractTransitions` (line 127): Extracts `TransitionEdge` from each state's children
- `extractGuard` (line 38): Gets first ext with `id="guard"`
- `extractParameters` (line 45): Gets href-only children of transition descriptors as parameter names
- `resolveRt` (line 34): Strips `#` prefix from rt values

## 2. Current `AlpsMeta` Annotations and Needed Extensions

**Current `AlpsMeta` in `src/Frank.Statecharts/Ast/Types.fs` (lines 78-81)**:
```fsharp
type AlpsMeta =
    | AlpsTransitionType of AlpsTransitionKind  // Safe/Unsafe/Idempotent
    | AlpsDescriptorHref of string              // href-only reference preservation
    | AlpsExtension of name: string * value: string  // ext element (name/value pair)
```

**New cases needed for full-fidelity roundtripping** (per spec FR-007):
1. **`AlpsDocumentation of format: string option * value: string`** -- Preserves documentation elements on states, transitions, and the document. Currently `StateNode.Label` captures the doc value for states but loses the `format` attribute. Transition documentation is not preserved at all.
2. **`AlpsLink of rel: string * href: string`** -- Preserves link elements at document, state, and descriptor levels. Currently no way to roundtrip links.
3. **`AlpsDataDescriptor of id: string * documentation: (string option * string) option`** -- Preserves pure data descriptors so the generator can emit them as top-level semantic descriptors.
4. **`AlpsVersion of string`** -- Preserves the ALPS version string. Currently the mapper hardcodes `"1.0"` on `fromStatechartDocument`.
5. **`AlpsExtension` needs expansion** -- Currently `name * value` but ALPS extensions also have an optional `href` field. The current shape loses `href`. Should become `AlpsExtension of id: string * href: string option * value: string option`.

**Key finding**: The existing `AlpsExtension` case in `AlpsMeta` loses the `Href` field and forces `Value` to be non-optional. This must be fixed for full fidelity.

## 3. WSD Migration Pattern (Reference Implementation)

**Key structural decisions**:
- **`Wsd/Types.fs`**: Contains ONLY lexer tokens (`TokenKind`, `Token`). NO semantic types.
- **`Wsd/Parser.fs`**: Produces `ParseResult` directly (line 805), containing a `StatechartDocument`. Format-specific data stored via `WsdAnnotation(...)`.
- **`Wsd/Serializer.fs`**: Consumes `StatechartDocument` directly (line 158). Reads `WsdAnnotation` from node annotations.
- **`Wsd/Generator.fs`**: Consumes `StateMachineMetadata`, produces `Result<StatechartDocument, GeneratorError>`.

**Important difference from WSD**: WSD has a 1:1 mapping (participants = states, messages = transitions). ALPS requires heuristic classification. This makes the parser absorption more complex.

## 4. ALPS Test Suite Structure and Migration Needs

| File | Test count | Migration impact |
|------|-----------|-----------------|
| `GoldenFiles.fs` | 0 (data only) | No changes needed |
| `TypeTests.fs` | 10 tests | Must be rewritten or deleted — tests construct `AlpsDocument` directly |
| `JsonParserTests.fs` | 39 tests (16 core + 15 edge case + 8 error) | Must be rewritten — parser returns `ParseResult` not `Result<AlpsDocument, ...>` |
| `JsonGeneratorTests.fs` | 16 tests (4 core + 12 structure) | Must be rewritten — generator accepts `StatechartDocument` |
| `MapperTests.fs` | 33 tests | Absorbed into `JsonParserTests.fs` — file deleted |
| `RoundTripTests.fs` | 10 tests | Must be rewritten — roundtrip compares `StatechartDocument` equality |

**Total**: ~110 tests across 5 test files (plus 1 AST partial population test). All depend on `Alps.Types` types that will be deleted.

## 5. Risks: ALPS Descriptor Hierarchy Reconstruction in Generator

**Risk 1: Ambiguous descriptor nesting.** Transitions become top-level `TransitionEdge` elements with `Source` field. Generator must group by `Source` and nest under correct state descriptor.

**Risk 2: href-only reference reconstruction.** Parser resolves `{ "href": "#viewGame" }` to an actual `TransitionEdge` with `AlpsAnnotation(AlpsDescriptorHref "#viewGame")`. Generator must detect this annotation and emit href-only descriptor instead of full transition descriptor.

**Risk 3: Data descriptor ordering.** ALPS convention puts data descriptors before state descriptors. Generator must maintain this ordering.

**Risk 4: Top-level shared transition deduplication.** `viewGame` is a top-level transition descriptor referenced via `{ "href": "#viewGame" }` from multiple states. Parser creates multiple `TransitionEdge` entries. Generator must deduplicate shared transitions: emit `viewGame` as top-level descriptor while states emit `{ "href": "#viewGame" }` references. **This is the most complex part of the generator** and doesn't exist in the current `fromStatechartDocument`.

**Risk 5: Documentation format loss.** `StateNode.Label` stores only doc value text, losing `format` attribute. With new `AlpsMeta.AlpsDocumentation`, both are preserved.

**Risk 6: Structural equality for roundtrip tests.** Order of annotations matters for F# structural equality. Parser and generator must produce annotations in consistent order.

## 6. Cross-Format Validator Compatibility

The cross-format validator already works with `StatechartDocument` directly. It does NOT reference `AlpsDocument`, `Alps.Types`, or `Alps.Mapper`. No validator code changes needed.

## 7. Compilation Order

In `src/Frank.Statecharts/Frank.Statecharts.fsproj`:
- `Ast/Types.fs` (line 15) compiles first
- `Alps/Types.fs` (line 32) -- will be deleted or reduced
- `Alps/JsonParser.fs` (line 33) -- will now open `Ast`
- `Alps/JsonGenerator.fs` (line 34) -- will now open `Ast`
- `Alps/Mapper.fs` (line 35) -- will be deleted

## Decisions

| # | Decision | Rationale | Impact |
|---|----------|-----------|--------|
| D-001 | Expand `AlpsExtension` to 3 fields (id, href option, value option) | Current shape loses href field | AlpsMeta DU change |
| D-002 | Add 4 new AlpsMeta cases (Documentation, Link, DataDescriptor, Version) | Required for full-fidelity roundtripping | AlpsMeta DU change |
| D-003 | Absorb mapper heuristics into parser, not into generator | Parser is the logical place for classification | Parser complexity increase |
| D-004 | Generator must handle shared transition deduplication | Risk 4 — most complex generator logic | Generator complexity |
| D-005 | Annotations emitted in deterministic order | Risk 6 — roundtrip equality depends on order | Parser/generator constraint |
| D-006 | TypeTests.fs and MapperTests.fs deleted, not migrated | Types being tested no longer exist | Test file reduction |
