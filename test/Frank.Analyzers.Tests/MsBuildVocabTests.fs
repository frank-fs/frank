module Frank.Analyzers.Tests.MsBuildVocabTests

open System
open System.IO
open System.Diagnostics
open Expecto

// ── Paths ─────────────────────────────────────────────────────────────────────

let private repoRoot =
    let assemblyDir = AppContext.BaseDirectory

    let rec find (dir: string) =
        if Directory.Exists(Path.Combine(dir, "src", "Frank.Analyzers")) then
            Some dir
        else
            let parent = Directory.GetParent dir
            if isNull parent then None else find parent.FullName

    match find assemblyDir with
    | Some d -> d
    | None -> failwith "Could not find repo root"

let private frankCliMsBuildDir = Path.Combine(repoRoot, "src", "Frank.Cli.MSBuild")

let private msbuildFixturesDir =
    Path.Combine(repoRoot, "test", "Frank.Analyzers.Tests", "msbuild-fixtures")

// ── Process runner ────────────────────────────────────────────────────────────

let private runProcess (exe: string) (args: string) (workDir: string) : int * string =
    let psi = ProcessStartInfo(exe, args)
    psi.WorkingDirectory <- workDir
    psi.UseShellExecute <- false
    psi.RedirectStandardOutput <- true
    psi.RedirectStandardError <- true
    psi.EnvironmentVariables["DOTNET_SYSTEM_GLOBALIZATION_INVARIANT"] <- "1"

    use proc = new Process(StartInfo = psi)
    proc.Start() |> ignore
    let stdout = proc.StandardOutput.ReadToEnd()
    let stderr = proc.StandardError.ReadToEnd()
    proc.WaitForExit()
    proc.ExitCode, stdout + stderr

// ── Test helpers ──────────────────────────────────────────────────────────────

let private buildFrankCliMsBuild () =
    let exitCode, output = runProcess "dotnet" "build -c Debug" frankCliMsBuildDir

    if exitCode <> 0 then
        failwith $"Frank.Cli.MSBuild build failed:\n{output}"

let private runBuildFixture (fixtureName: string) : int * string =
    let projDir = Path.Combine(msbuildFixturesDir, fixtureName)
    let projFile = Path.Combine(projDir, $"{fixtureName}.proj")
    runProcess "dotnet" $"msbuild \"{projFile}\" /t:Build" projDir

// ── Tests ─────────────────────────────────────────────────────────────────────

[<Tests>]
let msbuildTests =
    testList
        "MSBuild FrankCheckVocab"
        [

          testCase "Setup: build Frank.Cli.MSBuild" <| fun _ -> buildFrankCliMsBuild ()

          testCase "AT4: undereferenceable vocab + TreatWarningsAsErrors → build fails with FRANK002"
          <| fun _ ->
              let exitCode, output = runBuildFixture "VocabWarn"
              Expect.isGreaterThan exitCode 0 "Build should fail with FRANK002 error"
              Expect.stringContains output "FRANK002" "Output should mention FRANK002"

          testCase "Benign guard: fetched vocab + TreatWarningsAsErrors → build succeeds"
          <| fun _ ->
              let exitCode, output = runBuildFixture "VocabBenign"
              Expect.equal exitCode 0 $"Build should succeed for benign vocab. Output:\n{output}"

          testCase "AT2 MSBuild: resource /tictactoe in source covers /tictactoe# namespace → no FRANK002"
          <| fun _ ->
              let exitCode, output = runBuildFixture "VocabRouted"
              Expect.equal exitCode 0 $"Build should succeed when route covers namespace. Output:\n{output}"

          testCase "AT2 MSBuild fail-when-broken: resource /tic does not cover /tictactoe# → FRANK002 fires"
          <| fun _ ->
              let exitCode, output = runBuildFixture "VocabRoutedBroken"
              Expect.isGreaterThan exitCode 0 "Build should fail when route does not cover namespace"
              Expect.stringContains output "FRANK002" "Output should mention FRANK002" ]
