module Frank.TestSupport.RunCli

open System.Diagnostics
open System.Threading.Tasks

/// Run `dotnet <cliDll> <args>` and return (exitCode, stdout, stderr).
/// Bound by capMs (Holzmann rule 10); kills the process and raises on timeout.
/// `cliDll` is caller-supplied (not resolved here) — it must be located relative to
/// the CALLING test assembly's own output directory (via that assembly's
/// `Assembly.GetExecutingAssembly().Location`), which differs per test project.
let run (cliDll: string) (args: string[]) (capMs: int) : int * string * string =
    let argStr = String.concat " " (args |> Array.map (fun a -> $"\"{a}\""))
    let psi = ProcessStartInfo("dotnet", $"\"{cliDll}\" {argStr}")
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
            proc.Kill()
        with _ ->
            ()

        invalidOp $"CLI did not complete within cap of {capMs}ms"

    proc.WaitForExit()
    proc.ExitCode, stdoutTask.Result, stderrTask.Result
