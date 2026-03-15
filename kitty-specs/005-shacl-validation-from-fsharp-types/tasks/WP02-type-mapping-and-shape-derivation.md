---
work_package_id: "WP02"
title: "Type Mapping & Shape Derivation Engine"
lane: "doing"
dependencies: ["WP01"]
requirement_refs: ["FR-001", "FR-002", "FR-003", "FR-004", "FR-005", "FR-006", "FR-007", "FR-017", "FR-020"]
subtasks: ["T006", "T007", "T008", "T009", "T010", "T011", "T012", "T013", "T014", "T014b"]
agent: "claude-opus-reviewer"
shell_pid: "40749"
history:
  - timestamp: "2026-03-07T00:00:00Z"
    lane: "planned"
    agent: "system"
    shell_pid: ""
    action: "Prompt generated via /spec-kitty.tasks"
---

# Work Package Prompt: WP02 -- Type Mapping & Shape Derivation Engine

## Review Feedback Status

**Read this first if you are implementing this task!**

- **Has review feedback?**: Check the `review_status` field above. If it says `has_feedback`, scroll to the **Review Feedback** section immediately.
- **You must address all feedback** before your work is complete.
- **Mark as acknowledged**: When you understand the feedback and begin addressing it, update `review_status: acknowledged` in the frontmatter.

---

## Review Feedback

> **Populated by `/spec-kitty.review`** -- Reviewers add detailed feedback here when work needs changes.

*[This section is empty initially.]*

---

## Implementation Command

```bash
spec-kitty implement WP02 --base WP01
```

Depends on WP01 (core types).

---

## Objectives & Success Criteria

- Implement `TypeMapping.fs`: map all supported F# CLR types to XSD datatypes (FR-001)
- Implement `ShapeDerivation.fs`: derive SHACL NodeShapes from F# record types and DUs via .NET reflection
- Handle records (FR-001), option types (FR-002), simple DUs (FR-003), nested records (FR-004), recursive types (FR-005), payload DUs (FR-006), and generic types (FR-007)
- Exclude framework types (HttpContext, HttpRequest, CancellationToken) from derivation (FR-017)
- All derived shapes pass W3C SHACL syntax validation (SC-001)
- Tests cover every mapping and derivation scenario

---

## Context & Constraints

**Reference documents**:
- `kitty-specs/005-shacl-validation-from-fsharp-types/research.md` -- Decision 1 (reflection strategy), Decision 3 (type mapping table)
- `kitty-specs/005-shacl-validation-from-fsharp-types/data-model.md` -- ShapeDerivation function signatures
- `kitty-specs/005-shacl-validation-from-fsharp-types/spec.md` -- FR-001 through FR-007, edge cases

**Key constraints**:
- .NET reflection only (no FSharp.Compiler.Service dependency)
- Use `FSharp.Reflection.FSharpType` for IsRecord/GetRecordFields, IsUnion/GetUnionCases
- Recursive type derivation capped at configurable depth (default 5)
- Guid auto-derives `sh:pattern` for UUID format
- NodeShape URIs follow pattern: `urn:frank:shape:{TypeFullName}`
- Property path URIs follow pattern: `urn:frank:property:{FieldName}`

---

## Subtasks & Detailed Guidance

### Subtask T006 -- Create `TypeMapping.fs`

**Implementation note**: NodeShape URIs follow the pattern `urn:frank:shape:{assembly-name}:{type-full-name}`. Generic type parameters are expanded at point of use (e.g., `PagedResult<Customer>` becomes `urn:frank:shape:MyApp:MyApp.PagedResult_MyApp.Customer`). Type names are URL-encoded to handle special characters.

**Purpose**: Define the static mapping from F# CLR types to XSD datatypes. This is a pure function used by shape derivation.

**Steps**:
1. Create `src/Frank.Validation/TypeMapping.fs`
2. Implement the mapping table from research.md Decision 3:

```fsharp
namespace Frank.Validation

open System

module TypeMapping =
    /// Map an F# CLR type to its XSD datatype. Returns None for types
    /// that require sh:node references (records, collections) rather than sh:datatype.
    let mapType (typ: Type) : XsdDatatype option =
        match typ with
        | t when t = typeof<string> -> Some XsdString
        | t when t = typeof<int> || t = typeof<int32> -> Some XsdInteger
        | t when t = typeof<int64> -> Some XsdLong
        | t when t = typeof<float> || t = typeof<double> -> Some XsdDouble
        | t when t = typeof<decimal> -> Some XsdDecimal
        | t when t = typeof<bool> -> Some XsdBoolean
        | t when t = typeof<DateTimeOffset> -> Some XsdDateTimeStamp
        | t when t = typeof<DateTime> -> Some XsdDateTime
        | t when t = typeof<DateOnly> -> Some XsdDate
        | t when t = typeof<TimeOnly> -> Some XsdTime
        | t when t = typeof<TimeSpan> -> Some XsdDuration
        | t when t = typeof<Uri> -> Some XsdAnyUri
        | t when t = typeof<byte[]> -> Some XsdBase64Binary
        | t when t = typeof<Guid> -> Some XsdString // + pattern constraint added by derivation
        | _ -> None

    /// Get the XSD URI string for a datatype.
    let xsdUri (dt: XsdDatatype) : Uri =
        let xsd = "http://www.w3.org/2001/XMLSchema#"
        match dt with
        | XsdString -> Uri(xsd + "string")
        | XsdInteger -> Uri(xsd + "integer")
        | XsdLong -> Uri(xsd + "long")
        | XsdDouble -> Uri(xsd + "double")
        | XsdDecimal -> Uri(xsd + "decimal")
        | XsdBoolean -> Uri(xsd + "boolean")
        | XsdDateTimeStamp -> Uri(xsd + "dateTimeStamp")
        | XsdDateTime -> Uri(xsd + "dateTime")
        | XsdDate -> Uri(xsd + "date")
        | XsdTime -> Uri(xsd + "time")
        | XsdDuration -> Uri(xsd + "duration")
        | XsdAnyUri -> Uri(xsd + "anyURI")
        | XsdBase64Binary -> Uri(xsd + "base64Binary")
        | Custom uri -> uri
```

3. Add `TypeMapping.fs` to the `<Compile>` list in `Frank.Validation.fsproj` after `Constraints.fs`.

**Files**: `src/Frank.Validation/TypeMapping.fs`
**Notes**: `Guid` maps to `XsdString` here but shape derivation adds an automatic `sh:pattern` constraint for UUID format. The `mapType` function returns `None` for complex types (records, DUs) that need `sh:node` references.

### Subtask T007 -- Implement record field derivation

**Purpose**: Convert a single F# record field (`PropertyInfo`) into a `PropertyShape` with appropriate datatype and cardinality.

**Steps**:
1. In `src/Frank.Validation/ShapeDerivation.fs`, implement `deriveProperty`:

```fsharp
module ShapeDerivation =
    let deriveProperty (maxDepth: int) (stack: Set<Type>) (field: PropertyInfo) : PropertyShape =
        let fieldType = field.PropertyType
        // 1. Check if option type -> unwrap inner type, set minCount 0
        // 2. Check if collection type -> set maxCount None, derive element type
        // 3. Map to XSD datatype via TypeMapping.mapType
        // 4. If no XSD mapping, check if derivable record/DU -> sh:node reference
        // 5. Handle Guid: add pattern constraint for UUID format
```

2. The function must handle:
   - Required fields: `minCount = 1`, `maxCount = Some 1`
   - Option fields: `minCount = 0`, `maxCount = Some 1`, inner type for datatype
   - Collection fields (list, array, seq): `minCount = 0`, `maxCount = None`
   - Primitive types: `Datatype = Some xsdType`, `NodeReference = None`
   - Record types: `Datatype = None`, `NodeReference = Some nodeShapeUri`

**Files**: `src/Frank.Validation/ShapeDerivation.fs`
**Notes**: The `stack` parameter is for cycle detection (T011). Pass it through but cycle detection logic is implemented in T011.

### Subtask T008 -- Implement option type unwrapping

**Purpose**: Handle `option<T>` types: unwrap to the inner type, set `minCount = 0`.

**Steps**:
1. In `ShapeDerivation.fs`, add helper to detect and unwrap option types:

```fsharp
let isOptionType (t: Type) =
    t.IsGenericType && t.GetGenericTypeDefinition() = typedefof<option<_>>

let unwrapOptionType (t: Type) =
    if isOptionType t then
        Some (t.GetGenericArguments().[0])
    else
        None
```

2. Integrate into `deriveProperty`: if the field type is `option<T>`, derive from `T` but set `minCount = 0`.
3. Handle nested options (`option<option<T>>`) by unwrapping once -- inner option is treated as a regular type.

**Files**: `src/Frank.Validation/ShapeDerivation.fs`
**Notes**: `option<T>` in F# compiles to `FSharpOption<T>` in CLR. Use `typedefof<option<_>>` for comparison.

### Subtask T009 -- Implement DU derivation

**Purpose**: Derive SHACL constraints from F# discriminated unions. Simple DUs (no payload) produce `sh:in`; payload DUs produce `sh:or` with per-case NodeShapes. (FR-003, FR-006)

**Steps**:
1. In `ShapeDerivation.fs`, add DU handling:

```fsharp
let deriveDuConstraint (maxDepth: int) (stack: Set<Type>) (duType: Type) =
    let cases = FSharpType.GetUnionCases(duType)
    let allSimple = cases |> Array.forAll (fun c -> c.GetFields().Length = 0)
    if allSimple then
        // Simple DU: sh:in with case names as string values
        InValues (cases |> Array.map (fun c -> c.Name) |> Array.toList)
    else
        // Payload DU: sh:or with per-case NodeShapes
        let caseUris = cases |> Array.map (fun c ->
            let caseShape = deriveCaseShape maxDepth stack c
            caseShape.NodeShapeUri
        ) |> Array.toList
        OrShapes caseUris
```

2. For payload DU cases, each case becomes a NodeShape with properties for its fields. The case name is a discriminator field.
3. Store derived case shapes in the shape cache for reuse.

**Files**: `src/Frank.Validation/ShapeDerivation.fs`
**Notes**: `FSharpType.GetUnionCases` returns `UnionCaseInfo[]`. Each `UnionCaseInfo` has `Name` and `GetFields()` returning `PropertyInfo[]`. For mixed DUs (some cases with payloads, some without), treat all as payload DUs with `sh:or`.

### Subtask T010 -- Implement nested record handling

**Purpose**: When a record field's type is another record, produce an `sh:node` reference to the nested type's independently derived NodeShape. (FR-004)

**Steps**:
1. In `ShapeDerivation.fs`, when `deriveProperty` encounters a field whose type is a record:
   - Recursively call `deriveShape` for the nested type
   - Set `NodeReference = Some nestedShape.NodeShapeUri` on the PropertyShape
   - Store the nested shape in the cache
2. The `deriveShape` function orchestrates the full derivation:

```fsharp
let rec deriveShape (maxDepth: int) (stack: Set<Type>) (typ: Type) : ShaclShape =
    let uri = Uri(sprintf "urn:frank:shape:%s" typ.FullName)
    if FSharpType.IsRecord typ then
        let fields = FSharpType.GetRecordFields typ
        let properties = fields |> Array.map (deriveProperty maxDepth stack) |> Array.toList
        { TargetType = typ
          NodeShapeUri = uri
          Properties = properties
          Closed = true
          Description = None }
    elif FSharpType.IsUnion typ then
        // DU handling (T009)
        ...
    else
        // Fallback for unsupported types
        ...
```

**Files**: `src/Frank.Validation/ShapeDerivation.fs`
**Notes**: The shape cache (`ConcurrentDictionary<Type, ShaclShape>`) is populated as shapes are derived. If a nested type is already in the cache, reuse it rather than re-deriving.

### Subtask T011 -- Implement recursive type cycle detection

**Purpose**: Detect and handle recursive/self-referential F# types during shape derivation. (FR-005)

**Steps**:
1. Maintain a `Set<Type>` derivation stack passed through all recursive calls
2. Before deriving a type, check if it is already on the stack:
   - If yes: emit `sh:node` reference to the existing NodeShape URI (breaking the cycle)
   - If no: add to stack, derive, remove from stack
3. Apply configurable maximum derivation depth (default 5) as safety net:
   - At depth limit: emit `sh:nodeKind sh:BlankNodeOrIRI` with a warning logged via `ILogger`

```fsharp
let rec deriveShape (maxDepth: int) (stack: Set<Type>) (typ: Type) : ShaclShape =
    if stack.Contains typ then
        // Cycle detected: produce reference-only shape
        { TargetType = typ
          NodeShapeUri = Uri(sprintf "urn:frank:shape:%s" typ.FullName)
          Properties = []
          Closed = false
          Description = Some "Recursive reference (cycle detected)" }
    elif stack.Count >= maxDepth then
        // Depth limit reached
        { TargetType = typ
          NodeShapeUri = Uri(sprintf "urn:frank:shape:%s" typ.FullName)
          Properties = []
          Closed = false
          Description = Some (sprintf "Depth limit reached (%d)" maxDepth) }
    else
        let stack' = stack |> Set.add typ
        // ... normal derivation with stack'
```

**Files**: `src/Frank.Validation/ShapeDerivation.fs`
**Notes**: Example recursive type: `type TreeNode = { Value: string; Children: TreeNode list }`. The `Children` property should get `sh:node` pointing to `TreeNode`'s NodeShape URI without infinite expansion.

### Subtask T012 -- Implement generic type expansion

**Purpose**: Expand generic type parameters at point of use, producing concrete NodeShapes. (FR-007)

**Steps**:
1. In `ShapeDerivation.fs`, handle generic types:

```fsharp
let expandGenericType (typ: Type) =
    if typ.IsGenericType && not (isOptionType typ) && not (isCollectionType typ) then
        // Generic record/DU: use the closed generic type for derivation
        // e.g., PagedResult<Customer> -> derive with Customer-specific properties
        let fullName = sprintf "%s[%s]" typ.Name
            (typ.GetGenericArguments() |> Array.map (fun t -> t.FullName) |> String.concat ",")
        Some fullName
    else
        None
```

2. Generic types use their full closed type name as the NodeShape URI (e.g., `urn:frank:shape:PagedResult[Customer]`)
3. Each distinct instantiation (e.g., `PagedResult<Customer>` vs `PagedResult<Order>`) produces a separate NodeShape

**Files**: `src/Frank.Validation/ShapeDerivation.fs`
**Notes**: Common generic types like `option<T>` and `list<T>` are handled specially (T008, collection handling). This subtask handles user-defined generic records like `PagedResult<'T>`.

### Subtask T013 -- Create `TypeMappingTests.fs`

**Purpose**: Verify the type mapping table produces correct XSD datatypes for all supported F# types.

**Steps**:
1. Create `test/Frank.Validation.Tests/TypeMappingTests.fs`
2. Write tests for every mapping in the table:
   - `string -> XsdString`
   - `int -> XsdInteger`
   - `int64 -> XsdLong`
   - `float -> XsdDouble`
   - `decimal -> XsdDecimal`
   - `bool -> XsdBoolean`
   - `DateTimeOffset -> XsdDateTimeStamp`
   - `DateTime -> XsdDateTime`
   - `DateOnly -> XsdDate`
   - `TimeOnly -> XsdTime`
   - `TimeSpan -> XsdDuration`
   - `Uri -> XsdAnyUri`
   - `byte[] -> XsdBase64Binary`
   - `Guid -> XsdString`
3. Test that unsupported types (records, DUs, custom classes) return `None`
4. Test `xsdUri` produces correct URI strings for each datatype

**Files**: `test/Frank.Validation.Tests/TypeMappingTests.fs`
**Parallel?**: Yes -- can proceed once T006 is done.

### Subtask T014 -- Create `ShapeDerivationTests.fs`

**Purpose**: Verify shape derivation produces correct NodeShapes for various F# type scenarios.

**Steps**:
1. Create `test/Frank.Validation.Tests/ShapeDerivationTests.fs`
2. Define test domain types:

```fsharp
type SimpleRecord = { Name: string; Age: int; Email: string option }
type NestedRecord = { Customer: SimpleRecord; OrderId: int }
type PaymentMethod = CreditCard | BankTransfer | Crypto
type Shape = Circle of radius: float | Rectangle of width: float * height: float
type TreeNode = { Value: string; Children: TreeNode list }
type PagedResult<'T> = { Items: 'T list; TotalCount: int; Page: int }
```

3. Write tests:
   - **Simple record**: 3 properties, correct datatypes, Name/Age minCount=1, Email minCount=0
   - **Nested record**: Customer property has sh:node reference, child NodeShape is valid
   - **Simple DU**: PaymentMethod produces sh:in [CreditCard; BankTransfer; Crypto]
   - **Payload DU**: Shape produces sh:or with Circle and Rectangle NodeShapes
   - **Recursive type**: TreeNode derives without infinite loop, Children has sh:node back to TreeNode
   - **Generic type**: `PagedResult<SimpleRecord>` produces concrete shape with SimpleRecord-specific Items
   - **Non-derivable type**: `typeof<HttpContext>` returns appropriate result (no shape / skip)

**Files**: `test/Frank.Validation.Tests/ShapeDerivationTests.fs`
**Parallel?**: Yes -- can proceed once T007-T012 are defined.
**Validation**: `dotnet test test/Frank.Validation.Tests/` passes with all tests green.

---

## Test Strategy

- Run `dotnet build` to verify compilation of both TypeMapping.fs and ShapeDerivation.fs
- Run `dotnet test test/Frank.Validation.Tests/` for all mapping and derivation tests
- Verify shapes for the four User Story 1 acceptance scenarios in spec.md

---

## Risks & Mitigations

| Risk | Mitigation |
|------|-----------|
| F# reflection metadata gaps for DU case field names | `FSharpType.GetUnionCases` + `UnionCaseInfo.GetFields()` provides full metadata; prototype early |
| Generic type expansion complexity | Start with common cases (single-param generics), extend for multi-param later |
| Collection type detection (list, array, seq, ResizeArray) | Create `isCollectionType` helper checking `IEnumerable<T>` + exclude string |
| Type.FullName is null for some generic types | Use `Type.Name` + generic argument names as fallback |

---

## Review Guidance

- Verify TypeMapping covers all types from research.md Decision 3 mapping table
- Verify `deriveShape` handles all scenarios: records, DUs (simple + payload), nested, recursive, generic
- Verify cycle detection uses `Set<Type>` and respects maxDepth
- Verify `isDerivableType` excludes HttpContext, HttpRequest, CancellationToken
- Verify option type unwrapping sets minCount=0 correctly
- Run `dotnet build Frank.sln` and `dotnet test` to confirm green

---

## Activity Log

- 2026-03-07T00:00:00Z -- system -- lane=planned -- Prompt created.
- 2026-03-15T19:37:21Z – unknown – lane=for_review – Moved to for_review
- 2026-03-15T19:42:15Z – claude-opus-reviewer – shell_pid=40749 – lane=doing – Started review via workflow command
