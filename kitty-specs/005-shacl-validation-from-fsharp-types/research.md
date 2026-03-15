# Research: Frank.Validation -- SHACL Shape Validation from F# Types

**Feature**: 005-shacl-validation-from-fsharp-types
**Date**: 2026-03-07

## Decision 1: SHACL Shape Derivation Strategy

### Question

How should SHACL NodeShapes be derived from F# types -- via .NET reflection at startup, or via FSharp.Compiler.Service at compile time?

### Decision: .NET Reflection at Startup

**Rationale**: .NET reflection on compiled types provides sufficient metadata for shape derivation without introducing a heavyweight compile-time dependency.

**Evidence**:

1. `FSharp.Reflection.FSharpType` exposes:
   - `IsRecord`, `GetRecordFields` -- field names, types, and ordering
   - `IsUnion`, `GetUnionCases` -- case names and case field types
   - `IsModule`, `IsTuple` -- structural type identification

2. `System.Reflection.PropertyInfo` provides:
   - `PropertyType` -- the CLR type of each field
   - Custom attributes (if developers annotate types)
   - `IsGenericType` / `GetGenericArguments()` -- handles `option<T>`, `list<T>`, etc.

3. FSharp.Compiler.Service would provide richer metadata (doc comments, source ranges) but:
   - Adds ~30MB dependency
   - Requires access to source files at startup (not available in published apps)
   - Significantly increases startup time
   - Would violate Constitution III (Library, Not Framework -- excessive dependency for a validation library)

**Alternatives rejected**:
- **FSharp.Compiler.Service**: Too heavy, requires source files at runtime, startup cost unacceptable.
- **Source generators**: F# does not yet support source generators (as of .NET 10). Would require C# interop layer, violating Constitution II (Idiomatic F#).
- **Myriad/type providers**: Myriad generates F# source at build time but produces code, not SHACL graphs. Type providers run at compile time but are for consuming data, not producing metadata.

### Implementation Approach

Shape derivation runs once at application startup via `IHostedService` or on first request. The derivation pipeline:

```
System.Type
  |-> FSharpType.IsRecord? -> GetRecordFields -> PropertyShape per field
  |-> FSharpType.IsUnion?  -> GetUnionCases   -> sh:in or sh:or constraints
  |-> IsGenericType?       -> expand generic parameters
  |-> recursive? -> detect cycles, cap at max depth
  |-> produce ShaclShape (NodeShape + PropertyShapes)
  |-> cache in ConcurrentDictionary<Type, ShaclShape>
```

---

## Decision 2: dotNetRdf.Core SHACL Validation API Usage

### Question

How does dotNetRdf.Core expose SHACL validation, and how should Frank.Validation invoke it?

### Decision: Use VDS.RDF.Shacl.ShapesGraph for Validation

**Rationale**: dotNetRdf.Core (already a dependency of Frank.LinkedData) includes a SHACL validation engine in the `VDS.RDF.Shacl` namespace.

**API surface**:

1. Construct a shapes graph (`IGraph`) containing the derived SHACL shapes
2. Construct a data graph (`IGraph`) from the deserialized request data
3. Create a `ShapesGraph` from the shapes graph
4. Call `shapesGraph.Validate(dataGraph)` to get a `Report` (SHACL ValidationReport)
5. The `Report` exposes `Conforms` (bool) and `Results` (collection of `Result` objects)

**Key types**:
- `VDS.RDF.Shacl.ShapesGraph` -- wraps an `IGraph` of SHACL shapes
- `VDS.RDF.Shacl.Validation.Report` -- the SHACL ValidationReport
- `VDS.RDF.Shacl.Validation.Result` -- individual ValidationResult entries

**Lifecycle**: The `ShapesGraph` is constructed once at startup from the derived shapes and reused for all validations. Only the data graph is constructed per-request.

**Alternatives rejected**:
- **TopQuadrant SHACL (Java)**: Not .NET-native. Would require interop or a separate process.
- **Hand-rolled validation**: Would duplicate the SHACL specification logic. dotNetRdf already implements the W3C SHACL Core spec.
- **JSON Schema validation with SHACL output mapping**: Loses semantic fidelity. The point is native SHACL validation for semantic clients.

---

## Decision 3: F# Type to XSD Datatype Mapping

### Question

How should F# types map to XSD datatypes in derived SHACL property shapes?

### Decision: Static Mapping Table with Extension Point

**Mapping table**:

| F# Type | XSD Datatype | SHACL Property |
|---------|-------------|----------------|
| `string` | `xsd:string` | `sh:datatype` |
| `int` / `int32` | `xsd:integer` | `sh:datatype` |
| `int64` | `xsd:long` | `sh:datatype` |
| `float` / `double` | `xsd:double` | `sh:datatype` |
| `decimal` | `xsd:decimal` | `sh:datatype` |
| `bool` | `xsd:boolean` | `sh:datatype` |
| `DateTimeOffset` | `xsd:dateTimeStamp` | `sh:datatype` |
| `DateTime` | `xsd:dateTime` | `sh:datatype` |
| `DateOnly` | `xsd:date` | `sh:datatype` |
| `TimeOnly` | `xsd:time` | `sh:datatype` |
| `TimeSpan` | `xsd:duration` | `sh:datatype` |
| `Guid` | `xsd:string` (with pattern) | `sh:datatype` + `sh:pattern` |
| `Uri` | `xsd:anyURI` | `sh:datatype` |
| `byte[]` | `xsd:base64Binary` | `sh:datatype` |

**Option types**: `option<T>` unwraps to `T`'s mapping with `sh:minCount 0` (vs `sh:minCount 1` for required).

**Collection types**: `list<T>`, `T[]`, `seq<T>`, `ResizeArray<T>` produce `sh:node` pointing to a list shape, with the item type determining the element constraint.

**DU types (no payload)**: Produce `sh:in` listing case names as `xsd:string` values.

**DU types (with payload)**: Produce `sh:or` with one `sh:node` per case, each case's NodeShape derived from its fields.

**Nested records**: Produce `sh:node` referencing the nested type's independently derived NodeShape.

**Extension point**: A `TypeMappingOverride` function (`Type -> XsdDatatype option`) allows developers to register custom mappings for domain types not in the default table.

---

## Decision 4: ValidationReport Serialization Approach

### Question

How should SHACL ValidationReports be serialized for different client types?

### Decision: Dual-Path Serialization via Content Negotiation

**Path 1 -- Semantic clients** (`Accept: application/ld+json`, `text/turtle`, `application/rdf+xml`):
- The dotNetRdf `Report` object produces an `IGraph` containing the SHACL ValidationReport triples
- This graph is handed to Frank.LinkedData's existing content negotiation infrastructure
- Frank.LinkedData serializes it in the requested RDF format
- No custom serialization code needed -- LinkedData already handles `IGraph` -> RDF format

**Path 2 -- Standard clients** (`Accept: application/json` or no semantic Accept header):
- The `Report` is mapped to an RFC 9457 Problem Details structure:
  ```json
  {
    "type": "urn:frank:validation:shacl-violation",
    "title": "Validation Failed",
    "status": 422,
    "detail": "Request body violates 3 SHACL constraints",
    "errors": [
      {
        "path": "$.name",
        "constraint": "sh:minCount",
        "message": "Field 'name' is required (sh:minCount 1)",
        "value": null
      }
    ]
  }
  ```
- The `errors` array contains one entry per `ValidationResult`, with the field path, constraint type, message, and offending value

**Decision rationale**: Semantic clients get native SHACL ValidationReports (the whole point of the library). Non-semantic clients get standard Problem Details (familiar, tooling-friendly). Both paths share the same underlying violation data; only serialization differs.

**Alternatives rejected**:
- **SHACL-only**: Would alienate non-semantic clients. Most API consumers expect JSON error responses.
- **Problem Details-only**: Would defeat the purpose of SHACL validation for semantic web consumers.
- **Custom error format**: Would require clients to learn a Frank-specific format. Both SHACL and Problem Details are standards.

---

## Decision 5: Pipeline Integration Pattern

### Question

Should validation be implemented as ASP.NET Core middleware or as an endpoint filter?

### Decision: Middleware (Consistent with Frank.LinkedData and Frank.Auth)

**Rationale**: Frank's extension pattern uses middleware + endpoint metadata markers. Frank.LinkedData uses `LinkedDataMarker` + `useLinkedData` middleware. Frank.Auth uses `AuthRequirement` metadata + ASP.NET Core's built-in auth middleware. Frank.Validation follows the same pattern.

**Pattern**:
1. `validate` CE custom operation adds `ValidationMarker` to endpoint metadata (contains the derived `ShaclShape` reference and configuration)
2. `useValidation` registers `ValidationMiddleware` in the pipeline
3. Middleware checks endpoint metadata for `ValidationMarker`
4. If present: deserialize request -> construct data graph -> validate against shape -> pass through or short-circuit with 422
5. If absent: pass through to next middleware (zero overhead)

**Pipeline ordering**: `useAuth` -> `useValidation` -> handler dispatch. This ensures:
- Unauthenticated requests are rejected before validation (no wasted work)
- Authorized but invalid requests are rejected before the handler sees them
- The shape resolver can access the authenticated principal for capability-dependent shapes

**Alternatives rejected**:
- **Endpoint filters** (ASP.NET Core 7+): Would work technically but breaks consistency with Frank's established extension pattern. All existing Frank extensions use middleware + metadata markers.
- **Action filters**: Frank doesn't use MVC controllers, so action filters don't apply.
- **Inside the handler**: Would require every handler to call validation manually, defeating the purpose of declarative validation.

---

## Decision 6: Recursive Type Handling

### Question

How should recursive/self-referential F# types be handled during shape derivation?

### Decision: Cycle Detection with Configurable Depth Limit

**Approach**:
1. Maintain a `Set<Type>` of types currently being derived (the "derivation stack")
2. When encountering a type already on the stack, emit an `sh:node` reference to the existing NodeShape URI (breaking the cycle)
3. Apply a configurable maximum derivation depth (default 5) as a safety net for deeply nested but non-recursive types
4. At depth limit, emit `sh:nodeKind sh:BlankNodeOrIRI` (accept any node) with a warning logged via `ILogger`

**Example**: For `type TreeNode = { Value: string; Children: TreeNode list }`:
- `TreeNode` NodeShape has `sh:property` for `Value` (xsd:string) and `Children`
- `Children` property has `sh:node` pointing back to `TreeNode`'s NodeShape URI
- No infinite expansion

**Configuration**: `ValidationOptions.MaxDerivationDepth` (default 5), settable via `useValidation` configuration callback.
