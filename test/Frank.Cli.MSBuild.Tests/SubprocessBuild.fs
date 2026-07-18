module Frank.Cli.MSBuild.Tests.SubprocessBuild

open System.Diagnostics
open System.IO
open System.Threading.Tasks
open Frank.Cli.MSBuild

/// Walk up n directory levels from path.
let goUp (n: int) (path: string) : string =
    let rec loop remaining current =
        if remaining = 0 then
            current
        else
            loop (remaining - 1) (Path.GetDirectoryName(current: string))

    loop n path

/// Worktree root: 6 levels up from the test assembly path.
/// Assembly path: <root>/test/Frank.Cli.MSBuild.Tests/bin/<cfg>/net10.0/<name>.dll
let worktreeRoot: string = typeof<ValidateLockFileTask>.Assembly.Location |> goUp 6

/// Shared subprocess driver for tests that shell out to a real `dotnet build`/`dotnet msbuild`.
/// Captures combined stdout+stderr; kills the entire process tree on capMs timeout — a bare
/// Kill() leaves MSBuild's `nodeReuse:true` worker children orphaned (#402).
let runProcess (exe: string) (args: string) (capMs: int) : int * string =
    let psi = ProcessStartInfo(exe, args)
    psi.RedirectStandardOutput <- true
    psi.RedirectStandardError <- true
    psi.UseShellExecute <- false
    psi.Environment.["DOTNET_SYSTEM_GLOBALIZATION_INVARIANT"] <- "1"

    use proc = new Process()
    proc.StartInfo <- psi
    proc.Start() |> ignore

    let stdoutTask: Task<string> = proc.StandardOutput.ReadToEndAsync()
    let stderrTask: Task<string> = proc.StandardError.ReadToEndAsync()
    let allDone = Task.WhenAll([| stdoutTask :> Task; stderrTask :> Task |])
    let finished = allDone.Wait(capMs)

    if not finished then
        try
            proc.Kill(true)
        with _ ->
            ()

        invalidOp $"{exe} did not complete within cap of {capMs}ms"

    proc.WaitForExit()
    proc.ExitCode, stdoutTask.Result + stderrTask.Result
