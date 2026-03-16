# Research: SCXML Parser and Generator

**Feature**: 018-scxml-parser-generator
**Date**: 2026-03-16

## R1: W3C SCXML Element Structure

**Decision**: Parse the 9 in-scope SCXML element types per the W3C Recommendation.

**Rationale**: The W3C SCXML spec (https://www.w3.org/TR/scxml/) defines a precise XML schema. Adhering to the standard ensures interoperability with SCXML-compatible tools.

**Key findings from W3C spec**:

| Element | Required Attributes | Optional Attributes | Notes |
|---------|-------------------|-------------------|-------|
| `<scxml>` | `version` ("1.0"), `xmlns` | `initial`, `name`, `datamodel`, `binding` | Root element. Must have at least one `<state>`, `<parallel>`, or `<final>` child. |
| `<state>` | (none) | `id`, `initial` | `id` is optional per W3C but practically always present. Contains transitions, child states, data model, history, invoke. |
| `<parallel>` | (none) | `id` | Like `<state>` but children execute concurrently. |
| `<final>` | (none) | `id` | Terminal state. May contain `<onentry>`, `<onexit>`, `<donedata>`. |
| `<transition>` | (none) | `event`, `cond`, `target`, `type` | Must have at least one of `event`, `cond`, or `target`. `target` can be space-separated list. `type` defaults to "external". |
| `<datamodel>` | (none) | (none) | Container for `<data>` elements. |
| `<data>` | `id` | `src`, `expr` | Cannot have both `src` and `expr`. May have child text content instead of `expr`. |
| `<history>` | (none) | `id`, `type` | `type` defaults to "shallow". Contains a `<transition>` child (default history target). |
| `<invoke>` | (none) | `type`, `src`, `id` | Invocation annotation. We preserve attributes but do not execute. |

**Initial state inference** (W3C section 3.2): When `<scxml>` has no `initial` attribute, the processor MUST enter the first child `<state>` in document order.

**Multiple transition targets** (W3C section 3.5): The `target` attribute can contain a space-separated list of state IDs.

**Out of scope**: `<script>`, `<assign>`, `<send>`, `<raise>`, `<log>`, `<cancel>`, `<foreach>`, `<param>`, `<content>`, `<donedata>`, `<finalize>`, `<onentry>`, `<onexit>`. These are executable content elements that relate to runtime behavior.

**Alternatives considered**: None. W3C SCXML is the authoritative spec.

## R2: System.Xml.Linq Parsing Strategy

**Decision**: Use `XDocument.Parse` for string input with `LoadOptions.SetLineInfo` to retain line information on all elements. For `TextReader` and `Stream` inputs, use `XDocument.Load` with `LoadOptions.SetLineInfo`.

**Rationale**: `System.Xml.Linq` provides a high-level, LINQ-friendly API that maps naturally to F# functional patterns. Line info is available by casting `XElement`/`XAttribute` to `IXmlLineInfo`.

**Key API patterns**:

1. **String parsing**: `XDocument.Parse(xml, LoadOptions.SetLineInfo)`
2. **TextReader**: `XDocument.Load(reader, LoadOptions.SetLineInfo)`
3. **Stream**: `XDocument.Load(stream, LoadOptions.SetLineInfo)`
4. **Line info extraction**: `(element :> IXmlLineInfo).LineNumber`, `.LinePosition`
5. **Namespace handling**: `XNamespace.Get("http://www.w3.org/2005/07/scxml")` + `ns + "state"` for qualified element names
6. **Error handling**: Catch `System.Xml.XmlException` which provides `.LineNumber` and `.LinePosition`

**Alternatives considered**:
- `XmlReader` directly: More control over position tracking but significantly more complex code for tree construction. Rejected for ergonomics.
- `XmlDocument` (DOM): Legacy API, no LINQ integration. Rejected.

## R3: Layered Architecture with Deferred Shared AST Mapping

**Decision**: Three-layer architecture: (1) SCXML-specific types, (2) Parser/Generator operating on those types, (3) Mapper to/from shared AST (spec 020).

**Rationale**: Decouples the SCXML parser from the shared AST spec (020), which is still in Draft status. The parser, generator, and all their tests can be built and validated independently. Only the thin mapper layer is blocked on spec 020.

**Layer details**:

| Layer | Module | Dependency | Blockable? |
|-------|--------|-----------|-----------|
| Types | `Scxml/Types.fs` | None | No |
| Parser | `Scxml/Parser.fs` | `System.Xml.Linq`, `Scxml/Types.fs` | No |
| Generator | `Scxml/Generator.fs` | `System.Xml.Linq`, `Scxml/Types.fs` | No |
| Mapper | `Scxml/AstMapper.fs` | `Scxml/Types.fs`, shared AST (spec 020) | Yes (blocked on 020) |

**Alternatives considered**:
- Single-layer directly populating shared AST: Would block all development on spec 020.
- Stub types mirroring shared AST: Would create throwaway code and potential drift.

## R4: SCXML Namespace Handling

**Decision**: Support both default namespace (`xmlns="http://www.w3.org/2005/07/scxml"`) and prefixed namespace (e.g., `xmlns:sc="http://www.w3.org/2005/07/scxml"`).

**Rationale**: Real-world SCXML documents use either form. The W3C spec does not mandate default namespace usage.

**Implementation approach**: Define `let scxmlNs = XNamespace.Get("http://www.w3.org/2005/07/scxml")` and always look up elements by qualified name (`scxmlNs + "state"`). `System.Xml.Linq` resolves both default and prefixed namespaces transparently via `XNamespace`.

**Alternatives considered**: Stripping namespaces before parsing. Rejected because it loses namespace information and may break on documents with multiple namespaces.

## R5: Error Handling Strategy

**Decision**: Two error categories: (1) XML syntax errors (malformed XML) caught as `XmlException`, (2) SCXML structural errors (valid XML but invalid SCXML structure) detected during tree walking.

**Rationale**: Separating XML-level from SCXML-level errors provides clear, actionable messages.

**Error types**:

| Category | Source | Position Available? | Example |
|----------|--------|-------------------|---------|
| XML syntax error | `XmlException` | Yes (`.LineNumber`, `.LinePosition`) | Unclosed tag, malformed attribute |
| Missing required element | Tree walk | Yes (via `IXmlLineInfo`) | `<scxml>` with no child states |
| Invalid attribute value | Tree walk | Yes (via `IXmlLineInfo`) | `<history type="invalid">` |
| Unknown element (warning) | Tree walk | Yes (via `IXmlLineInfo`) | Unrecognized child element inside `<state>` |

**Result type**: `Result<ScxmlDocument, ParseError list>` for hard failures (malformed XML). For valid XML with structural warnings, return `ScxmlParseResult` containing the document plus warnings.

**Alternatives considered**: Throwing exceptions. Rejected per constitution principle VII (structured errors required).

## R6: Data Entry Scoping

**Decision**: State-scoped data entries stored as a `DataEntry list` field on the SCXML state node. Top-level data entries stored on the `ScxmlDocument` root.

**Rationale**: Mirrors the SCXML XML structure where `<datamodel>` can appear both at `<scxml>` level and inside `<state>` elements. Keeps data co-located with the state it belongs to. A flat list with parent-state-id references would lose the structural relationship.

**Alternatives considered**: Flat list with parent IDs. Rejected because it breaks the hierarchical relationship and complicates generator output.
