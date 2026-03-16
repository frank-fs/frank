# Research: ALPS Parser and Generator (Spec 011)

**Date**: 2026-03-15
**Status**: Complete
**Derived From**: Spec 011 (spec.md), feasibility research (spec 003), shared AST design (spec 020)

---

## Executive Summary

This research resolves all technical unknowns for the ALPS parser and generator implementation. The ALPS specification defines a simple document structure with descriptors, extensions, documentation, and links in both JSON and XML serializations. The implementation uses System.Text.Json and System.Xml.Linq (both built-in) for parsing, and Utf8JsonWriter for generation. The ALPS-specific AST is self-contained; only the mapper to the shared statechart AST (spec 020) has an external dependency.

---

## Research Area 1: ALPS Specification Structure

### ALPS JSON Format

The ALPS JSON format uses a root `alps` object containing:

```json
{
  "alps": {
    "version": "1.0",
    "doc": { "format": "text", "value": "Description text" },
    "descriptor": [
      {
        "id": "descriptorId",
        "type": "semantic",
        "doc": { "format": "text", "value": "..." },
        "href": "#otherDescriptor",
        "rt": "#returnType",
        "descriptor": [ ... ],
        "ext": [
          { "id": "extId", "href": "...", "value": "extValue" }
        ],
        "link": [
          { "rel": "help", "href": "http://example.com/help" }
        ]
      }
    ],
    "link": [
      { "rel": "self", "href": "http://example.com/profile" }
    ],
    "ext": [
      { "id": "extId", "href": "...", "value": "extValue" }
    ]
  }
}
```

Key properties:
- `version`: Optional string (e.g., `"1.0"`)
- `doc`: Optional documentation element with `format` (optional, defaults to `"text"`) and `value` (text content)
- `descriptor`: Array of descriptor objects (the core of ALPS)
- `link`: Optional array of link objects with `rel` and `href`
- `ext`: Optional array of extension objects with `id`, optional `href`, and `value`

### ALPS XML Format

The XML format maps the same concepts:

```xml
<alps version="1.0">
  <doc format="text">Description text</doc>
  <descriptor id="descriptorId" type="semantic" href="#otherDescriptor" rt="#returnType">
    <doc format="text">...</doc>
    <descriptor id="nested" type="safe" />
    <ext id="extId" href="..." value="extValue" />
    <link rel="help" href="http://example.com/help" />
  </descriptor>
  <link rel="self" href="http://example.com/profile" />
  <ext id="extId" href="..." value="extValue" />
</alps>
```

Key rules from the spec:
- `alps`, `doc`, `descriptor`, and `ext` are always XML **elements**
- All other ALPS properties appear as XML **attributes** (`id`, `type`, `href`, `rt`, `rel`, `version`, `format`, `value`)
- `descriptor` nesting is achieved via child `<descriptor>` elements

### Descriptor Types

| Type | Meaning | HTTP Method Hint |
|------|---------|-----------------|
| `semantic` | Data element (default if omitted) | N/A (not a transition) |
| `safe` | Read-only transition | GET |
| `unsafe` | State-modifying transition | POST |
| `idempotent` | Repeatable state-modifying transition | PUT (caveat: DELETE is also idempotent per D-006) |

**Decision R-001**: Descriptors without a `type` attribute default to `semantic` per the ALPS specification. This is FR-006 in the spec.

### href Semantics

- Local fragment reference: `"#position"` -- refers to another descriptor by id within the same document
- External URL: `"http://example.com/profile"` -- refers to a descriptor in an external ALPS document
- The parser preserves both forms. The mapper treats local references as internal links and external references as external links.

### rt (Return Type) Semantics

- `rt` is a single-valued property on transition descriptors
- Value is a fragment reference to another descriptor (e.g., `"#OTurn"`)
- ALPS cannot express conditional returns (e.g., makeMove leading to OTurn OR Won OR Draw) -- each target requires a separate descriptor (D-006 limitation)

**Decision R-002**: The parser preserves `rt` as a single string. The mapper creates one transition per descriptor-rt pair. The generator emits one descriptor per source-target combination.

---

## Research Area 2: Serialization Strategy

### JSON Parsing (ALPS JSON to AlpsDocument)

**Approach**: Manual deserialization using `System.Text.Json.JsonDocument` and `JsonElement`.

Rationale:
- ALPS JSON is a simple, well-defined structure -- no need for attribute-based deserialization
- `JsonDocument` provides zero-allocation read access to JSON
- Manual traversal gives full control over error reporting (structured `AlpsParseError`)
- Forward-compatible: unknown properties are simply skipped during enumeration
- Constitution principle VI: `JsonDocument` is `IDisposable` -- must use `use` binding

Pattern:
```fsharp
let parseAlpsJson (json: string) : Result<AlpsDocument, AlpsParseError list> =
    use doc = JsonDocument.Parse(json)
    let root = doc.RootElement
    // walk the structure, building AlpsDocument
```

**Decision R-003**: Use `JsonDocument.Parse` with `use` binding. Wrap in try/catch for `JsonException` only, converting to `AlpsParseError`. No catch-all handlers (constitution principle VII).

### XML Parsing (ALPS XML to AlpsDocument)

**Approach**: `System.Xml.Linq.XDocument` with LINQ-to-XML queries.

Rationale:
- ALPS XML is simple element/attribute structure -- no namespaces required (though we handle them per edge case)
- `XDocument.Parse` provides in-memory DOM
- LINQ-to-XML queries are idiomatic and readable
- `XDocument` is NOT `IDisposable` -- no `use` binding needed (unlike `XmlReader`)
- Forward-compatible: unknown elements are simply not queried

Pattern:
```fsharp
let parseAlpsXml (xml: string) : Result<AlpsDocument, AlpsParseError list> =
    let doc = XDocument.Parse(xml)
    let root = doc.Root
    // walk the structure, building AlpsDocument
```

**Decision R-004**: Use `XDocument.Parse`. Wrap in try/catch for `XmlException` only, converting to `AlpsParseError` with line/column info from the exception.

### JSON Generation (AlpsDocument to ALPS JSON)

**Approach**: Manual construction using `System.Text.Json.Utf8JsonWriter`.

Rationale:
- ALPS JSON output is a simple, predictable structure
- `Utf8JsonWriter` is high-performance and zero-allocation for the writer itself
- Avoids adding FSharp.SystemTextJson NuGet dependency (constitution principle III)
- Full control over output formatting (indented for readability)
- The ALPS structure has no F# DU serialization challenge -- all output is strings, arrays, and objects

Pattern:
```fsharp
let generateAlpsJson (doc: AlpsDocument) : string =
    use stream = new MemoryStream()
    use writer = new Utf8JsonWriter(stream, JsonWriterOptions(Indented = true))
    // write the ALPS JSON structure
    writer.Flush()
    Encoding.UTF8.GetString(stream.ToArray())
```

**Decision R-005**: Use `Utf8JsonWriter` with `use` bindings for both stream and writer. Output indented JSON for human readability.

---

## Research Area 3: Shared AST Dependency Management

### Spec 020 Status

Spec 020 (shared statechart AST) has completed planning and defines:
- `StatechartDocument` (root AST node)
- `StateNode`, `TransitionEdge`, `DataEntry`, etc.
- `AlpsMeta` stub: `AlpsTransitionType of AlpsTransitionKind`, `AlpsDescriptorHref of string`, `AlpsExtension of name * value`
- `Annotation` DU with `AlpsAnnotation of AlpsMeta` case
- Located in `src/Frank.Statecharts/Ast/Types.fs`

### Decoupling Strategy

The ALPS parser and generator are fully self-contained:
- `Alps/Types.fs` defines the ALPS-specific AST independently
- `Alps/JsonParser.fs` and `Alps/XmlParser.fs` produce `AlpsDocument` (ALPS AST)
- `Alps/JsonGenerator.fs` consumes `AlpsDocument` (ALPS AST)
- Only `Alps/Mapper.fs` depends on the shared AST types from spec 020

**Decision R-006**: The mapper module (`Alps/Mapper.fs`) is the only file with a dependency on spec 020. All other ALPS files can be developed, tested, and merged independently. The mapper WP should be scheduled after spec 020 lands.

### AlpsMeta Enrichment

The spec 020 `AlpsMeta` stub needs to be "fleshed out" by this spec (per spec 020 data-model.md). The current stub has:
- `AlpsTransitionType of AlpsTransitionKind` -- captures safe/unsafe/idempotent
- `AlpsDescriptorHref of string` -- captures href references
- `AlpsExtension of name: string * value: string` -- captures ext elements

This is sufficient for the mapper. No changes to the `AlpsMeta` stub are needed unless the mapper reveals gaps during implementation.

**Decision R-007**: Accept the `AlpsMeta` stub as-is from spec 020. If the mapper implementation reveals missing cases, propose additions via the spec 020 amendment process.

---

## Research Area 4: Golden File Content

### Tic-Tac-Toe ALPS (from feasibility research, spec 003)

The feasibility research provides a partial ALPS JSON example. The full golden file must include all states and transitions:

States: XTurn, OTurn, Won, Draw (semantic descriptors)
Data elements: gameState, position, player (semantic descriptors)
Transitions:
- makeMove (unsafe, from XTurn to OTurn -- single rt limitation means separate descriptors for XTurn->OTurn, XTurn->Won, XTurn->Draw, OTurn->XTurn, OTurn->Won, OTurn->Draw)
- viewGame (safe, available from all states)

Guard labels: `role=PlayerX` on XTurn transitions, `role=PlayerO` on OTurn transitions, `wins` and `boardFull` conditions via ext elements.

**Decision R-008**: Create the full tic-tac-toe ALPS golden file as a JSON string constant in `test/Frank.Statecharts.Tests/Alps/GoldenFiles.fs`. Include all states, all transition-target combinations (one descriptor per rt value), guard ext elements, and documentation. Also create the equivalent XML version for cross-format testing.

### Onboarding ALPS

The onboarding example from the feasibility research represents a simpler, more linear state machine. States include Welcome, CollectEmail, CollectProfile, Review, Complete. Transitions are primarily safe (navigation) and unsafe (form submissions).

**Decision R-009**: Create the onboarding ALPS golden file following the same pattern. This provides a second validation point with different characteristics (linear vs. cyclic).

---

## Research Area 5: Error Handling Strategy

### Parse Error Categories

1. **Structural errors**: Invalid JSON/XML syntax -- caught by `JsonDocument.Parse` / `XDocument.Parse` exceptions
2. **Schema errors**: Valid JSON/XML but missing required ALPS fields (e.g., descriptor without `id`)
3. **Semantic warnings**: Valid ALPS but with issues (e.g., `href` referencing non-existent descriptor)

**Decision R-010**: Structural errors are fatal -- return `Error` immediately. Schema errors are collected and returned as a list. Semantic warnings are collected separately (logged but not blocking). This matches the WSD parser's `ParseResult` pattern with `Errors` and `Warnings`.

### Error Type Design

```fsharp
type AlpsParseError =
    { Description: string
      Position: AlpsSourcePosition option }

type AlpsSourcePosition =
    { Line: int; Column: int }
```

Position is available for XML errors (from `XmlException.LineNumber/LinePosition`) but not for JSON errors (System.Text.Json provides byte offset, not line/column). For JSON errors, position is `None`.

**Decision R-011**: Use `option` for position on parse errors. JSON structural errors provide description only. XML structural errors include line/column from the exception.

---

## Research Area 6: Compile Order and fsproj Integration

### Current Compile Order (Frank.Statecharts.fsproj)

```xml
<Compile Include="Wsd/Types.fs" />
<Compile Include="Wsd/Lexer.fs" />
<Compile Include="Wsd/GuardParser.fs" />
<Compile Include="Wsd/Parser.fs" />
<Compile Include="Types.fs" />
<Compile Include="Store.fs" />
...
```

### Required Additions

ALPS files must be added after WSD files (parallel format, no dependency between them) but before `Types.fs` if the mapper needs to reference runtime types, or after it if Types.fs doesn't depend on ALPS.

Since the ALPS types are self-contained (AD-001) and the mapper depends on spec 020's shared AST (not the current `Types.fs`), the ALPS files can be placed in any position relative to WSD. For consistency, place them after WSD:

```xml
<Compile Include="Wsd/Types.fs" />
<Compile Include="Wsd/Lexer.fs" />
<Compile Include="Wsd/GuardParser.fs" />
<Compile Include="Wsd/Parser.fs" />
<Compile Include="Alps/Types.fs" />
<Compile Include="Alps/JsonParser.fs" />
<Compile Include="Alps/XmlParser.fs" />
<Compile Include="Alps/JsonGenerator.fs" />
<Compile Include="Alps/Mapper.fs" />
<Compile Include="Types.fs" />
...
```

**Decision R-012**: Add ALPS files between WSD and Types.fs in the fsproj compile order. The mapper is last in the Alps group since it may depend on types from both Alps/Types.fs and (eventually) the shared AST.

### Test Project Additions

```xml
<Compile Include="Alps/GoldenFiles.fs" />
<Compile Include="Alps/TypeTests.fs" />
<Compile Include="Alps/JsonParserTests.fs" />
<Compile Include="Alps/XmlParserTests.fs" />
<Compile Include="Alps/JsonGeneratorTests.fs" />
<Compile Include="Alps/MapperTests.fs" />
<Compile Include="Alps/RoundTripTests.fs" />
```

**Decision R-013**: Test files follow the same pattern as WSD tests. Golden file constants are in a separate module loaded first so parser and generator tests can reference them.

---

## Decision Summary

| Decision | What | Rationale |
|----------|------|-----------|
| R-001 | Default descriptor type is `semantic` | ALPS specification requirement (FR-006) |
| R-002 | Single rt per descriptor, one descriptor per target | ALPS single-value rt limitation (D-006) |
| R-003 | JsonDocument.Parse with use binding | IDisposable discipline (constitution VI), structured errors |
| R-004 | XDocument.Parse for XML | Simple DOM, line/column errors, not IDisposable |
| R-005 | Utf8JsonWriter for generation | Zero NuGet deps (constitution III), full output control |
| R-006 | Only mapper depends on spec 020 | Enables parallel development of parser/generator |
| R-007 | Accept AlpsMeta stub as-is | Amend if mapper reveals gaps |
| R-008 | Full tic-tac-toe golden file in test module | All states, all transition-target combos, guards |
| R-009 | Onboarding golden file as second validation | Linear vs cyclic pattern coverage |
| R-010 | Structural errors fatal, schema errors collected | Matches WSD parser error pattern |
| R-011 | Optional position on parse errors | JSON has no line/column, XML does |
| R-012 | Alps files between Wsd and Types.fs in fsproj | Consistent ordering, no cross-dependencies |
| R-013 | Separate golden files module in tests | Shared across parser and generator tests |
