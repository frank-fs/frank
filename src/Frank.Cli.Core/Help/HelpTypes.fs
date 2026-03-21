namespace Frank.Cli.Core.Help

/// A concrete invocation example for a command.
type CommandExample =
    { /// The full command-line invocation (e.g., "frank extract --project MyApp.fsproj --base-uri http://example.org/")
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

/// A standalone help topic (e.g., "workflows", "concepts").
type HelpTopic =
    { /// The topic identifier used in `frank help <topic>` (e.g., "workflows")
      Name: string
      /// One-line summary shown in topic listings
      Summary: string
      /// Full content body displayed when the topic is selected
      Content: string }

/// Result of resolving a help argument.
type HelpLookupResult =
    /// Matched a registered command -- display full enriched help
    | CommandMatch of CommandHelp
    /// Matched a help topic -- display topic content
    | TopicMatch of HelpTopic
    /// No match found -- suggest alternatives
    | NoMatch of suggestions: string list

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
      /// Path to the state directory (obj/frank/)
      StateDirectory: string }
