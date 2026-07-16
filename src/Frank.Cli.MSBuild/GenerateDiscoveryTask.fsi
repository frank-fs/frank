namespace Frank.Cli.MSBuild

open Microsoft.Build.Framework
open Microsoft.Build.Utilities

/// MSBuild task: reads the semantic lock file, builds a minimal VocabularyRegistry
/// from the lock's Vocabularies map, and calls DiscoveryEmitter to write GeneratedDiscovery.fs
/// to the intermediate output directory.
/// Public: loaded by reflection via <UsingTask TaskName="Frank.Cli.MSBuild.GenerateDiscoveryTask">
/// in build/Frank.Cli.MSBuild.targets — MSBuild's task loader requires a public type (#392).
type GenerateDiscoveryTask =
    inherit Task
    new: unit -> GenerateDiscoveryTask

    [<Required>]
    member LockFilePath: string with get, set

    [<Required>]
    member OutputPath: string with get, set

    [<Required>]
    member ModuleName: string with get, set

    member ProfileUri: string with get, set

    [<Output>]
    member GeneratedFile: string with get, set

    override Execute: unit -> bool
