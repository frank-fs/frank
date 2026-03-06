# Data Model: Phase 1.1 Code Review Fixes

No new entities are introduced. This document captures type changes to existing entities.

## Modified Types

### ExtractionState (Frank.Cli.Core/State/ExtractionState.fs)

**Current**:
```
SourceMap: Dictionary<Uri, SourceLocation>
```

**Target**:
```
SourceMap: Map<string, SourceLocation>
```

**Migration**: `Uri.ToString()` as key during load of existing state files.

---

### ValidationIssue.Severity (Frank.Cli.Core/Commands/ValidateCommand.fs)

**Current**: String-typed (`"Error"`, `"Warning"`, `"Info"`)

**Target**: Evaluate DU vs static byte array based on hot-path analysis. Escalate to user if trade-off is unclear.

```fsharp
// Option A: DU (idiomatic)
type Severity = Error | Warning | Info

// Option B: Static byte array (if on hot path, per Frank.Datastar precedent)
// Determine during implementation
```

---

### DiffEntry.Type (Frank.Cli.Core/State/DiffEngine.fs)

**Current**: String-typed (`"Added"`, `"Removed"`, `"Modified"`)

**Target**: Same evaluation as ValidationIssue.Severity — DU vs performance representation.

```fsharp
// Option A: DU (idiomatic)
type DiffType = Added | Removed | Modified
```

---

### TypeAnalyzer / InstanceProjector XSD Mappings

**Current inconsistency**:
- `TypeAnalyzer.fs`: `Int64` → `xsd:long`
- `InstanceProjector.fs`: `Int64` → `xsd:integer`

**Target**: Both map `Int64` → `xsd:long`

**Current bug**:
- `TypeAnalyzer.fs`: `Decimal` → `xsd:double` (lossy)

**Target**: `Decimal` → `xsd:decimal`

---

## New Module

### UriHelpers (Frank.Cli.Core/Extraction/UriHelpers.fs)

Consolidates duplicated helpers from TypeMapper, ShapeGenerator, RouteMapper, CapabilityMapper:

| Function | Source modules | Signature |
|----------|---------------|-----------|
| `classUri` | TypeMapper, ShapeGenerator | `baseUri → typeName → string` |
| `propertyUri` | TypeMapper, ShapeGenerator | `baseUri → typeName → fieldName → string` |
| `resourceUri` | RouteMapper, CapabilityMapper | `baseUri → routeTemplate → string` |
| `routeToSlug` | RouteMapper, CapabilityMapper | `routeTemplate → string` |
| `fieldKindToRange` | TypeMapper, ShapeGenerator | `FieldKind → string * bool` |

### RdfHelpers (Frank.LinkedData — location TBD)

Consolidates duplicated helpers from JsonLdFormatter, InstanceProjector, WebHostBuilderExtensions:

| Function | Source modules | Signature |
|----------|---------------|-----------|
| `localName` | JsonLdFormatter, InstanceProjector | `uri: string → string` |
| `namespaceUri` | JsonLdFormatter, WebHostBuilderExtensions | `uri: string → string` |

---

## Affected Interfaces

### InstanceProjector Cache

**Current**: `RuntimeHelpers.GetHashCode(obj)` as dictionary key
**Target**: Structural hash of RDF-relevant properties

### negotiateRdfType

**Current**: `accept.Contains("application/ld+json")` etc.
**Target**: `MediaTypeHeaderValue.ParseList` with proper quality factor handling

### LinkedData Middleware

**Current**: `with | _ -> ()` catch-all
**Target**: `ILogger` injection, logged exception with severity + context, fail-fast for unrecoverable errors

### FSharpRdf Module

**Current**: `[<AutoOpen>] module FSharpRdf`
**Target**: Remove `[<AutoOpen>]`; all consumers add explicit `open Frank.Cli.Core.Rdf.FSharpRdf`
