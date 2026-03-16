# Data Model: frank-cli LLM-Ready Help System

**Date**: 2026-03-15
**Feature**: 016-frank-cli-help-system

## Entity Definitions

### CommandHelp

A structured record associating a command with its enriched help metadata. One record per registered command.

```fsharp
/// A concrete invocation example for a command.
type CommandExample =
    { /// The full command-line invocation (e.g., "frank-cli extract --project MyApp.fsproj --base-uri http://example.org/")
      Invocation: string
      /// Brief description of what this example demonstrates
      Description: string }

/// Where a command sits in the extraction pipeline.
type WorkflowPosition =
    { /// Step number in the pipeline (1-based). Non-pipeline commands (help, status) use 0.
      StepNumber: int
      /// Command names that must be run before this command (empty for first step)
      Prerequisites: string list
      /// Command names that logically follow this command (empty for last step)
      NextSteps: string list
      /// Whether this command is optional in the pipeline
      IsOptional: bool }

/// Complete help metadata for a single command.
type CommandHelp =
    { /// The command name as registered (e.g., "extract", "compile")
      Name: string
      /// One-line summary of what the command does
      Summary: string
      /// Concrete invocation examples (at least one required)
      Examples: CommandExample list
      /// Position in the extraction workflow pipeline
      Workflow: WorkflowPosition
      /// Detailed explanation of the command's semantic meaning in Frank's model.
      /// Displayed only when --context is active.
      Context: string }
```

**Validation Rules**:
- `Name` must be non-empty and match a registered command name
- `Summary` must be non-empty
- `Examples` must have at least one entry (FR-005)
- `Workflow.Prerequisites` entries must reference valid command names (FR-006)
- `Workflow.NextSteps` entries must reference valid command names
- `Context` must be non-empty

### HelpTopic

A named documentation article discoverable via `frank-cli help`.

```fsharp
/// A standalone help topic (e.g., "workflows", "concepts").
type HelpTopic =
    { /// The topic identifier used in `frank-cli help <topic>` (e.g., "workflows")
      Name: string
      /// One-line summary shown in topic listings
      Summary: string
      /// Full content body displayed when the topic is selected
      Content: string }
```

**Validation Rules**:
- `Name` must be non-empty and unique across all topics
- `Summary` must be non-empty
- `Content` must be non-empty

### ProjectStatus

The result of inspecting a project's extraction and compilation state.

```fsharp
/// The detected state of a project's extraction pipeline.
type ExtractionStatus =
    /// No extraction state file exists
    | NotExtracted
    /// Extraction state exists and is current (source files unchanged)
    | Current
    /// Extraction state exists but source files have changed since extraction
    | Stale
    /// Extraction state file exists but could not be parsed
    | Unreadable of reason: string

/// Whether compiled artifacts exist.
type ArtifactStatus =
    /// All three artifacts (ontology.owl.xml, shapes.shacl.ttl, manifest.json) are present
    | Present
    /// Some or all artifacts are missing
    | Missing of missingFiles: string list

/// The recommended next action based on current project state.
type RecommendedAction =
    /// No extraction has been performed yet
    | RunExtract
    /// Extraction is stale and should be re-run
    | ReExtract
    /// Extraction is current but no compiled artifacts exist
    | RunCompile
    /// Everything is up to date
    | UpToDate
    /// State is unreadable; re-extract to recover
    | RecoverExtract of reason: string

/// Complete project status report.
type ProjectStatus =
    { /// Path to the .fsproj file that was inspected
      ProjectPath: string
      /// State of the extraction
      Extraction: ExtractionStatus
      /// State of compiled artifacts
      Artifacts: ArtifactStatus
      /// Recommended next action
      RecommendedAction: RecommendedAction
      /// Path to the state directory (obj/frank-cli/)
      StateDirectory: string }
```

**State Transition Logic**:

```
No state.json        -> NotExtracted + Missing    -> RunExtract
Unreadable state.json -> Unreadable  + Missing/Present -> RecoverExtract
Current state.json   -> Current     + Missing     -> RunCompile
Current state.json   -> Current     + Present     -> UpToDate
Stale state.json     -> Stale       + Missing/Present -> ReExtract
```

### HelpLookupResult

The result of looking up a help argument (command name, topic name, or unknown).

```fsharp
/// Result of resolving a help argument.
type HelpLookupResult =
    /// Matched a registered command -- display full enriched help
    | CommandMatch of CommandHelp
    /// Matched a help topic -- display topic content
    | TopicMatch of HelpTopic
    /// No match found -- suggest alternatives
    | NoMatch of suggestions: string list
```

## Shared Staleness Checker (extracted from ValidateCommand)

```fsharp
/// Result of checking whether extraction state is stale relative to source files.
type StalenessResult =
    /// Source files have not changed since extraction
    | Fresh
    /// Source files have changed since extraction
    | Stale
    /// No source files recorded in state (cannot determine staleness)
    | Indeterminate

module StalenessChecker =
    /// Compute a SHA-256 hash of a file's contents.
    val computeFileHash: filePath: string -> string

    /// Check whether the extraction state is stale by comparing current source
    /// file hashes against the stored SourceHash.
    val checkStaleness: state: ExtractionState -> StalenessResult
```

## Relationships

```
CommandHelp 1──1 WorkflowPosition     (each command has exactly one workflow position)
CommandHelp *──* CommandHelp           (prerequisites/nextSteps reference other commands by name)
HelpTopic   (standalone, no FK)
ProjectStatus 1──1 ExtractionStatus   (status includes one extraction state)
ProjectStatus 1──1 ArtifactStatus     (status includes one artifact state)
ProjectStatus 1──1 RecommendedAction  (status includes one recommendation)
StalenessChecker ──> ExtractionState  (reads from existing state type)
ValidateCommand ──> StalenessChecker  (refactored to use shared module)
StatusCommand ──> StalenessChecker    (new consumer of shared module)
```
