# Spec 026 Quality Fixes Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Fix all critical and important code review findings from the spec 026 (CLI statechart commands) review — typed error DUs, explicit serialization, no silent failures, proper test coverage.

**Architecture:** Replace stringly-typed errors with a `StatechartError` DU in a new shared types file. Extract ANSI color helpers to a shared module. Fix JSON serialization to preserve AST hierarchy and use explicit toString functions. Add Expecto unit tests for pure-function modules.

**Tech Stack:** F# 8.0+ / .NET 10.0 / Expecto / System.CommandLine 2.0.3

**Worktree:** `/Users/ryanr/Code/frank/.worktrees/026-cli-statechart-commands-WP10`

---

## File Map

| Action | Path | Responsibility |
|--------|------|---------------|
| Create | `src/Frank.Cli.Core/Statechart/StatechartError.fs` | Typed error DU for all statechart commands |
| Create | `src/Frank.Cli.Core/Output/AnsiColors.fs` | Shared ANSI color helpers (isColorEnabled, bold, red, green, yellow) |
| Modify | `src/Frank.Cli.Core/Statechart/StatechartExtractor.fs` | Use typed errors, mark extractFromAssembly as NotImplementedException |
| Modify | `src/Frank.Cli.Core/Statechart/FormatDetector.fs` | Add FormatTag.toString |
| Modify | `src/Frank.Cli.Core/Statechart/FormatPipeline.fs` | Use typed errors |
| Modify | `src/Frank.Cli.Core/Statechart/StatechartDocumentJson.fs` | Use StateKind.toString, preserve AST hierarchy, include notes/directives, add format field to parse output |
| Modify | `src/Frank.Cli.Core/Statechart/ValidationReportFormatter.fs` | Use shared AnsiColors, remove duplicated helpers |
| Modify | `src/Frank.Cli.Core/Commands/StatechartExtractCommand.fs` | Use typed errors |
| Modify | `src/Frank.Cli.Core/Commands/StatechartGenerateCommand.fs` | Use typed errors, report generation failures instead of swallowing |
| Modify | `src/Frank.Cli.Core/Commands/StatechartValidateCommand.fs` | Use typed errors |
| Modify | `src/Frank.Cli.Core/Commands/StatechartParseCommand.fs` | Use typed errors, remove dead emptyDocument, add format to result serialization |
| Modify | `src/Frank.Cli.Core/Output/TextOutput.fs` | Use shared AnsiColors, replace sprintf "%A" with toString |
| Modify | `src/Frank.Cli.Core/Output/JsonOutput.fs` | Replace sprintf "%A" with toString, uniform error envelope |
| Modify | `src/Frank.Cli/Program.fs` | Fix parse --output-format, uniform JSON error envelopes, fix --version |
| Modify | `src/Frank.Statecharts/build/Frank.Statecharts.targets` | Use --help instead of --version for availability check |
| Modify | `src/Frank.Cli.Core/Frank.Cli.Core.fsproj` | Add new files in correct compile order |
| Create | `test/Frank.Cli.Core.Tests/Statechart/FormatDetectorTests.fs` | Unit tests for format detection |
| Create | `test/Frank.Cli.Core.Tests/Statechart/FormatPipelineTests.fs` | Unit tests for format pipeline (resourceSlug, parseFormat) |
| Create | `test/Frank.Cli.Core.Tests/Statechart/StatechartDocumentJsonTests.fs` | Unit tests for JSON serialization |
| Create | `test/Frank.Cli.Core.Tests/Statechart/ValidationReportFormatterTests.fs` | Unit tests for report formatting |
| Modify | `test/Frank.Cli.Core.Tests/Frank.Cli.Core.Tests.fsproj` | Add new test files |

---

## Task 1: Create StatechartError DU

**Files:**
- Create: `src/Frank.Cli.Core/Statechart/StatechartError.fs`
- Modify: `src/Frank.Cli.Core/Frank.Cli.Core.fsproj`

This is the foundation — every subsequent task uses this type.

- [ ] **Step 1: Create the error DU**

```fsharp
module Frank.Cli.Core.Statechart.StatechartError

open Frank.Statecharts.Validation

/// Typed errors for statechart CLI commands.
/// Each case carries structured data for programmatic handling.
type StatechartError =
    // Assembly loading errors
    | AssemblyNotFound of path: string
    | AssemblyLoadFailed of path: string * reason: string
    | InvalidAssemblyFormat of path: string * reason: string
    | MissingDependency of assemblyName: string * reason: string
    | AssemblyLoadError of path: string * reason: string
    | ExtractionNotImplemented
    // Format errors
    | UnknownFormat of format: string
    | UnsupportedFileExtension of extension: string * filePath: string
    | AmbiguousFileExtension of filePath: string * candidates: FormatTag list
    // File errors
    | FileNotFound of path: string
    | FileReadError of path: string * reason: string
    // Parse errors
    | ParseFailed of filePath: string * errors: string list
    | AmbiguousParseFailed of filePath: string * attempts: string list
    // Generation errors
    | GenerationFailed of format: FormatTag * resourceSlug: string * reason: string
    | UnrecognizedMachineType of typeName: string
    // Resource errors
    | ResourceNotFound of name: string * available: string list
    // Validation errors
    | CodeTruthExtractionFailed of reason: string

/// Render a StatechartError to a human-readable string.
let formatError (error: StatechartError) : string =
    match error with
    | AssemblyNotFound path -> sprintf "Assembly not found: %s" path
    | AssemblyLoadFailed(path, reason) -> sprintf "Failed to load assembly '%s': %s" path reason
    | InvalidAssemblyFormat(path, reason) -> sprintf "Invalid assembly format '%s': %s" path reason
    | MissingDependency(name, reason) -> sprintf "Missing dependency '%s': %s" name reason
    | AssemblyLoadError(path, reason) -> sprintf "Unexpected error loading '%s': %s" path reason
    | ExtractionNotImplemented ->
        "Assembly-based extraction is not yet implemented. Use 'statechart parse' to parse spec files directly."
    | UnknownFormat fmt ->
        sprintf "Unknown format: '%s'. Supported: wsd, alps, scxml, smcat, xstate" fmt
    | UnsupportedFileExtension(ext, path) ->
        sprintf "Unsupported file extension '%s' for '%s'. Supported: .wsd, .alps.json, .scxml, .smcat, .xstate.json" ext path
    | AmbiguousFileExtension(path, _) ->
        sprintf "Ambiguous file extension for '%s'. Use a compound extension (.alps.json, .xstate.json) or --format to disambiguate." path
    | FileNotFound path -> sprintf "File not found: %s" path
    | FileReadError(path, reason) -> sprintf "Cannot read file '%s': %s" path reason
    | ParseFailed(path, errors) ->
        sprintf "Parse errors in '%s': %s" path (errors |> String.concat "; ")
    | AmbiguousParseFailed(path, attempts) ->
        sprintf "Could not parse '%s' as any supported format. Attempts: %s" path (attempts |> String.concat "; ")
    | GenerationFailed(format, slug, reason) ->
        sprintf "Failed to generate %s for resource '%s': %s" (FormatTag.toString format) slug reason
    | UnrecognizedMachineType typeName ->
        sprintf "Unrecognized machine type: %s" typeName
    | ResourceNotFound(name, available) ->
        sprintf "No resource matching '%s' found. Available: %s" name (available |> String.concat ", ")
    | CodeTruthExtractionFailed reason ->
        sprintf "Code-truth extraction failed: %s" reason

/// Render a StatechartError to a structured JSON string for machine consumption.
let formatErrorJson (error: StatechartError) : string =
    let errorCode =
        match error with
        | AssemblyNotFound _ -> "assembly_not_found"
        | AssemblyLoadFailed _ -> "assembly_load_failed"
        | InvalidAssemblyFormat _ -> "invalid_assembly_format"
        | MissingDependency _ -> "missing_dependency"
        | AssemblyLoadError _ -> "assembly_load_error"
        | ExtractionNotImplemented -> "extraction_not_implemented"
        | UnknownFormat _ -> "unknown_format"
        | UnsupportedFileExtension _ -> "unsupported_extension"
        | AmbiguousFileExtension _ -> "ambiguous_extension"
        | FileNotFound _ -> "file_not_found"
        | FileReadError _ -> "file_read_error"
        | ParseFailed _ -> "parse_failed"
        | AmbiguousParseFailed _ -> "ambiguous_parse_failed"
        | GenerationFailed _ -> "generation_failed"
        | UnrecognizedMachineType _ -> "unrecognized_machine_type"
        | ResourceNotFound _ -> "resource_not_found"
        | CodeTruthExtractionFailed _ -> "code_truth_extraction_failed"

    use stream = new System.IO.MemoryStream()
    use writer = new System.Text.Json.Utf8JsonWriter(stream, System.Text.Json.JsonWriterOptions(Indented = true))
    writer.WriteStartObject()
    writer.WriteString("status", "error")
    writer.WriteString("code", errorCode)
    writer.WriteString("message", formatError error)
    writer.WriteEndObject()
    writer.Flush()
    System.Text.Encoding.UTF8.GetString(stream.ToArray())
```

Note: `FormatTag.toString` is defined in Task 2. This file depends on it — compile order must place this after FormatDetector.

- [ ] **Step 2: Add FormatTag.toString to FormatDetector.fs**

Add to the end of the `FormatDetector` module in `src/Frank.Cli.Core/Statechart/FormatDetector.fs`:

```fsharp
/// Canonical string representation of a FormatTag for display and serialization.
/// Do NOT use sprintf "%A" — this is the stable, explicit mapping.
module FormatTag =
    let toString (tag: FormatTag) : string =
        match tag with
        | Wsd -> "WSD"
        | Alps -> "ALPS"
        | Scxml -> "SCXML"
        | Smcat -> "smcat"
        | XState -> "XState"

    let toLower (tag: FormatTag) : string =
        match tag with
        | Wsd -> "wsd"
        | Alps -> "alps"
        | Scxml -> "scxml"
        | Smcat -> "smcat"
        | XState -> "xstate"
```

**Important:** This must be a nested module inside the file, NOT a type extension on `FormatTag` (which is defined in `Frank.Statecharts.Validation` — a different project). Alternatively, add it as standalone functions in the `FormatDetector` module.

- [ ] **Step 3: Add StateKind.toString to StatechartDocumentJson.fs**

Add a private helper at the top of `src/Frank.Cli.Core/Statechart/StatechartDocumentJson.fs`:

```fsharp
let private stateKindToString (kind: StateKind) : string =
    match kind with
    | Regular -> "Regular"
    | Initial -> "Initial"
    | Final -> "Final"
    | Parallel -> "Parallel"
    | ShallowHistory -> "ShallowHistory"
    | DeepHistory -> "DeepHistory"
    | Choice -> "Choice"
    | ForkJoin -> "ForkJoin"
    | Terminate -> "Terminate"
```

Then replace `sprintf "%A" state.Kind` with `stateKindToString state.Kind`.

- [ ] **Step 4: Update fsproj compile order**

In `src/Frank.Cli.Core/Frank.Cli.Core.fsproj`, reorder the Statechart section. **CRITICAL:** `StatechartExtractor.fs` currently compiles FIRST (before FormatDetector). After this change, it depends on `StatechartError`, which depends on `FormatDetector`. Also add `AnsiColors.fs` BEFORE the Statechart section (ValidationReportFormatter needs it).

The complete corrected compile order for these sections:

```xml
    <!-- Shared output helpers -->
    <Compile Include="Output/AnsiColors.fs" />
    <!-- Statechart pipeline -->
    <Compile Include="Statechart/FormatDetector.fs" />
    <Compile Include="Statechart/StatechartError.fs" />
    <Compile Include="Statechart/StatechartExtractor.fs" />
    <Compile Include="Statechart/FormatPipeline.fs" />
    <Compile Include="Statechart/StatechartDocumentJson.fs" />
    <Compile Include="Statechart/ValidationReportFormatter.fs" />
```

Note: `AnsiColors.fs` is placed before the Statechart section because `ValidationReportFormatter.fs` opens it. This is done here in Task 1 even though Task 2 creates the file — the fsproj entry can exist before the file does (build will warn but not fail for later tasks).

- [ ] **Step 5: Build and verify**

Run: `dotnet build src/Frank.Cli.Core/Frank.Cli.Core.fsproj`
Expected: Build succeeds (the new file is not yet referenced by other modules)

- [ ] **Step 6: Commit**

```bash
git add src/Frank.Cli.Core/Statechart/StatechartError.fs src/Frank.Cli.Core/Statechart/FormatDetector.fs src/Frank.Cli.Core/Statechart/StatechartDocumentJson.fs src/Frank.Cli.Core/Frank.Cli.Core.fsproj
git commit -m "feat: add StatechartError DU, FormatTag.toString, StateKind.toString"
```

---

## Task 2: Extract shared AnsiColors module

**Files:**
- Create: `src/Frank.Cli.Core/Output/AnsiColors.fs`
- Modify: `src/Frank.Cli.Core/Statechart/ValidationReportFormatter.fs`
- Modify: `src/Frank.Cli.Core/Output/TextOutput.fs`
- Modify: `src/Frank.Cli.Core/Frank.Cli.Core.fsproj`

- [ ] **Step 1: Create AnsiColors.fs**

```fsharp
module Frank.Cli.Core.Output.AnsiColors

open System

let isColorEnabled () =
    let noColor = Environment.GetEnvironmentVariable("NO_COLOR")
    isNull noColor && Console.IsOutputRedirected |> not

let bold text =
    if isColorEnabled () then sprintf "\033[1m%s\033[0m" text else text

let red text =
    if isColorEnabled () then sprintf "\033[31m%s\033[0m" text else text

let green text =
    if isColorEnabled () then sprintf "\033[32m%s\033[0m" text else text

let yellow text =
    if isColorEnabled () then sprintf "\033[33m%s\033[0m" text else text
```

- [ ] **Step 2: Verify fsproj entry**

`AnsiColors.fs` was already added to the fsproj in Task 1 Step 4 (before the Statechart section). Verify the entry exists. Do NOT add a second entry.

- [ ] **Step 3: Update ValidationReportFormatter.fs — remove duplicated helpers, open AnsiColors**

Remove lines 9-25 (the 5 duplicated functions and the comment). Add `open Frank.Cli.Core.Output.AnsiColors` after the existing opens. The `bold`, `red`, `green`, `yellow` calls throughout the file will now resolve to AnsiColors.

- [ ] **Step 4: Update TextOutput.fs — remove private helpers, open AnsiColors**

Remove the `private isColorEnabled`, `private bold`, `private yellow`, `private red`, `private green` functions (lines 9-23). Add `open Frank.Cli.Core.Output.AnsiColors` at the top.

Note: The existing TextOutput functions use `$"\033[1m{text}\033[0m"` (string interpolation) while AnsiColors uses `sprintf`. Both produce the same output. The AnsiColors version avoids the nested interpolation issue that caused build errors earlier.

- [ ] **Step 5: Build and verify**

Run: `dotnet build src/Frank.Cli.Core/Frank.Cli.Core.fsproj`
Expected: Build succeeds, 0 warnings, 0 errors

- [ ] **Step 6: Commit**

```bash
git add src/Frank.Cli.Core/Output/AnsiColors.fs src/Frank.Cli.Core/Statechart/ValidationReportFormatter.fs src/Frank.Cli.Core/Output/TextOutput.fs src/Frank.Cli.Core/Frank.Cli.Core.fsproj
git commit -m "refactor: extract shared AnsiColors module, remove duplicated color helpers"
```

---

## Task 3: Fix StatechartExtractor to use typed errors

**Files:**
- Modify: `src/Frank.Cli.Core/Statechart/StatechartExtractor.fs`

- [ ] **Step 1: Update extract function return type and error cases**

Change `extract` to return `Result<ExtractedStatechart list, StatechartError>`.

Replace string error construction with typed error cases:
- `Error $"Assembly not found: {fullPath}"` → `Error (AssemblyNotFound fullPath)`
- `Error $"Failed to load assembly: {ex.Message}"` → `Error (AssemblyLoadFailed(fullPath, ex.Message))`
- `Error $"Invalid assembly format: {ex.Message}"` → `Error (InvalidAssemblyFormat(fullPath, ex.Message))`
- `Error $"Missing dependency: {ex.FileName} — {ex.Message}"` → `Error (MissingDependency(ex.FileName, ex.Message))`
- `Error $"Unexpected error loading assembly: {ex.Message}"` → `Error (AssemblyLoadError(fullPath, ex.Message))`

Replace the `extractFromAssembly` placeholder (line 65) with:
```fsharp
raise (System.NotImplementedException(
    "Host-based extraction not yet implemented. See spec 026 WP02."))
```

And wrap the `extractFromAssembly` call in the `extract` function with:
```fsharp
try
    extractFromAssembly assembly
with
| :? System.NotImplementedException ->
    Error ExtractionNotImplemented
```

This makes the error explicit and typed rather than hiding behind a string that looks like a normal error message.

- [ ] **Step 2: Build and verify**

Run: `dotnet build src/Frank.Cli.Core/Frank.Cli.Core.fsproj`
Expected: Build errors in downstream files that still expect `Result<_, string>`. This is expected — we fix them in subsequent tasks.

- [ ] **Step 3: Commit**

```bash
git add src/Frank.Cli.Core/Statechart/StatechartExtractor.fs
git commit -m "fix: use typed StatechartError in StatechartExtractor, mark extraction as NotImplemented"
```

---

## Task 4: Fix FormatPipeline to use typed errors

**Files:**
- Modify: `src/Frank.Cli.Core/Statechart/FormatPipeline.fs`

- [ ] **Step 1: Change generateFormat return type**

Change `generateFormat` signature from `Result<string, string>` to `Result<string, StatechartError>`.

Replace error construction:
- `Error $"Unrecognized machine type: {typeName}"` → `Error (UnrecognizedMachineType typeName)`

Open `Frank.Cli.Core.Statechart.StatechartError` at the top.

- [ ] **Step 2: Build and commit**

```bash
git add src/Frank.Cli.Core/Statechart/FormatPipeline.fs
git commit -m "fix: use typed StatechartError in FormatPipeline"
```

---

## Task 5: Fix all command modules to use typed errors

**Files:**
- Modify: `src/Frank.Cli.Core/Commands/StatechartExtractCommand.fs`
- Modify: `src/Frank.Cli.Core/Commands/StatechartGenerateCommand.fs`
- Modify: `src/Frank.Cli.Core/Commands/StatechartValidateCommand.fs`
- Modify: `src/Frank.Cli.Core/Commands/StatechartParseCommand.fs`

- [ ] **Step 1: Fix StatechartExtractCommand.fs**

Change `execute` return type to `Result<ExtractResult, StatechartError>`. Open `StatechartError`.

- [ ] **Step 2: Fix StatechartGenerateCommand.fs — typed errors AND fix silent swallowing**

Change `execute` return type to `Result<GenerateResult, StatechartError>`.

Change `parseFormat` to return `Result<FormatTag list, StatechartError>`:
```fsharp
| other -> Error (UnknownFormat other)
```

**Fix I-004 (silent error swallowing):** Replace the list comprehension with explicit error collection:

```fsharp
let mutable generationErrors = []
let artifacts =
    [ for m in filtered do
          let slug = FormatPipeline.resourceSlug m.RouteTemplate
          for fmt in formats do
              match FormatPipeline.generateFormat fmt slug m.RawMetadata with
              | Ok content ->
                  { ResourceSlug = slug
                    RouteTemplate = m.RouteTemplate
                    Format = fmt
                    Content = content
                    FilePath = None }
              | Error err ->
                  generationErrors <- err :: generationErrors ]
```

Add `GenerationErrors: StatechartError list` to `GenerateResult` type. Populate it from `generationErrors`.

Update the output formatters (TextOutput, JsonOutput) to render generation errors when present:

In `TextOutput.formatStatechartGenerateResult`, after rendering artifacts, add:
```fsharp
if not result.GenerationErrors.IsEmpty then
    sb.AppendLine() |> ignore
    sb.AppendLine(red (sprintf "Generation errors (%d):" result.GenerationErrors.Length)) |> ignore
    for err in result.GenerationErrors do
        sb.AppendLine(sprintf "  - %s" (StatechartError.formatError err)) |> ignore
```

In `JsonOutput.formatStatechartGenerateResult`, add a `"generationErrors"` array after `"artifacts"`.

- [ ] **Step 3: Fix StatechartValidateCommand.fs**

Change `execute` return type to `Result<ValidateResult, StatechartError>`.

Replace all string error construction with typed cases:
- File not found → `FileNotFound filePath`
- Read error → `FileReadError(filePath, ex.Message)`
- Unsupported extension → `UnsupportedFileExtension(ext, filePath)`
- Ambiguous → `AmbiguousFileExtension(filePath, candidates)`
- Parse errors → `ParseFailed(filePath, errorStrings)`
- Code truth → `CodeTruthExtractionFailed reason`
- Machine type → `UnrecognizedMachineType typeName`

- [ ] **Step 4: Fix StatechartParseCommand.fs — typed errors, remove dead code, add format to output**

Change `execute` return type to `Result<ParseCommandResult, StatechartError>`.

Remove the unused `emptyDocument` definition (lines 22-27).

Replace string error construction with typed cases:
- `Error (sprintf "File not found: %s" specFile)` → `Error (FileNotFound specFile)`
- `Error (sprintf "Unknown format: ...")` → `Error (UnknownFormat other)`
- etc.

- [ ] **Step 5: Build and verify**

Run: `dotnet build src/Frank.Cli.Core/Frank.Cli.Core.fsproj`
Expected: Build errors in Program.fs and Output modules (they still pattern-match on string errors). Fix in next task.

- [ ] **Step 6: Commit**

```bash
git add src/Frank.Cli.Core/Commands/
git commit -m "fix: use typed StatechartError in all command modules, fix silent error swallowing"
```

---

## Task 6: Fix output formatters and Program.fs

**Files:**
- Modify: `src/Frank.Cli.Core/Output/TextOutput.fs`
- Modify: `src/Frank.Cli.Core/Output/JsonOutput.fs`
- Modify: `src/Frank.Cli/Program.fs`
- Modify: `src/Frank.Statecharts/build/Frank.Statecharts.targets`

- [ ] **Step 1: Replace sprintf "%A" in TextOutput.fs**

Replace `sprintf "%A" a.Format` (line 251) with `FormatTag.toString a.Format`. Open `Frank.Cli.Core.Statechart.FormatDetector` for `FormatTag` module access.

- [ ] **Step 2: Replace sprintf "%A" in JsonOutput.fs and add statechart error formatter**

Replace `(sprintf "%A" a.Format).ToLowerInvariant()` with `FormatTag.toLower a.Format`.

Add a statechart-specific error formatter that delegates to `StatechartError.formatErrorJson`:

```fsharp
let formatStatechartError (error: StatechartError.StatechartError) : string =
    StatechartError.formatErrorJson error
```

- [ ] **Step 3: Fix Program.fs — parse --output-format (I-010)**

In the parse command handler, read the output format option:

```fsharp
let outputFormat = parseResult.GetValue(scParseOutputFormatOpt)
```

Use it to determine output rendering for both success and error paths.

- [ ] **Step 4: Fix Program.fs — uniform JSON error envelopes (I-011)**

All statechart command error handlers should use:

```fsharp
| Error e ->
    Environment.ExitCode <- 1
    let output =
        if outputFormat = "json" then
            StatechartError.formatErrorJson e
        else
            AnsiColors.red (sprintf "Error: %s" (StatechartError.formatError e))
    Console.Error.WriteLine(output)
```

- [ ] **Step 5: Fix Program.fs — add parse format to output (I-012)**

In the parse command success handler, include the detected format in the output. Modify the `serializeParseResult` call or wrap it to include the format field.

The simplest approach: add a `serializeParseResultWithFormat` function to `StatechartDocumentJson.fs` that takes `ParseResult * FormatTag` and includes a top-level `"format"` field.

- [ ] **Step 6: Fix MSBuild targets (I-008)**

In `src/Frank.Statecharts/build/Frank.Statecharts.targets`, change the availability check from `frank-cli --version` to `frank-cli help`:

```xml
<Exec Command="frank-cli help"
      IgnoreExitCode="true"
      StandardOutputImportance="low"
      StandardErrorImportance="low"
      ConsoleToMsBuild="true">
  <Output TaskParameter="ExitCode" PropertyName="_FrankCliExitCode" />
</Exec>
```

**Design decision for parse --output-format text:** The parse command's primary output IS the StatechartDocument JSON. When `--output-format text`, output the same JSON (since the document itself is the content, not a status report). The `--output-format` flag on parse only affects error rendering (text vs JSON error envelope). Document this in the help text.

- [ ] **Step 7: Build full solution and verify**

Run: `dotnet build src/Frank.Cli/Frank.Cli.fsproj`
Expected: 0 errors, 0 warnings

- [ ] **Step 8: Commit**

```bash
git add src/Frank.Cli.Core/Output/ src/Frank.Cli/Program.fs src/Frank.Statecharts/build/
git commit -m "fix: uniform JSON error envelopes, fix parse --output-format, fix MSBuild version check"
```

---

## Task 7: Fix StatechartDocumentJson — preserve AST hierarchy

**Files:**
- Modify: `src/Frank.Cli.Core/Statechart/StatechartDocumentJson.fs`

- [ ] **Step 1: Replace flattened state collection with tree-preserving serialization**

Replace `collectStates` (which flattens hierarchy) with a recursive `writeStateNode` function that preserves children:

```fsharp
let rec private writeStateNode (w: Utf8JsonWriter) (state: StateNode) =
    w.WriteStartObject()
    w.WriteString("identifier", state.Identifier)
    w.WriteString("kind", stateKindToString state.Kind)
    writeOptional w "label" state.Label

    if not state.Children.IsEmpty then
        w.WriteStartArray("children")
        for child in state.Children do
            writeStateNode w child
        w.WriteEndArray()

    w.WriteEndObject()
```

- [ ] **Step 2: Add note and directive serialization**

```fsharp
let private writeNote (w: Utf8JsonWriter) (note: NoteContent) =
    w.WriteStartObject()
    w.WriteString("target", note.Target)
    w.WriteString("content", note.Content)
    w.WriteEndObject()

let private writeDirective (w: Utf8JsonWriter) (directive: Directive) =
    w.WriteStartObject()
    match directive with
    | TitleDirective(title, _) ->
        w.WriteString("type", "title")
        w.WriteString("value", title)
    | AutoNumberDirective _ ->
        w.WriteString("type", "autoNumber")
    w.WriteEndObject()
```

- [ ] **Step 2b: Add group serialization**

```fsharp
let private writeGroup (w: Utf8JsonWriter) (group: GroupBlock) =
    w.WriteStartObject()
    w.WriteString("kind", sprintf "%A" group.Kind)  // GroupKind is internal, %A is acceptable here
    w.WriteStartArray("branches")
    for branch in group.Branches do
        w.WriteStartObject()
        writeOptional w "condition" branch.Condition
        w.WriteStartArray("elements")
        for el in branch.Elements do
            writeElement w el  // recursive — writeElement dispatches on StatechartElement
        w.WriteEndArray()
        w.WriteEndObject()
    w.WriteEndArray()
    w.WriteEndObject()
```

Note: `writeElement` is the top-level dispatcher that calls `writeStateNode`, `writeTransition`, `writeNote`, `writeGroup`, `writeDirective` recursively. Define it using `and` for mutual recursion:

```fsharp
let rec private writeElement (w: Utf8JsonWriter) (el: StatechartElement) =
    match el with
    | StateDecl s -> writeStateNode w s
    | TransitionElement t -> writeTransition w t
    | NoteElement n -> writeNote w n
    | GroupElement g -> writeGroup w g
    | DirectiveElement d -> writeDirective w d
and private writeGroup ...
```

- [ ] **Step 3: Update serializeDocument to write elements preserving type info**

Instead of separate `states` and `transitions` arrays, write an `elements` array preserving the original order and types:

```fsharp
writer.WriteStartArray("elements")
for el in doc.Elements do
    match el with
    | StateDecl s -> writeStateNode writer s
    | TransitionElement t -> writeTransition writer t
    | NoteElement n -> writeNote writer n
    | GroupElement g -> writeGroup writer g
    | DirectiveElement d -> writeDirective writer d
writer.WriteEndArray()
```

Also keep the flattened `states` and `transitions` arrays for backward compatibility — downstream consumers may depend on them.

- [ ] **Step 4: Add serializeParseResultWithFormat**

```fsharp
let serializeParseResultWithFormat (result: ParseResult) (format: FormatTag) : string =
    use stream = new MemoryStream()
    use writer = new Utf8JsonWriter(stream, JsonWriterOptions(Indented = true))
    writer.WriteStartObject()
    writer.WriteString("format", FormatTag.toLower format)
    writer.WritePropertyName("document")
    writeDocumentInline writer result.Document
    // ... errors, warnings arrays
    writer.WriteEndObject()
    writer.Flush()
    Encoding.UTF8.GetString(stream.ToArray())
```

- [ ] **Step 5: Build and verify**

Run: `dotnet build src/Frank.Cli.Core/Frank.Cli.Core.fsproj`

- [ ] **Step 6: Commit**

```bash
git add src/Frank.Cli.Core/Statechart/StatechartDocumentJson.fs
git commit -m "fix: preserve AST hierarchy in JSON, include notes/directives, add format field"
```

---

## Task 8: Add FormatDetector tests

**Files:**
- Create: `test/Frank.Cli.Core.Tests/Statechart/FormatDetectorTests.fs`
- Modify: `test/Frank.Cli.Core.Tests/Frank.Cli.Core.Tests.fsproj`

- [ ] **Step 1: Write tests**

```fsharp
module Frank.Cli.Core.Tests.Statechart.FormatDetectorTests

open Expecto
open Frank.Statecharts.Validation
open Frank.Cli.Core.Statechart.FormatDetector

[<Tests>]
let tests =
    testList "FormatDetector" [
        testList "detect" [
            testCase "detects .wsd as WSD" <| fun _ ->
                Expect.equal (detect "game.wsd") (Detected Wsd) "should detect WSD"

            testCase "detects .scxml as SCXML" <| fun _ ->
                Expect.equal (detect "game.scxml") (Detected Scxml) "should detect SCXML"

            testCase "detects .smcat as smcat" <| fun _ ->
                Expect.equal (detect "game.smcat") (Detected Smcat) "should detect smcat"

            testCase "detects .alps.json as ALPS (compound before .json)" <| fun _ ->
                Expect.equal (detect "game.alps.json") (Detected Alps) "should detect ALPS"

            testCase "detects .xstate.json as XState (compound before .json)" <| fun _ ->
                Expect.equal (detect "game.xstate.json") (Detected XState) "should detect XState"

            testCase "plain .json is ambiguous" <| fun _ ->
                Expect.equal (detect "game.json") (Ambiguous [ Alps; XState ]) "should be ambiguous"

            testCase "unsupported extension" <| fun _ ->
                match detect "game.txt" with
                | Unsupported ".txt" -> ()
                | other -> failtest (sprintf "Expected Unsupported .txt, got %A" other)

            testCase "case insensitive" <| fun _ ->
                Expect.equal (detect "GAME.WSD") (Detected Wsd) "should detect WSD case-insensitively"

            testCase "full path works" <| fun _ ->
                Expect.equal (detect "/path/to/game.wsd") (Detected Wsd) "should detect WSD from full path"
        ]

        testList "formatExtension" [
            testCase "WSD → .wsd" <| fun _ ->
                Expect.equal (formatExtension Wsd) ".wsd" ""
            testCase "ALPS → .alps.json" <| fun _ ->
                Expect.equal (formatExtension Alps) ".alps.json" ""
            testCase "XState → .xstate.json" <| fun _ ->
                Expect.equal (formatExtension XState) ".xstate.json" ""
        ]

        testList "FormatTag.toString" [
            testCase "Wsd → WSD" <| fun _ ->
                Expect.equal (FormatTag.toString Wsd) "WSD" ""
            testCase "Alps → ALPS" <| fun _ ->
                Expect.equal (FormatTag.toString Alps) "ALPS" ""
            testCase "Smcat → smcat" <| fun _ ->
                Expect.equal (FormatTag.toString Smcat) "smcat" ""
        ]
    ]
```

- [ ] **Step 2: Add to test fsproj**

Add `<Compile Include="Statechart/FormatDetectorTests.fs" />` before the `Commands/` test entries.

- [ ] **Step 3: Run tests**

Run: `dotnet test test/Frank.Cli.Core.Tests/`
Expected: All FormatDetector tests pass

- [ ] **Step 4: Commit**

```bash
git add test/Frank.Cli.Core.Tests/Statechart/FormatDetectorTests.fs test/Frank.Cli.Core.Tests/Frank.Cli.Core.Tests.fsproj
git commit -m "test: add FormatDetector unit tests"
```

---

## Task 9: Add FormatPipeline tests

**Files:**
- Create: `test/Frank.Cli.Core.Tests/Statechart/FormatPipelineTests.fs`
- Modify: `test/Frank.Cli.Core.Tests/Frank.Cli.Core.Tests.fsproj`

- [ ] **Step 1: Write tests for pure helper functions**

```fsharp
module Frank.Cli.Core.Tests.Statechart.FormatPipelineTests

open Expecto
open Frank.Cli.Core.Statechart.FormatPipeline

[<Tests>]
let tests =
    testList "FormatPipeline" [
        testList "resourceSlug" [
            testCase "extracts slug from simple route" <| fun _ ->
                Expect.equal (resourceSlug "/games/{id}") "games" ""

            testCase "extracts slug from multi-segment route" <| fun _ ->
                Expect.equal (resourceSlug "/api/orders/{orderId}") "api-orders" ""

            testCase "handles root route" <| fun _ ->
                Expect.equal (resourceSlug "/") "resource" ""

            testCase "handles route with no parameters" <| fun _ ->
                Expect.equal (resourceSlug "/health") "health" ""

            testCase "filters multiple parameter segments" <| fun _ ->
                Expect.equal (resourceSlug "/api/{version}/items/{id}") "api-items" ""
        ]

        testCase "allFormats contains exactly 5 formats" <| fun _ ->
            Expect.equal allFormats.Length 5 "should have 5 formats"
    ]
```

- [ ] **Step 2: Add to test fsproj and run**

- [ ] **Step 3: Commit**

```bash
git add test/Frank.Cli.Core.Tests/Statechart/FormatPipelineTests.fs test/Frank.Cli.Core.Tests/Frank.Cli.Core.Tests.fsproj
git commit -m "test: add FormatPipeline unit tests"
```

---

## Task 10: Add ValidationReportFormatter tests

**Files:**
- Create: `test/Frank.Cli.Core.Tests/Statechart/ValidationReportFormatterTests.fs`
- Modify: `test/Frank.Cli.Core.Tests/Frank.Cli.Core.Tests.fsproj`

- [ ] **Step 1: Write tests**

```fsharp
module Frank.Cli.Core.Tests.Statechart.ValidationReportFormatterTests

open Expecto
open Frank.Statecharts.Validation
open Frank.Cli.Core.Statechart.ValidationReportFormatter

let private emptyReport =
    { TotalChecks = 0; TotalSkipped = 0; TotalFailures = 0
      Checks = []; Failures = [] }

let private passingReport =
    { TotalChecks = 2; TotalSkipped = 0; TotalFailures = 0
      Checks =
        [ { Name = "state-count"; Status = Pass; Reason = None }
          { Name = "transition-count"; Status = Pass; Reason = None } ]
      Failures = [] }

let private failingReport =
    { TotalChecks = 3; TotalSkipped = 1; TotalFailures = 1
      Checks =
        [ { Name = "state-count"; Status = Pass; Reason = None }
          { Name = "state-names"; Status = Fail; Reason = Some "Names differ" }
          { Name = "guard-names"; Status = Skip; Reason = Some "No guards" } ]
      Failures =
        [ { Formats = [ Wsd; Alps ]
            EntityType = "state name"
            Expected = "WaitingForPlayers"
            Actual = "Waiting"
            Description = "State name mismatch" } ] }

[<Tests>]
let tests =
    testList "ValidationReportFormatter" [
        testList "formatText" [
            testCase "empty report shows PASSED" <| fun _ ->
                let text = formatText emptyReport
                Expect.stringContains text "PASSED" "should show PASSED"

            testCase "passing report includes check names" <| fun _ ->
                let text = formatText passingReport
                Expect.stringContains text "state-count" "should include check name"

            testCase "failing report shows FAILED" <| fun _ ->
                let text = formatText failingReport
                Expect.stringContains text "FAILED" "should show FAILED"

            testCase "failing report includes failure details" <| fun _ ->
                let text = formatText failingReport
                Expect.stringContains text "State name mismatch" "should include failure description"
                Expect.stringContains text "WaitingForPlayers" "should include expected value"
        ]

        testList "formatJson" [
            testCase "empty report has status passed" <| fun _ ->
                let json = formatJson emptyReport
                Expect.stringContains json "\"passed\"" "should have passed status"

            testCase "failing report has status failed" <| fun _ ->
                let json = formatJson failingReport
                Expect.stringContains json "\"failed\"" "should have failed status"

            testCase "JSON includes totalChecks" <| fun _ ->
                let json = formatJson failingReport
                Expect.stringContains json "\"totalChecks\": 3" "should include totalChecks"

            testCase "JSON includes failure details" <| fun _ ->
                let json = formatJson failingReport
                Expect.stringContains json "\"entityType\": \"state name\"" "should include entityType"
        ]
    ]
```

- [ ] **Step 2: Add to test fsproj and run**

- [ ] **Step 3: Commit**

```bash
git add test/Frank.Cli.Core.Tests/Statechart/ValidationReportFormatterTests.fs test/Frank.Cli.Core.Tests/Frank.Cli.Core.Tests.fsproj
git commit -m "test: add ValidationReportFormatter unit tests"
```

---

## Task 11: Add StatechartDocumentJson tests

**Files:**
- Create: `test/Frank.Cli.Core.Tests/Statechart/StatechartDocumentJsonTests.fs`
- Modify: `test/Frank.Cli.Core.Tests/Frank.Cli.Core.Tests.fsproj`

- [ ] **Step 1: Write tests covering hierarchy preservation, notes, directives**

Test that:
- States with children produce nested JSON (not flattened)
- Notes appear as NoteElement in elements array
- Directives appear as DirectiveElement in elements array
- Empty documents produce valid JSON
- ParseResult with errors produces errors array
- StateKind values use explicit toString (not `%A`)

Use `System.Text.Json.JsonDocument.Parse` to validate JSON structure programmatically.

- [ ] **Step 2: Add to test fsproj and run**

- [ ] **Step 3: Commit**

```bash
git add test/Frank.Cli.Core.Tests/Statechart/StatechartDocumentJsonTests.fs test/Frank.Cli.Core.Tests/Frank.Cli.Core.Tests.fsproj
git commit -m "test: add StatechartDocumentJson unit tests"
```

---

## Task 12: Final verification

- [ ] **Step 1: Full build**

Run: `dotnet build src/Frank.Cli/Frank.Cli.fsproj`
Expected: 0 errors, 0 warnings

- [ ] **Step 2: Full test suite**

Run: `dotnet test test/Frank.Cli.Core.Tests/`
Expected: All tests pass, including new Statechart tests

- [ ] **Step 3: Verify CLI help still works**

Run: `dotnet run --project src/Frank.Cli -- --help`
Run: `dotnet run --project src/Frank.Cli -- statechart --help`
Run: `dotnet run --project src/Frank.Cli -- statechart parse --help`

- [ ] **Step 4: Verify parse command actually works (it's the one functional command)**

Create a small test .wsd file and parse it:
```bash
echo "stateDiagram\n  WaitingForPlayers --> InProgress : start\n  InProgress --> GameOver : finish" > /tmp/test.wsd
dotnet run --project src/Frank.Cli -- statechart parse /tmp/test.wsd
```

Expected: JSON output with document, states, transitions

- [ ] **Step 5: Commit any remaining fixes**

- [ ] **Step 6: Final commit message**

```bash
git commit -m "chore: final verification — all builds pass, all tests pass"
```
