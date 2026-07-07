module Frank.Cli.MSBuild.Tests.HrefVarsGateTests

open System.Diagnostics
open System.IO
open System.Threading.Tasks
open Expecto
open Frank.Cli.MSBuild

// ── Path resolution ───────────────────────────────────────────────────────────

let private goUp (n: int) (path: string) : string =
    let rec loop remaining current =
        if remaining = 0 then
            current
        else
            loop (remaining - 1) (Path.GetDirectoryName(current: string))

    loop n path

/// Worktree root: 6 levels up from the test assembly path.
let private worktreeRoot: string =
    typeof<ValidateLockFileTask>.Assembly.Location |> goUp 6

let private hrefVarsFixtureFsproj: string =
    Path.Combine(
        worktreeRoot,
        "test",
        "Frank.Discovery.HrefVarsFixture",
        "Frank.Discovery.HrefVarsFixture.fsproj"
    )

let private runProcess (exe: string) (args: string) (capMs: int) : int * string =
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
            proc.Kill()
        with _ ->
            ()

        invalidOp $"{exe} did not complete within cap of {capMs}ms"

    proc.WaitForExit()
    proc.ExitCode, stdoutTask.Result + stderrTask.Result

// ── Tests ─────────────────────────────────────────────────────────────────────

[<Tests>]
let hrefVarsGateTests =
    testSequenced
    <| testList
        "A3 — HrefVarsFixture build gate (subprocess dotnet build)"
        [ test "Negative: bad fixture build fails non-zero and names gameId" {
              let capMs = 180_000
              let exitCode, combined = runProcess "dotnet" $"build \"{hrefVarsFixtureFsproj}\"" capMs

              Expect.isTrue (exitCode <> 0) $"Bad fixture must exit non-zero; got {exitCode}"

              Expect.stringContains combined "gameId" "build output must name the unmapped variable 'gameId'"
          }

          test "Positive: FrankSkipHrefVarsValidation=true skips gate (target condition evaluates false)" {
              // Run only the FrankValidateHrefVars MSBuild target with the skip flag set.
              // The target condition includes '$(FrankSkipHrefVarsValidation)' != 'true',
              // so MSBuild skips the target and exits 0 — no compilation or app launch needed.
              let args =
                  $"msbuild \"{hrefVarsFixtureFsproj}\" /t:FrankValidateHrefVars /p:FrankSkipHrefVarsValidation=true"

              let capMs = 30_000
              let exitCode, combined = runProcess "dotnet" args capMs

              Expect.equal
                  exitCode
                  0
                  $"FrankSkipHrefVarsValidation=true must cause gate to be skipped (exit 0); output:\n{combined}"
          } ]
