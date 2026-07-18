module Frank.Cli.MSBuild.Tests.BuildGateIntegrationTests

open System
open System.IO
open Expecto
open Frank.Cli.MSBuild
open Frank.Cli.MSBuild.Tests.Fixtures
open Frank.Cli.MSBuild.Tests.StubBuildEngine
open Frank.Cli.MSBuild.Tests.SubprocessBuild
open Frank.TestSupport.TempDir

// ── Path resolution ───────────────────────────────────────────────────────────

let private targetsFilePath: string =
    Path.Combine(worktreeRoot, "src", "Frank.Cli.MSBuild", "build", "Frank.Cli.MSBuild.targets")

/// The task DLL co-located with the test output — same binary the test process already loaded.
let private taskDllPath: string = typeof<ValidateLockFileTask>.Assembly.Location

/// Frank.Discovery.fsproj in this worktree.
let private frankDiscoveryFsproj: string =
    Path.Combine(worktreeRoot, "src", "Frank.Discovery", "Frank.Discovery.fsproj")

let private frankSemanticFsproj: string =
    Path.Combine(worktreeRoot, "src", "Frank.Semantic", "Frank.Semantic.fsproj")

let private runDotnetBuild (projPath: string) (capMs: int) : int * string =
    runProcess "dotnet" $"build \"{projPath}\"" capMs

/// Same as runDotnetBuild but at normal verbosity so MessageImportance.High messages appear.
let private runDotnetBuildNormal (projPath: string) (capMs: int) : int * string =
    runProcess "dotnet" $"build \"{projPath}\" -v:n" capMs

let private runDotnetMsBuildTarget (projPath: string) (target: string) (capMs: int) : int * string =
    runProcess "dotnet" $"msbuild \"{projPath}\" /t:{target} /p:Configuration=Debug" capMs

/// Single writer for all fixture projects; callers supply only what varies.
/// extraProperties: raw XML to append inside <PropertyGroup> (leading newline + indent)
/// extraItemGroups: raw XML to append after the Compile item group (leading newline + indent)
/// targetOverrides: raw XML for <Target> overrides (indented lines, trailing newline)
let private writeProjectWith
    (dir: string)
    (stubModule: string)
    (projName: string)
    (lockPath: string)
    (extraProperties: string)
    (extraItemGroups: string)
    (targetOverrides: string)
    : string =
    let stubPath = Path.Combine(dir, "Stub.fs")
    File.WriteAllText(stubPath, $"module {stubModule}\n")
    let projPath = Path.Combine(dir, $"{projName}.fsproj")

    let content =
        $"""<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net10.0</TargetFramework>
    <FrankLockFilePath>{lockPath}</FrankLockFilePath>
    <FrankMSBuildAssemblyFile>{taskDllPath}</FrankMSBuildAssemblyFile>{extraProperties}
  </PropertyGroup>
  <ItemGroup>
    <Compile Include="Stub.fs" />
  </ItemGroup>{extraItemGroups}
  <Import Project="{targetsFilePath}" />
{targetOverrides}</Project>
"""

    File.WriteAllText(projPath, content)
    projPath

/// Minimal fixture project: ValidateLockFileTask gate under test.
/// Overrides FrankGenerateFcsEmitters — fixture has no Vocabulary.fs or FCS package refs.
let private writeFixtureProject (dir: string) (lockPath: string) : string =
    writeProjectWith dir "Stub" "BuildFixture" lockPath "" "" "  <Target Name=\"FrankGenerateFcsEmitters\" />\n"

/// Discovery fixture project: uses a genuine ProjectReference to trigger _FrankHasDiscovery.
/// Canonicalized temp dir ensures MSBuild can resolve the ProjectReference path correctly.
let private writeDiscoveryFixtureProject (dir: string) (lockPath: string) : string =
    writeProjectWith
        dir
        "FixtureApp.Stub"
        "DiscoveryFixture"
        lockPath
        $"\n    <FrankDiscoveryModuleName>FixtureApp.GeneratedDiscovery</FrankDiscoveryModuleName>"
        $"\n  <ItemGroup>\n    <ProjectReference Include=\"{frankDiscoveryFsproj}\" />\n  </ItemGroup>"
        """  <Target Name="FrankGenerateDiscoveryModel" />
  <Target Name="FrankInjectGeneratedFile" />
"""

/// Single-package fixture: has Frank.Semantic ProjectReference + minimal vocabulary source.
/// Used to test incremental skip: only GeneratedSemantics.fs is written, so correct
/// Outputs= must list only that file (not all four) for the second build to skip.
let private writeSinglePackageFixtureProject (dir: string) (lockPath: string) : string =
    let vocabPath = Path.Combine(dir, "Vocabulary.fs")

    File.WriteAllText(
        vocabPath,
        """namespace SinglePkg

open Frank.Semantic

type Widget = { id: int }

module Vocab =
    let registry =
        vocabulary {
            prefix "ex" "https://example.org/"
            using "ex"
            seeAlso typeof<Widget> "ex:Widget"
        }
"""
    )

    // AppEntry.fs must be the last domain file so FrankInjectGeneratedSemanticModelFile
    // anchors here — placing GeneratedSemantics.fs AFTER Vocabulary.fs (which defines Widget).
    // Without an anchor after Vocabulary.fs the inject puts GeneratedSemantics.fs before
    // Vocabulary.fs → FS0039 (typeof<SinglePkg.Widget> undefined at that compile position).
    let appEntryPath = Path.Combine(dir, "AppEntry.fs")
    File.WriteAllText(appEntryPath, "module SinglePkg.AppEntry\n")

    writeProjectWith
        dir
        "SinglePkg.Stub"
        "SinglePkgFixture"
        lockPath
        ""
        $"\n  <ItemGroup>\n    <Compile Include=\"Vocabulary.fs\" />\n    <Compile Include=\"AppEntry.fs\" />\n  </ItemGroup>\n  <ItemGroup>\n    <ProjectReference Include=\"{frankSemanticFsproj}\" />\n  </ItemGroup>"
        ""

// ── Tests ─────────────────────────────────────────────────────────────────────

/// testSequencedGroup: all subprocess-`dotnet build`-spawning tests across this project
/// share the "msbuild-subprocess" group so Expecto never races them for MSBuild
/// node-reuse locks — independent of what else is on the machine (#402).
[<Tests>]
let buildGateIntegrationTests =
    testSequencedGroup "msbuild-subprocess"
    <| testList
        "D2 — Build gate integration (subprocess dotnet build)"
        [ test "AT2/AT3: dotnet build with proposed+unresolved lock exits non-zero and emits MS001" {
              withTempDir (fun dir ->
                  let lockPath = writeLockFile dir proposedLock
                  let projPath = writeFixtureProject dir lockPath

                  let capMs = 180_000
                  let exitCode, combined = runDotnetBuild projPath capMs

                  Expect.isTrue (exitCode <> 0) $"Expected non-zero exit code but got {exitCode}"

                  Expect.stringContains combined "MS001" "MS001 error code must appear in build output")
          }

          test "AT3: dotnet build with unresolved-only lock (no proposed) exits non-zero and emits MS001" {
              withTempDir (fun dir ->
                  let lockPath = writeLockFile dir unresolvedOnlyLock
                  let projPath = writeFixtureProject dir lockPath

                  let capMs = 180_000
                  let exitCode, combined = runDotnetBuild projPath capMs

                  Expect.isTrue (exitCode <> 0) $"Unresolved-only lock must fail build; got exit {exitCode}"

                  Expect.stringContains
                      combined
                      "MS001"
                      "MS001 must appear even when only Unresolved (no Proposed) entries present")
          }

          test
              "AT1: dotnet msbuild FrankGenerateDiscovery with confirmed ex: lock produces GeneratedDiscovery.fs with ex: IRI" {
              withTempDir (fun dir ->
                  let lockPath = writeLockFile dir confirmedExLock
                  let projPath = writeDiscoveryFixtureProject dir lockPath

                  // Run only the generation target — no compilation needed to assert file content.
                  let capMs = 60_000

                  let exitCode, combined =
                      runDotnetMsBuildTarget projPath "FrankGenerateDiscovery" capMs

                  Expect.equal
                      exitCode
                      0
                      $"FrankGenerateDiscovery must succeed with all-confirmed ex: lock; output:\n{combined}"

                  // IntermediateOutputPath uses OS-specific separators (obj\Debug/ on macOS via SDK).
                  // Search recursively to find the file regardless of separator convention.
                  let genFiles =
                      Directory.GetFiles(dir, "GeneratedDiscovery.fs", SearchOption.AllDirectories)

                  Expect.isTrue
                      (genFiles.Length > 0)
                      $"GeneratedDiscovery.fs must exist under {dir} (not found — build output:\n{combined})"

                  let src = File.ReadAllText genFiles.[0]

                  Expect.stringContains
                      src
                      "http://example.org/tictactoe#Game"
                      "generated file must contain exact ex: Game IRI"

                  Expect.isFalse
                      (src.Contains "https://schema.org/Game")
                      "generated file must NOT contain schema.org/Game — swap must be complete")
          }

          test "AT4: dotnet build with confirmed-only lock exits 0 (gate passes after drift detected)" {
              withTempDir (fun dir ->
                  let lockPath = writeLockFile dir confirmedLock
                  let projPath = writeFixtureProject dir lockPath

                  let capMs = 180_000
                  let exitCode, combined = runDotnetBuild projPath capMs

                  Expect.equal exitCode 0 $"Confirmed-only lock must pass build gate; output:\n{combined}")
          }

          test
              "AC1c: second dotnet build of single-package consumer skips FrankGenerateFcsEmitters (incremental, A1 #386)" {
              withTempDir (fun dir ->
                  let lockPath = writeLockFile dir singlePkgLock
                  let projPath = writeSinglePackageFixtureProject dir lockPath

                  let capMs = 600_000

                  // First build: FrankGenerateFcsEmitters must run.
                  let exitCode1, combined1 = runDotnetBuildNormal projPath capMs

                  Expect.equal exitCode1 0 $"First build must succeed; output:\n{combined1}"

                  Expect.stringContains
                      combined1
                      "FRANK_FCS_PASS_COUNT=1"
                      "First build must run FrankGenerateFcsEmitters (FRANK_FCS_PASS_COUNT=1 expected)"

                  // Second build without clean: GeneratedSemantics.fs already exists and is up-to-date.
                  // FrankGenerateFcsEmitters must be SKIPPED (incremental). A1 fix: Outputs= must
                  // match what was actually written — only GeneratedSemantics.fs — so MSBuild skips.
                  let exitCode2, combined2 = runDotnetBuildNormal projPath capMs

                  Expect.equal exitCode2 0 $"Second build must succeed; output:\n{combined2}"

                  Expect.isFalse
                      (combined2.Contains "FRANK_FCS_PASS_COUNT=")
                      $"Second build must skip FrankGenerateFcsEmitters (incremental); output:\n{combined2}")
          }

          test "AC1b: TicTacToe-v732 build emits FRANK_FCS_PASS_COUNT=1 exactly once (#386)" {
              let tttFsproj =
                  Path.Combine(worktreeRoot, "sample", "TicTacToe-v732", "TicTacToe.v732.fsproj")

              Expect.isTrue (File.Exists tttFsproj) $"TicTacToe-v732 fsproj must exist at {tttFsproj}"

              let capMs = 600_000

              // Clean before build so FrankGenerateFcsEmitters always runs (not skipped incrementally).
              runProcess "dotnet" $"clean \"{tttFsproj}\"" capMs |> ignore
              // Build at normal verbosity so MessageImportance.High messages appear.
              let exitCode, combined = runDotnetBuildNormal tttFsproj capMs

              Expect.equal exitCode 0 $"TicTacToe-v732 must build successfully; output:\n{combined}"

              Expect.stringContains
                  combined
                  "FRANK_FCS_PASS_COUNT=1"
                  "FRANK_FCS_PASS_COUNT=1 must appear in build output (FrankGenerateFcsEmitters ran and counted one FCS pass)"

              let passLines =
                  combined.Split('\n')
                  |> Array.filter (fun l -> l.Contains("FRANK_FCS_PASS_COUNT="))

              let passLinesStr = passLines |> String.concat "\n"

              Expect.equal
                  passLines.Length
                  1
                  $"FRANK_FCS_PASS_COUNT must appear exactly once (one consolidated FCS task); got {passLines.Length} occurrences: {passLinesStr}"
          } ]

/// Item-2: library-style fixture (no Program.fs) — inject ordering with non-domain trailing file.
///
/// Fixture: @(Compile) = [Model.fs, Vocabulary.fs, Extra.fs, GeneratedStub.fs]
///   GeneratedStub.fs matches the Generated* exclusion → NOT in the domain set.
///   Domain set = [Model.fs, Vocabulary.fs, Extra.fs]; domain-last = Extra.fs.
///
/// Expected order after FrankInjectGeneratedFile with domain-anchored logic:
///   [Model.fs, Vocabulary.fs, GeneratedDiscovery.fs, Extra.fs, GeneratedStub.fs]
///   genIdx(2) < extraIdx(3) ← PASSES only with domain-last anchor.
///
/// With naive positional-last anchor (GeneratedStub.fs is last @(Compile) item):
///   [Model.fs, Vocabulary.fs, Extra.fs, GeneratedDiscovery.fs, GeneratedStub.fs]
///   genIdx(3) > extraIdx(2) ← assertion FAILS → confirms the fix is load-bearing.
[<Tests>]
let item2LibraryInjectTests =
    testSequencedGroup "msbuild-subprocess"
    <| testList
        "Item-2: domain-anchored inject ordering for library projects (no Program.fs)"
        [ test "FrankInjectGeneratedFile places Generated before last domain file, not trailing Generated* file" {
              withTempDir (fun dir ->
                  let lockPath = writeLockFile dir confirmedLock

                  File.WriteAllText(Path.Combine(dir, "Model.fs"), "module LibFix.Model\ntype Widget = { Id: int }\n")

                  File.WriteAllText(Path.Combine(dir, "Vocabulary.fs"), "module LibFix.Vocabulary\nlet x = 1\n")

                  File.WriteAllText(Path.Combine(dir, "Extra.fs"), "module LibFix.Extra\nlet y = 2\n")

                  // GeneratedStub.fs matches Generated* exclusion → outside domain set.
                  // Placed AFTER Extra.fs so positional-last ≠ domain-last.
                  File.WriteAllText(
                      Path.Combine(dir, "GeneratedStub.fs"),
                      "module LibFix.GeneratedStub\nlet stub = ()\n"
                  )

                  let intermediateOutputPath = Path.Combine(dir, "obj", "Debug", "net10.0")
                  Directory.CreateDirectory(intermediateOutputPath) |> ignore

                  File.WriteAllText(
                      Path.Combine(intermediateOutputPath, "GeneratedDiscovery.fs"),
                      "module LibFix.GeneratedDiscovery\n"
                  )

                  let projPath = Path.Combine(dir, "LibFix.fsproj")

                  let projContent =
                      $"""<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net10.0</TargetFramework>
    <FrankLockFilePath>{lockPath}</FrankLockFilePath>
    <FrankMSBuildAssemblyFile>{taskDllPath}</FrankMSBuildAssemblyFile>
  </PropertyGroup>
  <ItemGroup>
    <Compile Include="Model.fs" />
    <Compile Include="Vocabulary.fs" />
    <Compile Include="Extra.fs" />
    <Compile Include="GeneratedStub.fs" />
  </ItemGroup>
  <Import Project="{targetsFilePath}" />
  <Target Name="FrankGenerateFcsEmitters" />
  <Target Name="DumpCompileOrder" AfterTargets="FrankInjectGeneratedFile">
    <WriteLinesToFile File="$(MSBuildProjectDirectory)/compile-order.txt"
                      Lines="@(Compile->'%%(Filename)%%(Extension)')"
                      Overwrite="true" />
  </Target>
</Project>
"""

                  File.WriteAllText(projPath, projContent)

                  let capMs = 60_000

                  let exitCode, combined =
                      runDotnetMsBuildTarget projPath "FrankInjectGeneratedFile,DumpCompileOrder" capMs

                  Expect.equal exitCode 0 $"FrankInjectGeneratedFile must succeed; output:\n{combined}"

                  let orderFile = Path.Combine(dir, "compile-order.txt")
                  Expect.isTrue (File.Exists orderFile) "DumpCompileOrder must have written compile-order.txt"

                  let compileOrder = File.ReadAllLines(orderFile) |> Array.toList

                  let idxOf name =
                      compileOrder
                      |> List.tryFindIndex (fun f -> f.Equals(name, StringComparison.OrdinalIgnoreCase))

                  let modelIdx = idxOf "Model.fs" |> Option.defaultValue -1
                  let vocabIdx = idxOf "Vocabulary.fs" |> Option.defaultValue -1
                  let genIdx = idxOf "GeneratedDiscovery.fs" |> Option.defaultValue -1
                  let extraIdx = idxOf "Extra.fs" |> Option.defaultValue -1

                  Expect.isGreaterThan genIdx -1 $"GeneratedDiscovery.fs must be in @(Compile); order: {compileOrder}"

                  Expect.isGreaterThan genIdx modelIdx "Generated must come after Model.fs"
                  Expect.isGreaterThan genIdx vocabIdx "Generated must come after Vocabulary.fs"

                  // Key assertion: Generated anchors before Extra.fs (last DOMAIN file),
                  // not before GeneratedStub.fs (positional-last, excluded from domain set).
                  // With naive positional-last anchor, genIdx > extraIdx → this assertion fails.
                  Expect.isLessThan
                      genIdx
                      extraIdx
                      $"Generated must anchor before Extra.fs (domain-last), not after it; order: {compileOrder}")
          } ]
