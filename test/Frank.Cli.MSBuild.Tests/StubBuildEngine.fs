module Frank.Cli.MSBuild.Tests.StubBuildEngine

open Microsoft.Build.Framework

/// Minimal IBuildEngine stub that captures error and warning messages for assertion.
type StubBuildEngine() =
    let errors = System.Collections.Generic.List<BuildErrorEventArgs>()
    let warnings = System.Collections.Generic.List<BuildWarningEventArgs>()
    let messages = System.Collections.Generic.List<BuildMessageEventArgs>()

    member _.Errors = errors |> Seq.toList
    member _.ErrorCodes = errors |> Seq.map (fun e -> e.Code) |> Seq.toList
    member _.Warnings = warnings |> Seq.toList
    member _.WarningCodes = warnings |> Seq.map (fun w -> w.Code) |> Seq.toList
    member _.WarningMessages = warnings |> Seq.map (fun w -> w.Message) |> Seq.toList

    interface IBuildEngine with
        member _.ContinueOnError = false
        member _.LineNumberOfTaskNode = 0
        member _.ColumnNumberOfTaskNode = 0
        member _.ProjectFileOfTaskNode = ""

        member _.LogErrorEvent(e) = errors.Add e
        member _.LogMessageEvent(e) = messages.Add e
        member _.LogWarningEvent(w) = warnings.Add w
        member _.LogCustomEvent(_) = ()

        member _.BuildProjectFile(_, _, _, _) = false
