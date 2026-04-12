---
source: kitty-specs/016-frank-cli-help-system
status: complete
type: spec
---

# Feature Specification: frank-cli LLM-Ready Help System

**Feature Branch**: `016-frank-cli-help-system`
**Created**: 2026-03-15
**Status**: Done
**Input**: GitHub issue #110 -- frank-cli: LLM-ready help system with workflow guidance, help topics, and project status
**GitHub Issue**: #110

---

## Clarifications

### Session 2026-03-15

- Q: The issue references `.frank/` state directory, but the existing codebase stores extraction state in `obj/frank-cli/`. Which path convention should the status command use? -> A: Use the existing `obj/frank-cli/` convention established by `ExtractionState.defaultStatePath`. The `.frank/` reference in the issue is aspirational; this spec follows the established codebase convention.
- Q: Should help content (workflow positions, examples, topics) be hardcoded as data literals or loaded from external files? -> A: Hardcoded as F# data records within the `HelpContent` module. This keeps the content co-located with the code, eliminates file-not-found failure modes, and is consistent with how System.CommandLine descriptions are defined today in `Program.fs`.
- Q: What is the primary audience for the help system? -> A: LLM coding agents are the primary audience. The help output must be structured, complete, and parseable without needing prior context. Human developers are a secondary audience and benefit from the same clear documentation.

---

## User Scenarios & Testing

### User Story 1 - LLM Agent Discovers Command Workflow (Priority: P1)

An LLM coding agent is given `frank-cli` as a tool and needs to determine the correct sequence of commands to produce semantic definitions from a Frank project. The agent invokes `frank-cli help workflows` and receives a structured guide explaining that `extract` must precede `compile`, that `clarify` is optional but recommended, and that `validate` should be run before `compile`. With this information, the agent can plan and execute the correct command sequence without external documentation.

**Why this priority**: This is the core problem stated in the issue -- without workflow guidance, agents can construct syntactically valid commands but cannot determine the correct order. This unlocks autonomous agent usage of frank-cli.

**Independent Test**: Invoke `frank-cli help workflows` and verify it returns structured content describing the command pipeline with prerequisites and sequencing.

**Acceptance Scenarios**:

1. **Given** a fresh installation of frank-cli, **When** `frank-cli help workflows` is invoked, **Then** the output describes the end-to-end pipeline (extract -> clarify -> validate -> compile) with clear sequencing and prerequisite information.
2. **Given** an LLM agent with no prior knowledge of frank-cli, **When** the agent reads the `help workflows` output, **Then** the agent can determine that `extract` must be run before `compile` and that `clarify` is optional.
3. **Given** `frank-cli help workflows` is invoked with `--format json`, **When** the output is parsed, **Then** each command entry includes its step number, prerequisites list, next steps list, and whether it is optional, and the output includes a `totalSteps` field derived from the count of commands with `StepNumber > 0`.

---

### User Story 2 - Enriched Per-Command Help (Priority: P1)

An LLM agent invokes `frank-cli extract --help` and receives not only the standard System.CommandLine usage and options, but also a WORKFLOW section (showing prerequisites and next steps) and an EXAMPLES section (showing concrete invocation examples). This gives the agent enough context to construct a valid command invocation and understand where the command fits in the overall pipeline.

**Why this priority**: Per-command help is the most frequently accessed documentation surface. Enriching the existing `--help` output requires no new commands to learn -- agents already know to try `--help`.

**Independent Test**: Run `frank-cli extract --help` and verify the output contains WORKFLOW and EXAMPLES sections in addition to standard usage.

**Acceptance Scenarios**:

1. **Given** any frank-cli command, **When** `--help` is invoked, **Then** the output includes a WORKFLOW section showing the command's step number, prerequisites, and next steps.
2. **Given** any frank-cli command, **When** `--help` is invoked, **Then** the output includes an EXAMPLES section with at least one concrete invocation example.
3. **Given** a command invoked with `--help --context`, **When** the output is rendered, **Then** an additional CONTEXT section appears explaining the semantic meaning of the command within Frank's model (e.g., what "extraction" means in terms of F# types to OWL + SHACL).
4. **Given** a command invoked with `--help` only (no `--context`), **When** the output is rendered, **Then** the CONTEXT section is omitted to keep output concise.

---

### User Story 3 - Project Status and Next-Action Recommendation (Priority: P1)

An LLM agent is dropped into a Frank project directory and needs to orient itself. It runs `frank-cli status --project <path>` and receives a summary of the current project state: whether extraction has been performed, whether it is stale, whether compiled artifacts exist, and what the recommended next action is. This enables the agent to resume work on a project without needing to guess what has already been done.

**Why this priority**: The status command is the "drop an agent into a project and orient" capability described in the issue. Without it, an agent must probe multiple files to determine what state exists, which is error-prone and wastes tokens.

**Independent Test**: Run `frank-cli status --project <path>` against projects in various states (fresh, extracted, stale, compiled) and verify the output correctly describes the state and recommends the appropriate next action.

**Acceptance Scenarios**:

1. **Given** a project with no extraction state, **When** `frank-cli status --project <path>` is invoked, **Then** the output indicates no extraction has been performed and recommends running `extract`.
2. **Given** a project with current (non-stale) extraction state but no compiled artifacts, **When** `frank-cli status` is invoked, **Then** the output indicates extraction is current and recommends running `compile`.
3. **Given** a project with stale extraction state (source files changed since last extraction), **When** `frank-cli status` is invoked, **Then** the output indicates the extraction is stale and recommends re-running `extract`.
4. **Given** a project with current extraction state and compiled artifacts, **When** `frank-cli status` is invoked, **Then** the output indicates the project is up-to-date with no recommended action.
5. **Given** `frank-cli status --project <path> --format json` is invoked, **When** the output is parsed, **Then** it includes structured fields for extraction state, staleness, artifact presence, and recommended next action.

---

### User Story 4 - Help Topics for Conceptual Understanding (Priority: P2)

An LLM agent needs to understand what "extraction" means in Frank's semantic model, or how OWL ontologies relate to F# types. The agent invokes `frank-cli help concepts` and receives an explanation of Frank's semantic model -- how F# types map to OWL classes, how route definitions become resource identities, and how SHACL shapes encode constraints. This enables the agent to make informed decisions during the clarify step.

**Why this priority**: Conceptual understanding is important for agents to make good clarification decisions, but agents can function (less effectively) without it. Workflow sequencing (P1) and per-command help (P1) are required for basic operation.

**Independent Test**: Run `frank-cli help concepts` and verify it returns structured content explaining Frank's semantic model without implementation details.

**Acceptance Scenarios**:

1. **Given** `frank-cli help concepts` is invoked, **When** the output is rendered, **Then** it explains the relationship between F# types and OWL ontologies, route definitions and resource identities, and type constraints and SHACL shapes.
2. **Given** `frank-cli help` is invoked without arguments, **When** the output is rendered, **Then** it lists all available topics (workflows, concepts) and all available commands with brief summaries.
3. **Given** `frank-cli help <command-name>` is invoked, **When** the command name is valid, **Then** the output shows the full enriched help for that command (equivalent to `<command> --help --context`).
4. **Given** `frank-cli help <unknown>` is invoked, **When** the argument does not match a command or topic, **Then** the output includes a "did you mean?" suggestion if a close match exists, or lists available topics and commands.

---

### User Story 5 - Help System Content Completeness (Priority: P2)

Every command registered in frank-cli has complete help metadata: a summary, at least one example, workflow position data (step number, prerequisites, next steps, optional flag), and context documentation. This ensures no command is a documentation dead end for an LLM agent.

**Why this priority**: Incomplete documentation defeats the purpose of the help system. However, this is a content quality concern that is validated by tests rather than a user-facing interaction flow.

**Independent Test**: Run automated content validation tests that verify every registered command has the required help metadata fields populated.

**Acceptance Scenarios**:

1. **Given** the set of all commands registered in frank-cli, **When** each command's help metadata is inspected, **Then** every command has a non-empty summary, at least one example, and a workflow position with valid prerequisites (referencing existing commands).
2. **Given** the set of all help topics, **When** each topic is inspected, **Then** every topic has a non-empty name, summary, and content body.
3. **Given** a command's workflow position lists prerequisites, **When** each prerequisite is looked up, **Then** it corresponds to a valid registered command name.

---

### Edge Cases

- What happens when `frank-cli status` is run outside a project directory (no .fsproj found)? The command should return a clear error indicating no project was found at the specified path.
- What happens when `frank-cli help <name>` matches both a command and a topic? Commands take priority; the output should note that a topic with the same name also exists.
- What happens when the extraction state file exists but is corrupted or in an old format? The status command should report "extraction state unreadable" and recommend re-running `extract`.
- How does the help system handle commands added in future versions? The content model is data-driven (CommandHelp records), so new commands register their help metadata alongside their command definition. Tests enforce completeness.
- What happens when `--format json` is used with `help` subcommand output? The help content should be serializable to JSON for machine consumption, just like all other frank-cli commands.

## Requirements

### Functional Requirements

**Content Model**

- **FR-001**: The system MUST define a structured help metadata record for each command, containing: name, summary, examples list, workflow position, and context description.
- **FR-002**: The workflow position metadata MUST include: step number in the pipeline, list of prerequisite command names, list of next-step command names, and an optional/required flag.
- **FR-003**: The system MUST define a structured help topic record containing: name, summary, and content body.
- **FR-004**: Every command registered in frank-cli MUST have a corresponding help metadata record with all fields populated.
- **FR-005**: Every command's help metadata MUST include at least one concrete invocation example.
- **FR-006**: Prerequisite references in workflow positions MUST reference valid, existing command names.

**Per-Command Help Enrichment**

- **FR-007**: The system MUST customize the standard `--help` output for each command to append a WORKFLOW section showing prerequisites, step number, and next steps.
- **FR-008**: The system MUST customize the standard `--help` output for each command to append an EXAMPLES section showing concrete invocations.
- **FR-009**: The system MUST support a `--context` flag on the root command that, when combined with `--help`, appends a CONTEXT section explaining the semantic meaning of the command.
- **FR-010**: When `--context` is not provided, the CONTEXT section MUST be omitted from `--help` output.

**Help Subcommand**

- **FR-011**: The system MUST provide a `help` subcommand on the root command.
- **FR-012**: `frank-cli help` (no arguments) MUST list all available topics and all available commands with brief summaries.
- **FR-013**: `frank-cli help <command>` MUST display the full enriched help for the named command, including workflow, examples, and context sections.
- **FR-014**: `frank-cli help <topic>` MUST display the topic's content.
- **FR-015**: `frank-cli help workflows` MUST display an end-to-end workflow guide covering the full command pipeline.
- **FR-016**: `frank-cli help concepts` MUST display an explanation of Frank's semantic model.
- **FR-017**: When the argument to `frank-cli help` does not match any command or topic, the system MUST suggest close matches if any exist (fuzzy matching), or list available options.

**Status Command**

- **FR-018**: The system MUST provide a `status` subcommand that accepts a `--project <path>` parameter pointing to a .fsproj file.
- **FR-019**: The status command MUST inspect the project's extraction state directory (`obj/frank-cli/`) to determine whether extraction has been performed.
- **FR-020**: The status command MUST detect whether the extraction state is stale by comparing source file hashes (reusing the existing staleness-detection logic from `ValidateCommand`).
- **FR-021**: The status command MUST detect whether compiled artifacts (OWL/XML, SHACL files) exist in the output directory.
- **FR-022**: The status command MUST recommend the next appropriate action based on the current state (e.g., "run extract", "run compile", "up to date").
- **FR-023**: The status command MUST support both text and JSON output formats via the `--format` option.
- **FR-024**: The status command MUST return a clear error when the specified project path does not exist or is not a valid .fsproj file.

**Output Integration**

- **FR-025**: All help and status outputs MUST support the existing `--format` option (text and json), consistent with other frank-cli commands.
- **FR-026**: JSON output from the help subcommand MUST be structured and parseable, following the same conventions as existing command JSON output.
- **FR-027**: Text output MUST respect the existing `NO_COLOR` environment variable convention for terminal formatting. Implementation note: HelpRenderer must use the existing `TextOutput.isColorEnabled()` function rather than reimplementing NO_COLOR detection.

### Key Entities

- **CommandHelp**: A structured record associating a command name with its summary, examples, workflow position, and contextual explanation. One record per registered command.
- **WorkflowPosition**: Metadata describing where a command sits in the pipeline -- its step number, prerequisite commands, next-step commands, and whether it is optional. Note: `TotalSteps` is not stored in this record; it is derived at render time from the count of commands with `StepNumber > 0` (i.e., commands that are part of the pipeline workflow, excluding utility commands like `help` and `status`).
- **HelpTopic**: A named documentation article (e.g., "workflows", "concepts") with a summary and body content. Discoverable via `frank-cli help`.
- **ProjectStatus**: The result of inspecting a project's state, including extraction presence, staleness, artifact presence, and recommended next action.

## Success Criteria

### Measurable Outcomes

- **SC-001**: An LLM coding agent given only `frank-cli` as a tool (no external SKILL.md or prompt) can determine the correct command sequence to produce semantic definitions by reading help output alone.
- **SC-002**: Every registered command in frank-cli has complete help metadata (summary, at least 1 example, workflow position with valid prerequisites), verified by automated tests.
- **SC-003**: The `status` command correctly identifies and reports all four project states (no extraction, current extraction, stale extraction, fully compiled) with appropriate next-action recommendations.
- **SC-004**: The `help` subcommand resolves all valid command names and topic names, and provides useful suggestions for unrecognized arguments.
- **SC-005**: All new command outputs (help, status) produce valid, parseable JSON when `--format json` is specified, consistent with existing frank-cli JSON output conventions.
- **SC-006**: Existing frank-cli commands and their `--help` output continue to function correctly -- no regressions in current behavior.

## Assumptions

- The existing `obj/frank-cli/` state directory convention is used (not `.frank/` as mentioned aspirationally in the issue).
- Help content is hardcoded as F# data records, not loaded from external files. This keeps content co-located with code and eliminates file-not-found failure modes.
- The `--context` flag is a global option on the root command, not a per-command option. It has no effect except when combined with `--help`.
- The `status` command reuses the existing staleness-detection logic from `ValidateCommand.checkStaleness` rather than reimplementing it.
- The `help` subcommand coexists with System.CommandLine's built-in `--help` option. They serve different purposes: `--help` gives per-command usage; `help <topic>` gives enriched cross-cutting documentation.
- System.CommandLine 2.0.3 (the version already used) supports the `HelpOption` customization API needed for appending WORKFLOW and EXAMPLES sections.
- Fuzzy matching for `frank-cli help <unknown>` uses simple edit-distance or prefix matching -- no external NLP library is needed.
- The five existing commands (extract, clarify, validate, diff, compile) are the commands that receive help metadata. The new `help` and `status` commands also get basic help metadata but are not part of the extraction pipeline workflow.


## Research

# Research: frank-cli LLM-Ready Help System

**Date**: 2026-03-15
**Feature**: 016-frank-cli-help-system

## R1: System.CommandLine 2.0.3 Help Customization API

### Decision: Use `HelpOption.Action` wrapper pattern

### Rationale

System.CommandLine 2.0.3 provides a documented approach for customizing help output:

1. Find the `HelpOption` in a command's `Options` collection
2. Wrap its existing `HelpAction` with a custom `SynchronousCommandLineAction`
3. The custom action calls the default `HelpAction.Invoke(parseResult)` for standard output, then appends custom sections

Key API types:
- `HelpOption` -- the built-in `-h`/`--help` option, auto-added to `RootCommand`
- `HelpAction` -- the default action that renders standard help
- `SynchronousCommandLineAction` -- base class for custom synchronous actions
- `ParseResult` -- provides access to the parsed command, options, and `CommandResult.Command`

The custom action receives a `ParseResult` from which it can determine:
- Which command's help is being rendered (`parseResult.CommandResult.Command`)
- Whether `--context` was specified (check for the global option)

This is the officially documented pattern from Microsoft's System.CommandLine documentation.

### Alternatives Considered

1. **`HelpBuilder.CustomizeLayout`** -- This was the approach in older beta versions. In 2.0.3, the recommended approach for adding sections before/after help is the `HelpAction` wrapper pattern. `CustomizeLayout` still exists but is more suited for rearranging existing sections, not appending entirely new text blocks.

2. **Intercepting Console.Out** -- Capturing stdout to inject content. Rejected as fragile and not composable.

3. **Replacing help entirely** -- Writing a complete custom help renderer. Rejected because we want to preserve standard System.CommandLine help (usage, options, arguments) and only append our sections.

## R2: System.CommandLine Dependency Placement

### Decision: Add System.CommandLine 2.0.3 to Frank.Cli.Core

### Rationale

The user explicitly directed that all help infrastructure goes in `Frank.Cli.Core`. Currently, System.CommandLine is only referenced by `Frank.Cli` (the thin executable project). Adding it to `Frank.Cli.Core` is needed because:

- `HelpAction.fs` needs `SynchronousCommandLineAction`, `HelpAction`, `ParseResult`
- `HelpSubcommand.fs` needs command registration types
- The help action wrapper needs to be created in Core so it can be tested without the executable

Since `Frank.Cli` already depends on `Frank.Cli.Core`, and `Frank.Cli.Core` will now also depend on `System.CommandLine 2.0.3`, there is no version conflict -- both projects use the same version.

### Alternatives Considered

1. **Keep SCL only in Frank.Cli** -- Would require either (a) passing delegate-based abstractions from Cli to Core for help rendering, creating an unnecessary abstraction layer, or (b) putting all help logic in Program.fs, violating the "thin wiring" principle.

## R3: Staleness Detection Extraction

### Decision: Extract `checkStaleness` and `computeFileHash` from `ValidateCommand` into a shared `StalenessChecker` module

### Rationale

The spec requires the status command to detect stale extraction state (FR-020). The logic already exists in `ValidateCommand.checkStaleness` (lines 124-163 of `ValidateCommand.fs`). Constitution Principle VIII prohibits duplicating this logic.

The extraction involves:
1. Moving `computeFileHash` and `checkStaleness` to a new `Commands/StalenessChecker.fs` module
2. Having both `ValidateCommand` and `StatusCommand` call the shared module
3. The shared module returns the same `ValidationIssue` type (or a simpler staleness result that both consumers can map)

Since the staleness check needs `ExtractionState` (specifically `SourceMap` and `Metadata.SourceHash`), the shared module takes an `ExtractionState` and returns staleness information.

### Alternatives Considered

1. **StatusCommand calls ValidateCommand.execute** -- Rejected because `execute` does much more (OWL class validation, shape validation) and the status command only needs staleness info + basic state presence.

2. **Make `checkStaleness` public on ValidateCommand** -- Would work but mixes concerns. A dedicated module is cleaner and follows the existing pattern of focused modules.

## R4: Fuzzy Matching for "Did You Mean?" Suggestions

### Decision: Implement simple Levenshtein distance in a `FuzzyMatch` module

### Rationale

The spec requires fuzzy matching for `frank-cli help <unknown>` (FR-017). With only 7 commands and 2 topics (9 total candidates), a simple O(n*m) Levenshtein implementation is perfectly adequate. No external NLP library is needed.

The implementation:
1. Compute Levenshtein distance between the unknown input and each known name
2. Return candidates within a configurable threshold (e.g., distance <= 3 or <= 50% of input length)
3. Sort suggestions by distance (closest first)
4. Also check prefix matching (e.g., "ext" matches "extract")

### Alternatives Considered

1. **External library (FuzzySharp, etc.)** -- Overkill for 9 candidates. Adds a dependency for a trivial computation.
2. **Prefix matching only** -- Too limited. "comiple" (typo for "compile") would not match.
3. **Jaro-Winkler distance** -- Slightly better for short strings but unnecessary complexity for this use case.

## R5: Compiled Artifact Detection for Status Command

### Decision: Check for specific artifact filenames in the output directory

### Rationale

The status command (FR-021) must detect whether compiled artifacts exist. From `ArtifactSerializer.writeArtifacts`, the three artifact files are:
- `ontology.owl.xml`
- `shapes.shacl.ttl`
- `manifest.json`

These are written to `obj/frank-cli/` by default (same directory as `state.json`). The status command simply checks `File.Exists` for each of these files in the project's `obj/frank-cli/` directory.

The status command does NOT need to parse or validate these files -- their presence alone indicates compilation has been performed. Staleness of compiled artifacts relative to extraction state could be detected by comparing file timestamps, but the spec does not require this level of detail.

## R6: --context Global Option Design

### Decision: Add `--context` as a global `Option<bool>` on the `RootCommand`

### Rationale

The spec says `--context` is a global option on the root command (Assumption 3). It only has an effect when combined with `--help`. The custom `HelpAction` wrapper checks whether `--context` was set in the `ParseResult` and conditionally includes the CONTEXT section.

In System.CommandLine 2.0.3:
- Global options are added via `RootCommand.Options.Add(...)` (they propagate to all subcommands)
- The custom help action checks `parseResult.GetValue(contextOption)` to determine if context was requested
- The `--context` option needs to be accessible to the help action, so it must be created in a place where the help action can reference it (or looked up by name from the parse result)

### Alternatives Considered

1. **Per-command option** -- Spec explicitly says it's a global option. Per-command would require adding it to every command definition.
2. **Environment variable** -- Less discoverable. The spec wants it as a CLI flag.
