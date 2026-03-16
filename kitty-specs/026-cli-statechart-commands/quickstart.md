# Quickstart: CLI Statechart Commands

**Feature**: 026-cli-statechart-commands
**Date**: 2026-03-16

## Prerequisites

- A compiled Frank application containing `statefulResource` definitions (e.g., the tic-tac-toe sample)
- `frank-cli` built with statechart command support

## Build Your Application

```bash
# From your Frank application directory
dotnet build
```

This produces a compiled assembly at `bin/Debug/net10.0/MyApp.dll`.

## Extract State Machine Metadata

```bash
# See what state machines are in your application
frank-cli statechart extract bin/Debug/net10.0/MyApp.dll

# JSON output for programmatic use
frank-cli statechart extract bin/Debug/net10.0/MyApp.dll --format json
```

## Generate Spec Artifacts

```bash
# Generate WSD notation
frank-cli statechart generate --format wsd bin/Debug/net10.0/MyApp.dll

# Generate all formats, write to files
frank-cli statechart generate --format all bin/Debug/net10.0/MyApp.dll --output ./specs/

# Generate for a specific resource only
frank-cli statechart generate --format wsd bin/Debug/net10.0/MyApp.dll --resource games
```

## Validate Spec Against Code

```bash
# Check if your WSD file matches the implementation
frank-cli statechart validate specs/games.wsd bin/Debug/net10.0/MyApp.dll

# Validate multiple spec files (cross-format validation)
frank-cli statechart validate specs/games.wsd specs/games.alps.json bin/Debug/net10.0/MyApp.dll

# JSON output for CI integration
frank-cli statechart validate specs/games.wsd bin/Debug/net10.0/MyApp.dll --format json
```

## Import a Spec File

```bash
# Parse an XState JSON file for LLM consumption
frank-cli statechart import game.xstate.json

# Parse a WSD file
frank-cli statechart import onboarding.wsd

# Handle ambiguous .json files
frank-cli statechart import game.json --spec-format xstate
```

## Typical Workflows

### Code-First (extract from existing code)
```bash
dotnet build
frank-cli statechart extract bin/Debug/net10.0/MyApp.dll
frank-cli statechart generate --format all bin/Debug/net10.0/MyApp.dll --output ./specs/
git add specs/
```

### Design-First (import spec, scaffold code)
```bash
# Parse the design spec
frank-cli statechart import game.xstate.json --format json
# Feed the JSON output to an LLM for F# code scaffolding
```

### Validation in CI
```bash
dotnet build
frank-cli statechart validate specs/*.wsd specs/*.alps.json bin/Debug/net10.0/MyApp.dll --format json
# Non-zero exit code on validation failures
```

## Implementation Starting Point

The implementation touches these areas:

1. **XState serializer** (`src/Frank.Statecharts/XState/`): New format module
2. **Assembly loading** (`src/Frank.Cli.Core/Statechart/StatechartExtractor.fs`): AssemblyLoadContext-based metadata extraction
3. **Format pipeline** (`src/Frank.Cli.Core/Statechart/FormatPipeline.fs`): Metadata-to-text generation
4. **CLI commands** (`src/Frank.Cli.Core/Commands/Statechart*.fs`): Command logic
5. **CLI wiring** (`src/Frank.Cli/Program.fs`): System.CommandLine registration
6. **Output formatting** (`src/Frank.Cli.Core/Output/`): JSON and text formatters for statechart results
