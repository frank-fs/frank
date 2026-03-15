# Data Model: ALPS Parser and Generator (Spec 011)

**Date**: 2026-03-15
**Status**: Complete
**Derived From**: spec.md (key entities), research.md decisions R-001 through R-013

---

## Entity Relationship Overview

```
AlpsDocument (root)
  |-- Version: string option
  |-- Documentation: AlpsDocumentation option
  |-- Descriptors: Descriptor list (ordered)
  |-- Links: AlpsLink list
  |-- Extensions: AlpsExtension list
  |
  +-- Descriptor
       |-- Id: string option (required for inline, absent for href-only)
       |-- Type: DescriptorType (defaults to Semantic)
       |-- Href: string option (local fragment or external URL)
       |-- ReturnType: string option (rt value)
       |-- Documentation: AlpsDocumentation option
       |-- Descriptors: Descriptor list (nested children)
       |-- Extensions: AlpsExtension list
       |-- Links: AlpsLink list
```

```
AlpsParseResult = Result<AlpsDocument, AlpsParseError list>
```

---

## Core Entities

### AlpsDocument (FR-001, FR-002)

The root AST node representing a complete parsed ALPS document. Identical structure produced by both JSON and XML parsers.

| Field | Type | Required | Description |
|-------|------|----------|-------------|
| Version | `string option` | No | ALPS version string (e.g., `"1.0"`) |
| Documentation | `AlpsDocumentation option` | No | Top-level `doc` element |
| Descriptors | `Descriptor list` | Yes (may be empty) | Ordered list of top-level descriptors |
| Links | `AlpsLink list` | Yes (may be empty) | Top-level `link` elements |
| Extensions | `AlpsExtension list` | Yes (may be empty) | Top-level `ext` elements |

**Structural Equality**: Yes (all fields are value types or immutable lists)
**Notes**: An empty document (no descriptors) is valid per edge case specification.

---

### Descriptor (FR-001, FR-003, FR-005, FR-006)

The core ALPS element representing a semantic descriptor or transition.

| Field | Type | Required | Description |
|-------|------|----------|-------------|
| Id | `string option` | Conditional | Unique identifier (required for inline, absent for href-only references) |
| Type | `DescriptorType` | Yes | Descriptor classification (defaults to `Semantic` if omitted in source) |
| Href | `string option` | No | Reference to another descriptor (fragment `#id` or external URL) |
| ReturnType | `string option` | No | `rt` value -- target descriptor for transitions |
| Documentation | `AlpsDocumentation option` | No | Descriptor-level `doc` element |
| Descriptors | `Descriptor list` | Yes (may be empty) | Nested child descriptors |
| Extensions | `AlpsExtension list` | Yes (may be empty) | Extension elements (guards, metadata) |
| Links | `AlpsLink list` | Yes (may be empty) | Descriptor-level `link` elements |

**Structural Equality**: Yes
**Self-referential**: `Descriptors` field contains nested `Descriptor list` for hierarchy
**Default Type**: If `type` is omitted in the source document, defaults to `Semantic` (per ALPS spec, R-001)

---

### AlpsDocumentation (FR-007)

ALPS `doc` element with optional format and text content.

| Field | Type | Required | Description |
|-------|------|----------|-------------|
| Format | `string option` | No | Documentation format (e.g., `"text"`, `"html"`). Defaults to `"text"` per ALPS spec. |
| Value | `string` | Yes | Documentation text content |

**Structural Equality**: Yes

---

### AlpsExtension (FR-004)

ALPS `ext` element for format extensions (used for guard labels and other metadata).

| Field | Type | Required | Description |
|-------|------|----------|-------------|
| Id | `string` | Yes | Extension identifier (e.g., `"guard"`) |
| Href | `string option` | No | Reference URL for extension definition |
| Value | `string option` | No | Extension value content (e.g., `"role=PlayerX"`) |

**Structural Equality**: Yes
**Notes**: Guard labels are extracted by the mapper from extensions with `id = "guard"` (FR-012).

---

### AlpsLink (FR-008)

ALPS `link` element for relation links.

| Field | Type | Required | Description |
|-------|------|----------|-------------|
| Rel | `string` | Yes | Relation type (e.g., `"self"`, `"help"`, `"profile"`) |
| Href | `string` | Yes | Target URL |

**Structural Equality**: Yes

---

## Discriminated Unions

### DescriptorType (FR-003)

```fsharp
type DescriptorType =
    | Semantic
    | Safe
    | Unsafe
    | Idempotent
```

| Case | ALPS String | HTTP Method Hint | Description |
|------|-------------|-----------------|-------------|
| Semantic | `"semantic"` | N/A | Data element descriptor |
| Safe | `"safe"` | GET | Read-only transition |
| Unsafe | `"unsafe"` | POST | State-modifying transition |
| Idempotent | `"idempotent"` | PUT (caveat: DELETE also idempotent) | Repeatable state-modifying transition |

---

## Error Types

### AlpsParseError (FR-016)

```fsharp
type AlpsParseError =
    { Description: string
      Position: AlpsSourcePosition option }
```

| Field | Type | Required | Description |
|-------|------|----------|-------------|
| Description | `string` | Yes | Human-readable error description |
| Position | `AlpsSourcePosition option` | No | Source position (available for XML, unavailable for JSON) |

---

### AlpsSourcePosition

```fsharp
[<Struct>]
type AlpsSourcePosition =
    { Line: int
      Column: int }
```

1-based line and column. Available from XML parse errors (`XmlException.LineNumber/LinePosition`). Not available from JSON parse errors (System.Text.Json provides byte offset only).

---

## Function Signatures

### Parsers

```fsharp
// Alps/JsonParser.fs
module Frank.Statecharts.Alps.JsonParser

val parseAlpsJson : json: string -> Result<AlpsDocument, AlpsParseError list>
```

```fsharp
// Alps/XmlParser.fs
module Frank.Statecharts.Alps.XmlParser

val parseAlpsXml : xml: string -> Result<AlpsDocument, AlpsParseError list>
```

### Generator

```fsharp
// Alps/JsonGenerator.fs
module Frank.Statecharts.Alps.JsonGenerator

val generateAlpsJson : doc: AlpsDocument -> string
```

### Mapper (blocked on spec 020)

```fsharp
// Alps/Mapper.fs
module Frank.Statecharts.Alps.Mapper

/// Map ALPS AST to shared statechart AST (StatechartDocument from spec 020)
val toStatechartDocument : doc: AlpsDocument -> StatechartDocument

/// Map shared statechart AST back to ALPS AST
val fromStatechartDocument : doc: StatechartDocument -> AlpsDocument
```

---

## Mapping Rules (ALPS AST to Shared Statechart AST)

These rules define how the mapper converts between the ALPS-specific AST and the shared statechart AST from spec 020.

### Descriptor to StateNode

| ALPS Concept | Shared AST Field | Rule |
|-------------|------------------|------|
| Semantic descriptor (containing transition hrefs) | `StateNode.Identifier` | Descriptor `id` becomes state identifier |
| Semantic descriptor | `StateNode.Kind` | Always `Regular` (ALPS has no state type classification) |
| Nested semantic descriptors | `StateNode.Children` | Nested descriptors become child states |
| Descriptor doc | Via annotations or ignored | Documentation preserved if annotation supports it |

### Transition Descriptor to TransitionEdge

| ALPS Concept | Shared AST Field | Rule |
|-------------|------------------|------|
| Transition descriptor id | `TransitionEdge.Event` | Descriptor `id` becomes event name |
| Parent semantic descriptor | `TransitionEdge.Source` | Containing semantic descriptor `id` becomes source state |
| `rt` value | `TransitionEdge.Target` | Return type reference (strip `#` prefix) becomes target state |
| Descriptor type | `TransitionEdge.Annotations` | Becomes `AlpsAnnotation(AlpsTransitionType ...)` |
| `ext` with id="guard" | `TransitionEdge.Guard` | Extension value becomes guard expression |
| Nested parameter descriptors | `TransitionEdge.Parameters` | Parameter descriptor `id` values become parameter names |

### HTTP Method Mapping (FR-010)

| DescriptorType | HTTP Method | Notes |
|---------------|-------------|-------|
| Safe | GET | Read-only |
| Unsafe | POST | State-modifying |
| Idempotent | PUT | Caveat: DELETE is also idempotent (D-006) |
| Semantic | N/A | Not a transition |

---

## File Structure

```
src/Frank.Statecharts/
  Alps/
    Types.fs          -- All ALPS-specific AST types (AlpsDocument, Descriptor, etc.)
    JsonParser.fs     -- ALPS JSON parser (produces AlpsDocument)
    XmlParser.fs      -- ALPS XML parser (produces AlpsDocument)
    JsonGenerator.fs  -- ALPS JSON generator (consumes AlpsDocument)
    Mapper.fs         -- Bidirectional mapper to/from shared AST (blocked on spec 020)

test/Frank.Statecharts.Tests/
  Alps/
    GoldenFiles.fs        -- JSON and XML golden file string constants
    TypeTests.fs          -- AST construction, equality, default type tests
    JsonParserTests.fs    -- JSON parsing including golden files and edge cases
    XmlParserTests.fs     -- XML parsing including golden files and cross-format equivalence
    JsonGeneratorTests.fs -- Generation including golden files and well-formedness
    MapperTests.fs        -- Mapper tests (blocked on spec 020)
    RoundTripTests.fs     -- Parse -> generate -> re-parse consistency
```

The `Frank.Statecharts.fsproj` compile order must list `Alps/Types.fs` before all other `Alps/*.fs` files. The `Alps/Mapper.fs` file is listed last in the Alps group.
