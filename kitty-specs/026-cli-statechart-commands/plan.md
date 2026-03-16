# Implementation Plan: CLI Statechart Commands

**Branch**: `026-cli-statechart-commands` | **Date**: 2026-03-16 | **Spec**: [spec.md](spec.md)
**Input**: Feature specification from `kitty-specs/026-cli-statechart-commands/spec.md`
**GitHub Issue**: #94 | **Parent**: #57

## Summary

Add a `frank statechart` subcommand group to frank-cli that exposes all statechart pipeline operations through the command line: `extract`, `generate`, `validate`, and `import`. The commands are thin wrappers over existing library modules in `Frank.Statecharts`. The primary new infrastructure is assembly loading (to extract `StateMachineMetadata` from compiled Frank applications), an XState v5 JSON serializer/deserializer (~250-350 lines), output formatting, and validation report rendering. All five notation formats (WSD, ALPS, SCXML, smcat, XState JSON) are supported.

## Technical Context

**Language/Version**: F# 8.0+ targeting .NET 10.0 (matching Frank.Cli and Frank.Cli.Core)
**Primary Dependencies**: System.CommandLine 2.0.3 (existing), Frank.Statecharts (new project reference from Frank.Cli.Core), System.Text.Json (in-framework)
**Storage**: N/A (stateless CLI commands -- reads compiled assemblies and spec files, writes to stdout or output directory)
**Testing**: Expecto + TestHost pattern (matching existing Frank test projects), targeting net10.0
**Target Platform**: .NET 10.0 CLI tool (cross-platform)
**Project Type**: Library additions to existing `Frank.Cli.Core` + CLI wiring in `Frank.Cli/Program.fs`
**Performance Goals**: Extract completes within 5 seconds for typical applications (up to 10 stateful resources) per SC-001
**Constraints**: Assembly loading must use isolated `AssemblyLoadContext` to prevent dependency conflicts (FR-006). All format modules in Frank.Statecharts are currently `internal` -- visibility must be expanded.
**Scale/Scope**: 4 new CLI subcommands, 1 new XState serializer/deserializer module, 1 new assembly loading module, output formatting additions

## Constitution Check

*GATE: Must pass before Phase 0 research. Re-check after Phase 1 design.*

| Principle | Status | Notes |
|-----------|--------|-------|
| I. Resource-Oriented Design | PASS | CLI commands expose resource-oriented metadata (stateful resources by route template). No route-centric thinking introduced. |
| II. Idiomatic F# | PASS | New modules use F# idioms: DUs for format tags, Result types for error handling, pipeline-friendly function signatures. |
| III. Library, Not Framework | PASS | CLI commands are thin wrappers over library functions. Generators, parsers, and validators remain library code. |
| IV. ASP.NET Core Native | PASS | Assembly loading uses ASP.NET Core's own patterns (AssemblyLoadContext, endpoint metadata). No platform abstractions hidden. |
| V. Performance Parity | PASS | CLI commands are I/O-bound (assembly loading, file parsing). No hot-path performance concerns. |
| VI. Resource Disposal Discipline | PASS | AssemblyLoadContext created with `isCollectible: true`, disposed after use. JsonDocument, StreamReader use `use` bindings. |
| VII. No Silent Exception Swallowing | PASS | Assembly loading errors, parser errors, and startup failures produce clear error messages (FR-005). No catch-all handlers. |
| VIII. No Duplicated Logic | PASS | Format detection logic centralized in FormatDetector module. Output formatting extends existing JsonOutput/TextOutput modules. WSD Generator reused as central metadata-to-AST converter. |

## Project Structure

### Documentation (this feature)

```
kitty-specs/026-cli-statechart-commands/
├── plan.md              # This file
├── research.md          # Complete (pre-existing)
├── spec.md              # Feature specification
├── data-model.md        # Phase 1 output
├── quickstart.md        # Phase 1 output
└── contracts/           # Phase 1 output (CLI interface contracts)
```

### Source Code (repository root)

```
src/Frank.Statecharts/
├── XState/                           # NEW: XState v5 JSON serializer/deserializer
│   ├── Serializer.fs                 # StatechartDocument -> XState v5 JSON
│   └── Deserializer.fs               # XState v5 JSON -> Ast.ParseResult
├── Frank.Statecharts.fsproj          # MODIFIED: add XState files, add InternalsVisibleTo
└── (existing format modules unchanged)

src/Frank.Cli.Core/
├── Statechart/                       # NEW: statechart CLI command infrastructure
│   ├── StatechartExtractor.fs        # Assembly loading + metadata extraction
│   ├── FormatDetector.fs             # File extension -> FormatTag mapping
│   ├── FormatPipeline.fs             # Metadata -> format text generation pipelines
│   ├── ValidationReportFormatter.fs  # ValidationReport -> text/json output
│   └── StatechartDocumentJson.fs     # StatechartDocument -> JSON serialization (for import/extract)
├── Commands/
│   ├── StatechartExtractCommand.fs   # NEW: extract subcommand logic
│   ├── StatechartGenerateCommand.fs  # NEW: generate subcommand logic
│   ├── StatechartValidateCommand.fs  # NEW: validate subcommand logic
│   └── StatechartImportCommand.fs    # NEW: import subcommand logic
├── Output/
│   ├── JsonOutput.fs                 # MODIFIED: add statechart output formatters
│   └── TextOutput.fs                 # MODIFIED: add statechart output formatters
├── Help/
│   └── HelpContent.fs               # MODIFIED: add statechart command help entries
└── Frank.Cli.Core.fsproj            # MODIFIED: add Frank.Statecharts reference, new files

src/Frank.Cli/
└── Program.fs                        # MODIFIED: add statechart parent command + 4 subcommands

test/Frank.Cli.Statechart.Tests/      # NEW: test project
├── Frank.Cli.Statechart.Tests.fsproj
├── ExtractorTests.fs
├── FormatDetectorTests.fs
├── FormatPipelineTests.fs
├── XStateSerializerTests.fs
├── StatechartCommandTests.fs         # Integration tests via TestHost
└── ValidationReportFormatterTests.fs
```

**Structure Decision**: New statechart infrastructure goes in `src/Frank.Cli.Core/Statechart/` (matching the existing organizational pattern of `Extraction/`, `Analysis/`, `Rdf/`). Command modules go in `Commands/` (matching existing commands). XState serializer goes in `src/Frank.Statecharts/XState/` (matching existing format subdirectories WSD, ALPS, SCXML, smcat).

## Technical Decisions

### D-001: Command Registration Pattern
Follow existing imperative System.CommandLine pattern in Program.fs. Add a `statechart` parent Command with four subcommands. No abstraction layer.

### D-002: Project Reference
Add `Frank.Statecharts` project reference to `Frank.Cli.Core.fsproj`. This adds the `Microsoft.AspNetCore.App` framework reference transitively. Acceptable because the CLI already targets net10.0 and assembly loading requires ASP.NET Core types.

### D-003: Assembly Loading Strategy
Use forced module initialization first (simplest): load assembly into isolated `AssemblyLoadContext`, scan types for `Resource` structs containing `Endpoint[]`, extract `StateMachineMetadata` from endpoint metadata. Fall back to entry-point invocation if reflection-based approach does not find resources.

### D-004: Visibility
Add `InternalsVisibleTo Include="Frank.Cli.Core"` to `Frank.Statecharts.fsproj`. This is the minimal change that unblocks CLI access to generators, parsers, and mappers without changing the public API surface of Frank.Statecharts.

### D-005: XState Serializer Location
New `src/Frank.Statecharts/XState/` subdirectory with `Serializer.fs` and `Deserializer.fs`, following the pattern of existing format modules.

### D-006: Code-Truth Artifact
The validate command generates a `FormatArtifact` from extracted metadata using `Wsd.Generator.generate` (the central metadata-to-AST converter). This artifact uses `FormatTag.Wsd` since it is generated by the WSD generator. Cross-format rules compare spec files against this code-derived WSD artifact.

### D-007: Output Format Disambiguation
The `generate` command uses `--format` for the target notation format (wsd/alps/scxml/smcat/xstate/all) and `--output-format` for text/json rendering of the command's own status messages. Other commands (`extract`, `validate`, `import`) use `--format` for text/json output format, consistent with existing CLI commands.

### D-008: MSBuild Integration (P3)
Deferred to a future spec. Not included as a work package in this plan. The generate command provides the prerequisite capability.

## Key Implementation Details

### Assembly Loading Pipeline

```
DLL path
  -> AssemblyLoadContext.LoadFromAssemblyPath()
  -> Scan types for module-level Resource/Endpoint[] values
  -> For each Endpoint, check endpoint.Metadata for StateMachineMetadata
  -> Extract route template from RouteEndpoint.RoutePattern.RawText
  -> Return ExtractedStatechart list
```

Critical constraint: The loaded assembly's `Frank.Statecharts.StateMachineMetadata` type must be the SAME assembly version as frank-cli's. This is ensured by the project reference (both share the same `Frank.Statecharts` assembly at build time). `AssemblyDependencyResolver` resolves the target assembly's `.deps.json` for its other dependencies.

### Format Generation Pipelines

| Format | Pipeline |
|--------|----------|
| WSD | `StateMachineMetadata` -> `Wsd.Generator.generate` -> `StatechartDocument` -> `Wsd.Serializer.serialize` -> text |
| ALPS | `StateMachineMetadata` -> `Wsd.Generator.generate` -> `StatechartDocument` -> `Alps.Mapper.fromStatechartDocument` -> `AlpsDocument` -> `Alps.JsonGenerator.generateAlpsJson` -> JSON |
| SCXML | `StateMachineMetadata` -> `Wsd.Generator.generate` -> `StatechartDocument` -> `Scxml.Mapper.fromStatechartDocument` -> `ScxmlDocument` -> `Scxml.Generator.generate` -> XML |
| smcat | `StateMachineMetadata` -> `Smcat.Generator.generate` -> text (direct, no AST intermediate) |
| XState | `StateMachineMetadata` -> `Wsd.Generator.generate` -> `StatechartDocument` -> `XState.Serializer.serialize` -> JSON |

### Format Detection (File Extension -> FormatTag)

| Extension | Format | Notes |
|-----------|--------|-------|
| `.wsd` | Wsd | Unambiguous |
| `.alps.json` | Alps | Compound extension, check before `.json` |
| `.scxml` | Scxml | Unambiguous |
| `.smcat` | Smcat | Unambiguous |
| `.xstate.json` | XState | Compound extension, check before `.json` |
| `.json` | Ambiguous | Require `--format` flag or try both ALPS and XState parsers |

### Import Command Pipeline

```
spec file path
  -> FormatDetector.detect (file extension -> FormatTag)
  -> Read file contents
  -> Dispatch to format parser:
     - Wsd: Wsd.Parser.parseWsd -> Ast.ParseResult (shared AST directly)
     - Alps: Alps.JsonParser.parseAlpsJson -> Result<AlpsDocument,...> -> Alps.Mapper.toStatechartDocument
     - Scxml: Scxml.Parser.parseString -> ScxmlParseResult -> Scxml.Mapper.toStatechartDocument
     - Smcat: Smcat.Parser.parseSmcat -> Smcat.Types.ParseResult -> Smcat.Mapper.toStatechartDocument
     - XState: XState.Deserializer.deserialize -> Ast.ParseResult
  -> Serialize StatechartDocument to JSON
  -> Output
```

### Validate Command Pipeline

```
spec files + assembly path
  -> FormatDetector.detect each spec file
  -> Parse each into FormatArtifact (via import pipeline + FormatTag wrapping)
  -> StatechartExtractor.extract assembly -> StateMachineMetadata list
  -> For each metadata: Wsd.Generator.generate -> StatechartDocument -> FormatArtifact(Wsd)
  -> Validator.validate (SelfConsistencyRules.rules @ CrossFormatRules.rules) allArtifacts
  -> ValidationReportFormatter.format report
```

## Risk Register

| Risk | Severity | Mitigation |
|------|----------|------------|
| Assembly type identity mismatch (loaded `StateMachineMetadata` != CLI's type) | High | Shared project reference ensures same assembly. Test with real compiled assemblies early in WP01. |
| Reflection-based resource scanning misses resources defined inside `webHost` CE | Medium | Fall back to entry-point invocation. Document limitation in help text. Prototype in WP01. |
| Target assembly startup side effects (DB, network) | Medium | Reflection-first approach avoids calling Main. Entry-point fallback should set env vars to signal extraction mode. |
| Internal module visibility blocks CLI access | Low | `InternalsVisibleTo` in fsproj. Single-line change per decision D-004. |
| XState v5 schema complexity (nested/parallel states) | Low | Implement flat-state mapping first. Compound states can be added later. |

## Complexity Tracking

No constitution violations requiring justification. All new code follows existing patterns.

## Dependencies

All of the following must be complete before implementation begins:
- #90 WSD Parser (spec 007)
- #91 WSD Generator (spec 017)
- #97 ALPS Parser + Generator (spec 011)
- #98 SCXML Parser + Generator (spec 018)
- #100 smcat Parser + Generator (spec 013)
- #112 Cross-format Validator (spec 021)

## Post-Design Constitution Re-Check

| Principle | Status | Notes |
|-----------|--------|-------|
| VI. Resource Disposal | PASS | `AssemblyLoadContext` uses `use` binding with `isCollectible: true`. File streams use `use`. `Utf8JsonWriter` uses `use`. |
| VII. No Silent Swallowing | PASS | Assembly load failures -> `Result.Error` with descriptive message. Parser errors -> included in output. Startup exceptions -> caught and reported with context. |
| VIII. No Duplicated Logic | PASS | FormatDetector centralizes extension-to-tag mapping (used by both validate and import). FormatPipeline centralizes metadata-to-text generation (used by both generate and validate). Existing WSD Generator serves as the single metadata-to-AST converter. |
