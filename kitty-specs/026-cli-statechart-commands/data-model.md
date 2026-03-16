# Data Model: CLI Statechart Commands

**Feature**: 026-cli-statechart-commands
**Date**: 2026-03-16

## Existing Types (Referenced, Not Modified)

### StateMachineMetadata (Frank.Statecharts)
Source: `src/Frank.Statecharts/StatefulResourceBuilder.fs:59-85`

The primary data source for extraction. Contains all state machine information populated at CE evaluation time.

| Field | Type | CLI Use |
|-------|------|---------|
| Machine | `obj` | Not used (generic type erased) |
| StateHandlerMap | `Map<string, (string * RequestDelegate) list>` | State names (keys), HTTP methods per state |
| ResolveInstanceId | `HttpContext -> string` | Not used (runtime) |
| TransitionObservers | `(obj -> unit) list` | Not used (runtime) |
| InitialStateKey | `string` | Initial state |
| GuardNames | `string list` | Guard name display |
| StateMetadataMap | `Map<string, StateInfo>` | IsFinal, AllowedMethods, Description per state |
| GetCurrentStateKey | `IServiceProvider -> HttpContext -> string -> Task<string>` | Not used (runtime) |
| EvaluateGuards | `HttpContext -> GuardResult` | Not used (runtime) |
| EvaluateEventGuards | `HttpContext -> GuardResult` | Not used (runtime) |
| ExecuteTransition | `IServiceProvider -> HttpContext -> string -> Task<TransitionAttemptResult>` | Not used (runtime) |

### StateInfo (Frank.Statecharts)
Source: `src/Frank.Statecharts/Types.fs:42-45`

```fsharp
type StateInfo =
    { AllowedMethods: string list
      IsFinal: bool
      Description: string option }
```

### StatechartDocument (Frank.Statecharts.Ast)
Source: `src/Frank.Statecharts/Ast/Types.fs:205-210`

The shared AST used as interchange format between all parsers and generators.

```fsharp
type StatechartDocument =
    { Title: string option
      InitialStateId: string option
      Elements: StatechartElement list
      DataEntries: DataEntry list
      Annotations: Annotation list }
```

### FormatTag, FormatArtifact, ValidationReport (Frank.Statecharts.Validation)
Source: `src/Frank.Statecharts/Validation/Types.fs`

```fsharp
type FormatTag = Wsd | Alps | Scxml | Smcat | XState

type FormatArtifact =
    { Format: FormatTag
      Document: StatechartDocument }

type ValidationReport =
    { TotalChecks: int
      TotalSkipped: int
      TotalFailures: int
      Checks: ValidationCheck list
      Failures: ValidationFailure list }
```

## New Types

### ExtractedStatechart (Frank.Cli.Core.Statechart)
Location: `src/Frank.Cli.Core/Statechart/StatechartExtractor.fs`

Structured representation of a single stateful resource extracted from an assembly.

```fsharp
type ExtractedStatechart =
    { /// Route template from RouteEndpoint.RoutePattern.RawText (e.g., "/games/{id}")
      RouteTemplate: string
      /// Slug derived from route template for file naming (e.g., "games")
      ResourceSlug: string
      /// State names from StateHandlerMap keys
      StateNames: string list
      /// DU case name of the initial state
      InitialState: string
      /// Precomputed guard names
      GuardNames: string list
      /// Per-state metadata (allowed methods, final flag, description)
      StateMetadata: Map<string, StateInfo>
      /// Raw metadata for downstream generator use
      Metadata: StateMachineMetadata }
```

### StatechartFormat (Frank.Cli.Core.Statechart)
Location: `src/Frank.Cli.Core/Statechart/FormatDetector.fs`

Represents a detected file format from extension analysis.

```fsharp
type StatechartFormat =
    | Wsd
    | Alps
    | Scxml
    | Smcat
    | XState
    | Ambiguous of candidates: StatechartFormat list

type FormatDetectionResult =
    | Detected of StatechartFormat
    | Unsupported of extension: string
```

### GenerateTarget (Frank.Cli.Core.Statechart)
Location: `src/Frank.Cli.Core/Statechart/FormatPipeline.fs`

```fsharp
type GenerateTarget =
    | SingleFormat of StatechartFormat
    | AllFormats
```

### Extract Command Result Types
Location: `src/Frank.Cli.Core/Commands/StatechartExtractCommand.fs`

```fsharp
type StatechartExtractResult =
    { StateMachines: ExtractedStatechart list }
```

### Generate Command Result Types
Location: `src/Frank.Cli.Core/Commands/StatechartGenerateCommand.fs`

```fsharp
type GeneratedArtifact =
    { ResourceSlug: string
      Format: StatechartFormat
      Content: string
      /// File path if written to disk (--output)
      OutputPath: string option }

type StatechartGenerateResult =
    { Artifacts: GeneratedArtifact list }
```

### Validate Command Result Types
Location: `src/Frank.Cli.Core/Commands/StatechartValidateCommand.fs`

```fsharp
type StatechartValidateResult =
    { Report: ValidationReport
      /// True when TotalFailures = 0
      IsValid: bool }
```

### Import Command Result Types
Location: `src/Frank.Cli.Core/Commands/StatechartImportCommand.fs`

```fsharp
type StatechartImportResult =
    { Document: StatechartDocument
      Errors: ParseFailure list
      Warnings: ParseWarning list
      SourceFormat: StatechartFormat }
```

## Relationships

```
                            ┌──────────────────────┐
                            │  Compiled Assembly   │
                            │   (.dll + .deps.json) │
                            └──────────┬───────────┘
                                       │
                            ┌──────────▼───────────┐
                            │ StatechartExtractor   │
                            │ (AssemblyLoadContext)  │
                            └──────────┬───────────┘
                                       │
                            ┌──────────▼───────────┐
                            │ ExtractedStatechart[] │
                            │ (route, states, meta) │
                            └──────────┬───────────┘
                                       │
              ┌────────────┬───────────┼───────────┐
              │            │           │           │
        ┌─────▼─────┐ ┌───▼───┐ ┌─────▼─────┐ ┌───▼───┐
        │  extract   │ │generate│ │ validate  │ │import │
        │  command   │ │command │ │ command   │ │command│
        └─────┬─────┘ └───┬───┘ └─────┬─────┘ └───┬───┘
              │            │           │           │
              │     ┌──────▼──────┐    │    ┌──────▼──────┐
              │     │FormatPipeline│   │    │ FormatDetect │
              │     │ (5 formats) │    │    │ + Parser     │
              │     └──────┬──────┘    │    └──────┬──────┘
              │            │           │           │
              │            │     ┌─────▼─────┐     │
              │            │     │ Validator  │     │
              │            │     │ (spec 021) │     │
              │            │     └───────────┘     │
              │            │                       │
              ▼            ▼                       ▼
        JSON/text    Format files         StatechartDocument
         output      (wsd, alps,              JSON output
                      scxml, smcat,
                      xstate.json)
```

## State Transitions

N/A -- CLI commands are stateless. Each invocation is independent.

## Validation Rules

| Entity | Field | Rule |
|--------|-------|------|
| Assembly path | path | Must exist, must be a valid .NET assembly |
| Spec file path | path | Must exist, must be readable |
| File extension | extension | Must be recognized (.wsd, .alps.json, .scxml, .smcat, .xstate.json) or explicit `--format` provided |
| Format flag (generate) | format | Must be one of: wsd, alps, scxml, smcat, xstate, all |
| Output directory | path | Created automatically if missing (FR-015) |
| Resource filter | name | If `--resource` specified, must match at least one extracted resource slug |
