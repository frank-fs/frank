# Data Model: ALPS Shared AST Migration

**Feature**: 023-alps-shared-ast-migration
**Date**: 2026-03-16

## Entity Changes

### Modified Entity: `AlpsMeta` (in `src/Frank.Statecharts/Ast/Types.fs`)

#### Current Shape

```fsharp
type AlpsMeta =
    | AlpsTransitionType of AlpsTransitionKind  // Safe | Unsafe | Idempotent
    | AlpsDescriptorHref of string              // href-only reference preservation
    | AlpsExtension of name: string * value: string  // ext element (name/value pair)
```

#### Target Shape

```fsharp
type AlpsMeta =
    | AlpsTransitionType of AlpsTransitionKind
    | AlpsDescriptorHref of string
    | AlpsExtension of id: string * href: string option * value: string option
    | AlpsDocumentation of format: string option * value: string
    | AlpsLink of rel: string * href: string
    | AlpsDataDescriptor of id: string * doc: (string option * string) option
    | AlpsVersion of string
```

#### Field-by-Field Specification

| Case | Fields | Source in ALPS JSON | Placed On |
|------|--------|-------------------|-----------|
| `AlpsTransitionType` | `AlpsTransitionKind` | `descriptor.type` (safe/unsafe/idempotent) | `TransitionEdge.Annotations` |
| `AlpsDescriptorHref` | `string` | `descriptor.href` (href-only reference) | `TransitionEdge.Annotations` |
| `AlpsExtension` | `id: string * href: string option * value: string option` | `ext` element | `StateNode.Annotations`, `TransitionEdge.Annotations`, `StatechartDocument.Annotations` |
| `AlpsDocumentation` | `format: string option * value: string` | `doc` element | `StateNode.Annotations`, `TransitionEdge.Annotations`, `StatechartDocument.Annotations` |
| `AlpsLink` | `rel: string * href: string` | `link` element | `StateNode.Annotations`, `StatechartDocument.Annotations` |
| `AlpsDataDescriptor` | `id: string * doc: (string option * string) option` | Top-level semantic descriptors not classified as states | `StatechartDocument.Annotations` |
| `AlpsVersion` | `string` | `alps.version` | `StatechartDocument.Annotations` |

### Breaking Change: `AlpsExtension`

**Before**: `AlpsExtension of name: string * value: string`
**After**: `AlpsExtension of id: string * href: string option * value: string option`

Changes:
1. Field `name` renamed to `id` (matches ALPS JSON attribute name)
2. New field `href: string option` added (was lost in current shape)
3. Field `value` changed from `string` to `string option` (ALPS ext elements can have href without value)

**Impact**: All pattern matches on `AlpsExtension(name, value)` must change to `AlpsExtension(id, href, value)`. Since `AlpsMeta` is in an `internal` module, no external consumers are affected.

### Deleted Entities (in `src/Frank.Statecharts/Alps/Types.fs`)

All of these types are deleted after migration:

| Type | Replacement |
|------|-------------|
| `AlpsSourcePosition` | `SourcePosition` (already in `Ast/Types.fs`) |
| `AlpsParseError` | `ParseFailure` (already in `Ast/Types.fs`) |
| `AlpsDocumentation` | `AlpsMeta.AlpsDocumentation` case |
| `AlpsExtension` | `AlpsMeta.AlpsExtension` case (expanded) |
| `AlpsLink` | `AlpsMeta.AlpsLink` case |
| `DescriptorType` | No direct replacement; `AlpsTransitionKind` covers transition types; semantic is implicit |
| `Descriptor` | `StateNode` + `TransitionEdge` + annotations |
| `AlpsDocument` | `StatechartDocument` |

### Intermediate Parser Type (private to JsonParser.fs)

A private descriptor record is used within `JsonParser.fs` for the JSON-to-record parsing pass. This is NOT exposed and exists only to keep JSON parsing clean before the classification pass.

```fsharp
/// Private intermediate type for JSON parsing pass.
type private ParsedDescriptor =
    { Id: string option
      Type: string option   // raw string, not DU
      Href: string option
      ReturnType: string option
      DocFormat: string option
      DocValue: string option
      Children: ParsedDescriptor list
      Extensions: ParsedExtension list
      Links: ParsedLink list }

and private ParsedExtension =
    { Id: string
      Href: string option
      Value: string option }

and private ParsedLink =
    { Rel: string
      Href: string }
```

These mirror the current `Descriptor`/`AlpsExtension`/`AlpsLink` shapes but are private and scoped to the parser module.

## Annotation Ordering Rules

For deterministic structural equality in roundtrip tests:

### On `TransitionEdge.Annotations`
1. `AlpsAnnotation(AlpsTransitionType _)` -- always first
2. `AlpsAnnotation(AlpsDescriptorHref _)` -- if present
3. `AlpsAnnotation(AlpsDocumentation _)` -- if present
4. `AlpsAnnotation(AlpsExtension _)` -- in document order (excluding guards, which are in `TransitionEdge.Guard`)

### On `StateNode.Annotations`
1. `AlpsAnnotation(AlpsDocumentation _)` -- if present
2. `AlpsAnnotation(AlpsExtension _)` -- in document order
3. `AlpsAnnotation(AlpsLink _)` -- in document order

### On `StatechartDocument.Annotations`
1. `AlpsAnnotation(AlpsVersion _)` -- if present
2. `AlpsAnnotation(AlpsDocumentation _)` -- if present
3. `AlpsAnnotation(AlpsLink _)` -- in document order
4. `AlpsAnnotation(AlpsExtension _)` -- in document order
5. `AlpsAnnotation(AlpsDataDescriptor _)` -- in document order

## State Classification Heuristic (from Mapper.fs)

A top-level semantic descriptor is classified as a **state** if ANY of:
1. It contains children with transition-type (`safe`, `unsafe`, `idempotent`)
2. Its `id` is in the set of `rt` targets (resolved from all descriptors recursively)
3. It contains children that are href-only references (`href` present, `id` absent)

Everything else is a **data descriptor** (preserved as `AlpsDataDescriptor` annotation).
