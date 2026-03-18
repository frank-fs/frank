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
