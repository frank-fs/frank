---
work_package_id: WP03
title: AST & Type Analysis
lane: "for_review"
dependencies: [WP01]
base_branch: 001-semantic-resources-phase1-WP01
base_commit: e940579b385af16c14a3af584d68d03952856347
created_at: '2026-03-05T15:23:57.207142+00:00'
subtasks:
- T013
- T014
- T016
- T017
phase: Phase 1 - Analysis
assignee: ''
agent: "claude-opus"
shell_pid: "80270"
review_status: ''
reviewed_by: ''
history:
- timestamp: '2026-03-04T22:10:13Z'
  lane: planned
  agent: system
  shell_pid: ''
  action: Prompt generated via /spec-kitty.tasks
requirement_refs:
- FR-007
---

# WP03: AST & Type Analysis

## Implementation Command

```
spec-kitty implement WP03 --base WP01
```

## Objectives

Implement FCS-based analyzers for extracting type definitions, route templates, and handler registrations from F# source files using AST analysis only (no compiled assembly required).

## Context

- The existing `DuplicateHandlerAnalyzer.fs` in `src/Frank.Analyzers/` demonstrates the exact AST-walking pattern used to detect CE operations. Study this file before implementing.
- FCS untyped AST key patterns:
  - `SynExpr.App` for function application chains
  - `SynExpr.ComputationExpr` for `{ ... }` CE bodies
  - `SynConst.String` for string literal arguments
- FCS typed AST key patterns:
  - `FSharpEntity.IsFSharpUnion`, `.UnionCases` for DU analysis
  - `FSharpEntity.IsFSharpRecord`, `.FSharpFields` for record analysis
  - `FSharpEntity.IsEnum` for enum types
- Ionide.ProjInfo is the standard tool for cracking .fsproj files into `FSharpProjectOptions` that FCS can consume.

## Subtask Details

### T013: AstAnalyzer.fs — Untyped AST Walker

**Module**: `Frank.Cli.Core.Analysis.AstAnalyzer`

**File location**: `src/Frank.Cli.Core/Analysis/AstAnalyzer.fs`

Define output types:

```fsharp
type HttpMethod = Get | Post | Put | Delete | Patch | Head | Options

type AnalyzedResource = {
    RouteTemplate: string
    Name: string option
    HttpMethods: HttpMethod list
    HasLinkedData: bool
    Location: SourceLocation  // file, line, col of the resource CE
}
```

Walk the `ParsedInput` (untyped AST) to find Frank's `resource` computation expression invocations. The pattern to match is:

```
SynExpr.App(
    funcExpr = SynExpr.App(
        funcExpr = SynExpr.Ident "resource",
        argExpr = SynExpr.Const(SynConst.String(routeTemplate, ...))
    ),
    argExpr = SynExpr.ComputationExpr(expr = ceBody)
)
```

Once the CE body is found, walk it recursively for:
- `SynExpr.Ident "get"` / `"post"` / `"put"` / `"delete"` / `"patch"` / `"head"` / `"options"` → add to HttpMethods
- `SynExpr.App(funcExpr = SynExpr.Ident "name", argExpr = SynExpr.Const(SynConst.String(n, ...)))` → set Name
- `SynExpr.Ident "linkedData"` or `SynExpr.App(funcExpr = SynExpr.Ident "linkedData", ...)` → set HasLinkedData = true

Main function signature:
- `analyzeFile : ParsedInput -> AnalyzedResource list` — walks a single file's AST
- `analyzeFiles : ParsedInput list -> AnalyzedResource list` — maps over multiple files

Use `SynExpr` active patterns from FCS. Handle `SynExpr.Sequential` and `SynExpr.LetOrUse` wrappers when walking CE bodies (CE bodies may contain let bindings and sequential expressions).

### T014: TypeAnalyzer.fs — Typed AST Walker

**Module**: `Frank.Cli.Core.Analysis.TypeAnalyzer`

**File location**: `src/Frank.Cli.Core/Analysis/TypeAnalyzer.fs`

Define output types:

```fsharp
type FieldKind =
    | Primitive of xsdType: string  // xsd:string, xsd:integer, etc.
    | Optional of inner: FieldKind
    | Collection of element: FieldKind  // List<T>, array, seq
    | Reference of typeName: string     // another analyzed type

type AnalyzedField = {
    Name: string
    Kind: FieldKind
    IsRequired: bool  // false when wrapped in Option
}

type DuCase = {
    Name: string
    Fields: AnalyzedField list
}

type TypeKind =
    | Record of fields: AnalyzedField list
    | DiscriminatedUnion of cases: DuCase list
    | Enum of values: string list

type AnalyzedType = {
    FullName: string       // namespace-qualified name
    ShortName: string
    Kind: TypeKind
    GenericParameters: string list
    SourceLocation: SourceLocation option
}
```

Walk `FSharpCheckProjectResults.AssemblySignature.Entities` recursively (entities can be nested in modules). For each `FSharpEntity`:

- If `IsFSharpUnion`: extract each `UnionCase`, and for each case extract its `UnionCaseFields` → build `DuCase list`
- If `IsFSharpRecord`: extract `FSharpFields` → build `AnalyzedField list`
- If `IsEnum`: extract enum member names
- Skip compiler-generated entities (`IsProvided`, names starting with `<`)

F# type → `FieldKind` mapping:
- `string`, `System.String` → `Primitive "xsd:string"`
- `int`, `int32`, `System.Int32` → `Primitive "xsd:integer"`
- `float`, `double`, `System.Double` → `Primitive "xsd:double"`
- `bool`, `System.Boolean` → `Primitive "xsd:boolean"`
- `System.DateTime`, `System.DateTimeOffset` → `Primitive "xsd:dateTime"`
- `System.Guid` → `Primitive "xsd:string"`
- `Microsoft.FSharp.Core.option<T>` → `Optional (mapType T)`
- `Microsoft.FSharp.Collections.list<T>`, `T[]`, `seq<T>` → `Collection (mapType T)`
- Any other entity → `Reference entity.DisplayName`

Main function:
- `analyzeTypes : FSharpCheckProjectResults -> AnalyzedType list`

### T016: ProjectLoader.fs — FCS Project Loading

**Module**: `Frank.Cli.Core.Analysis.ProjectLoader`

**File location**: `src/Frank.Cli.Core/Analysis/ProjectLoader.fs`

Define output type:

```fsharp
type LoadedProject = {
    ProjectPath: string
    ParsedFiles: (string * ParsedInput) list    // (filePath, parsedAST)
    CheckResults: FSharpCheckProjectResults
}
```

Implement using Ionide.ProjInfo:

1. Initialize the MSBuild toolchain: `ProjectLoader.Init.init` (or the equivalent Ionide.ProjInfo 0.74.x API — check the actual API surface, it may have changed from earlier versions)
2. Create a `WorkspaceLoader`: `WorkspaceLoader.Create(toolsPath, globalProperties)`
3. Load the project: `loader.LoadProjects [fsprojPath]` — this returns `ProjectOptions list`
4. Convert to FCS options using `Ionide.ProjInfo.FCS.mapToFSharpProjectOptions`
5. Create an `FSharpChecker` instance and call `checker.ParseAndCheckProject(fcsOptions)` → `FSharpCheckProjectResults`
6. Extract parsed AST from `CheckResults.AssemblySignature` for typed analysis and parse each file individually for untyped AST

Multi-target handling: if the .fsproj produces multiple TFMs, pick the highest available (net10.0 > net9.0 > net8.0). Inspect `ProjectOptions` for the target framework property.

Main function:
- `loadProject : string -> Async<Result<LoadedProject, string>>` — async because FCS type checking is async

### T017: Fixture Files and Tests

**Test project**: Frank.Cli.Core.Tests

Create fixture .fs files under `test/Frank.Cli.Core.Tests/Fixtures/`:

**`SimpleTypes.fs`** — basic DU and record:
```fsharp
module Fixtures.SimpleTypes

type Status = Active | Inactive

type Product = {
    Id: int
    Name: string
    IsAvailable: bool
}
```

**`ComplexTypes.fs`** — nested records, optional fields, lists:
```fsharp
module Fixtures.ComplexTypes

type Address = {
    Street: string
    City: string
    PostalCode: string
}

type Customer = {
    Id: System.Guid
    Name: string
    Email: string option
    Address: Address option
    Tags: string list
    CreatedAt: System.DateTime
}
```

**`SimpleResource.fs`** — minimal resource CE:
```fsharp
module Fixtures.SimpleResource
// Simulated structure matching Frank's resource CE pattern
// (actual CE not needed — just needs to parse correctly for AST walking)
let routeTemplate = "/"
let handler (ctx: obj) = async { return () }
```

Note: fixture files for AST analysis tests should contain the actual Frank CE syntax if Frank.Cli.Core has a dependency path to it; otherwise, write the AST walking tests using FCS to parse inline F# strings via `FSharpChecker.ParseFile`.

**`MultiMethodResource.fs`** — multiple HTTP methods:
Write a file that, when parsed, produces CE body nodes for get, post, put, delete.

**Test files**:

`test/Frank.Cli.Core.Tests/Analysis/AstAnalyzerTests.fs`:
- Parse `SimpleResource.fs` fixture inline string → call `analyzeFile` → verify at least the route template is extracted
- Test that CE bodies with `get`/`post` keywords produce correct `HttpMethods` list

`test/Frank.Cli.Core.Tests/Analysis/TypeAnalyzerTests.fs`:
- Parse and type-check `SimpleTypes.fs` fixture → call `analyzeTypes` → verify `Product` record has 3 fields with correct `FieldKind` values
- Verify `Status` DU has 2 cases with no fields each
- Parse `ComplexTypes.fs` → verify `Customer.Email` field has `Optional (Primitive "xsd:string")` kind
- Verify `Customer.Tags` field has `Collection (Primitive "xsd:string")` kind

`test/Frank.Cli.Core.Tests/Analysis/ProjectLoaderTests.fs`:
- If a real .fsproj is available in the repo (e.g., a sample project), test `loadProject` against it
- Otherwise, create a minimal temp .fsproj pointing to a fixture .fs file and test loading it
- Verify `LoadedProject.ParsedFiles` is non-empty and `CheckResults` has no critical errors

## Review Guidance

- AstAnalyzer: step through the DuplicateHandlerAnalyzer pattern carefully — the SynExpr tree can be deeply nested and ordering matters. AstAnalyzer now also handles HTTP method detection (previously in removed ReflectionAnalyzer)
- TypeAnalyzer: recursion over entities must handle modules-within-modules (Frank's source uses nested modules)
- ProjectLoader: Ionide.ProjInfo 0.74.x API may differ from older versions — read the actual Ionide.ProjInfo source or NuGet README before writing the loading code. No compiled assembly is needed.
- All tests must pass with `dotnet test` — no skipped or inconclusive tests in this WP

## Activity Log

- 2026-03-05T15:23:57Z – claude-opus – shell_pid=80270 – lane=doing – Assigned agent via workflow command
- 2026-03-05T17:03:11Z – claude-opus – shell_pid=80270 – lane=for_review – 15 tests pass: AstAnalyzer extracts resource CEs (route, methods, name, linkedData), TypeAnalyzer maps F# types to XSD, ProjectLoader uses Ionide.ProjInfo + FCS.
