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
