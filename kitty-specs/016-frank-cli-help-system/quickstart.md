# Quickstart: frank-cli LLM-Ready Help System

**Date**: 2026-03-15
**Feature**: 016-frank-cli-help-system

## Implementation Order

### Phase 1: Foundation (no System.CommandLine dependency needed)

1. **HelpTypes.fs** -- Define `CommandHelp`, `WorkflowPosition`, `HelpTopic`, `ProjectStatus`, and related types
2. **FuzzyMatch.fs** -- Implement Levenshtein distance and suggestion logic
3. **StalenessChecker.fs** -- Extract `computeFileHash` and `checkStaleness` from `ValidateCommand.fs` into shared module
4. **Refactor ValidateCommand.fs** -- Update to call `StalenessChecker` instead of local functions

### Phase 2: Content and Logic

5. **HelpContent.fs** -- Define hardcoded help data for all 7 commands and 2 topics
6. **StatusCommand.fs** -- Implement project status inspection logic
7. **HelpSubcommand.fs** -- Implement help lookup logic (command/topic resolution, fuzzy matching)

### Phase 3: Rendering and Integration

8. **HelpRenderer.fs** -- Text and JSON rendering for enriched help sections (WORKFLOW, EXAMPLES, CONTEXT)
9. **Extend TextOutput.fs** -- Add `formatStatusResult` and `formatHelpOutput` functions
10. **Extend JsonOutput.fs** -- Add `formatStatusResult` and `formatHelpOutput` functions
11. **HelpAction.fs** -- Custom `SynchronousCommandLineAction` that wraps default `HelpAction` (requires System.CommandLine)
12. **Update Frank.Cli.Core.fsproj** -- Add System.CommandLine 2.0.3 dependency, add all new Compile entries

### Phase 4: Wiring

13. **Update Program.fs** -- Register `status` and `help` commands, add `--context` global option, attach custom `HelpAction` to all commands

### Phase 5: Tests

14. **HelpTypesTests.fs** -- Content model validation
15. **FuzzyMatchTests.fs** -- Levenshtein distance correctness
16. **HelpContentTests.fs** -- Completeness validation (every command has metadata, valid cross-references)
17. **StalenessCheckerTests.fs** -- Shared staleness detection
18. **StatusCommandTests.fs** -- All four project states + error cases
19. **HelpSubcommandTests.fs** -- Topic/command lookup, fuzzy matching, "did you mean?"
20. **HelpRendererTests.fs** -- Output format verification

## Key Integration Points

### Adding System.CommandLine to Frank.Cli.Core.fsproj

```xml
<PackageReference Include="System.CommandLine" Version="2.0.3" />
```

### Compile order in Frank.Cli.Core.fsproj

New files must be inserted in dependency order. The approximate insertion points:

```xml
<!-- After State/DiffEngine.fs, before Output/ArtifactSerializer.fs -->
<Compile Include="Help/HelpTypes.fs" />
<Compile Include="Help/FuzzyMatch.fs" />
<Compile Include="Help/HelpContent.fs" />
<Compile Include="Help/HelpRenderer.fs" />
<Compile Include="Help/HelpSubcommand.fs" />
<Compile Include="Help/HelpAction.fs" />

<!-- Before Commands/ValidateCommand.fs -->
<Compile Include="Commands/StalenessChecker.fs" />
<Compile Include="Commands/StatusCommand.fs" />
```

Note: `StalenessChecker.fs` must appear before `ValidateCommand.fs` and `StatusCommand.fs` in the compile order since both depend on it. This means reordering the existing Commands/ entries.

### ValidateCommand refactoring

Replace the private `computeFileHash` and `checkStaleness` functions with calls to `StalenessChecker`:

```fsharp
// Before (ValidateCommand.fs lines 124-163):
let private computeFileHash (filePath: string) : string = ...
let private checkStaleness (state: ExtractionState) : ValidationIssue list = ...

// After:
// These functions are removed; ValidateCommand calls StalenessChecker.checkStaleness
// and maps the result to ValidationIssue
```

### Program.fs wiring pattern

```fsharp
// 1. Create --context global option
let contextOpt = Option<bool>("--context")
contextOpt.Description <- "Include semantic context in --help output"
root.Options.Add(contextOpt)

// 2. Register status command
let statusCmd = Command("status")
// ... add --project, --format options, set action ...
root.Subcommands.Add(statusCmd)

// 3. Register help subcommand
let helpCmd = Command("help")
// ... add argument, --format option, set action ...
root.Subcommands.Add(helpCmd)

// 4. Attach custom HelpAction to all commands
// Find HelpOption on root and replace its action
// The custom action wraps the default and appends WORKFLOW/EXAMPLES/CONTEXT
```

## Build and Test

```bash
# Build
dotnet build src/Frank.Cli.Core/Frank.Cli.Core.fsproj
dotnet build src/Frank.Cli/Frank.Cli.fsproj

# Test
dotnet test test/Frank.Cli.Core.Tests/Frank.Cli.Core.Tests.fsproj

# Manual verification
dotnet run --project src/Frank.Cli -- extract --help
dotnet run --project src/Frank.Cli -- --context extract --help
dotnet run --project src/Frank.Cli -- help
dotnet run --project src/Frank.Cli -- help workflows
dotnet run --project src/Frank.Cli -- help concepts
dotnet run --project src/Frank.Cli -- help extract
dotnet run --project src/Frank.Cli -- help comiple
dotnet run --project src/Frank.Cli -- status --project path/to/MyApp.fsproj
dotnet run --project src/Frank.Cli -- status --project path/to/MyApp.fsproj --format json
```
