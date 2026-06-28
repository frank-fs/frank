module Frank.Cli.Core.Tests.PipelineTests

open System
open System.IO
open System.Reflection
open Expecto
open Frank.Semantic
open Frank.Semantic.LockFile
open Frank.Semantic.VocabFetcher
open Frank.Cli.Core

// ── Helpers ───────────────────────────────────────────────────────────────────

let private frankSemanticDllPath () =
    let asm = Assembly.GetAssembly(typeof<VocabularyRegistry>)
    asm.Location

let private fsharpCoreDllPath () =
    let asm = Assembly.GetAssembly(typeof<int list>)
    asm.Location

/// Writes a minimal fixture project: two domain types + a vocabulary file + .fsproj.
/// Returns (projectFile, lockFilePath).
let private writeFixtureProject (tmpDir: string) : string * string =
    let domainSource =
        """namespace FixtureApp

type Order = { Id: int; Total: decimal }
type Customer = { Name: string; Email: string }
"""

    let vocabSource =
        """module Vocabulary
open Frank.Semantic

// No 'using' declared — no network fetch needed in tests.
// Types will score as Unresolved (no in-scope vocabulary terms).
let registry =
    vocabulary {
        prefix "schema" "https://schema.org/"
    }
"""

    File.WriteAllText(Path.Combine(tmpDir, "Domain.fs"), domainSource)
    File.WriteAllText(Path.Combine(tmpDir, "Vocabulary.fs"), vocabSource)

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

    let projectFile = Path.Combine(tmpDir, "FixtureApp.fsproj")
    File.WriteAllText(projectFile, fsprojContent)
    let lockFilePath = Path.Combine(tmpDir, ".frank", "semantic-mappings.lock.json")
    projectFile, lockFilePath

let private dllRefs () =
    [ frankSemanticDllPath (); fsharpCoreDllPath () ]

// ── AT1: pipeline end-to-end ──────────────────────────────────────────────────

[<Tests>]
let at1PipelineTests =
    testList
        "AT1 - extract pipeline end-to-end"
        [ test "extract writes lock file to .frank/semantic-mappings.lock.json" {
              let tmpDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"))
              Directory.CreateDirectory(tmpDir) |> ignore

              try
                  let projectFile, lockFilePath = writeFixtureProject tmpDir

                  let result =
                      Pipeline.run
                          { ProjectFile = projectFile
                            VocabularyFile = None
                            AssemblyRefs = dllRefs ()
                            OutputFormat = Pipeline.Text }

                  Expect.isOk result "pipeline should succeed"
                  Expect.isTrue (File.Exists lockFilePath) "lock file must be written"
              finally
                  Directory.Delete(tmpDir, true)
          }

          test "extract lock file has schemaVersion 1" {
              let tmpDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"))
              Directory.CreateDirectory(tmpDir) |> ignore

              try
                  let projectFile, lockFilePath = writeFixtureProject tmpDir

                  Pipeline.run
                      { ProjectFile = projectFile
                        VocabularyFile = None
                        AssemblyRefs = dllRefs ()
                        OutputFormat = Pipeline.Text }
                  |> ignore

                  let lockResult = LockFile.read lockFilePath
                  let lf = Expect.wantOk lockResult "lock file must parse"
                  Expect.equal lf.SchemaVersion 1 "schemaVersion must be 1"
              finally
                  Directory.Delete(tmpDir, true)
          }

          test "extract summary counts are non-negative" {
              let tmpDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"))
              Directory.CreateDirectory(tmpDir) |> ignore

              try
                  let projectFile, _ = writeFixtureProject tmpDir

                  let result =
                      Pipeline.run
                          { ProjectFile = projectFile
                            VocabularyFile = None
                            AssemblyRefs = dllRefs ()
                            OutputFormat = Pipeline.Text }

                  let summary = Expect.wantOk result "pipeline should succeed"
                  Expect.isTrue (summary.Confirmed >= 0) "Confirmed >= 0"
                  Expect.isTrue (summary.Proposed >= 0) "Proposed >= 0"
                  Expect.isTrue (summary.Unresolved >= 0) "Unresolved >= 0"
              finally
                  Directory.Delete(tmpDir, true)
          }

          test "extract total equals sum of confirmed + proposed + unresolved" {
              let tmpDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"))
              Directory.CreateDirectory(tmpDir) |> ignore

              try
                  let projectFile, lockFilePath = writeFixtureProject tmpDir

                  Pipeline.run
                      { ProjectFile = projectFile
                        VocabularyFile = None
                        AssemblyRefs = dllRefs ()
                        OutputFormat = Pipeline.Text }
                  |> ignore

                  let lf = LockFile.read lockFilePath |> Result.defaultWith (fun e -> failwith e)
                  let total = lf.Mappings.Length

                  let confirmed =
                      lf.Mappings |> List.filter (fun m -> m.Status = Confirmed) |> List.length

                  let proposed =
                      lf.Mappings |> List.filter (fun m -> m.Status = Proposed) |> List.length

                  let unresolved =
                      lf.Mappings |> List.filter (fun m -> m.Status = Unresolved) |> List.length

                  Expect.equal (confirmed + proposed + unresolved) total "counts must sum to total"
              finally
                  Directory.Delete(tmpDir, true)
          } ]

// ── AT2: merge preserves confirmed llm/manual entries ─────────────────────────

[<Tests>]
let at2MergeTests =
    testList
        "AT2 - merge preserves llm/manual confirmed entries"
        [ test "pre-seeded llm+confirmed entry is preserved after re-extract" {
              let tmpDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"))
              Directory.CreateDirectory(tmpDir) |> ignore

              try
                  let projectFile, lockFilePath = writeFixtureProject tmpDir
                  Directory.CreateDirectory(Path.GetDirectoryName lockFilePath) |> ignore

                  let existingLock: LockFile =
                      { SchemaVersion = 1
                        Generated = DateTimeOffset.UtcNow
                        Vocabularies = Map.empty
                        DeclaredPrefixes = Map.empty
                        Mappings =
                          [ { FSharpType = "FixtureApp.Order"
                              Iri = Some "https://schema.org/Order"
                              Confidence = 1.0
                              Source = Llm
                              Status = Confirmed
                              Alternates = []
                              Shape = MappingShape.Record [] } ] }

                  LockFile.write lockFilePath existingLock

                  Pipeline.run
                      { ProjectFile = projectFile
                        VocabularyFile = None
                        AssemblyRefs = dllRefs ()
                        OutputFormat = Pipeline.Text }
                  |> ignore

                  let updated = LockFile.read lockFilePath |> Result.defaultWith (fun e -> failwith e)

                  let order =
                      updated.Mappings |> List.tryFind (fun m -> m.FSharpType = "FixtureApp.Order")

                  Expect.isSome order "Order mapping must be present after re-extract"
                  let m = order.Value
                  Expect.equal m.Source Llm "Source must remain Llm (not overwritten by convention)"
                  Expect.equal m.Status Confirmed "Status must remain Confirmed"
              finally
                  Directory.Delete(tmpDir, true)
          } ]

// ── AT3: curation ─────────────────────────────────────────────────────────────

[<Tests>]
let at3CurationTests =
    testList
        "AT3 - curateSourceFiles excludes Program.fs / Generated*.fs / .fsi"
        [ test "Program.fs is excluded" {
              let files = [ "/app/Domain.fs"; "/app/Program.fs"; "/app/Vocabulary.fs" ]
              let curated = Pipeline.curateSourceFiles files
              Expect.isFalse (List.contains "/app/Program.fs" curated) "Program.fs must be excluded"
              Expect.isTrue (List.contains "/app/Domain.fs" curated) "Domain.fs must be kept"
          }

          test "Generated*.fs is excluded" {
              let files =
                  [ "/app/Model.fs"
                    "/app/GeneratedDiscovery.fs"
                    "/app/GeneratedLinkedData.fs"
                    "/app/GeneratedSemantics.fs" ]

              let curated = Pipeline.curateSourceFiles files
              Expect.equal curated [ "/app/Model.fs" ] "only non-Generated files kept"
          }

          test ".fsi files are excluded" {
              let files = [ "/app/Model.fsi"; "/app/Model.fs"; "/app/Vocabulary.fs" ]
              let curated = Pipeline.curateSourceFiles files
              Expect.isFalse (List.contains "/app/Model.fsi" curated) ".fsi must be excluded"
              Expect.isTrue (List.contains "/app/Model.fs" curated) ".fs kept"
          }

          test "all three exclusions apply together" {
              let files =
                  [ "/app/Model.fsi"
                    "/app/Model.fs"
                    "/app/GameStore.fs"
                    "/app/Vocabulary.fs"
                    "/app/GeneratedDiscovery.fs"
                    "/app/Program.fs" ]

              let curated = Pipeline.curateSourceFiles files

              Expect.equal
                  curated
                  [ "/app/Model.fs"; "/app/GameStore.fs"; "/app/Vocabulary.fs" ]
                  "only domain+vocab files remain"
          } ]

// ── AT4: determinism ─────────────────────────────────────────────────────────

[<Tests>]
let at4DeterminismTests =
    testList
        "AT4 - two extracts produce byte-identical lock files"
        [ test "two consecutive extracts produce identical JSON (modulo timestamp)" {
              let tmpDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"))
              Directory.CreateDirectory(tmpDir) |> ignore

              try
                  let projectFile, lockFilePath = writeFixtureProject tmpDir

                  let runOnce () =
                      Pipeline.run
                          { ProjectFile = projectFile
                            VocabularyFile = None
                            AssemblyRefs = dllRefs ()
                            OutputFormat = Pipeline.Text }
                      |> ignore

                      LockFile.read lockFilePath |> Result.defaultWith (fun e -> failwith e)

                  let lf1 = runOnce ()
                  let lf2 = runOnce ()

                  let normalize (lf: LockFile) =
                      { lf with
                          Generated = DateTimeOffset.MinValue }

                  let mappingsEqual = normalize lf1 = normalize lf2

                  Expect.isTrue mappingsEqual "Two extracts must produce identical mappings"
              finally
                  Directory.Delete(tmpDir, true)
          } ]

// ── AT5: merge preserves excluded manual decisions ────────────────────────────

[<Tests>]
let at5ExcludedPreservationTests =
    testList
        "AT5 - merge preserves Excluded Manual decisions across re-extract"
        [ test "Excluded+Manual entry survives re-extract unchanged" {
              let tmpDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"))
              Directory.CreateDirectory(tmpDir) |> ignore

              try
                  let projectFile, lockFilePath = writeFixtureProject tmpDir
                  Directory.CreateDirectory(Path.GetDirectoryName lockFilePath) |> ignore

                  let existingLock: LockFile =
                      { SchemaVersion = 1
                        Generated = DateTimeOffset.UtcNow
                        Vocabularies = Map.empty
                        DeclaredPrefixes = Map.empty
                        Mappings =
                          [ { FSharpType = "FixtureApp.Order"
                              Iri = None
                              Confidence = 0.0
                              Source = Manual
                              Status = Excluded
                              Alternates = []
                              Shape = MappingShape.Record [] } ] }

                  LockFile.write lockFilePath existingLock

                  Pipeline.run
                      { ProjectFile = projectFile
                        VocabularyFile = None
                        AssemblyRefs = dllRefs ()
                        OutputFormat = Pipeline.Text }
                  |> ignore

                  let updated = LockFile.read lockFilePath |> Result.defaultWith (fun e -> failwith e)

                  let order =
                      updated.Mappings |> List.tryFind (fun m -> m.FSharpType = "FixtureApp.Order")

                  Expect.isSome order "Order mapping must be present after re-extract"
                  let m = order.Value
                  Expect.equal m.Status Excluded "Status must remain Excluded (decision must not be overwritten)"
                  Expect.equal m.Source Manual "Source must remain Manual"
              finally
                  Directory.Delete(tmpDir, true)
          }

          test "Excluded+Convention entry survives re-extract unchanged" {
              let tmpDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"))
              Directory.CreateDirectory(tmpDir) |> ignore

              try
                  let projectFile, lockFilePath = writeFixtureProject tmpDir
                  Directory.CreateDirectory(Path.GetDirectoryName lockFilePath) |> ignore

                  let existingLock: LockFile =
                      { SchemaVersion = 1
                        Generated = DateTimeOffset.UtcNow
                        Vocabularies = Map.empty
                        DeclaredPrefixes = Map.empty
                        Mappings =
                          [ { FSharpType = "FixtureApp.Order"
                              Iri = None
                              Confidence = 0.0
                              Source = Convention
                              Status = Excluded
                              Alternates = []
                              Shape = MappingShape.Record [] } ] }

                  LockFile.write lockFilePath existingLock

                  Pipeline.run
                      { ProjectFile = projectFile
                        VocabularyFile = None
                        AssemblyRefs = dllRefs ()
                        OutputFormat = Pipeline.Text }
                  |> ignore

                  let updated = LockFile.read lockFilePath |> Result.defaultWith (fun e -> failwith e)

                  let order =
                      updated.Mappings |> List.tryFind (fun m -> m.FSharpType = "FixtureApp.Order")

                  Expect.isSome order "Order mapping must be present after re-extract"
                  let m = order.Value
                  Expect.equal m.Status Excluded "Excluded status preserved regardless of Source"
                  Expect.equal m.Source Convention "Source=Convention preserved on Excluded entry"
              finally
                  Directory.Delete(tmpDir, true)
          }

          test "Confirmed+Convention entry is preserved after re-extract (decided regardless of source)" {
              let tmpDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"))
              Directory.CreateDirectory(tmpDir) |> ignore

              try
                  let projectFile, lockFilePath = writeFixtureProject tmpDir
                  Directory.CreateDirectory(Path.GetDirectoryName lockFilePath) |> ignore

                  let existingLock: LockFile =
                      { SchemaVersion = 1
                        Generated = DateTimeOffset.UtcNow
                        Vocabularies = Map.empty
                        DeclaredPrefixes = Map.empty
                        Mappings =
                          [ { FSharpType = "FixtureApp.Order"
                              Iri = Some "https://schema.org/Order"
                              Confidence = 0.9
                              Source = Convention
                              Status = Confirmed
                              Alternates = []
                              Shape = MappingShape.Record [] } ] }

                  LockFile.write lockFilePath existingLock

                  Pipeline.run
                      { ProjectFile = projectFile
                        VocabularyFile = None
                        AssemblyRefs = dllRefs ()
                        OutputFormat = Pipeline.Text }
                  |> ignore

                  let updated = LockFile.read lockFilePath |> Result.defaultWith (fun e -> failwith e)

                  let order =
                      updated.Mappings |> List.tryFind (fun m -> m.FSharpType = "FixtureApp.Order")

                  Expect.isSome order "Order mapping must be present"
                  let m = order.Value
                  Expect.equal m.Status Confirmed "Confirmed+Convention must be preserved (decided entry)"
                  Expect.equal m.Source Convention "Source=Convention preserved on Confirmed entry"
                  Expect.equal m.Iri (Some "https://schema.org/Order") "human-confirmed IRI must not be overwritten"
              finally
                  Directory.Delete(tmpDir, true)
          }

          test "Proposed+Convention entry is replaced by fresh re-extract (undecided re-runs)" {
              let tmpDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"))
              Directory.CreateDirectory(tmpDir) |> ignore

              try
                  let projectFile, lockFilePath = writeFixtureProject tmpDir
                  Directory.CreateDirectory(Path.GetDirectoryName lockFilePath) |> ignore

                  let existingLock: LockFile =
                      { SchemaVersion = 1
                        Generated = DateTimeOffset.UtcNow
                        Vocabularies = Map.empty
                        DeclaredPrefixes = Map.empty
                        Mappings =
                          [ { FSharpType = "FixtureApp.Order"
                              Iri = Some "https://schema.org/SomeStaleProposal"
                              Confidence = 0.3
                              Source = Convention
                              Status = Proposed
                              Alternates = []
                              Shape = MappingShape.Record [] } ] }

                  LockFile.write lockFilePath existingLock

                  Pipeline.run
                      { ProjectFile = projectFile
                        VocabularyFile = None
                        AssemblyRefs = dllRefs ()
                        OutputFormat = Pipeline.Text }
                  |> ignore

                  let updated = LockFile.read lockFilePath |> Result.defaultWith (fun e -> failwith e)

                  let order =
                      updated.Mappings |> List.tryFind (fun m -> m.FSharpType = "FixtureApp.Order")

                  Expect.isSome order "Order mapping must be present"
                  let m = order.Value

                  Expect.notEqual
                      m.Iri
                      (Some "https://schema.org/SomeStaleProposal")
                      "stale Proposed IRI must be replaced by fresh extract"
              finally
                  Directory.Delete(tmpDir, true)
          } ]

// ── AT6 fixture (with `using`) ────────────────────────────────────────────────

/// Minimal valid Turtle bytes — empty graph with a base prefix declaration.
let private minimalTurtleBytes () : byte[] =
    System.Text.Encoding.UTF8.GetBytes "@prefix schema: <https://schema.org/> .\n"

/// Stub fetch: returns minimal Turtle bytes for any URI (no network).
let private stubFetch: Fetch =
    fun _uri ->
        async {
            return
                Ok
                    {| ContentType = Some "text/turtle"
                       Body = minimalTurtleBytes () |}
        }

/// Writes a fixture with `using "schema"` so the pipeline puts schema in inScopePrefixes.
let private writeFixtureProjectWithUsing (tmpDir: string) : string * string =
    let domainSource =
        """namespace FixtureApp

type Game = { Id: int; Title: string }
"""

    let vocabSource =
        """module Vocabulary
open Frank.Semantic

let registry =
    vocabulary {
        prefix "schema" "https://schema.org/"
        using "schema"
    }
"""

    File.WriteAllText(Path.Combine(tmpDir, "Domain.fs"), domainSource)
    File.WriteAllText(Path.Combine(tmpDir, "Vocabulary.fs"), vocabSource)

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

    let projectFile = Path.Combine(tmpDir, "FixtureApp.fsproj")
    File.WriteAllText(projectFile, fsprojContent)
    let lockFilePath = Path.Combine(tmpDir, ".frank", "semantic-mappings.lock.json")
    projectFile, lockFilePath

// ── AT6: vocabularies block populated ────────────────────────────────────────

[<Tests>]
let at6VocabulariesTests =
    testList
        "AT6 - extract populates lock vocabularies block"
        [ test "lock vocabularies contains schema prefix with uri and non-empty hash after extract" {
              let tmpDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"))
              Directory.CreateDirectory(tmpDir) |> ignore

              try
                  let projectFile, lockFilePath = writeFixtureProjectWithUsing tmpDir

                  let result =
                      Pipeline.runWithFetch
                          stubFetch
                          { ProjectFile = projectFile
                            VocabularyFile = None
                            AssemblyRefs = dllRefs ()
                            OutputFormat = Pipeline.Text }

                  Expect.isOk result "pipeline should succeed"
                  let lf = LockFile.read lockFilePath |> Result.defaultWith (fun e -> failwith e)
                  let entry = Map.tryFind "schema" lf.Vocabularies
                  Expect.isSome entry "Vocabularies must contain 'schema' prefix"
                  let v = entry.Value
                  Expect.equal v.Uri "https://schema.org/" "Uri must match registry prefix"
                  Expect.isNotEmpty v.Hash "Hash must be non-empty"
              finally
                  Directory.Delete(tmpDir, true)
          } ]
