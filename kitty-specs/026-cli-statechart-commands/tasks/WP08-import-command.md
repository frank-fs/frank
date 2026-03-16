---
work_package_id: "WP08"
title: "Import Command"
lane: "planned"
dependencies: ["WP01", "WP03", "WP04"]
subtasks:
  - "T048"
  - "T049"
  - "T050"
  - "T051"
  - "T052"
  - "T053"
  - "T054"
assignee: ""
agent: ""
shell_pid: ""
review_status: ""
reviewed_by: ""
history:
  - timestamp: "2026-03-16T19:12:54Z"
    lane: "planned"
    agent: "system"
    shell_pid: ""
    action: "Prompt generated via /spec-kitty.tasks"
---

# Work Package Prompt: WP08 -- Import Command

## Important: Review Feedback Status

**Read this first if you are implementing this task!**

- **Has review feedback?**: Check the `review_status` field above. If it says `has_feedback`, scroll to the **Review Feedback** section immediately.
- **Mark as acknowledged**: When you understand the feedback and begin addressing it, update `review_status: acknowledged` in the frontmatter.

---

## Review Feedback

*[This section is empty initially. Reviewers will populate it if the work is returned from review.]*

---

## Implementation Command

Depends on WP01, WP03, and WP04:

```bash
spec-kitty implement WP08 --base WP04
```

---

## Objectives & Success Criteria

1. Create `StatechartImportCommand` module implementing `frank statechart import <spec-file>`.
2. Detect format from file extension using `FormatDetector`.
3. Parse the spec file using the appropriate format parser.
4. Output the parsed `StatechartDocument` as JSON.
5. Include parse errors/warnings in output when present.
6. Exit with non-zero code when parse errors exist.

**Success**: Command parses files in all 5 formats and produces JSON `StatechartDocument` output. Parse errors are included in the output.

---

## Context & Constraints

- **Spec**: `kitty-specs/026-cli-statechart-commands/spec.md` -- User Story 4 (FR-026 through FR-031)
- **Plan**: `kitty-specs/026-cli-statechart-commands/plan.md` -- Import Command Pipeline
- **Dependencies**:
  - `FormatDetector.detect` from WP03
  - `StatechartDocumentJson.serializeParseResult` from WP04
  - `XState.Deserializer.deserialize` from WP01
  - Existing parsers (all `internal`, accessible via `InternalsVisibleTo` from WP01):
    - `Wsd.Parser.parseWsd` -> `Ast.ParseResult`
    - `Alps.JsonParser.parseAlpsJson` -> `Result<AlpsDocument, ...>`
    - `Scxml.Parser.parseString` -> `ScxmlParseResult`
    - `Smcat.Parser.parseSmcat` -> `Smcat.Types.ParseResult`
- **No assembly loading needed**: Import works on spec files only, not compiled assemblies.

---

## Subtasks & Detailed Guidance

### Subtask T048 -- Create StatechartImportCommand.fs

- **Purpose**: Implement the import command logic.
- **Steps**:
  1. Create `src/Frank.Cli.Core/Commands/StatechartImportCommand.fs`
  2. Module declaration: `module Frank.Cli.Core.Commands.StatechartImportCommand`
  3. Define result type:
     ```fsharp
     type ImportResult =
         { ParseResult: Ast.ParseResult
           Format: FormatTag
           HasErrors: bool }
     ```
  4. Implement `execute`:
     ```fsharp
     let execute
         (specFile: string)
         (explicitFormat: string option)
         : Result<ImportResult, string>
     ```

- **Files**: `src/Frank.Cli.Core/Commands/StatechartImportCommand.fs` (NEW, ~80-130 lines)

### Subtask T049 -- Implement format detection from extension

- **Purpose**: Determine which parser to use based on the file extension (FR-027).
- **Steps**:
  1. Call `FormatDetector.detect specFile`
  2. Handle each `DetectionResult` case:
     - `Detected tag` -> use `tag` to select parser
     - `Ambiguous candidates` -> check if `explicitFormat` is provided
     - `Unsupported ext` -> return error with supported extensions list
  3. When `explicitFormat` is `Some fmt`, parse it and override detection:
     ```fsharp
     match fmt.ToLowerInvariant() with
     | "wsd" -> Ok Wsd
     | "alps" -> Ok Alps
     | "scxml" -> Ok Scxml
     | "smcat" -> Ok Smcat
     | "xstate" -> Ok XState
     | other -> Error $"Unknown format: '{other}'"
     ```

- **Files**: `src/Frank.Cli.Core/Commands/StatechartImportCommand.fs`

### Subtask T050 -- Implement parser dispatch

- **Purpose**: Call the correct parser for each format (FR-028).
- **Steps**:
  1. Read file contents:
     ```fsharp
     if not (File.Exists specFile) then
         Error $"File not found: {specFile}"
     else
         let content = File.ReadAllText(specFile)
     ```
  2. Dispatch to parser based on detected format:

     **WSD**:
     ```fsharp
     | Wsd ->
         let result = Wsd.Parser.parseWsd content
         Ok { ParseResult = result; Format = Wsd; HasErrors = not result.Errors.IsEmpty }
     ```

     **ALPS**:
     ```fsharp
     | Alps ->
         match Alps.JsonParser.parseAlpsJson content with
         | Ok alpsDoc ->
             let doc = Alps.Mapper.toStatechartDocument alpsDoc
             Ok { ParseResult = { Document = doc; Errors = []; Warnings = [] }
                  Format = Alps; HasErrors = false }
         | Error e ->
             // Create ParseResult with error
             let failure: ParseFailure =
                 { Position = None; Description = e
                   Expected = "valid ALPS JSON"; Found = "parse error"
                   CorrectiveExample = "" }
             let emptyDoc: StatechartDocument =
                 { Title = None; InitialStateId = None
                   Elements = []; DataEntries = []; Annotations = [] }
             Ok { ParseResult = { Document = emptyDoc; Errors = [ failure ]; Warnings = [] }
                  Format = Alps; HasErrors = true }
     ```

     **SCXML**:
     ```fsharp
     | Scxml ->
         let scxmlResult = Scxml.Parser.parseString content
         let doc = Scxml.Mapper.toStatechartDocument scxmlResult
         // Map SCXML-specific errors/warnings to AST types
         Ok { ParseResult = { Document = doc; Errors = []; Warnings = [] }
              Format = Scxml; HasErrors = false }
     ```

     **smcat**:
     ```fsharp
     | Smcat ->
         let smcatResult = Smcat.Parser.parseSmcat content
         let doc = Smcat.Mapper.toStatechartDocument smcatResult
         Ok { ParseResult = { Document = doc; Errors = []; Warnings = [] }
              Format = Smcat; HasErrors = false }
     ```

     **XState**:
     ```fsharp
     | XState ->
         let result = XState.Deserializer.deserialize content
         Ok { ParseResult = result; Format = XState
              HasErrors = not result.Errors.IsEmpty }
     ```

  3. **IMPORTANT**: Read the actual parser source files to verify function signatures and return types. The above is based on plan.md but actual signatures may differ:
     - Check `src/Frank.Statecharts/Alps/JsonParser.fs` for `parseAlpsJson` signature
     - Check `src/Frank.Statecharts/Scxml/Parser.fs` for `parseString` signature and return type
     - Check `src/Frank.Statecharts/Smcat/Parser.fs` for `parseSmcat` signature and return type
     - Check `src/Frank.Statecharts/Alps/Mapper.fs` for `toStatechartDocument` signature
     - Check `src/Frank.Statecharts/Scxml/Mapper.fs` for `toStatechartDocument` signature
     - Check `src/Frank.Statecharts/Smcat/Mapper.fs` for `toStatechartDocument` signature

- **Files**: `src/Frank.Cli.Core/Commands/StatechartImportCommand.fs`
- **Notes**: Each parser has a different return type. The import command normalizes them all to `Ast.ParseResult`. Some parsers return errors inline; others return `Result<doc, error>`. Handle both patterns.

### Subtask T051 -- Implement ambiguous .json handling

- **Purpose**: When a `.json` file is provided without `--format`, try both ALPS and XState parsers (FR-031).
- **Steps**:
  1. When `FormatDetector.detect` returns `Ambiguous [ Alps; XState ]`:
     a. If `explicitFormat` is provided (via `--format`), use it directly.
     b. If `explicitFormat` is `None`, try both parsers:
        ```fsharp
        let alpsResult = tryParseAs Alps content
        let xstateResult = tryParseAs XState content
        match alpsResult, xstateResult with
        | Ok a, Error _ -> Ok a  // Only ALPS succeeded
        | Error _, Ok x -> Ok x  // Only XState succeeded
        | Ok a, Ok x ->
            // Both succeeded -- ambiguous, prefer ALPS if it has an "alps" key
            // or require --format flag
            Error "Ambiguous .json file: could be ALPS or XState JSON. Use --format alps or --format xstate to disambiguate."
        | Error ae, Error xe ->
            Error $"Could not parse .json file as ALPS ({ae}) or XState ({xe}). Use --format to specify."
        ```
  2. The `tryParseAs` function wraps each parser in try/with to catch parse failures.

- **Files**: `src/Frank.Cli.Core/Commands/StatechartImportCommand.fs`
- **Notes**: For ALPS, check if the JSON has an `"alps"` root key as a disambiguation heuristic. The `--format` flag name matches spec FR-031.

### Subtask T052 -- Implement JSON output of StatechartDocument

- **Purpose**: Serialize the parsed result to JSON for output (FR-026, FR-029).
- **Steps**:
  1. Add output formatters:

  **Text output** (in `TextOutput.fs`):
  ```fsharp
  let formatStatechartImportResult (result: StatechartImportCommand.ImportResult) : string =
      StatechartDocumentJson.serializeParseResult result.ParseResult
  ```
  Note: Even text output for import is JSON (the StatechartDocument itself). The text/json distinction is mainly for status/error messages.

  **JSON output** (in `JsonOutput.fs`):
  ```fsharp
  let formatStatechartImportResult (result: StatechartImportCommand.ImportResult) : string =
      // Wrap in status envelope
      let sb = StringBuilder()
      // ... or use StatechartDocumentJson.serializeParseResult
  ```

  2. The import command's primary output IS the JSON representation of the parsed document. The `--format` flag is for format disambiguation only (per FR-031), not output format.

- **Files**: `src/Frank.Cli.Core/Output/TextOutput.fs`, `src/Frank.Cli.Core/Output/JsonOutput.fs`

### Subtask T053 -- Handle parse errors and exit codes

- **Purpose**: Include errors/warnings in output and set non-zero exit on errors (FR-029, FR-030).
- **Steps**:
  1. When `result.HasErrors = true`:
     - The output still includes the best-effort parsed document (FR-029)
     - The CLI wiring (WP09) sets `Environment.ExitCode <- 1`
  2. Errors and warnings are part of the `ParseResult` structure, serialized by `StatechartDocumentJson.serializeParseResult`.

- **Files**: `src/Frank.Cli.Core/Commands/StatechartImportCommand.fs`

### Subtask T054 -- Add compile entry to Frank.Cli.Core.fsproj

- **Purpose**: Register the new command module.
- **Steps**:
  1. Add after `StatechartValidateCommand.fs`:
     ```xml
     <Compile Include="Commands/StatechartImportCommand.fs" />
     ```

- **Files**: `src/Frank.Cli.Core/Frank.Cli.Core.fsproj`

---

## Risks & Mitigations

| Risk | Mitigation |
|------|------------|
| Parser function signatures differ from documentation | Read actual source files before implementing dispatch. |
| ALPS/SCXML mappers may not have `toStatechartDocument` | Check mapper source for actual function name (may be different direction). |
| Ambiguous `.json` detection | Provide clear error message with `--format` flag suggestion. |

---

## Review Guidance

- Verify parser dispatch works for all 5 formats.
- Verify ambiguous `.json` handling follows FR-031.
- Verify parse errors are included in output (not swallowed).
- Verify exit code is non-zero when parse errors exist.
- Verify best-effort document is always present (even with errors).
- Verify `dotnet build` passes cleanly.

---

## Activity Log

- 2026-03-16T19:12:54Z -- system -- lane=planned -- Prompt created.
