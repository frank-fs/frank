---
work_package_id: WP11
title: OpenAPI Consistency Validation
lane: "for_review"
dependencies:
- WP02
base_branch: 031-unified-resource-pipeline-WP02
base_commit: 35175b217c2cce39589a312a0e8317e273430068
created_at: '2026-03-19T04:06:36.072157+00:00'
subtasks:
- T062
- T063
- T064
- T065
- T066
- T067
phase: Phase 3 - Integration
assignee: ''
agent: "claude-opus"
shell_pid: "26221"
review_status: ''
reviewed_by: ''
history:
- timestamp: '2026-03-19T02:15:00Z'
  lane: planned
  agent: system
  shell_pid: ''
  action: Prompt generated via /spec-kitty.tasks
requirement_refs:
- FR-014
- FR-015
- FR-016
- FR-033
---

# Work Package Prompt: WP11 -- OpenAPI Consistency Validation

## Important: Review Feedback Status

**Read this first if you are implementing this task!**

- **Has review feedback?**: Check the `review_status` field above. If it says `has_feedback`, scroll to the **Review Feedback** section immediately.
- **You must address all feedback** before your work is complete. Feedback items are your implementation TODO list.
- **Mark as acknowledged**: When you understand the feedback and begin addressing it, update `review_status: acknowledged` in the frontmatter.
- **Report progress**: As you address each feedback item, update the Activity Log explaining what you changed.

---

## Review Feedback

> **Populated by `/spec-kitty.review`** -- Reviewers add detailed feedback here when work needs changes. Implementation must address every item listed below before returning for re-review.

*[This section is empty initially. Reviewers will populate it if the work is returned from review. If you see feedback here, treat each item as a must-do before completion.]*

---

## Markdown Formatting
Wrap HTML/XML tags in backticks: `` `<div>` ``, `` `<script>` ``
Use language identifiers in code blocks: ````fsharp`, ````json`

---

## Implementation Command

Depends on WP02 (unified model types). Can run in parallel with WP03-WP10.

```bash
spec-kitty implement WP11 --base WP02
```

---

## Objectives & Success Criteria

1. Create `OpenApiConsistencyValidator.fs` in `Frank.Cli.Core` that compares the unified model's type descriptions against expected OpenAPI JSON Schema components.
2. Use `FSharp.Data.JsonSchema.OpenApi`'s `FSharpSchemaTransformer` logic as the canonical opinion for how F# types should map to JSON Schema (FR-024a).
3. Detect and report: unmapped F# fields, orphan OpenAPI properties, type mismatches, and route discrepancies.
4. Wire the validation into a `frank-cli validate --project <fsproj> --openapi` command path in `Program.fs`.
5. Format results using the existing `ValidationReport` structure (reuse `ValidationReportFormatter` patterns).
6. Write tests with intentional type/OpenAPI mismatches to verify discrepancy detection.

**Success**: Running `frank-cli validate --project <fsproj> --openapi` against a project where an F# record has a field not exposed in the OpenAPI schema reports the unmapped field as a warning. Running against a fully consistent project reports all checks passed.

---

## Context & Constraints

- **Spec**: `kitty-specs/031-unified-resource-pipeline/spec.md` -- User Story 4 (FR-014, FR-015, FR-016)
- **Plan**: `kitty-specs/031-unified-resource-pipeline/plan.md` -- OpenAPI consistency section
- **Data Model**: `kitty-specs/031-unified-resource-pipeline/data-model.md` -- `UnifiedResource.TypeInfo` as the source of expected schema
- **Research**: `kitty-specs/031-unified-resource-pipeline/research.md` -- R5 (comparison strategy: generate expected components from unified model, compare against runtime OpenAPI via TestHost), R6 (FSharp.Data.JsonSchema as canonical type mapping)

**Key design decisions**:
- The unified model is **authoritative** (FR-016). Discrepancies are reported as drift from the code, not as errors in the code. The F# types are the source of truth.
- Comparison is at the **JSON Schema component level** -- the validator generates expected JSON Schema properties from `UnifiedResource.TypeInfo` using the same `FSharpSchemaTransformer` that `Frank.OpenApi` uses at runtime, then diffs them.
- This is fundamentally an **integration test** -- the "actual" OpenAPI schema comes from running the app via TestHost and fetching the OpenAPI document. But the CLI command performs a static comparison using the unified model as the expected side.
- The `ValidationReport` structure from `Frank.Statecharts.Validation` is reused for consistent output formatting (text and JSON) across CLI validation commands.

**Existing infrastructure to reuse**:
- `src/Frank.Cli.Core/Statechart/ValidationReportFormatter.fs` -- formatting patterns for text/JSON validation output
- `src/Frank.Cli.Core/Commands/ValidateCommand.fs` -- existing semantic validation command (structural reference, not dependency)
- `src/Frank.Cli.Core/Commands/StatechartValidateCommand.fs` -- statechart validation command (formatting patterns)
- `Frank.OpenApi` project at `src/Frank.OpenApi/` -- uses `FSharpSchemaTransformer` for runtime schema generation

**Dependencies**:
- `FSharp.Data.JsonSchema.OpenApi` NuGet package (needs to be added to `Frank.Cli.Core` -- currently only in `Frank.OpenApi`)
- `UnifiedExtractionState` and `UnifiedResource` types from WP02

---

## Subtasks & Detailed Guidance

### Subtask T062 -- Create `OpenApiConsistencyValidator.fs`

- **Purpose**: Implement the core validation logic that compares the unified model's type information against expected OpenAPI schema components.

- **Steps**:
  1. Create `src/Frank.Cli.Core/Unified/OpenApiConsistencyValidator.fs`.
  2. Module declaration: `module Frank.Cli.Core.Unified.OpenApiConsistencyValidator`
  3. Open necessary namespaces:
     ```fsharp
     open System
     open System.Text.Json
     open Frank.Cli.Core.Analysis  // AnalyzedType, AnalyzedField
     open Frank.Statecharts.Validation  // ValidationReport, ValidationCheck, CheckStatus
     ```
  4. Define the comparison result types:
     ```fsharp
     type FieldDiscrepancy =
         | UnmappedField of typeName: string * fieldName: string
         | OrphanProperty of schemaName: string * propertyName: string
         | TypeMismatch of typeName: string * fieldName: string * expected: string * actual: string
         | RouteDiscrepancy of unifiedRoute: string * openApiPath: string

     type ConsistencyResult =
         { Discrepancies: FieldDiscrepancy list
           CheckedTypes: int
           CheckedProperties: int
           IsConsistent: bool }
     ```
  5. Implement the main comparison function:
     ```fsharp
     /// Compare unified model type info against a JSON Schema document.
     let validate
         (unifiedTypes: AnalyzedType list)
         (openApiSchemas: JsonElement)  // The "components/schemas" section
         : ConsistencyResult =
         ...
     ```
  6. The function should:
     - For each `AnalyzedType`, look up the corresponding JSON Schema component by name
     - For each `AnalyzedField` in the type, look up the corresponding property in the schema
     - Report fields present in the unified model but not in the schema (`UnmappedField`)
     - Report properties present in the schema but not in the unified model (`OrphanProperty`)
     - Compare field types against schema types for mismatches (`TypeMismatch`)

- **Files**: `src/Frank.Cli.Core/Unified/OpenApiConsistencyValidator.fs` (NEW, ~150-200 lines)
- **Notes**:
  - The validator operates on `JsonElement` (the parsed OpenAPI document's schema section), not on the OpenAPI types directly. This avoids a dependency on the OpenAPI library in the CLI.
  - F# `AnalyzedField.Kind` (Primitive, Guid, Optional, Collection, Reference) needs to map to expected JSON Schema types. See T063 for the mapping logic.

### Subtask T063 -- Generate expected JSON Schema components from `UnifiedResource.TypeInfo`

- **Purpose**: Use `FSharp.Data.JsonSchema.OpenApi`'s `FSharpSchemaTransformer` logic to produce the expected JSON Schema that the unified model's types should produce. This is the "expected" side of the comparison.

- **Steps**:
  1. Add `FSharp.Data.JsonSchema.OpenApi` as a NuGet dependency to `Frank.Cli.Core.fsproj`:
     ```xml
     <PackageReference Include="FSharp.Data.JsonSchema.OpenApi" Version="3.0.0" />
     ```
  2. In `OpenApiConsistencyValidator.fs`, implement the expected schema generation:
     ```fsharp
     /// Map an AnalyzedField.Kind to the expected JSON Schema type string.
     let private expectedSchemaType (kind: FieldKind) : string =
         match kind with
         | Primitive "string" -> "string"
         | Primitive "int" | Primitive "int32" -> "integer"
         | Primitive "int64" -> "integer"
         | Primitive "float" | Primitive "double" -> "number"
         | Primitive "bool" | Primitive "boolean" -> "boolean"
         | Primitive "decimal" -> "number"
         | Primitive other -> other
         | Guid -> "string"  // format: uuid
         | Optional inner -> expectedSchemaType inner
         | Collection inner -> "array"
         | Reference typeName -> "object"  // $ref to another schema
     ```
  3. The `FSharpSchemaTransformer` from the NuGet package provides the canonical mapping. If it exposes a usable API for generating schemas from F# type metadata, use it directly. If it is tightly coupled to OpenAPI middleware, replicate its mapping decisions in the validator (and document which version's decisions are replicated).

  4. Build a `Map<string, Map<string, string>>` of expected schemas: `typeName -> (fieldName -> expectedType)`. This is compared field-by-field against the actual OpenAPI schema properties.

- **Files**: `src/Frank.Cli.Core/Unified/OpenApiConsistencyValidator.fs` (same file), `src/Frank.Cli.Core/Frank.Cli.Core.fsproj` (add NuGet reference)
- **Notes**:
  - The `FSharp.Data.JsonSchema.OpenApi` package version should match what `Frank.OpenApi` uses. Check `src/Frank.OpenApi/Frank.OpenApi.fsproj` for the current version.
  - The `FieldKind` type is defined in `src/Frank.Cli.Core/Analysis/TypeAnalyzer.fs`. Ensure all variants are handled. If new variants have been added since the initial implementation, add mappings for them.
  - For DU types, `FSharpSchemaTransformer` maps them as `oneOf` with a discriminator. The validator should handle this by checking the schema's `oneOf` property for DU types.

### Subtask T064 -- Implement field-level comparison logic

- **Purpose**: Perform the actual property-by-property comparison between the unified model's expected schema and the actual OpenAPI schema components.

- **Steps**:
  1. Implement the core diff function:
     ```fsharp
     /// Compare a single type's fields against a JSON Schema object's properties.
     let private compareTypeToSchema
         (typeName: string)
         (fields: AnalyzedField list)
         (schemaProperties: JsonElement)
         : FieldDiscrepancy list =

         let fieldNames = fields |> List.map (fun f -> f.Name) |> Set.ofList
         let schemaPropertyNames =
             if schemaProperties.ValueKind = JsonValueKind.Object then
                 [ for prop in schemaProperties.EnumerateObject() -> prop.Name ]
                 |> Set.ofList
             else
                 Set.empty

         let unmapped =
             Set.difference fieldNames schemaPropertyNames
             |> Set.toList
             |> List.map (fun f -> UnmappedField(typeName, f))

         let orphans =
             Set.difference schemaPropertyNames fieldNames
             |> Set.toList
             |> List.map (fun p -> OrphanProperty(typeName, p))

         let mismatches =
             Set.intersect fieldNames schemaPropertyNames
             |> Set.toList
             |> List.choose (fun name ->
                 let field = fields |> List.find (fun f -> f.Name = name)
                 let schemaProp = schemaProperties.GetProperty(name)
                 let expected = expectedSchemaType field.Kind
                 match schemaProp.TryGetProperty("type") with
                 | true, actualType ->
                     let actual = actualType.GetString()
                     if expected <> actual then
                         Some (TypeMismatch(typeName, name, expected, actual))
                     else
                         None
                 | _ -> None  // Schema uses $ref or oneOf -- skip type comparison
             )

         unmapped @ orphans @ mismatches
     ```

  2. Handle field name casing: `FSharpSchemaTransformer` converts F# PascalCase field names to camelCase in JSON Schema. The comparison must account for this:
     ```fsharp
     let private toCamelCase (s: string) =
         if String.IsNullOrEmpty s then s
         elif Char.IsLower s.[0] then s
         else string (Char.ToLowerInvariant s.[0]) + s.[1..]
     ```
     Apply `toCamelCase` to F# field names before comparing against JSON Schema property names.

  3. Handle `Option<T>` types: In JSON Schema, `Option<T>` maps to `nullable: true` on the property. The validator should not report a type mismatch when the F# field is `Option<string>` and the schema has `type: "string", nullable: true`.

  4. Handle collection types: `AnalyzedField.Kind = Collection inner` maps to `type: "array"` with `items` in JSON Schema. The validator should compare the inner type against the schema's `items.type`.

- **Files**: `src/Frank.Cli.Core/Unified/OpenApiConsistencyValidator.fs` (same file)
- **Notes**:
  - camelCase conversion is critical. Without it, every PascalCase F# field would be reported as an unmapped field and every camelCase schema property would be an orphan.
  - Route comparison: The unified model uses ASP.NET Core route templates (`/games/{gameId}`), while OpenAPI uses path parameters (`/games/{gameId}`). These should match in most cases, but handle edge cases like constraint syntax (`{gameId:guid}` vs `{gameId}` with `format: uuid`).
  - Do NOT compare internal fields (fields with `internal` or `private` visibility). The unified extractor should already filter these, but add a defensive check.

### Subtask T065 -- Wire `frank-cli validate --project <fsproj> --openapi` command

- **Purpose**: Integrate the OpenAPI consistency validator into the CLI's command structure so users can invoke it from the command line.

- **Steps**:
  1. Create `src/Frank.Cli.Core/Commands/OpenApiValidateCommand.fs`:
     ```fsharp
     module Frank.Cli.Core.Commands.OpenApiValidateCommand

     open Frank.Cli.Core.Unified
     open Frank.Statecharts.Validation

     type OpenApiValidateResult =
         { Report: ValidationReport
           Discrepancies: OpenApiConsistencyValidator.FieldDiscrepancy list }

     /// Execute OpenAPI consistency validation.
     /// Requires unified extraction state and an OpenAPI document.
     let execute
         (projectPath: string)
         (openApiDocPath: string option)
         : Result<OpenApiValidateResult, string> =
         ...
     ```

  2. The command flow:
     - Load the unified extraction state from cache (`obj/frank-cli/unified-state.bin`)
     - If no `--openapi` document path is provided, attempt to fetch it from the running app via TestHost (integration mode) or look for a pre-generated `openapi.json` in the project directory
     - Parse the OpenAPI document's `components/schemas` section
     - Call `OpenApiConsistencyValidator.validate` with the unified model's types and the OpenAPI schemas
     - Convert the `ConsistencyResult` to a `ValidationReport` for consistent output

  3. Add the command module to `Frank.Cli.Core.fsproj` compile order, after existing command files and before output formatters.

  4. In `src/Frank.Cli/Program.fs`, wire the `--openapi` flag to the validate command:
     ```fsharp
     | "validate" ->
         match args with
         | "--openapi" :: rest -> OpenApiValidateCommand.execute ...
         | _ -> // existing semantic validation
     ```

- **Files**: `src/Frank.Cli.Core/Commands/OpenApiValidateCommand.fs` (NEW, ~80-120 lines), `src/Frank.Cli/Program.fs` (MODIFY), `src/Frank.Cli.Core/Frank.Cli.Core.fsproj` (MODIFY compile order)
- **Notes**:
  - The command should fail gracefully if the unified extraction state does not exist (prompt user to run `frank-cli extract --project` first).
  - The OpenAPI document can come from multiple sources: a file path (provided by user), the project's build output, or fetched via TestHost. Start with file path support; TestHost integration can be added later.
  - Consider supporting `--openapi-url` for fetching from a running server in future iterations.

### Subtask T066 -- Format results using existing `ValidationReport` structure

- **Purpose**: Reuse the established validation output formatting to provide consistent CLI output across all validation commands (statechart validation, semantic validation, OpenAPI consistency validation).

- **Steps**:
  1. Convert `OpenApiConsistencyValidator.ConsistencyResult` to `Frank.Statecharts.Validation.ValidationReport`:
     ```fsharp
     let toValidationReport (result: ConsistencyResult) : ValidationReport =
         let checks =
             result.Discrepancies
             |> List.map (fun d ->
                 match d with
                 | UnmappedField(typeName, fieldName) ->
                     { Name = $"openapi.field.{typeName}.{fieldName}"
                       Status = Fail
                       Reason = Some $"F# field '{fieldName}' on type '{typeName}' is not in OpenAPI schema"
                       Category = "openapi-consistency"
                       Tags = [ "unmapped-field" ] }
                 | OrphanProperty(schemaName, propName) ->
                     { Name = $"openapi.property.{schemaName}.{propName}"
                       Status = Fail
                       Reason = Some $"OpenAPI property '{propName}' on schema '{schemaName}' has no corresponding F# field"
                       Category = "openapi-consistency"
                       Tags = [ "orphan-property" ] }
                 | TypeMismatch(typeName, fieldName, expected, actual) ->
                     { Name = $"openapi.type.{typeName}.{fieldName}"
                       Status = Fail
                       Reason = Some $"Type mismatch for '{fieldName}': F# expects '{expected}', OpenAPI has '{actual}'"
                       Category = "openapi-consistency"
                       Tags = [ "type-mismatch" ] }
                 | RouteDiscrepancy(unified, openApi) ->
                     { Name = $"openapi.route.{unified}"
                       Status = Fail
                       Reason = Some $"Route mismatch: unified model has '{unified}', OpenAPI has '{openApi}'"
                       Category = "openapi-consistency"
                       Tags = [ "route-discrepancy" ] })

         { Checks = checks
           TotalChecks = result.CheckedTypes + result.CheckedProperties
           TotalFailures = result.Discrepancies |> List.length
           TotalSkipped = 0 }
     ```

  2. Use the existing `ValidationReportFormatter.formatText` and `ValidationReportFormatter.formatJson` functions for output. These are already wired up in the statechart validate command and produce ANSI-colored text or structured JSON.

  3. The `--output-format text|json` flag should work identically to the statechart validate command:
     ```fsharp
     match outputFormat with
     | "json" -> ValidationReportFormatter.formatJson report
     | _ -> ValidationReportFormatter.formatText report
     ```

- **Files**: `src/Frank.Cli.Core/Commands/OpenApiValidateCommand.fs` (same file as T065)
- **Notes**:
  - The `ValidationReport` type is in `Frank.Statecharts.Validation`. Check the exact type signature:
    ```fsharp
    type ValidationReport =
        { Checks: ValidationCheck list
          TotalChecks: int
          TotalFailures: int
          TotalSkipped: int }
    ```
  - Ensure the `ValidationCheck` type has all the fields used above (`Name`, `Status`, `Reason`, `Category`, `Tags`). If it does not have `Category` or `Tags`, either add them (if this is the first use) or omit them from the conversion.
  - When all checks pass (zero discrepancies), the report should show a "PASSED" status with the count of checked types and properties.

### Subtask T067 -- Write tests for OpenAPI consistency validation

- **Purpose**: Verify that the validator correctly detects intentional type/OpenAPI mismatches and reports them with the expected discrepancy types.

- **Steps**:
  1. Create test file in the CLI Core tests project (e.g., `test/Frank.Cli.Core.Tests/OpenApiConsistencyValidatorTests.fs`).

  2. Build test fixtures:

     **Fixture 1 -- Consistent types** (all checks pass):
     ```fsharp
     let consistentTypes = [
         { FullName = "Game"
           Fields = [
               { Name = "Board"; Kind = Primitive "string" }
               { Name = "CurrentTurn"; Kind = Primitive "string" }
           ]
           ... }
     ]

     let consistentSchema = """
     {
       "Game": {
         "type": "object",
         "properties": {
           "board": { "type": "string" },
           "currentTurn": { "type": "string" }
         }
       }
     }
     """
     ```

     **Fixture 2 -- Unmapped F# field**:
     - Add an `InternalState` field to the F# type that is not in the OpenAPI schema
     - Expect: `UnmappedField("Game", "InternalState")`

     **Fixture 3 -- Orphan OpenAPI property**:
     - Add a `"score"` property to the OpenAPI schema that has no F# field
     - Expect: `OrphanProperty("Game", "score")`

     **Fixture 4 -- Type mismatch**:
     - F# field `Score: int`, OpenAPI property `"score": { "type": "string" }`
     - Expect: `TypeMismatch("Game", "Score", "integer", "string")`

     **Fixture 5 -- camelCase normalization**:
     - F# field `CurrentTurn`, OpenAPI property `currentTurn` (camelCase)
     - Expect: NO discrepancy (casing is normalized)

     **Fixture 6 -- Option type handling**:
     - F# field `Winner: string option`, OpenAPI property `"winner": { "type": "string", "nullable": true }`
     - Expect: NO discrepancy (Option maps to nullable)

  3. Test the `toValidationReport` conversion:
     ```fsharp
     testCase "ValidationReport from discrepancies has correct counts" (fun () ->
         let result = {
             Discrepancies = [ UnmappedField("Game", "secret"); OrphanProperty("Game", "extra") ]
             CheckedTypes = 1
             CheckedProperties = 4
             IsConsistent = false
         }
         let report = toValidationReport result
         Expect.equal report.TotalFailures 2 "Should have 2 failures"
         Expect.isFalse (report.TotalFailures = 0) "Should not pass"
     )
     ```

  4. Test the full command flow (if integration tests are feasible):
     ```fsharp
     testCase "validate --openapi with consistent project reports success" (fun () ->
         // This requires a unified extraction state and an OpenAPI document
         // May be better as an integration test in WP13 (tic-tac-toe end-to-end)
         ...
     )
     ```

  5. Run tests: `dotnet test test/Frank.Cli.Core.Tests/`

- **Files**: `test/Frank.Cli.Core.Tests/OpenApiConsistencyValidatorTests.fs` (NEW, ~200-250 lines)
- **Notes**:
  - The unit tests operate on mock `AnalyzedType` lists and parsed `JsonElement` (from inline JSON strings). No project loading or FCS analysis needed.
  - Integration tests (full command flow with a real project) are better placed in WP13 (tic-tac-toe end-to-end validation).
  - Ensure the `AnalyzedType` constructor matches the actual type definition. Check `src/Frank.Cli.Core/Analysis/TypeAnalyzer.fs` for the current type shape.
  - Consider testing with DU types that produce `oneOf` schemas -- these are a common source of mismatches.

---

## Test Strategy

- **Unit tests**: Core comparison logic tested with mock `AnalyzedType` lists and inline JSON schemas. Pure functions, no I/O.
- **Integration tests**: Deferred to WP13 (tic-tac-toe end-to-end) where a real project with both unified extraction and OpenAPI is available.
- **Test framework**: Expecto (matching existing Frank test conventions).
- **Commands**:
  ```bash
  dotnet build src/Frank.Cli.Core/Frank.Cli.Core.fsproj
  dotnet test test/Frank.Cli.Core.Tests/
  ```
- **Coverage targets**: All four discrepancy types (unmapped field, orphan property, type mismatch, route discrepancy), camelCase normalization, Option/nullable handling, consistent project produces zero discrepancies.

---

## Risks & Mitigations

| Risk | Mitigation |
|------|------------|
| `FSharp.Data.JsonSchema.OpenApi` API is not easily usable outside the OpenAPI middleware context | Replicate mapping decisions locally with clear documentation of which version's decisions are used. Add a comment referencing the NuGet package version. |
| camelCase normalization misses edge cases (acronyms like `ID` -> `id`, `URL` -> `url`) | Use the same camelCase logic that `FSharp.Data.JsonSchema` uses internally. Test with common F# naming patterns. |
| `AnalyzedType` changes shape between WP02 implementation and WP11 | Design the validator to be defensive: if expected fields are missing, skip the check rather than crash. Report skipped checks in the report. |
| OpenAPI document format varies (OpenAPI 3.0 vs 3.1) | Standardize on OpenAPI 3.0 (which `Frank.OpenApi` currently generates). Document the expected format. |
| `ValidationCheck` type may not have `Category` and `Tags` fields | Check the type definition before implementation. If missing, use only the fields that exist. |

---

## Review Guidance

- Verify the `FSharp.Data.JsonSchema.OpenApi` NuGet dependency is added to `Frank.Cli.Core.fsproj`.
- Verify camelCase normalization is applied when comparing F# field names to JSON Schema property names.
- Verify all four discrepancy types are detected correctly.
- Verify `Option<T>` types do not produce false positives when the schema uses `nullable: true`.
- Verify the `ValidationReport` conversion produces correct check counts.
- Verify the command is wired into `Program.fs` with the `--openapi` flag.
- Verify the compile order in `Frank.Cli.Core.fsproj` places the new files correctly (after unified model types, before output formatters).
- Run `dotnet build Frank.sln` and `dotnet test test/Frank.Cli.Core.Tests/` to verify clean build and passing tests.
- Cross-check: existing semantic and statechart validation commands still work after adding the new validation command.

---

## Activity Log

> **CRITICAL**: Activity log entries MUST be in chronological order (oldest first, newest last).

### How to Add Activity Log Entries

**When adding an entry**:
1. Scroll to the bottom of this file (Activity Log section below "Valid lanes")
2. **APPEND the new entry at the END** (do NOT prepend or insert in middle)
3. Use exact format: `- YYYY-MM-DDTHH:MM:SSZ -- agent_id -- lane=<lane> -- <action>`
4. Timestamp MUST be current time in UTC (check with `date -u "+%Y-%m-%dT%H:%M:%SZ"`)
5. Lane MUST match the frontmatter `lane:` field exactly
6. Agent ID should identify who made the change (claude-sonnet-4-5, codex, etc.)

**Format**:
```
- YYYY-MM-DDTHH:MM:SSZ -- <agent_id> -- lane=<lane> -- <brief action description>
```

**Valid lanes**: `planned`, `doing`, `for_review`, `done`

**Initial entry**:
- 2026-03-19T02:15:00Z -- system -- lane=planned -- Prompt created.
- 2026-03-19T04:06:36Z – claude-opus – shell_pid=26221 – lane=doing – Assigned agent via workflow command
- 2026-03-19T04:16:59Z – claude-opus – shell_pid=26221 – lane=for_review – Ready for review: OpenAPI consistency validator with 4 discrepancy types, camelCase normalization, CLI wiring, and 9 tests
