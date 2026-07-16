namespace Frank.Cli.MSBuild

open Microsoft.Build.Framework
open Microsoft.Build.Utilities

/// Consolidated MSBuild task: runs ONE FCS ParseAndCheckProject over the shared sources
/// and calls LinkedData / Semantic / Validation / Provenance emitters in sequence.
///
/// HasLinkedData/HasValidation/HasProvenance gate their respective emitters.
/// Semantics emits unconditionally whenever the target runs (lock-only, no package gate).
/// HasSemantic is retained for API compatibility but is not used as a gate.
/// Discovery is NOT handled here — it uses its own GenerateDiscoveryTask (lock-only, no FCS).
///
/// FcsPassCount (output property) is always 1 after a successful run.
/// Use it in unit tests to assert exactly one ParseAndCheckProject call.
///
/// Public: loaded by reflection via <UsingTask TaskName="Frank.Cli.MSBuild.GenerateFcsEmittersTask">
/// in build/Frank.Cli.MSBuild.targets — MSBuild's task loader requires a public type (#392).
type GenerateFcsEmittersTask =
    inherit Task
    new: unit -> GenerateFcsEmittersTask

    [<Required>]
    member LockFilePath: string with get, set

    [<Required>]
    member OutputPath: string with get, set

    [<Required>]
    member SourceFiles: ITaskItem[] with get, set

    [<Required>]
    member AssemblyRefs: ITaskItem[] with get, set

    member VocabularyBinding: string with get, set

    member HasLinkedData: bool with get, set

    /// Retained for API compatibility. Semantics emits unconditionally; this flag is ignored.
    member HasSemantic: bool with get, set

    member HasValidation: bool with get, set
    member HasProvenance: bool with get, set

    member LinkedDataModuleName: string with get, set
    member SemanticsModuleName: string with get, set
    member ValidationModuleName: string with get, set
    member ProvenanceModuleName: string with get, set

    [<Output>]
    member GeneratedLinkedDataFile: string with get, set

    [<Output>]
    member GeneratedSemanticsFile: string with get, set

    [<Output>]
    member GeneratedValidationFile: string with get, set

    [<Output>]
    member GeneratedProvenanceFile: string with get, set

    /// Counts ParseAndCheckProject invocations. Always 1 after a successful run.
    /// Exposed as an output property for unit-test counting seam (AC1 #386).
    [<Output>]
    member FcsPassCount: int with get, set

    override Execute: unit -> bool
