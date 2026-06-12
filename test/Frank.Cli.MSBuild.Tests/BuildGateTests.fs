module Frank.Cli.MSBuild.Tests.BuildGateTests

open System
open System.IO
open System.Diagnostics
open Expecto

// ── Helpers ──────────────────────────────────────────────────────────────────

/// Path to Frank.Cli.MSBuild.dll, co-located with this test binary.
let private taskDllPath =
    Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Frank.Cli.MSBuild.dll")

/// A test-local .targets file that wires UsingTask to the test-build DLL.
/// Generated into each temp directory so paths are absolute and correct.
let private generateTargetsContent (dllPath: string) =
    let dll = dllPath.Replace("\\", "/")

    $"""<Project>

  <UsingTask TaskName="Frank.Cli.MSBuild.ValidateLockFileTask"    AssemblyFile="{dll}" />
  <UsingTask TaskName="Frank.Cli.MSBuild.GenerateValidationTask"  AssemblyFile="{dll}" />
  <UsingTask TaskName="Frank.Cli.MSBuild.GenerateLinkedDataTask"  AssemblyFile="{dll}" />
  <UsingTask TaskName="Frank.Cli.MSBuild.GenerateProvenanceTask"  AssemblyFile="{dll}" />
  <UsingTask TaskName="Frank.Cli.MSBuild.GenerateDiscoveryTask"   AssemblyFile="{dll}" />

  <PropertyGroup>
    <FrankLockFilePath Condition="'$(FrankLockFilePath)' == ''">$(MSBuildProjectDirectory)/.frank/semantic-mappings.lock.json</FrankLockFilePath>
    <FrankSemanticOutputDirectory Condition="'$(FrankSemanticOutputDirectory)' == ''">$(IntermediateOutputPath)frank-semantic/</FrankSemanticOutputDirectory>
  </PropertyGroup>

  <Target Name="FrankValidateLockFile"
          BeforeTargets="CoreCompile"
          Condition="Exists('$(FrankLockFilePath)')">

    <ValidateLockFileTask LockFilePath="$(FrankLockFilePath)" />

    <MakeDir Directories="$(FrankSemanticOutputDirectory)" />

    <GenerateValidationTask
      Condition="@(PackageReference->AnyHaveMetadataValue('Identity', 'Frank.Validation')) == 'true'"
      LockFilePath="$(FrankLockFilePath)"
      OutputDirectory="$(FrankSemanticOutputDirectory)" />

    <GenerateLinkedDataTask
      Condition="@(PackageReference->AnyHaveMetadataValue('Identity', 'Frank.LinkedData')) == 'true'"
      LockFilePath="$(FrankLockFilePath)"
      OutputDirectory="$(FrankSemanticOutputDirectory)" />

    <GenerateProvenanceTask
      Condition="@(PackageReference->AnyHaveMetadataValue('Identity', 'Frank.Provenance')) == 'true'"
      LockFilePath="$(FrankLockFilePath)"
      OutputDirectory="$(FrankSemanticOutputDirectory)" />

    <GenerateDiscoveryTask
      Condition="@(PackageReference->AnyHaveMetadataValue('Identity', 'Frank.Discovery')) == 'true'"
      LockFilePath="$(FrankLockFilePath)"
      OutputDirectory="$(FrankSemanticOutputDirectory)" />

  </Target>

</Project>"""

/// Creates a minimal F# project in a temp directory that imports a test-local targets file.
/// Returns the temp directory path.
let private createTempProject (lockFileJson: string option) =
    let dir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"))
    Directory.CreateDirectory(dir) |> ignore

    // Write the test-local targets file with the correct DLL path
    let targetsPath = Path.Combine(dir, "Frank.Cli.MSBuild.test.targets")
    File.WriteAllText(targetsPath, generateTargetsContent taskDllPath)

    // Minimal F# source file
    File.WriteAllText(Path.Combine(dir, "Library.fs"), "module Library\nlet x = 1\n")

    let fsproj =
        $"""<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net8.0</TargetFramework>
    <OutputType>Library</OutputType>
  </PropertyGroup>
  <ItemGroup>
    <Compile Include="Library.fs" />
  </ItemGroup>
  <Import Project="Frank.Cli.MSBuild.test.targets" />
</Project>"""

    File.WriteAllText(Path.Combine(dir, "TestProject.fsproj"), fsproj)

    match lockFileJson with
    | None -> ()
    | Some json ->
        let frankDir = Path.Combine(dir, ".frank")
        Directory.CreateDirectory(frankDir) |> ignore
        File.WriteAllText(Path.Combine(frankDir, "semantic-mappings.lock.json"), json)

    dir

/// Runs `dotnet build` in the given directory and returns (exitCode, combined output).
let private runDotnetBuild (dir: string) =
    let psi = ProcessStartInfo("dotnet", "build -nologo")
    psi.WorkingDirectory <- dir
    psi.RedirectStandardOutput <- true
    psi.RedirectStandardError <- true
    psi.UseShellExecute <- false
    psi.EnvironmentVariables["DOTNET_SYSTEM_GLOBALIZATION_INVARIANT"] <- "1"

    use p = Process.Start(psi)
    let stdout = p.StandardOutput.ReadToEnd()
    let stderr = p.StandardError.ReadToEnd()
    p.WaitForExit()
    p.ExitCode, stdout + stderr

// ── Sample lock file JSON ─────────────────────────────────────────────────────

let private confirmedLockFile =
    """{
  "schemaVersion": 1,
  "generated": "2026-04-20T12:00:00+00:00",
  "vocabularies": {},
  "mappings": [
    {
      "fsharpType": "MyApp.Order",
      "iri": "schema:Order",
      "confidence": 0.92,
      "source": "convention",
      "status": "confirmed",
      "fields": []
    }
  ]
}"""

let private proposedLockFile =
    """{
  "schemaVersion": 1,
  "generated": "2026-04-20T12:00:00+00:00",
  "vocabularies": {},
  "mappings": [
    {
      "fsharpType": "MyApp.Order",
      "iri": "schema:Order",
      "confidence": 0.55,
      "source": "convention",
      "status": "proposed",
      "fields": []
    }
  ]
}"""

let private unresolvedLockFile =
    """{
  "schemaVersion": 1,
  "generated": "2026-04-20T12:00:00+00:00",
  "vocabularies": {},
  "mappings": [
    {
      "fsharpType": "MyApp.Invoice",
      "iri": "",
      "confidence": 0.0,
      "source": "convention",
      "status": "unresolved",
      "fields": []
    }
  ]
}"""

let private mixedLockFile =
    """{
  "schemaVersion": 1,
  "generated": "2026-04-20T12:00:00+00:00",
  "vocabularies": {},
  "mappings": [
    {
      "fsharpType": "MyApp.Order",
      "iri": "schema:Order",
      "confidence": 0.92,
      "source": "convention",
      "status": "confirmed",
      "fields": []
    },
    {
      "fsharpType": "MyApp.Invoice",
      "iri": "",
      "confidence": 0.0,
      "source": "convention",
      "status": "proposed",
      "fields": []
    },
    {
      "fsharpType": "MyApp.Product",
      "iri": "",
      "confidence": 0.0,
      "source": "convention",
      "status": "unresolved",
      "fields": []
    }
  ]
}"""

// ── Tests ─────────────────────────────────────────────────────────────────────

[<Tests>]
let at1 =
    testList
        "AT1: Build gate rejects proposed entries"
        [ test "build fails when lock file has proposed entry" {
              let dir = createTempProject (Some proposedLockFile)

              try
                  let exitCode, output = runDotnetBuild dir
                  Expect.isGreaterThan exitCode 0 "exit code should be non-zero for proposed entry"
                  Expect.stringContains output "frank semantic" "output should contain 'frank semantic'"
                  Expect.stringContains output "MyApp.Order" "output should contain the type name"
                  Expect.stringContains output "frank semantic clarify" "output should suggest resolution command"
              finally
                  Directory.Delete(dir, true)
          } ]

[<Tests>]
let at2 =
    testList
        "AT2: Build gate rejects unresolved entries"
        [ test "build fails when lock file has unresolved entry" {
              let dir = createTempProject (Some unresolvedLockFile)

              try
                  let exitCode, output = runDotnetBuild dir
                  Expect.isGreaterThan exitCode 0 "exit code should be non-zero for unresolved entry"
                  Expect.stringContains output "frank semantic" "output should contain 'frank semantic'"
                  Expect.stringContains output "MyApp.Invoice" "output should contain the type name"
              finally
                  Directory.Delete(dir, true)
          } ]

[<Tests>]
let at3 =
    testList
        "AT3: Build succeeds when all entries confirmed"
        [ test "build succeeds when all mappings are confirmed" {
              let dir = createTempProject (Some confirmedLockFile)

              try
                  let exitCode, output = runDotnetBuild dir
                  Expect.equal exitCode 0 $"exit code should be 0. Output:\n{output}"
              finally
                  Directory.Delete(dir, true)
          } ]

[<Tests>]
let at4 =
    testList
        "AT4: Build succeeds when no lock file exists"
        [ test "build succeeds when .frank directory is absent" {
              let dir = createTempProject None

              try
                  let exitCode, output = runDotnetBuild dir
                  Expect.equal exitCode 0 $"exit code should be 0. Output:\n{output}"
              finally
                  Directory.Delete(dir, true)
          } ]

[<Tests>]
let at5 =
    testList
        "AT5: Error message guides developer"
        [ test "error output contains type names and resolution hint" {
              let dir = createTempProject (Some mixedLockFile)

              try
                  let exitCode, output = runDotnetBuild dir
                  Expect.isGreaterThan exitCode 0 "build should fail"
                  Expect.stringContains output "frank semantic" "mentions 'frank semantic'"
                  Expect.stringContains output "MyApp.Invoice" "lists first blocking type"
                  Expect.stringContains output "MyApp.Product" "lists second blocking type"
                  Expect.stringContains output "frank semantic clarify" "suggests clarify command"
              finally
                  Directory.Delete(dir, true)
          } ]
