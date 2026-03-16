# Feature Specification: CLI Statechart Commands

**Feature Branch**: `026-cli-statechart-commands`
**Created**: 2026-03-16
**Status**: Draft
**GitHub Issue**: #94
**Dependencies**: #91 (WSD Generator), #90 (WSD Parser), #97 (ALPS Parser + Generator), #98 (SCXML Parser + Generator), #100 (smcat Parser + Generator), #112 (Cross-format validator, spec 021)
**Parallel with**: #93 (ETag generation -- no dependency)
**Parent issue**: #57 (statechart spec pipeline)
**Location**: `src/Frank.Cli.Core/Commands/` (command modules), `src/Frank.Cli/Program.fs` (CLI wiring), `src/Frank.Cli.Core/Statechart/` (assembly loading and metadata extraction)

## Clarifications

### Session 2026-03-16

- Q: The issue references "extract StateMachineMetadata from compiled assemblies." StateMachineMetadata is stored on endpoint metadata during route registration (StatefulResourceBuilder.Run). Source analysis via FCS cannot access runtime metadata. What approach should be used? -> A: Use the host-based approach. Build a minimal WebApplication, register endpoints (which triggers StateMachineMetadata population), collect metadata from endpoint metadata, shut down. This is the pattern ASP.NET's own OpenAPI tooling uses.

## Background

The Frank statechart spec pipeline (#57) supports five notation formats: WSD, ALPS, SCXML, smcat, and XState JSON. Each format has its own parser and generator implemented as library modules within `src/Frank.Statecharts/`. A cross-format validator (spec 021) checks consistency across format artifacts. All parsers produce and all generators consume the shared AST (`StatechartDocument` from spec 020).

Currently, these library functions have no user-facing entry point. A developer must write custom F# code to call generators, invoke parsers, or run validation. This feature adds a `frank statechart` subcommand group to frank-cli that exposes all statechart pipeline operations through the command line, enabling both human developers and LLM coding agents to extract, generate, validate, and import statechart artifacts without writing code.

The CLI commands are thin wrappers. The core logic lives in the existing library modules. The CLI adds assembly loading (to extract `StateMachineMetadata` from compiled Frank applications), file I/O, output formatting, and validation reporting.

## User Scenarios & Testing

### User Story 1 - Extract State Machine Metadata from a Compiled Assembly (Priority: P1)

A developer has built a Frank application containing stateful resources (using `statefulResource` computation expressions). They run `frank statechart extract MyApp.dll` and receive a structured representation of all state machines defined in the application: state names, allowed HTTP methods per state, initial state, guard names, and state metadata (final flags, descriptions). The output is suitable for piping into other tools or for LLM consumption.

**Why this priority**: Extraction is the prerequisite for all other commands. Without the ability to read `StateMachineMetadata` from a compiled assembly, neither generation nor validation can operate. This is the foundational capability.

**Independent Test**: Build a Frank sample application with a known stateful resource (e.g., tic-tac-toe), run `frank statechart extract` against its compiled DLL, and verify the output contains the expected states, methods, guards, and initial state.

**Acceptance Scenarios**:

1. **Given** a compiled Frank assembly containing one stateful resource with three states (Locked, Unlocked, Broken), **When** `frank statechart extract <assembly>` is invoked, **Then** the output lists all three state names, the initial state, the allowed HTTP methods per state, and any guard names.
2. **Given** a compiled Frank assembly containing multiple stateful resources on different route templates, **When** `frank statechart extract <assembly>` is invoked, **Then** the output contains a separate entry for each stateful resource, keyed by route template.
3. **Given** a compiled assembly that contains no stateful resources (only regular Frank resources), **When** `frank statechart extract <assembly>` is invoked, **Then** the output indicates that no state machines were found and exits with a zero exit code (not an error -- absence is a valid result).
4. **Given** `frank statechart extract <assembly> --output-format json` is invoked, **When** the output is parsed, **Then** it is valid JSON containing an array of state machine entries, each with fields for route template, states, initial state, guard names, and state metadata.
5. **Given** a path to an assembly that does not exist, **When** `frank statechart extract <path>` is invoked, **Then** the command exits with a non-zero exit code and a clear error message indicating the file was not found.

---

### User Story 2 - Generate Statechart Spec Artifacts from a Compiled Assembly (Priority: P1)

A developer runs `frank statechart generate --format wsd MyApp.dll` and receives WSD text representing their application's state machines. They can generate any supported format (WSD, ALPS, SCXML, smcat, XState JSON) or all formats at once with `--format all`. Generated artifacts can be written to files for version control, documentation, or further processing.

**Why this priority**: Generation is the primary productive output of the pipeline. Developers use generated artifacts for documentation, visualization, cross-team communication, and as input to LLM-assisted scaffolding. This is the command most frequently invoked after initial setup.

**Independent Test**: Build a Frank sample application, run `frank statechart generate --format wsd` against its DLL, and verify the output is valid WSD text that can be parsed back through the WSD parser without errors.

**Acceptance Scenarios**:

1. **Given** a compiled Frank assembly with a stateful resource, **When** `frank statechart generate --format wsd <assembly>` is invoked, **Then** the output is valid WSD text representing the state machine, parseable by the WSD parser from spec 007.
2. **Given** a compiled Frank assembly, **When** `frank statechart generate --format alps <assembly>` is invoked, **Then** the output is valid ALPS JSON representing the state machine.
3. **Given** a compiled Frank assembly, **When** `frank statechart generate --format scxml <assembly>` is invoked, **Then** the output is valid SCXML XML representing the state machine.
4. **Given** a compiled Frank assembly, **When** `frank statechart generate --format smcat <assembly>` is invoked, **Then** the output is valid smcat notation representing the state machine.
5. **Given** a compiled Frank assembly, **When** `frank statechart generate --format xstate <assembly>` is invoked, **Then** the output is valid XState JSON representing the state machine.
6. **Given** `frank statechart generate --format all <assembly> --output ./specs/` is invoked, **When** the assembly contains two stateful resources (e.g., /games/{id} and /orders/{id}), **Then** the command writes 10 files (5 formats times 2 resources) to the output directory, named by resource and format.
7. **Given** `frank statechart generate --format all <assembly>` is invoked without `--output`, **When** the command runs, **Then** all generated artifacts are written to stdout, separated by format and resource headers.
8. **Given** an assembly with no stateful resources, **When** `frank statechart generate --format wsd <assembly>` is invoked, **Then** the command outputs a message indicating no state machines were found and exits with a zero exit code.

---

### User Story 3 - Validate a Spec File Against a Compiled Assembly (Priority: P1)

A developer has a WSD file describing their state machine and wants to verify it matches the actual implementation. They run `frank statechart validate game.wsd MyApp.dll` and receive a validation report showing whether the spec and code agree on states, transitions, guards, and method availability. Mismatches are reported with actionable diagnostics.

**Why this priority**: Validation catches specification drift -- the situation where documentation and code diverge over time. Without automated validation, developers must manually compare spec artifacts against code, which is error-prone and rarely done.

**Independent Test**: Create a WSD file with a known mismatch (e.g., an extra state not in the code), run validation against the compiled assembly, and verify the report identifies the discrepancy.

**Acceptance Scenarios**:

1. **Given** a WSD file that accurately describes the state machine in a compiled assembly, **When** `frank statechart validate game.wsd <assembly>` is invoked, **Then** the report shows all checks passed with zero failures.
2. **Given** a WSD file containing a state "Review" that does not exist in the compiled assembly's state machine, **When** `frank statechart validate game.wsd <assembly>` is invoked, **Then** the report contains a failure identifying "Review" as a state present in the spec but missing from the code.
3. **Given** a compiled assembly with a state "Maintenance" that does not appear in the WSD file, **When** `frank statechart validate game.wsd <assembly>` is invoked, **Then** the report contains a failure identifying "Maintenance" as a state present in the code but missing from the spec.
4. **Given** `frank statechart validate game.wsd <assembly> --output-format json` is invoked, **When** the output is parsed, **Then** it is a valid JSON `ValidationReport` containing total checks, total failures, check details, and failure details.
5. **Given** a spec file with an unrecognized file extension (e.g., `.xyz`), **When** `frank statechart validate unknown.xyz <assembly>` is invoked, **Then** the command exits with a non-zero exit code and a message listing supported formats.
6. **Given** multiple spec files covering different formats, **When** `frank statechart validate game.wsd game.alps.json <assembly>` is invoked, **Then** the validator runs both single-format and cross-format checks and produces a unified report.

---

### User Story 4 - Import a Spec File for LLM-Assisted Scaffolding (Priority: P2)

A developer or LLM agent has a spec file (e.g., an XState JSON definition, a WSD diagram, or an smcat notation) and wants to produce a structured representation suitable for generating F# code. They run `frank statechart import game.xstate.json` and receive a JSON representation of the parsed state machine -- states, transitions, guards, events -- that an LLM can consume to scaffold idiomatic F# state DUs, transition functions, guard stubs, and handler wiring.

**Why this priority**: Import enables the "design-first" workflow where a developer sketches a state machine in a notation format and then uses LLM tools to generate the corresponding F# code. This is valuable but secondary to the "code-first" workflow (extract/generate/validate) which works with existing code.

**Independent Test**: Create a simple XState JSON file with three states and two transitions, run `frank statechart import`, and verify the output JSON contains the expected state names, transition events, and structure.

**Acceptance Scenarios**:

1. **Given** a valid XState JSON file, **When** `frank statechart import game.xstate.json` is invoked, **Then** the output is a JSON representation of the parsed `StatechartDocument` containing states, transitions, and metadata.
2. **Given** a valid WSD file, **When** `frank statechart import onboarding.wsd` is invoked, **Then** the output contains the parsed state machine in JSON `StatechartDocument` format.
3. **Given** a valid smcat file, **When** `frank statechart import game.smcat` is invoked, **Then** the output contains the parsed state machine in JSON `StatechartDocument` format.
4. **Given** a valid ALPS JSON file, **When** `frank statechart import game.alps.json` is invoked, **Then** the output contains the parsed state machine in JSON `StatechartDocument` format.
5. **Given** a valid SCXML file, **When** `frank statechart import game.scxml` is invoked, **Then** the output contains the parsed state machine in JSON `StatechartDocument` format.
6. **Given** a spec file with parse errors, **When** `frank statechart import broken.wsd` is invoked, **Then** the output includes the best-effort parsed document along with the parse errors and warnings, and the command exits with a non-zero exit code.
7. **Given** `frank statechart import game.xstate.json --output-format json`, **When** the output is parsed, **Then** it matches the shared AST `StatechartDocument` schema with states, transitions, events, guards, and annotations.

---

### User Story 5 - MSBuild Integration for Automatic Spec Generation (Priority: P3)

A developer adds an MSBuild target to their project file that automatically generates statechart spec artifacts after each build. The target invokes `frank statechart generate --format all` against the compiled assembly and writes artifacts to the intermediate output directory. This ensures spec artifacts are always up to date with the code without manual intervention.

**Why this priority**: MSBuild integration automates the "code-first" workflow but requires the generate command (P1) to work first. It is a convenience layer, not a core capability.

**Independent Test**: Add the MSBuild target to a sample project, run `dotnet build`, and verify that spec artifacts appear in the intermediate output directory.

**Acceptance Scenarios**:

1. **Given** a project file with the `GenerateStatechartSpecs` MSBuild target, **When** `dotnet build` succeeds, **Then** statechart spec files appear in `$(IntermediateOutputPath)statecharts/` for each stateful resource and format.
2. **Given** a project where the stateful resource has not changed since the last build, **When** `dotnet build` runs, **Then** the generate command runs but produces identical output (idempotent).
3. **Given** a project that does not use stateful resources, **When** the MSBuild target runs after build, **Then** the generate command completes without error and produces no output files.

---

### Edge Cases

- Assembly that fails to load (missing dependencies, wrong target framework) produces a clear error explaining the load failure, not a generic crash
- Assembly that loads but whose WebApplication setup throws an exception during endpoint registration produces a clear error identifying the startup failure
- State names containing special characters (spaces, hyphens, Unicode) are correctly preserved through extract/generate/import roundtrips
- Very large assemblies with 20+ stateful resources complete extraction within a reasonable time (under 10 seconds) and do not exhaust memory
- Spec files with mixed line endings (Windows CRLF vs Unix LF) are parsed correctly by all format parsers
- The `--output` directory is created automatically if it does not exist when generating artifacts
- When `--format all` is used with `--output`, files use a consistent naming convention: `{resourceSlug}.{format-extension}` (e.g., `games.wsd`, `games.alps.json`, `games.scxml`, `games.smcat`, `games.xstate.json`)
- The import command detects file format from the file extension; ambiguous extensions (e.g., `.json` which could be ALPS or XState) are resolved by attempting both parsers and reporting which succeeded, or by requiring an explicit `--format` flag for notation disambiguation
- The validate command with a spec file that cannot be read (permissions, encoding) produces a clear error
- Guard names extracted from a compiled assembly match exactly the names used in the `Guard` DU constructors (case-sensitive)
- The extract command handles stateful resources that use parameterized DU cases (e.g., `Won "X"`, `Won "O"`) -- these share a single state key ("Won") via the existing `StateKeyExtractor`
- Generated XState JSON uses the standard XState v5 schema for maximum compatibility with the XState ecosystem

## Requirements

### Functional Requirements

**Assembly Loading and Metadata Extraction**

- **FR-001**: System MUST load a compiled .NET assembly by path using `AssemblyLoadContext`, build a minimal `WebApplication`, register endpoints to trigger `StatefulResourceBuilder.Run` execution, and collect `StateMachineMetadata` instances from endpoint metadata
- **FR-002**: System MUST extract the following information from each `StateMachineMetadata`: state names (from `StateHandlerMap` keys), initial state key, allowed HTTP methods per state, guard names, and state metadata (final flag, description) from `StateMetadataMap`
- **FR-003**: System MUST identify the route template associated with each stateful resource's endpoints for use as a resource identifier
- **FR-004**: System MUST handle assemblies containing zero stateful resources gracefully, reporting the absence without treating it as an error
- **FR-005**: System MUST produce a clear, structured error when the assembly cannot be loaded, when dependencies are missing, or when the application's startup code throws an exception during endpoint registration
- **FR-006**: System MUST isolate assembly loading so that the loaded application's dependencies do not conflict with frank-cli's own dependencies

**Extract Command**

- **FR-007**: System MUST provide a `frank statechart extract <assembly>` command that loads the assembly, extracts metadata, and outputs a structured representation of all state machines found
- **FR-008**: System MUST support `--output-format` option with values `text` and `json`, defaulting to `text`
- **FR-009**: JSON output from extract MUST include: an array of state machine entries, each containing route template, state names, initial state, guard names, and per-state metadata (allowed methods, final flag, description)

**Generate Command**

- **FR-010**: System MUST provide a `frank statechart generate --format <format> <assembly>` command that extracts metadata and generates spec artifacts
- **FR-011**: System MUST support format values: `wsd`, `alps`, `scxml`, `smcat`, `xstate`, and `all`
- **FR-012**: System MUST delegate to the existing format-specific generators: WSD generator (spec 017), ALPS JSON generator (#97), SCXML generator (spec 018), smcat generator (spec 013), and XState JSON serializer (included in this feature)
- **FR-013**: System MUST support an `--output <directory>` option that writes generated artifacts to files using the naming convention `{resourceSlug}.{format-extension}`
- **FR-014**: When `--output` is not specified, system MUST write all generated artifacts to stdout with resource and format headers separating each artifact
- **FR-015**: When `--output` is specified and the directory does not exist, system MUST create it
- **FR-016**: System MUST support generating artifacts for a single named resource via `--resource <name>` option; when omitted, all resources are processed

**XState JSON Serialization**

- **FR-017**: System MUST provide serialization of `StatechartDocument` to XState v5 JSON format, mapping states, transitions, initial state, and guards
- **FR-018**: System MUST provide deserialization of XState v5 JSON to produce an `Ast.ParseResult` (containing a `StatechartDocument`, errors, and warnings) for use by the import command

**Validate Command**

- **FR-019**: System MUST provide a `frank statechart validate <spec-file> [<spec-file>...] <assembly>` command that parses spec files, extracts metadata from the assembly, and runs the cross-format validator (spec 021)
- **FR-020**: System MUST detect the spec file format from the file extension: `.wsd` for WSD, `.alps.json` for ALPS, `.scxml` for SCXML, `.smcat` for smcat, `.xstate.json` for XState JSON
- **FR-021**: System MUST generate a `FormatArtifact` from the extracted `StateMachineMetadata` (by running it through the WSD generator and mapper to produce a `StatechartDocument`) to serve as the "code truth" artifact for cross-format validation
- **FR-022**: System MUST pass all artifacts (parsed spec files plus the code-derived artifact) to the `Validator.validate` function with the full set of registered validation rules (self-consistency plus cross-format)
- **FR-023**: System MUST format the `ValidationReport` for display, showing passed checks, failed checks with diagnostic details, and skipped checks with reasons
- **FR-024**: System MUST support `--output-format` option with values `text` and `json`, defaulting to `text`
- **FR-025**: System MUST exit with a non-zero exit code when the validation report contains one or more failures

**Import Command**

- **FR-026**: System MUST provide a `frank statechart import <spec-file>` command that parses the spec file and outputs the resulting `StatechartDocument` as JSON
- **FR-027**: System MUST detect the spec file format from the file extension (same mapping as FR-020)
- **FR-028**: System MUST delegate to the appropriate format parser: WSD parser (spec 007), ALPS JSON parser (spec 011), SCXML parser (spec 018), smcat parser (spec 013), or XState JSON deserializer (FR-018)
- **FR-029**: System MUST include parse errors and warnings in the output when present, alongside the best-effort parsed document
- **FR-030**: System MUST exit with a non-zero exit code when the parsed result contains errors
- **FR-031**: When the file extension is ambiguous (`.json`), system MUST accept an explicit `--format` flag to disambiguate (e.g., `--format alps` or `--format xstate`), or attempt all applicable parsers and report which succeeded

**Output Consistency**

- **FR-032**: All commands MUST support `--output-format text|json` for output rendering, defaulting to `text`. The `generate` and `import` commands additionally use `--format` for notation format selection (wsd/alps/scxml/smcat/xstate/all on generate; wsd/alps/scxml/smcat/xstate on import for ambiguous file extension disambiguation). The principle: `--format` = what notation format, `--output-format` = how to render output.
- **FR-033**: JSON output MUST follow the same conventions as existing frank-cli JSON output (from `JsonOutput` module)
- **FR-034**: Text output MUST respect the existing `NO_COLOR` environment variable convention via the `TextOutput.isColorEnabled()` function
- **FR-035**: All commands MUST be registered under a `statechart` parent command on the root command, creating the `frank statechart <subcommand>` structure

**CLI Integration**

- **FR-036**: System MUST integrate with the existing `frank-cli` System.CommandLine infrastructure in `Program.fs`, adding a `statechart` parent command with `extract`, `generate`, `validate`, and `import` subcommands
- **FR-037**: System MUST register help metadata for each new command following the patterns established by spec 016 (help system), including summary, examples, workflow position, and context description

### Key Entities

- **StatechartExtractor**: Module responsible for loading assemblies, building a minimal WebApplication host, registering endpoints, and collecting `StateMachineMetadata` instances from endpoint metadata. Handles assembly load context isolation, dependency resolution, and startup error recovery.
- **ExtractedStatechart**: A structured representation of a single stateful resource extracted from an assembly, containing: route template (resource identifier), state names, initial state key, guard names, per-state metadata (allowed methods, final flag, description), and the raw `StateMachineMetadata` for downstream processing by generators.
- **XStateSerializer**: Module providing bidirectional conversion between `StateMachineMetadata`/`StatechartDocument` and XState v5 JSON format. The only format-specific serializer included in this feature; all others delegate to existing library modules.
- **FormatDetector**: Logic for mapping file extensions to format tags (`.wsd` -> WSD, `.alps.json` -> ALPS, `.scxml` -> SCXML, `.smcat` -> smcat, `.xstate.json` -> XState JSON, `.json` -> ambiguous). Used by validate and import commands to determine which parser to invoke.
- **ValidationReportFormatter**: Module responsible for presenting a `ValidationReport` (from spec 021's `Validator.validate`) as human-readable text or structured JSON. Formats passed checks, failed checks with diagnostics, and skipped checks with reasons.

## Success Criteria

### Measurable Outcomes

- **SC-001**: A developer can run `frank statechart extract` against a compiled Frank assembly and receive a complete, accurate representation of all stateful resources within 5 seconds for a typical application (up to 10 stateful resources)
- **SC-002**: All five format generators (WSD, ALPS, SCXML, smcat, XState JSON) produce valid output from extracted metadata, verified by parsing each generated artifact back through its corresponding parser without errors
- **SC-003**: The validate command correctly identifies all intentionally introduced mismatches in a test suite, with zero false negatives and zero false positives against a known-good assembly
- **SC-004**: The import command successfully parses files in all five formats and produces a `StatechartDocument` JSON representation that preserves all states, transitions, events, and guards from the source file
- **SC-005**: All commands produce valid, parseable JSON when `--output-format json` is specified, consistent with existing frank-cli JSON output conventions
- **SC-006**: The `frank statechart generate --format all --output <dir>` command produces correctly named files for all resources and formats, and the generated files are identical across consecutive runs (idempotent)
- **SC-007**: Existing frank-cli commands (extract, clarify, validate, diff, compile, status, help) continue to function correctly with no regressions after the statechart subcommand group is added

## Assumptions

- The host-based assembly loading approach (building a minimal WebApplication, registering endpoints, collecting metadata) is the correct pattern for extracting `StateMachineMetadata`. This mirrors ASP.NET's own OpenAPI tooling approach.
- All dependent format parsers and generators (WSD spec 007/017, ALPS spec 011, SCXML spec 018, smcat spec 013) are complete and stable before this feature is implemented.
- The cross-format validator (spec 021) is complete and provides the `Validator.validate` function and all registered validation rules.
- The shared statechart AST (spec 020) is stable and provides the `StatechartDocument` type used as the interchange format.
- XState v5 JSON schema is used (not v4), as v5 is the current stable release of XState.
- The `StateMachineMetadata.StateHandlerMap` provides sufficient information to reconstruct state-capability diagrams. Transition targets between different states are NOT directly available from `StateMachineMetadata` (which maps states to HTTP method handlers, not state-to-state transitions). The generated artifacts represent state-capability views (what HTTP methods are available in each state), not full transition graphs.
- Assembly load context isolation (`AssemblyLoadContext`) is sufficient to prevent dependency conflicts between the loaded application and frank-cli itself. If conflicts arise, a separate-process invocation pattern may be needed (deferred to implementation).
- All commands use `--output-format` for text/json output rendering. The `--format` flag is used for notation format selection: on the `generate` command for target notation (wsd/alps/scxml/smcat/xstate/all), and on the `import` command for notation disambiguation of ambiguous file extensions (e.g., `.json`).
- File extension detection for format identification is sufficient for all practical cases. The `.json` ambiguity (ALPS vs XState) is resolved by convention (`.alps.json` for ALPS, `.xstate.json` for XState JSON) or by explicit `--format` flag on the import command.
- The frank-cli help system (spec 016) is integrated before or concurrently with this feature, so that help metadata registration follows established patterns.
