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

/// Writes a fixture with a vocab cache pre-seeded (no network fetch needed) whose term
/// graph has two rdf:Properties sharing the local name "identifier" across different
/// namespaces: buildTermMap drops it as ambiguous, and that drop must be surfaced via an
/// explicit ConventionDiagnostic (AmbiguousLocalNameDropped), not just implicitly via
/// every affected type degrading to Unresolved.
let private writeFixtureProjectWithAmbiguousLocalName (dir: string) : string =
    let domainSource =
        """namespace FixtureApp

type Order = { Id: int; Total: decimal }
"""

    let vocabSource =
        """module Vocabulary
open Frank.Semantic

let registry =
    vocabulary {
        prefix "vocab" "https://example.org/vocab#"
        using "vocab"
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

    // Pre-seed the vocab cache: fetchAndCacheConneg checks the on-disk cache first and
    // never fetches over the network when a matching "vocab.*" file is already present.
    let cacheDir = Path.Combine(dir, ".frank", "vocab")
    Directory.CreateDirectory cacheDir |> ignore

    let ambiguousTurtle =
        """@prefix rdf: <http://www.w3.org/1999/02/22-rdf-syntax-ns#> .
@prefix schema: <https://schema.org/> .
@prefix dct: <http://purl.org/dc/terms/> .

schema:identifier a rdf:Property .
dct:identifier a rdf:Property .
"""

    File.WriteAllText(Path.Combine(cacheDir, "vocab.deadbeef.ttl"), ambiguousTurtle)

    projectFile

// ── Tests ─────────────────────────────────────────────────────────────────────

[<Tests>]
let extractCliNoticeTests =
    testList
        "frank semantic extract CLI: ConventionDiagnostic printed for real collapse case"
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

[<Tests>]
let extractCliAmbiguousLocalNameTests =
    testList
        "frank semantic extract CLI: AmbiguousLocalNameDropped printed for an ambiguous local name (#427 AC2)"
        [ test "extract --format text prints a notice line for a dropped ambiguous local name" {
              withTempDir (fun dir ->
                  let projectFile = writeFixtureProjectWithAmbiguousLocalName dir
                  let capMs = 30_000

                  let exitCode, stdout, stderr =
                      Frank.TestSupport.RunCli.run
                          frankCliDll
                          [| "semantic"; "extract"; "--project"; projectFile; "--format"; "text" |]
                          capMs

                  Expect.equal exitCode 0 $"extract must succeed; stderr:\n{stderr}"

                  Expect.stringContains
                      stdout
                      "notice: ambiguous property local name 'identifier' dropped (http://purl.org/dc/terms/identifier, https://schema.org/identifier); affected types degrade to Unresolved"
                      $"expected ambiguous-local-name notice line on stdout; got:\n{stdout}")
          }

          test "extract --format json prints a notice object for a dropped ambiguous local name" {
              withTempDir (fun dir ->
                  let projectFile = writeFixtureProjectWithAmbiguousLocalName dir
                  let capMs = 30_000

                  let exitCode, stdout, stderr =
                      Frank.TestSupport.RunCli.run
                          frankCliDll
                          [| "semantic"; "extract"; "--project"; projectFile; "--format"; "json" |]
                          capMs

                  Expect.equal exitCode 0 $"extract must succeed; stderr:\n{stderr}"

                  Expect.stringContains
                      stdout
                      """{"notice":"ambiguousLocalNameDropped","category":"property","localName":"identifier","iris":["http://purl.org/dc/terms/identifier","https://schema.org/identifier"]}"""
                      $"expected ambiguous-local-name notice JSON line on stdout; got:\n{stdout}")
          } ]
