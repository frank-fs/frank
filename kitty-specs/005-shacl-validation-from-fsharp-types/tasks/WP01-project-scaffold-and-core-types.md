---
work_package_id: WP01
title: Project Scaffold & Core Types
lane: "for_review"
dependencies: []
base_branch: master
base_commit: eb3ec1d134df7f725105e1203a7aaaf2a9a6b507
created_at: '2026-03-08T17:30:46.117744+00:00'
subtasks: [T001, T002, T003, T004, T005]
shell_pid: "97836"
agent: "claude-opus"
history:
- timestamp: '2026-03-07T00:00:00Z'
  lane: planned
  agent: system
  shell_pid: ''
  action: Prompt generated via /spec-kitty.tasks
requirement_refs: [FR-001, FR-002, FR-003, FR-010]
---

# Work Package Prompt: WP01 -- Project Scaffold & Core Types

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
spec-kitty implement WP01
```

No dependencies -- this is the starting package.

---

## Objectives & Success Criteria

- Create `Frank.Validation` library project (multi-target net8.0;net9.0;net10.0)
- Create `Frank.Validation.Tests` test project (net10.0)
- Define all core F# types: `XsdDatatype`, `PropertyShape`, `ShaclShape`, `ValidationSeverity`, `ValidationResult`, `ValidationReport`
- Define constraint and marker types: `ConstraintKind`, `CustomConstraint`, `ValidationMarker`, `ShapeOverride`, `ShapeResolverConfig`
- Both projects compile; types are usable from test project
- All projects added to `Frank.sln`
- `dotnet build Frank.sln` and `dotnet test` pass

---

## Context & Constraints

**Reference documents**:
- `kitty-specs/005-shacl-validation-from-fsharp-types/plan.md` -- Project structure, design decisions
- `kitty-specs/005-shacl-validation-from-fsharp-types/data-model.md` -- Entity definitions and relationships
- `kitty-specs/005-shacl-validation-from-fsharp-types/spec.md` -- Requirements and acceptance scenarios

**Key constraints**:
- Follow `src/Frank.Auth/Frank.Auth.fsproj` structure for project configuration
- Multi-target: `net8.0;net9.0;net10.0` for library, `net10.0` for tests
- Project references to Frank, Frank.LinkedData, Frank.Auth (NOT NuGet -- project references like other extensions)
- NuGet reference to `dotNetRdf.Core` (shared with Frank.LinkedData)
- Types.fs must be first in compilation order (all other files depend on it)
- Use `option` types for optional fields, not nulls (Constitution II: Idiomatic F#)

---

## Subtasks & Detailed Guidance

### Subtask T001 -- Create `Frank.Validation.fsproj`

**Purpose**: Scaffold the library project with correct multi-targeting and project references.

**Steps**:
1. Create directory `src/Frank.Validation/`
2. Create `Frank.Validation.fsproj` with this structure:

```xml
<Project Sdk="Microsoft.NET.Sdk">

  <PropertyGroup>
    <TargetFrameworks>net8.0;net9.0;net10.0</TargetFrameworks>
    <PackageTags>validation;shacl;rdf;semantic</PackageTags>
    <Description>SHACL shape validation derived from F# types for Frank web framework</Description>
  </PropertyGroup>

  <ItemGroup>
    <Compile Include="Types.fs" />
    <Compile Include="Constraints.fs" />
  </ItemGroup>

  <ItemGroup>
    <ProjectReference Include="../Frank/Frank.fsproj" />
    <ProjectReference Include="../Frank.LinkedData/Frank.LinkedData.fsproj" />
    <ProjectReference Include="../Frank.Auth/Frank.Auth.fsproj" />
  </ItemGroup>

  <ItemGroup>
    <PackageReference Include="dotNetRdf.Core" Version="3.*" />
  </ItemGroup>

  <ItemGroup>
    <FrameworkReference Include="Microsoft.AspNetCore.App" />
  </ItemGroup>

</Project>
```

3. Add the project to `Frank.sln`:
   ```bash
   dotnet sln Frank.sln add src/Frank.Validation/Frank.Validation.fsproj
   ```

**Files**: `src/Frank.Validation/Frank.Validation.fsproj`
**Notes**: The `<Compile>` list will be extended in later WPs as files are added. Start with `Types.fs` and `Constraints.fs`. Check that `src/Directory.Build.props` applies (it has shared packaging props like VersionPrefix). Verify the `dotNetRdf.Core` version matches what Frank.LinkedData uses.

### Subtask T002 -- Create `Types.fs` with core types

**Purpose**: Define the foundational types that all other modules depend on.

**Steps**:
1. Create `src/Frank.Validation/Types.fs`
2. Use namespace `Frank.Validation`
3. Define the following types in this order:

```fsharp
namespace Frank.Validation

open System

/// XSD datatype assigned to a SHACL property shape, derived from F# CLR type.
type XsdDatatype =
    | XsdString
    | XsdInteger
    | XsdLong
    | XsdDouble
    | XsdDecimal
    | XsdBoolean
    | XsdDateTimeStamp
    | XsdDateTime
    | XsdDate
    | XsdTime
    | XsdDuration
    | XsdAnyUri
    | XsdBase64Binary
    | Custom of Uri

/// Severity level for a validation result.
type ValidationSeverity =
    | Violation
    | Warning
    | Info

/// A SHACL property shape corresponding to a single field of an F# record.
type PropertyShape =
    { Path: string
      Datatype: XsdDatatype option
      MinCount: int
      MaxCount: int option
      NodeReference: Uri option
      InValues: string list option
      OrShapes: Uri list option
      Pattern: string option
      MinInclusive: obj option
      MaxInclusive: obj option
      Description: string option }

/// A SHACL NodeShape derived from an F# type definition.
type ShaclShape =
    { TargetType: Type
      NodeShapeUri: Uri
      Properties: PropertyShape list
      Closed: bool
      Description: string option }

/// A single constraint violation within a ValidationReport.
type ValidationResult =
    { FocusNode: string
      ResultPath: string
      Value: obj option
      SourceConstraint: string
      Message: string
      Severity: ValidationSeverity }

/// A W3C SHACL ValidationReport produced when request data violates constraints.
type ValidationReport =
    { Conforms: bool
      Results: ValidationResult list
      ShapeUri: Uri }
```

**Files**: `src/Frank.Validation/Types.fs`
**Notes**:
- `XsdDatatype` uses a DU for type safety; the `Custom` case allows extension for domain-specific types
- `PropertyShape` uses `option` types throughout for optional SHACL predicates
- `ShaclShape.Closed` defaults to `true` for records (no additional properties allowed)
- `ValidationSeverity` maps directly to SHACL severity levels: `sh:Violation`, `sh:Warning`, `sh:Info`
- Types are immutable records -- no mutation after creation

### Subtask T003 -- Create `Constraints.fs` with constraint and marker types

**Purpose**: Define the constraint kinds, custom constraints, validation marker, and shape resolver configuration types.

**Steps**:
1. Create `src/Frank.Validation/Constraints.fs`
2. Define constraint and marker types:

```fsharp
namespace Frank.Validation

open System
open System.Security.Claims

/// Kinds of custom constraints that can extend auto-derived shapes.
type ConstraintKind =
    | PatternConstraint of regex: string
    | MinInclusiveConstraint of value: obj
    | MaxInclusiveConstraint of value: obj
    | MinExclusiveConstraint of value: obj
    | MaxExclusiveConstraint of value: obj
    | MinLengthConstraint of length: int
    | MaxLengthConstraint of length: int
    | InValuesConstraint of values: string list
    | SparqlConstraint of query: string
    | CustomShaclConstraint of predicateUri: Uri * value: obj

/// A developer-provided custom constraint that extends an auto-derived shape.
type CustomConstraint =
    { PropertyPath: string
      Constraint: ConstraintKind }

/// Shape override for capability-dependent validation.
type ShapeOverride =
    { RequiredClaim: string * string list
      Shape: ShaclShape }

/// Configuration for capability-dependent shape resolution.
type ShapeResolverConfig =
    { BaseShape: ShaclShape
      Overrides: ShapeOverride list }

/// Endpoint metadata marker placed by the `validate` CE custom operation.
/// Read by ValidationMiddleware to determine if validation applies.
type ValidationMarker =
    { ShapeType: Type
      CustomConstraints: CustomConstraint list
      ResolverConfig: ShapeResolverConfig option }
```

**Files**: `src/Frank.Validation/Constraints.fs`
**Notes**:
- `ConstraintKind` covers all custom constraint variants from the spec
- `CustomShaclConstraint` is the escape hatch for constraints not in the DU
- `ValidationMarker` follows the same metadata marker pattern as `LinkedDataMarker` in Frank.LinkedData
- `ShapeResolverConfig` is `option` on `ValidationMarker` -- `None` means no capability-dependent resolution

### Subtask T004 -- Create test project

**Purpose**: Scaffold the test project so types can be validated.

**Steps**:
1. Create directory `test/Frank.Validation.Tests/`
2. Create `Frank.Validation.Tests.fsproj`:

```xml
<Project Sdk="Microsoft.NET.Sdk">

  <PropertyGroup>
    <OutputType>Exe</OutputType>
    <TargetFramework>net10.0</TargetFramework>
    <IsPackable>false</IsPackable>
    <IsTestProject>true</IsTestProject>
    <GenerateProgramFile>false</GenerateProgramFile>
  </PropertyGroup>

  <ItemGroup>
    <Compile Include="TypeTests.fs" />
    <Compile Include="Program.fs" />
  </ItemGroup>

  <ItemGroup>
    <PackageReference Include="Microsoft.NET.Test.Sdk" Version="17.14.1" />
    <PackageReference Include="Expecto" Version="10.2.3" />
    <PackageReference Include="YoloDev.Expecto.TestSdk" Version="0.14.3" />
    <PackageReference Include="Microsoft.AspNetCore.TestHost" Version="10.0.0" />
  </ItemGroup>

  <ItemGroup>
    <ProjectReference Include="../../src/Frank.Validation/Frank.Validation.fsproj" />
  </ItemGroup>

</Project>
```

3. Create `test/Frank.Validation.Tests/Program.fs`:

```fsharp
module Program

open Expecto

[<EntryPoint>]
let main args =
    runTestsInAssemblyWithCLIArgs [] args
```

4. Add to solution:
   ```bash
   dotnet sln Frank.sln add test/Frank.Validation.Tests/Frank.Validation.Tests.fsproj
   ```

**Files**: `test/Frank.Validation.Tests/Frank.Validation.Tests.fsproj`, `test/Frank.Validation.Tests/Program.fs`
**Parallel?**: Yes -- can proceed in parallel with T002/T003 once T001 is done.
**Notes**: Check existing test projects (e.g., `Frank.Auth.Tests`, `Frank.Statecharts.Tests`) for exact package versions. The `<Compile>` list will be extended as test files are added in later WPs.

### Subtask T005 -- Create `TypeTests.fs` with unit tests

**Purpose**: Validate that all core types compile and behave correctly.

**Steps**:
1. Create `test/Frank.Validation.Tests/TypeTests.fs`
2. Write Expecto tests covering:

**a. XsdDatatype tests**:
- Verify each case can be constructed and pattern matched
- Verify `Custom` carries a Uri

**b. PropertyShape tests**:
- Construct a required field (minCount 1, datatype XsdString)
- Construct an optional field (minCount 0)
- Construct a field with sh:in constraint (InValues = Some [...])
- Construct a nested field (NodeReference = Some uri)

**c. ShaclShape tests**:
- Construct a shape with multiple PropertyShapes
- Verify Closed = true by default for records

**d. ValidationReport tests**:
- Construct a conforming report (Conforms = true, empty Results)
- Construct a non-conforming report with multiple ValidationResults
- Verify each ValidationResult has focusNode, resultPath, constraint, message

**e. ConstraintKind tests**:
- Construct each ConstraintKind variant and pattern match

**f. ValidationMarker tests**:
- Construct a marker with no custom constraints
- Construct a marker with custom constraints
- Construct a marker with ShapeResolverConfig

**Files**: `test/Frank.Validation.Tests/TypeTests.fs`
**Parallel?**: Yes -- can proceed once T002/T003 are done.
**Validation**: `dotnet test test/Frank.Validation.Tests/` passes with all tests green.

---

## Test Strategy

- Run `dotnet build` on both projects to verify compilation
- Run `dotnet test test/Frank.Validation.Tests/` to verify all type tests pass
- Run `dotnet build Frank.sln` to verify solution-level build succeeds on all three target frameworks

---

## Risks & Mitigations

| Risk | Mitigation |
|------|-----------|
| dotNetRdf.Core version mismatch with Frank.LinkedData | Check Frank.LinkedData.fsproj for exact version; use same version |
| Project reference chain (Frank -> LinkedData -> Auth -> Validation) | Verify no circular dependencies; Validation depends on all three, none depend on Validation |
| `obj` boxing in PropertyShape.MinInclusive/MaxInclusive | Acceptable for now; constrained types would require generic PropertyShape which adds complexity |

---

## Review Guidance

- Verify `.fsproj` files match Frank.Auth pattern (multi-target, project references, framework reference)
- Verify Types.fs compilation order is first in `.fsproj`
- Verify all types match `data-model.md` definitions exactly
- Verify `option` types used for optional fields throughout (no nulls)
- Verify test project follows existing test project patterns exactly
- Run `dotnet build Frank.sln` and `dotnet test` to confirm green

---

## Activity Log

- 2026-03-07T00:00:00Z -- system -- lane=planned -- Prompt created.
- 2026-03-08T17:30:46Z – claude-opus – shell_pid=97836 – lane=doing – Assigned agent via workflow command
- 2026-03-08T17:55:54Z – claude-opus – shell_pid=97836 – lane=for_review – T001-T005 complete. Builds clean. 13 tests pass.
