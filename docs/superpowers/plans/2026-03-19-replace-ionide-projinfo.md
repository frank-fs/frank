# Replace Ionide.ProjInfo with dotnet msbuild + FCS

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Replace the broken Ionide.ProjInfo dependency with direct `dotnet msbuild` structured JSON output to resolve F# project options for FCS, fixing `frank-cli extract` on .NET 10.

**Architecture:** Shell out to `dotnet msbuild <fsproj> /t:ResolveAssemblyReferences /p:DesignTimeBuild=true -getItem:Compile -getItem:ReferencePath -getProperty:DefineConstants -getProperty:OtherFlags` as a single command. Parse the JSON result to extract source files, assembly references, and defines. Feed them to `FSharpChecker.GetProjectOptionsFromCommandLineArgs` + `ParseAndCheckProject`. Zero binary coupling to MSBuild internals.

**Tech Stack:** `dotnet msbuild` (process invocation), `System.Text.Json` (parse structured output), `FSharp.Compiler.Service` (existing dependency)

---

### Task 1: Rewrite ProjectLoader.fs to use dotnet msbuild

**Files:**
- Modify: `src/Frank.Cli.Core/Analysis/ProjectLoader.fs`

- [ ] **Step 1: Write the MSBuild invocation helper**

Replace the entire `ProjectLoader` module. The new implementation:

```fsharp
namespace Frank.Cli.Core.Analysis

open System
open System.Diagnostics
open System.IO
open System.Text
open System.Text.Json
open FSharp.Compiler.CodeAnalysis
open FSharp.Compiler.Syntax
open FSharp.Compiler.Text

type LoadedProject = {
    ProjectPath: string
    ParsedFiles: (string * ParsedInput) list
    CheckResults: FSharpCheckProjectResults
}

module ProjectLoader =

    let private parseSourceFile (checker: FSharpChecker) (sourceFile: string) =
        async {
            let sourceText = SourceText.ofString (File.ReadAllText sourceFile)
            let parsingOptions = { FSharpParsingOptions.Default with SourceFiles = [| sourceFile |] }
            let! parseResult = checker.ParseFile(sourceFile, sourceText, parsingOptions)
            if parseResult.ParseHadErrors then return None
            else return Some (sourceFile, parseResult.ParseTree)
        }

    /// Detect the first target framework for multi-targeted projects.
    let private detectTargetFramework (fsprojPath: string) : string option =
        let psi = ProcessStartInfo("dotnet")
        psi.Arguments <- $"msbuild \"{fsprojPath}\" -getProperty:TargetFrameworks -getProperty:TargetFramework"
        psi.RedirectStandardOutput <- true
        psi.RedirectStandardError <- true
        psi.UseShellExecute <- false
        psi.WorkingDirectory <- Path.GetDirectoryName fsprojPath

        use proc = Process.Start(psi)
        let stdout = proc.StandardOutput.ReadToEnd()
        proc.StandardError.ReadToEnd() |> ignore
        proc.WaitForExit()

        if proc.ExitCode <> 0 then None
        else
        try
            let doc = JsonDocument.Parse(stdout)
            let props = doc.RootElement.GetProperty("Properties")

            // Check TargetFrameworks (plural) first — multi-targeted
            match props.TryGetProperty("TargetFrameworks") with
            | true, v ->
                let tfms = v.GetString()
                if not (String.IsNullOrWhiteSpace tfms) then
                    // Pick the last (highest) TFM
                    let parts = tfms.Split(';', StringSplitOptions.RemoveEmptyEntries)
                    if parts.Length > 0 then Some parts.[parts.Length - 1]
                    else None
                else
                    // Single-targeted: use TargetFramework
                    match props.TryGetProperty("TargetFramework") with
                    | true, v2 ->
                        let tfm = v2.GetString()
                        if String.IsNullOrWhiteSpace tfm then None else Some tfm
                    | _ -> None
            | _ ->
                match props.TryGetProperty("TargetFramework") with
                | true, v ->
                    let tfm = v.GetString()
                    if String.IsNullOrWhiteSpace tfm then None else Some tfm
                | _ -> None
        with _ -> None

    /// Run dotnet msbuild with structured JSON output to resolve project options.
    let private resolveProjectOptions (fsprojPath: string) : Result<string list * string list * string list * string list, string> =
        // Detect TFM for multi-targeted projects
        let tfmArg =
            match detectTargetFramework fsprojPath with
            | Some tfm -> $" /p:TargetFramework={tfm}"
            | None -> ""

        let psi = ProcessStartInfo("dotnet")
        psi.Arguments <-
            $"msbuild \"{fsprojPath}\" /t:ResolveAssemblyReferences /p:DesignTimeBuild=true{tfmArg} -getItem:Compile -getItem:ReferencePath -getProperty:DefineConstants -getProperty:OtherFlags"
        psi.RedirectStandardOutput <- true
        psi.RedirectStandardError <- true
        psi.UseShellExecute <- false
        psi.WorkingDirectory <- Path.GetDirectoryName fsprojPath

        use proc = Process.Start(psi)

        // Read stderr asynchronously to avoid pipe deadlock
        let stderrBuf = StringBuilder()
        proc.ErrorDataReceived.Add(fun args ->
            if not (isNull args.Data) then stderrBuf.AppendLine(args.Data) |> ignore)
        proc.BeginErrorReadLine()

        let stdout = proc.StandardOutput.ReadToEnd()
        let exited = proc.WaitForExit(120_000) // 2 minute timeout
        if not exited then
            proc.Kill()
            Error "dotnet msbuild timed out after 120 seconds. Ensure the project restores successfully: dotnet restore"
        else

        let stderr = stderrBuf.ToString()

        if proc.ExitCode <> 0 then
            Error $"dotnet msbuild failed (exit code {proc.ExitCode}):\n{stderr}"
        else

        try
            use doc = JsonDocument.Parse(stdout)
            let root = doc.RootElement

            let props = root.GetProperty("Properties")
            let items = root.GetProperty("Items")

            let defines =
                match props.TryGetProperty("DefineConstants") with
                | true, v ->
                    v.GetString().Split([| ';' |], StringSplitOptions.RemoveEmptyEntries)
                    |> Array.toList
                | _ -> []

            let otherFlags =
                match props.TryGetProperty("OtherFlags") with
                | true, v ->
                    let s = v.GetString()
                    if String.IsNullOrWhiteSpace s then []
                    else s.Split([| ' '; '\t' |], StringSplitOptions.RemoveEmptyEntries) |> Array.toList
                | _ -> []

            let sourceFiles =
                [ for item in items.GetProperty("Compile").EnumerateArray() ->
                      item.GetProperty("FullPath").GetString() ]

            let references =
                [ for item in items.GetProperty("ReferencePath").EnumerateArray() ->
                      item.GetProperty("Identity").GetString() ]

            if references.IsEmpty then
                Error $"No assembly references resolved for: {fsprojPath}\nThis usually means the project needs restoring. Run: dotnet restore \"{fsprojPath}\""
            else
                Ok (sourceFiles, references, defines, otherFlags)
        with ex ->
            let preview = if stdout.Length > 500 then stdout.[..499] else stdout
            Error $"Failed to parse MSBuild output: {ex.Message}\nOutput: {preview}"

    /// Build FSharpProjectOptions from resolved source files, references, and defines.
    let private buildFcsOptions
        (checker: FSharpChecker)
        (fsprojPath: string)
        (sourceFiles: string list)
        (references: string list)
        (defines: string list)
        (otherFlags: string list)
        : FSharpProjectOptions =
        let args =
            [| yield "--noframework"
               yield "--targetprofile:netcore"
               yield "--simpleresolution"
               for d in defines do
                   yield $"--define:{d}"
               for flag in otherFlags do
                   yield flag
               for r in references do
                   yield $"-r:{r}"
               yield! sourceFiles |]

        checker.GetProjectOptionsFromCommandLineArgs(fsprojPath, args)

    /// Load an F# project, parse and type-check all files.
    /// Uses dotnet msbuild structured output (no Ionide.ProjInfo dependency).
    let loadProject (fsprojPath: string) : Async<Result<LoadedProject, string>> =
        async {
            try
                if not (File.Exists fsprojPath) then
                    return Error $"Project file not found: {fsprojPath}"
                else
                    let fullPath = Path.GetFullPath fsprojPath

                    match resolveProjectOptions fullPath with
                    | Error e -> return Error e
                    | Ok (sourceFiles, references, defines, otherFlags) ->

                    if sourceFiles.IsEmpty then
                        return Error $"No source files found in project: {fullPath}"
                    else

                    let checker = FSharpChecker.Create()
                    let options = buildFcsOptions checker fullPath sourceFiles references defines otherFlags

                    let! projectResults = checker.ParseAndCheckProject(options)

                    if projectResults.HasCriticalErrors then
                        let errors =
                            projectResults.Diagnostics
                            |> Array.filter (fun d -> d.Severity = FSharp.Compiler.Diagnostics.FSharpDiagnosticSeverity.Error)
                            |> Array.map (fun d -> $"  {d.FileName}({d.StartLine},{d.StartColumn}): {d.Message}")
                            |> String.concat "\n"
                        return Error $"Type-check errors:\n{errors}"
                    else
                        let! parsedFiles =
                            options.SourceFiles
                            |> Array.map (parseSourceFile checker)
                            |> Async.Sequential
                        let parsedFiles = parsedFiles |> Array.choose id |> Array.toList

                        return Ok {
                            ProjectPath = fullPath
                            ParsedFiles = parsedFiles
                            CheckResults = projectResults
                        }
            with ex ->
                return Error $"Failed to load project: {ex.Message}"
        }
```

- [ ] **Step 2: Build to verify compilation**

Run: `dotnet build src/Frank.Cli.Core/Frank.Cli.Core.fsproj`
Expected: Build succeeds (Ionide references still present but unused)

---

### Task 2: Remove Ionide.ProjInfo dependencies

**Files:**
- Modify: `src/Frank.Cli.Core/Frank.Cli.Core.fsproj`

- [ ] **Step 1: Remove the two Ionide package references**

Remove these lines from the `<ItemGroup>` containing `<PackageReference>` entries:
```xml
    <PackageReference Include="Ionide.ProjInfo" Version="0.74.2" />
    <PackageReference Include="Ionide.ProjInfo.FCS" Version="0.74.2" />
```

- [ ] **Step 2: Build to verify no remaining Ionide references**

Run: `dotnet build src/Frank.Cli.Core/Frank.Cli.Core.fsproj`
Expected: Build succeeds with no Ionide-related errors

- [ ] **Step 3: Commit**

```bash
git add src/Frank.Cli.Core/Analysis/ProjectLoader.fs src/Frank.Cli.Core/Frank.Cli.Core.fsproj
git commit -m "fix: replace Ionide.ProjInfo with dotnet msbuild structured output

Ionide.ProjInfo 0.74.2 is incompatible with .NET 10 SDK (missing
Microsoft.NET.StringTools.SpanBasedStringBuilder.Equals method).
Replace with direct dotnet msbuild invocation using structured JSON
output (-getItem/-getProperty) to resolve source files, assembly
references, and defines. Feed results to FSharpChecker via
GetProjectOptionsFromCommandLineArgs."
```

---

### Task 3: Restore the Fixtures test to use ProjectLoader

**Files:**
- Modify: `test/Frank.Cli.Core.Tests/Analysis/TypeAnalyzerTests.fs`

The Fixtures test was changed to use `checkProjectSources` (direct FCS compilation) as a workaround for Ionide failure. Now that `ProjectLoader` uses `dotnet msbuild`, restore it to use `ProjectLoader.loadProject` — this proves the fix works end-to-end and is the stronger test (exercises the full project loading pipeline, not just FCS).

- [ ] **Step 1: Restore the Fixtures test to use ProjectLoader**

Change the test to:
```fsharp
          testCaseAsync "constraint attributes extracted from fixture project"
          <| async {
              let fixturesPath =
                  Path.GetFullPath(Path.Combine(__SOURCE_DIRECTORY__, "..", "Fixtures", "Fixtures.fsproj"))

              let! result = ProjectLoader.loadProject fixturesPath

              let checkResults =
                  match result with
                  | Ok p -> p.CheckResults
                  | Error e -> failwith $"Failed to load fixtures project: {e}"

              let types = TypeAnalyzer.analyzeTypes checkResults
```

- [ ] **Step 2: Run the fixture test**

Run: `dotnet test test/Frank.Cli.Core.Tests/Frank.Cli.Core.Tests.fsproj --filter "constraint attributes"`
Expected: PASS (1 test)

- [ ] **Step 3: Run full test suite**

Run: `dotnet test test/Frank.Cli.Core.Tests/Frank.Cli.Core.Tests.fsproj`
Expected: All 256 tests pass

- [ ] **Step 4: Commit**

```bash
git add test/Frank.Cli.Core.Tests/Analysis/TypeAnalyzerTests.fs
git commit -m "test: revert Fixtures test to use ProjectLoader (now works with dotnet msbuild)"
```

---

### Task 4: Expand ProjectLoader integration tests

**Files:**
- Modify: `test/Frank.Cli.Core.Tests/Analysis/ProjectLoaderTests.fs`

- [ ] **Step 1: Expand existing test file with integration tests**

The file already exists with one test (non-existent project). Add tests that load a real project through `ProjectLoader.loadProject` and verify the returned `LoadedProject` has the expected source files and check results.

```fsharp
module Frank.Cli.Core.Tests.Analysis.ProjectLoaderTests

open System.IO
open Expecto
open Frank.Cli.Core.Analysis

[<Tests>]
let tests =
    testList "ProjectLoader" [
        testCaseAsync "non-existent project returns Error" <| async {
            let! result = ProjectLoader.loadProject "/nonexistent/path/project.fsproj"
            match result with
            | Error msg ->
                Expect.stringContains msg "not found" "Should indicate file not found"
            | Ok _ ->
                failwith "Should return Error for non-existent project"
        }

        testCaseAsync "loads real F# project and returns source files" <| async {
            let fixturesPath =
                Path.GetFullPath(Path.Combine(__SOURCE_DIRECTORY__, "..", "Fixtures", "Fixtures.fsproj"))

            let! result = ProjectLoader.loadProject fixturesPath
            match result with
            | Error e -> failwith $"Should load successfully: {e}"
            | Ok loaded ->
                Expect.isGreaterThan loaded.ParsedFiles.Length 0 "Should have parsed files"
                let fileNames = loaded.ParsedFiles |> List.map (fun (path, _) -> Path.GetFileName path)
                Expect.contains fileNames "SimpleTypes.fs" "Should contain SimpleTypes.fs"
                Expect.contains fileNames "ConstraintAttributes.fs" "Should contain ConstraintAttributes.fs"
        }

        testCaseAsync "loaded project has type-check results" <| async {
            let fixturesPath =
                Path.GetFullPath(Path.Combine(__SOURCE_DIRECTORY__, "..", "Fixtures", "Fixtures.fsproj"))

            let! result = ProjectLoader.loadProject fixturesPath
            match result with
            | Error e -> failwith $"Should load successfully: {e}"
            | Ok loaded ->
                Expect.isFalse loaded.CheckResults.HasCriticalErrors "Should have no critical errors"
                let entities =
                    loaded.CheckResults.AssemblySignature.Entities |> Seq.length
                Expect.isGreaterThan entities 0 "Should have type entities"
        }
    ]
```

- [ ] **Step 2: Run ProjectLoader tests**

Run: `dotnet test test/Frank.Cli.Core.Tests/Frank.Cli.Core.Tests.fsproj --filter "ProjectLoader"`
Expected: All 3 tests pass

- [ ] **Step 3: Commit**

```bash
git add test/Frank.Cli.Core.Tests/Analysis/ProjectLoaderTests.fs
git commit -m "test: add ProjectLoader integration tests for real project loading"
```

---

### Task 5: Verify frank-cli extract works end-to-end

**Files:** None (verification only)

- [ ] **Step 1: Run frank-cli extract on TicTacToe sample**

Run:
```bash
dotnet run --project src/Frank.Cli/Frank.Cli.fsproj -- extract \
  --project sample/Frank.TicTacToe.Sample/Frank.TicTacToe.Sample.fsproj \
  --base-uri https://example.com/alps/games --force
```
Expected: Extraction succeeds, shows resource summary

- [ ] **Step 2: Run frank-cli extract on Fixtures project**

Run:
```bash
dotnet run --project src/Frank.Cli/Frank.Cli.fsproj -- extract \
  --project test/Frank.Cli.Core.Tests/Fixtures/Fixtures.fsproj \
  --base-uri https://example.com/ --force
```
Expected: Extraction succeeds (may find no resources since Fixtures has no CEs, but should not error)

- [ ] **Step 3: Generate affordance map from TicTacToe sample**

Run:
```bash
dotnet run --project src/Frank.Cli/Frank.Cli.fsproj -- generate \
  --project sample/Frank.TicTacToe.Sample/Frank.TicTacToe.Sample.fsproj \
  --format affordance-map --base-uri https://example.com/alps/games
```
Expected: Affordance map JSON output with entries for XTurn, OTurn, Won, Draw states

---

### Task 6: Run full solution test suite

**Files:** None (verification only)

- [ ] **Step 1: Run all tests**

Run: `dotnet test Frank.sln`
Expected: All tests pass (0 failures)

- [ ] **Step 2: Final commit if any cleanup needed**

Only if tests revealed issues that needed fixes.
