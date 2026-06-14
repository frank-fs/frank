module Frank.Cli.MSBuild.Tests.StubBuildEngine

open System.Collections
open Microsoft.Build.Framework

/// Minimal IBuildEngine stub that captures error messages for assertion.
type StubBuildEngine() =
    let errors = System.Collections.Generic.List<BuildErrorEventArgs>()
    let messages = System.Collections.Generic.List<BuildMessageEventArgs>()

    member _.Errors = errors |> Seq.toList
    member _.ErrorCodes = errors |> Seq.map (fun e -> e.Code) |> Seq.toList

    interface IBuildEngine with
        member _.ContinueOnError = false
        member _.LineNumberOfTaskNode = 0
        member _.ColumnNumberOfTaskNode = 0
        member _.ProjectFileOfTaskNode = ""

        member _.LogErrorEvent(e) = errors.Add e
        member _.LogMessageEvent(e) = messages.Add e
        member _.LogWarningEvent(_) = ()
        member _.LogCustomEvent(_) = ()

        member _.BuildProjectFile(_, _, _, _) = false
