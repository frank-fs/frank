---
source: kitty-specs/016-frank-cli-help-system
type: plan
---

# Implementation Plan: frank-cli LLM-Ready Help System

**Branch**: `016-frank-cli-help-system` | **Date**: 2026-03-15 | **Spec**: [spec.md](spec.md)
**Input**: Feature specification from `/kitty-specs/016-frank-cli-help-system/spec.md`
**GitHub Issue**: #110

## Summary

Add an LLM-ready help system to frank-cli that enables coding agents to autonomously discover command workflows, understand semantic concepts, and orient themselves within a project. The system introduces:

1. **Content model** -- structured `CommandHelp`, `WorkflowPosition`, and `HelpTopic` records with hardcoded content for all 5 existing commands plus the 2 new commands.
2. **Enriched per-command `--help`** -- appends WORKFLOW, EXAMPLES, and (optionally) CONTEXT sections to standard System.CommandLine help output via a custom `HelpAction` wrapper.
3. **`help` subcommand** -- lists topics/commands, displays topic content, shows enriched command help, and provides fuzzy "did you mean?" suggestions for unknown arguments.
4. **`status` subcommand** -- inspects a project's extraction state directory to report current state and recommend the next action.
5. **JSON output** -- all new outputs support `--format json` consistent with existing conventions.

## Technical Context

**Language/Version**: F# 8.0+ targeting .NET 10.0 (single target, matching Frank.Cli and Frank.Cli.Core)
**Primary Dependencies**: System.CommandLine 2.0.3 (added to Frank.Cli.Core), existing Frank.Cli.Core infrastructure
**Storage**: N/A -- reads existing `obj/frank-cli/state.json` (ExtractionState) for status command; no new persistence
**Testing**: Expecto (10.2.3) via Frank.Cli.Core.Tests, following existing test patterns
**Target Platform**: .NET CLI tool (cross-platform)
**Project Type**: Single project -- additions to existing `Frank.Cli.Core` library + thin wiring in `Frank.Cli`
**Performance Goals**: N/A -- help and status are interactive commands, not hot paths
**Constraints**: No external dependencies for fuzzy matching (simple Levenshtein). All help content hardcoded as F# data records.
**Scale/Scope**: 7 commands (5 existing + help + status), 2 help topics (workflows, concepts)

## Constitution Check

*GATE: Must pass before Phase 0 research. Re-check after Phase 1 design.*

| Principle | Status | Notes |
|-----------|--------|-------|
| I. Resource-Oriented Design | N/A | CLI tooling, not HTTP resource modeling |
| II. Idiomatic F# | PASS | Records, discriminated unions, Option types, pipeline-friendly functions |
| III. Library, Not Framework | PASS | Adds CLI features only; no framework opinions imposed |
| IV. ASP.NET Core Native | N/A | CLI tool, not ASP.NET Core middleware |
| V. Performance Parity | N/A | Interactive CLI commands, not hot paths |
| VI. Resource Disposal Discipline | WATCH | Status command opens files for hash computation -- must use `use` bindings (reuse ValidateCommand pattern) |
| VII. No Silent Exception Swallowing | WATCH | Status command handles corrupt state files -- must report errors, not swallow |
| VIII. No Duplicated Logic | WATCH | Staleness detection logic exists in ValidateCommand -- must extract to shared module, not copy |

**Post-design re-check**: See bottom of Phase 1 section.

## Project Structure

### Documentation (this feature)

```
kitty-specs/016-frank-cli-help-system/
├── plan.md              # This file
├── research.md          # Phase 0 output
├── data-model.md        # Phase 1 output
├── quickstart.md        # Phase 1 output
├── contracts/           # Phase 1 output
└── tasks.md             # Phase 2 output (NOT created by /spec-kitty.plan)
```

### Source Code (repository root)

```
src/Frank.Cli.Core/
├── Help/
│   ├── HelpTypes.fs          # CommandHelp, WorkflowPosition, HelpTopic records
│   ├── HelpContent.fs        # Hardcoded help data for all commands and topics
│   ├── HelpRenderer.fs       # Text/JSON rendering for help output
│   ├── HelpAction.fs         # Custom SynchronousCommandLineAction wrapping default HelpAction
│   ├── HelpSubcommand.fs     # help subcommand logic (topic/command lookup, fuzzy match)
│   └── FuzzyMatch.fs         # Levenshtein distance for "did you mean?" suggestions
├── Commands/
│   ├── StatusCommand.fs      # Project status inspection logic
│   └── StalenessChecker.fs   # Extracted from ValidateCommand (shared staleness detection)
├── Output/
│   ├── TextOutput.fs         # Extended with formatStatusResult, formatHelpOutput
│   └── JsonOutput.fs         # Extended with formatStatusResult, formatHelpOutput
└── Frank.Cli.Core.fsproj     # Add System.CommandLine 2.0.3, new Compile entries

src/Frank.Cli/
└── Program.fs                # Thin wiring: register help/status commands, attach HelpAction

test/Frank.Cli.Core.Tests/
├── Help/
│   ├── HelpTypesTests.fs     # Content model validation
│   ├── HelpContentTests.fs   # Completeness tests (every command has metadata)
│   ├── HelpRendererTests.fs  # Rendering output tests
│   ├── HelpSubcommandTests.fs # Topic/command lookup, fuzzy matching
│   └── FuzzyMatchTests.fs    # Levenshtein distance unit tests
├── Commands/
│   ├── StatusCommandTests.fs # Status command logic tests
│   └── StalenessCheckerTests.fs # Shared staleness detection tests
└── Frank.Cli.Core.Tests.fsproj # Add new Compile entries
```

**Structure Decision**: New files are organized under a `Help/` subdirectory within `Frank.Cli.Core` to keep the help system cohesive. The `StalenessChecker` is extracted from `ValidateCommand` into its own module under `Commands/` (Constitution VIII compliance -- no duplicated logic). Output formatters are extended in-place following existing patterns.

## Complexity Tracking

| Violation | Why Needed | Simpler Alternative Rejected Because |
|-----------|------------|-------------------------------------|
| Adding System.CommandLine to Frank.Cli.Core | Help customization requires `HelpOption`, `HelpAction`, `SynchronousCommandLineAction`, `ParseResult` types | Keeping SCL only in Frank.Cli would require duplicating the content model or creating a complex abstraction boundary |

## Constitution Check -- Post-Design Re-Check

| Principle | Status | Design Verification |
|-----------|--------|---------------------|
| VI. Resource Disposal | PASS | `StalenessChecker.computeFileHash` uses `use stream = File.OpenRead(filePath)` (same pattern as existing `ValidateCommand`). `StatusCommand` uses `ExtractionState.load` which already handles `JsonDocument` with `use`. No new `IDisposable` values introduced without `use` bindings. |
| VII. No Silent Swallowing | PASS | `StatusCommand` returns `ExtractionStatus.Unreadable of reason` when state file is corrupt -- the error message is surfaced to the user, not swallowed. The status command returns `Error` for missing project files. All error paths produce user-visible output. |
| VIII. No Duplicated Logic | PASS | `StalenessChecker.fs` extracts `computeFileHash` and `checkStaleness` from `ValidateCommand`. Both `ValidateCommand` and `StatusCommand` call the shared module. The refactoring is a prerequisite for the status command work package. |

No new violations introduced. All WATCH items resolved in the design.
