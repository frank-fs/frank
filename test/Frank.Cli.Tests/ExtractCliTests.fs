module Frank.Cli.Tests.ExtractCliTests

open System.IO
open System.Reflection
open Expecto
open Frank.TestSupport.TempDir

// ── Helpers ───────────────────────────────────────────────────────────────────

/// Frank.Cli.dll co-located with this test DLL (via <ProjectReference> to Frank.Cli).
let private frankCliDll: string =
    let testDir = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location)
    Path.Combine(testDir, "Frank.Cli.dll")

/// Writes a fixture where `type Order` has a declared `equivalentClass` but no
/// in-scope vocabulary terms (no 'using' — no network fetch needed), so it collapses
/// with no independent match: `frank semantic extract` must print a notice line.
let private writeFixtureProjectWithEquivalentClass (dir: string) : string =
    let domainSource =
        """namespace FixtureApp

type Order = { Id: int; Total: decimal }
"""

    let vocabSource =
        """module Vocabulary
open Frank.Semantic
open FixtureApp

// No 'using' declared — no network fetch needed. Order has no in-scope convention
// candidate of its own, so applyExplicitClass collapses it silently.
let registry =
    vocabulary {
        prefix "schema" "https://schema.org/"
        equivalentClass typeof<Order> "schema:Bar"
    }
"""

    File.WriteAllText(Path.Combine(dir, "Domain.fs"), domainSource)
    File.WriteAllText(Path.Combine(dir, "Vocabulary.fs"), vocabSource)

    let fsprojContent =
        """<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net10.0</TargetFramework>
    <OutputType>Library</OutputType>
  </PropertyGroup>
  <ItemGroup>
    <Compile Include="Domain.fs" />
    <Compile Include="Vocabulary.fs" />
  </ItemGroup>
</Project>
"""

    let projectFile = Path.Combine(dir, "FixtureApp.fsproj")
    File.WriteAllText(projectFile, fsprojContent)
    projectFile

// ── Tests ─────────────────────────────────────────────────────────────────────

[<Tests>]
let extractCliNoticeTests =
    testList
        "frank semantic extract CLI: EquivalentClassNotice printed for real collapse case"
        [ test "extract --format text prints a notice line for a type collapsed onto an explicit equivalentClass" {
              withTempDir (fun dir ->
                  let projectFile = writeFixtureProjectWithEquivalentClass dir
                  let capMs = 30_000

                  let exitCode, stdout, stderr =
                      Frank.TestSupport.RunCli.run
                          frankCliDll
                          [| "semantic"; "extract"; "--project"; projectFile; "--format"; "text" |]
                          capMs

                  Expect.equal exitCode 0 $"extract must succeed; stderr:\n{stderr}"

                  Expect.stringContains
                      stdout
                      "notice: FixtureApp.Order has no independent convention match; ClassIri collapsed to explicit equivalentClass target schema:Bar"
                      $"expected notice line on stdout; got:\n{stdout}")
          }

          test "extract --format json prints a notice object for a type collapsed onto an explicit equivalentClass" {
              withTempDir (fun dir ->
                  let projectFile = writeFixtureProjectWithEquivalentClass dir
                  let capMs = 30_000

                  let exitCode, stdout, stderr =
                      Frank.TestSupport.RunCli.run
                          frankCliDll
                          [| "semantic"; "extract"; "--project"; projectFile; "--format"; "json" |]
                          capMs

                  Expect.equal exitCode 0 $"extract must succeed; stderr:\n{stderr}"

                  Expect.stringContains
                      stdout
                      """{"notice":"equivalentClassCollapse","fsharpType":"FixtureApp.Order","explicitIri":"schema:Bar"}"""
                      $"expected notice JSON line on stdout; got:\n{stdout}")
          } ]
