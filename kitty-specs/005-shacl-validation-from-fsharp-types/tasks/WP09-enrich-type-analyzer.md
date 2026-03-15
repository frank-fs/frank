---
work_package_id: WP09
title: Enrich TypeAnalyzer with Validation-Grade Metadata
lane: done
dependencies: []
subtasks: [T050, T051, T052, T053, T054]
history:
- timestamp: '2026-03-14T00:00:00Z'
  lane: planned
  agent: system
  shell_pid: ''
  action: Prompt generated from build-time SHACL unification design spec
requirement_refs: [FR-001, FR-002, FR-003, FR-005, FR-006, FR-007, FR-020]
design_ref: docs/superpowers/specs/2026-03-14-build-time-shacl-unification-design.md
---

# Work Package Prompt: WP09 -- Enrich TypeAnalyzer with Validation-Grade Metadata

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
spec-kitty implement WP09
```

No dependencies on other new WPs. Frank.Cli.Core is independent.

---

## Objectives & Success Criteria

- Extend `Frank.Cli.Core.Analysis.TypeAnalyzer` to capture full metadata needed for validation-grade SHACL
- Add missing primitive type mappings: DateOnly, TimeOnly, TimeSpan, Uri, byte[] (currently 8 types; validation needs 13+)
- Emit Guid as its own `FieldKind` variant so ShapeGenerator (WP10) can attach `sh:pattern`
- Track scalar vs. collection cardinality so ShapeGenerator can emit `sh:maxCount`
- Mark records as closed (for `sh:closed true` emission)
- Extract custom constraint attributes (`[<Pattern("...")>]`, `[<MinInclusive(0)>]`) from FCS entity metadata into `AnalyzedField`
- All existing TypeAnalyzer tests continue to pass
- New tests cover every added type mapping and metadata extraction

---

## Context & Constraints

**Reference documents**:
- `docs/superpowers/specs/2026-03-14-build-time-shacl-unification-design.md` -- WP09 section
- `src/Frank.Validation/TypeMapping.fs` -- Reference for complete F# type -> XSD mapping table
- `src/Frank.Validation/ShapeDerivation.fs` -- Reference for how reflection captures metadata that FCS must now capture
- `kitty-specs/005-shacl-validation-from-fsharp-types/research.md` -- Decision 3: F# Type to XSD Datatype Mapping

**Key constraints**:
- Frank.Cli.Core targets net10.0 only
- FCS types use `FSharpEntity`, `FSharpField`, `FSharpType` -- not `System.Type`
- `FieldKind` ADT changes must be backwards-compatible with existing extraction pipeline consumers (TypeMapper, ShapeGenerator, RouteMapper)
- Custom constraint attributes must be defined somewhere FCS can see them -- `Frank.Validation.Annotations` module or similar

---

## Subtasks & Detailed Guidance

### Subtask T050 -- Extend `FieldKind` ADT with validation-grade variants

**Purpose**: Add new variants to `FieldKind` to carry the richer metadata needed for validation-grade SHACL.

**Steps**:
1. In `src/Frank.Cli.Core/Analysis/TypeAnalyzer.fs`, extend `FieldKind`:

```fsharp
type FieldKind =
    | Primitive of xsdType: string
    | Guid                              // NEW: distinct from Primitive "xsd:string" to enable sh:pattern
    | Optional of inner: FieldKind
    | Collection of element: FieldKind
    | Reference of typeName: string
```

2. Update all pattern matches on `FieldKind` across Frank.Cli.Core:
   - `TypeMapper.fs` (`fieldKindToRange`)
   - `ShapeGenerator.fs` (`fieldMinCount`, `assertPropertyShape`)
   - `UriHelpers.fs` (`fieldKindToRange`)
   - Any other consumers

3. For the `Guid` variant, `fieldKindToRange` should return `(Uri Xsd.String, false)` to maintain OWL compatibility.

**Files**: `src/Frank.Cli.Core/Analysis/TypeAnalyzer.fs`, `src/Frank.Cli.Core/Extraction/UriHelpers.fs`, `src/Frank.Cli.Core/Extraction/TypeMapper.fs`
**Notes**: The `Guid` variant is intentionally separate from `Primitive` even though both map to `xsd:string` in OWL, because ShapeGenerator (WP10) needs to know it's a Guid to emit `sh:pattern`.

### Subtask T051 -- Add missing primitive type mappings to `mapFieldType`

**Purpose**: Extend the FCS type -> FieldKind mapping to cover all types from Frank.Validation's TypeMapping table.

**Steps**:
1. In `mapFieldType`, add cases for:

```fsharp
| "System.DateOnly" -> Primitive "xsd:date"
| "System.TimeOnly" -> Primitive "xsd:time"
| "System.TimeSpan" -> Primitive "xsd:duration"
| "System.Uri" -> Primitive "xsd:anyURI"
| "System.Byte[]" -> Primitive "xsd:base64Binary"     // Note: byte[] is an array type
| "System.Guid" -> Guid
```

2. Handle `byte[]` specially -- it's technically an array type but should map to `xsd:base64Binary`, not `Collection(Primitive "xsd:integer")`. Check `fsharpType.TypeDefinition.IsArrayType` and inspect element type.

3. Update the `Decimal` mapping from `Primitive "xsd:double"` to `Primitive "xsd:decimal"` (current code maps Decimal -> "xsd:double" which loses precision).

**Files**: `src/Frank.Cli.Core/Analysis/TypeAnalyzer.fs`
**Test**: Unit test each new mapping: create mock FSharpType for each CLR type and verify correct FieldKind output.

### Subtask T052 -- Add `AnalyzedFieldMetadata` for cardinality and constraint attributes

**Purpose**: Extend `AnalyzedField` to carry additional metadata needed for validation-grade shapes.

**Steps**:
1. Extend the `AnalyzedField` type:

```fsharp
type ConstraintAttribute =
    | PatternAttr of regex: string
    | MinInclusiveAttr of value: obj
    | MaxInclusiveAttr of value: obj
    | MinLengthAttr of length: int
    | MaxLengthAttr of length: int

type AnalyzedField =
    { Name: string
      Kind: FieldKind
      IsRequired: bool
      IsScalar: bool                    // NEW: true for non-collection, non-optional fields
      Constraints: ConstraintAttribute list }  // NEW: extracted from F# attributes
```

2. In `makeField`, set `IsScalar` based on whether the `Kind` is `Collection` or not:

```fsharp
let private makeField (name: string) (fsharpType: FSharpType) : AnalyzedField =
    let kind = mapFieldType fsharpType
    let isRequired = match kind with Optional _ -> false | _ -> true
    let isScalar = match kind with Collection _ -> false | _ -> true
    let constraints = extractConstraintAttributes fsharpType  // T053
    { Name = name; Kind = kind; IsRequired = isRequired; IsScalar = isScalar; Constraints = constraints }
```

3. Extend `AnalyzedType` with a `Closed` flag:

```fsharp
type AnalyzedType = {
    FullName: string
    ShortName: string
    Kind: TypeKind
    GenericParameters: string list
    SourceLocation: SourceLocation option
    IsClosed: bool }                    // NEW: true for records
```

4. Set `IsClosed = true` for records, `false` for DUs and enums in `collectEntities`.

**Files**: `src/Frank.Cli.Core/Analysis/TypeAnalyzer.fs`
**Notes**: The `IsScalar` flag is used by ShapeGenerator (WP10) to decide whether to emit `sh:maxCount 1`. Collections have no maxCount; scalars have maxCount 1.

### Subtask T053 -- Extract custom constraint attributes from FCS metadata

**Purpose**: When FCS analyzes a field, check for custom constraint attributes and extract their values.

**Steps**:
1. Implement attribute extraction:

```fsharp
let private extractConstraintAttributes (field: FSharpField) : ConstraintAttribute list =
    field.FieldAttributes
    |> Seq.choose (fun attr ->
        let attrName = attr.AttributeType.DisplayName
        match attrName with
        | "PatternAttribute" | "Pattern" ->
            match attr.ConstructorArguments |> Seq.tryHead with
            | Some (_, (:? string as regex)) -> Some (PatternAttr regex)
            | _ -> None
        | "MinInclusiveAttribute" | "MinInclusive" ->
            match attr.ConstructorArguments |> Seq.tryHead with
            | Some (_, value) -> Some (MinInclusiveAttr value)
            | _ -> None
        | "MaxInclusiveAttribute" | "MaxInclusive" ->
            match attr.ConstructorArguments |> Seq.tryHead with
            | Some (_, value) -> Some (MaxInclusiveAttr value)
            | _ -> None
        | "MinLengthAttribute" | "MinLength" ->
            match attr.ConstructorArguments |> Seq.tryHead with
            | Some (_, (:? int as n)) -> Some (MinLengthAttr n)
            | _ -> None
        | "MaxLengthAttribute" | "MaxLength" ->
            match attr.ConstructorArguments |> Seq.tryHead with
            | Some (_, (:? int as n)) -> Some (MaxLengthAttr n)
            | _ -> None
        | _ -> None)
    |> Seq.toList
```

2. The attribute types themselves need to be defined in `Frank.Validation` (or a shared annotations assembly) so that user code can reference them. This is a cross-cutting concern with WP06 -- for now, extract attributes by name matching so the TypeAnalyzer works regardless of where the attributes are defined.

**Files**: `src/Frank.Cli.Core/Analysis/TypeAnalyzer.fs`
**Notes**: FCS exposes `FSharpField.FieldAttributes` as a sequence of `FSharpAttribute`. Each attribute has `AttributeType` (an `FSharpEntity`) and `ConstructorArguments` (a sequence of `FSharpType * obj` tuples). Match by attribute type name rather than full qualified name to be resilient to namespace changes.

### Subtask T054 -- Create tests for enriched TypeAnalyzer

**Purpose**: Verify all new type mappings, metadata extraction, and backwards compatibility.

**Steps**:
1. Add tests to existing TypeAnalyzer test file (or create new file if none exists).
2. Test cases:

**a. New primitive mappings**:
- `DateOnly` -> `Primitive "xsd:date"`
- `TimeOnly` -> `Primitive "xsd:time"`
- `TimeSpan` -> `Primitive "xsd:duration"`
- `Uri` -> `Primitive "xsd:anyURI"`
- `byte[]` -> `Primitive "xsd:base64Binary"` (not Collection)
- `Guid` -> `Guid` (not Primitive)
- `Decimal` -> `Primitive "xsd:decimal"` (not "xsd:double")

**b. IsScalar flag**:
- `string` field -> `IsScalar = true`
- `string list` field -> `IsScalar = false`
- `string option` field -> `IsScalar = true` (option wraps a scalar)

**c. IsClosed flag**:
- Record type -> `IsClosed = true`
- DU type -> `IsClosed = false`
- Enum type -> `IsClosed = false`

**d. Constraint attribute extraction**:
- Field with `[<Pattern("^\\d+$")>]` -> `[PatternAttr "^\\d+$"]`
- Field with `[<MinInclusive(0)>]` -> `[MinInclusiveAttr 0]`
- Field with multiple attributes -> all extracted
- Field with no attributes -> empty list

**e. Backwards compatibility**:
- All existing TypeAnalyzer tests still pass
- Existing `FieldKind` variants (`Primitive`, `Optional`, `Collection`, `Reference`) behave identically

**Files**: Test file in `test/Frank.Cli.Core.Tests/` (find existing test location)
**Validation**: `dotnet build` and `dotnet test` pass.

---

## Test Strategy

- Run `dotnet build` to verify Frank.Cli.Core compiles with extended types
- Run `dotnet test` for TypeAnalyzer tests
- Verify all existing extraction pipeline tests still pass (TypeMapper, ShapeGenerator, RouteMapper consumers of FieldKind)

---

## Risks & Mitigations

| Risk | Mitigation |
|------|-----------|
| `FieldKind.Guid` variant breaks existing pattern matches | Compiler will flag incomplete matches; add `Guid` case to every `match kind with` across Frank.Cli.Core |
| FCS `FieldAttributes` API differences across versions | Pin FCS version; test attribute extraction with actual F# source files |
| `byte[]` detection edge case (FCS may represent as generic array) | Test with actual FCS check results from a project containing `byte[]` fields |
| Constraint attribute names may not match exactly | Use suffix matching (e.g., ends with "PatternAttribute" or equals "Pattern") for resilience |

---

## Review Guidance

- Verify all 13+ XSD type mappings from the validation TypeMapping table are covered
- Verify `Guid` is distinct from `Primitive "xsd:string"`
- Verify `IsScalar` and `IsClosed` flags are set correctly
- Verify constraint attribute extraction handles edge cases (no attributes, unknown attributes, multiple attributes)
- Verify all existing tests pass (backwards compatibility)
- Run `dotnet build Frank.sln` and `dotnet test` to confirm green

---

## Activity Log

- 2026-03-14T00:00:00Z -- system -- lane=planned -- Prompt created from build-time SHACL unification design.
