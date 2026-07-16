namespace Frank.Cli.MSBuild

open Microsoft.Build.Framework
open Microsoft.Build.Utilities

/// MSBuild task: reads the semantic lock file and fails the build if any mapping
/// or field mapping has a status other than Confirmed.
/// Error code MS001 is emitted with the count of non-confirmed entries.
/// Public: loaded by reflection via <UsingTask TaskName="Frank.Cli.MSBuild.ValidateLockFileTask">
/// in build/Frank.Cli.MSBuild.targets — MSBuild's task loader requires a public type (#392).
type ValidateLockFileTask =
    inherit Task
    new: unit -> ValidateLockFileTask

    [<Required>]
    member LockFilePath: string with get, set

    override Execute: unit -> bool
