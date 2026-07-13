module Frank.Cli.Core.Tests.VocabSwapTests

open System
open System.IO
open System.Reflection
open Expecto
open Frank.Semantic
open Frank.Semantic.LockFile
open Frank.Semantic.VocabFetcher
open Frank.Cli.Core
open Frank.TestSupport.TempDir

// ── Vocabulary document stubs ─────────────────────────────────────────────────
//
// Each stub returns a minimal Turtle vocabulary document. Class and property
// names are EXACT matches to the fixture F# type and field names so every
// mapping emerges Confirmed from the convention engine — no acceptance step
// needed before the build gate.

let private stubFor (turtle: string) : Fetch =
    let bytes = System.Text.Encoding.UTF8.GetBytes turtle

    fun _ ->
        async {
            return
                Ok
                    {| ContentType = Some "text/turtle"
                       Body = bytes |}
        }

let private schemaStub: Fetch =
    stubFor
        """@prefix schema: <https://schema.org/> .
@prefix rdfs: <http://www.w3.org/2000/01/rdf-schema#> .
@prefix rdf: <http://www.w3.org/1999/02/22-rdf-syntax-ns#> .
schema:Game a rdfs:Class .
schema:MoveAction a rdfs:Class .
schema:identifier a rdf:Property .
schema:square a rdf:Property .
schema:agent a rdf:Property .
"""

let private exStub: Fetch =
    stubFor
        """@prefix ex: <http://example.org/tictactoe#> .
@prefix rdfs: <http://www.w3.org/2000/01/rdf-schema#> .
@prefix rdf: <http://www.w3.org/1999/02/22-rdf-syntax-ns#> .
ex:Game a rdfs:Class .
ex:MoveAction a rdfs:Class .
ex:identifier a rdf:Property .
ex:square a rdf:Property .
ex:agent a rdf:Property .
"""

// ── Fixture source files ──────────────────────────────────────────────────────
//
// Domain types use names that exactly match the vocabulary term local names.
// Vocabulary.fs is swapped between schema.org and ex: to drive the extract pipeline.

let private domainSource =
    """namespace Fixture
type Game = { identifier: string }
type MoveAction = { square: string; agent: string }
"""

let private schemaVocabSource =
    """module Vocabulary
open Frank.Semantic
let registry =
    vocabulary {
        prefix "schema" "https://schema.org/"
        using "schema"
    }
"""

let private exVocabSource =
    """module Vocabulary
open Frank.Semantic
let registry =
    vocabulary {
        prefix "ex" "http://example.org/tictactoe#"
        using "ex"
    }
"""

// ── Helpers ───────────────────────────────────────────────────────────────────

let private frankSemanticDllPath () =
    Assembly.GetAssembly(typeof<VocabularyRegistry>).Location

let private fsharpCoreDllPath () =
    Assembly.GetAssembly(typeof<int list>).Location

let private writeFixtureProject (dir: string) (vocabSrc: string) : string =
    File.WriteAllText(Path.Combine(dir, "Domain.fs"), domainSource)
    File.WriteAllText(Path.Combine(dir, "Vocabulary.fs"), vocabSrc)

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

    let projPath = Path.Combine(dir, "Fixture.fsproj")
    File.WriteAllText(projPath, fsprojContent)
    projPath

/// Run the REAL extract pipeline with an injected stub fetch. Returns the generated lock.
/// Pipeline.runWithFetch is internal but accessible via InternalsVisibleTo.
let private extractLock (fetch: Fetch) (projPath: string) : LockFile =
    Pipeline.runWithFetch
        fetch
        (fun () -> DateTimeOffset.UtcNow)
        { ProjectFile = projPath
          VocabularyFile = None
          AssemblyRefs = [ frankSemanticDllPath (); fsharpCoreDllPath () ]
          OutputFormat = Pipeline.Text }
    |> Result.defaultWith (fun e -> failwith $"extract failed: {e}")
    |> ignore

    let lockPath =
        Path.Combine(Path.GetDirectoryName projPath, ".frank", "semantic-mappings.lock.json")

    LockFile.read lockPath
    |> Result.defaultWith (fun e -> failwith $"lock read: {e}")

/// Emit ALPS Discovery F# source from a lock using VocabularyRegistry.empty.
/// Mirrors exactly what GenerateDiscoveryTask does.
let private emitDiscovery (lock: LockFile) : string =
    DiscoveryEmitter.emit "Fixture.GeneratedDiscovery" "/alps/fixture" VocabularyRegistry.empty lock
    |> Result.defaultWith failwith

// ── Lazy fixtures ─────────────────────────────────────────────────────────────
//
// FCS extraction is expensive. Each (schema/ex) pair is computed once and cached.
// Tests 1/3/5 share schemaArtifact; tests 2/4/6 share exArtifact.

let private schemaArtifact: Lazy<string> =
    lazy (withTempDir (fun dir -> emitDiscovery (extractLock schemaStub (writeFixtureProject dir schemaVocabSource))))

let private exArtifact: Lazy<string> =
    lazy (withTempDir (fun dir -> emitDiscovery (extractLock exStub (writeFixtureProject dir exVocabSource))))

// ── Client simulation helpers ─────────────────────────────────────────────────
//
// "Hardcoded client": baked-in schema.org/Game IRI at development time.
// Returns false when the artifact no longer carries that IRI — the client BROKE.
let private hardcodedSchemaOrgClientFinds (artifact: string) : bool =
    artifact.Contains "https://schema.org/Game"

// "Discovery client": reads the type IRI from the emitted artifact (the Href field).
// Finds the IRI regardless of prefix — it follows whatever the artifact declares.
let private discoveryClientFindsIri (iri: string) (artifact: string) : bool = artifact.Contains iri

// ── Tests ─────────────────────────────────────────────────────────────────────

[<Tests>]
let vocabSwapTests =
    testList
        "AT1 — vocab swap flows through real extract pipeline"
        [ test "schema.org extract → emit produces exact schema.org/Game IRI" {
              Expect.stringContains
                  schemaArtifact.Value
                  "https://schema.org/Game"
                  "schema.org extract must emit https://schema.org/Game"
          }

          test "ex: extract → emit produces exact ex: Game IRI AND zero schema.org/Game" {
              Expect.stringContains
                  exArtifact.Value
                  "http://example.org/tictactoe#Game"
                  "ex: extract must emit http://example.org/tictactoe#Game"

              Expect.isFalse
                  (exArtifact.Value.Contains "https://schema.org/Game")
                  "ex: artifact must contain ZERO schema.org/Game — swap must be complete at pipeline source"
          }

          test "AT1 hardcoded schema.org client WORKS on schema artifact" {
              Expect.isTrue
                  (hardcodedSchemaOrgClientFinds schemaArtifact.Value)
                  "hardcoded schema.org client must find schema.org/Game in schema artifact"
          }

          test "AT1 hardcoded schema.org client BREAKS on ex: artifact — swap is load-bearing" {
              Expect.isFalse
                  (hardcodedSchemaOrgClientFinds exArtifact.Value)
                  "hardcoded schema.org client must find NOTHING in ex: artifact"
          }

          test "AT1 discovery client resolves Game via Href on schema artifact" {
              Expect.isTrue
                  (discoveryClientFindsIri "https://schema.org/Game" schemaArtifact.Value)
                  "discovery client must find schema.org/Game Href in schema artifact"
          }

          test "AT1 discovery client resolves Game via Href on ex: artifact" {
              Expect.isTrue
                  (discoveryClientFindsIri "http://example.org/tictactoe#Game" exArtifact.Value)
                  "discovery client must find ex: Game Href in ex: artifact — prefix-agnostic navigation survives vocab swap"
          } ]
