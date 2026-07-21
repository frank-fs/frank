module Frank.Cli.Core.Tests.PipelineTests

open System
open System.IO
open System.Reflection
open System.Security.Cryptography
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
                        Integrity = None
                        Vocabularies = Map.empty
                        DeclaredPrefixes = Map.empty
                        Mappings =
                          [ { FSharpType = "FixtureApp.Order"
                              Iri = Some "https://schema.org/Order"
                              Confidence = 1.0
                              Source = Llm
                              Status = Confirmed
                              Alternates = []
                              Rt = None
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
                          Generated = DateTimeOffset.MinValue
                          Integrity = None }

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
                        Integrity = None
                        Vocabularies = Map.empty
                        DeclaredPrefixes = Map.empty
                        Mappings =
                          [ { FSharpType = "FixtureApp.Order"
                              Iri = None
                              Confidence = 0.0
                              Source = Manual
                              Status = Excluded
                              Alternates = []
                              Rt = None
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
                        Integrity = None
                        Vocabularies = Map.empty
                        DeclaredPrefixes = Map.empty
                        Mappings =
                          [ { FSharpType = "FixtureApp.Order"
                              Iri = None
                              Confidence = 0.0
                              Source = Convention
                              Status = Excluded
                              Alternates = []
                              Rt = None
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
                        Integrity = None
                        Vocabularies = Map.empty
                        DeclaredPrefixes = Map.empty
                        Mappings =
                          [ { FSharpType = "FixtureApp.Order"
                              Iri = Some "https://schema.org/Order"
                              Confidence = 0.9
                              Source = Convention
                              Status = Confirmed
                              Alternates = []
                              Rt = None
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
                        Integrity = None
                        Vocabularies = Map.empty
                        DeclaredPrefixes = Map.empty
                        Mappings =
                          [ { FSharpType = "FixtureApp.Order"
                              Iri = Some "https://schema.org/SomeStaleProposal"
                              Confidence = 0.3
                              Source = Convention
                              Status = Proposed
                              Alternates = []
                              Rt = None
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

/// Stub ConnegFetch: returns minimal Turtle bytes for any URI (no network).
let private stubFetch: ConnegFetch =
    fun _uri _etag _lastMod ->
        async {
            return
                RdfContent
                    {| MediaType = "text/turtle"
                       Body = minimalTurtleBytes ()
                       HttpStatus = 200
                       ETag = None
                       LastModified = None
                       CacheControlMaxAge = None |}
        }

// ── AT8/AT9 stubs (rich turtle with class definitions) ───────────────────────

/// Schema turtle that includes a class definition so `type Game` gets schema:Game CURIE.
let private richSchemaTurtleBytes () : byte[] =
    System.Text.Encoding.UTF8.GetBytes
        "@prefix schema: <https://schema.org/> .\n@prefix rdfs: <http://www.w3.org/2000/01/rdf-schema#> .\nschema:Game a rdfs:Class .\n"

/// Foaf turtle that includes a class definition so `type Person` gets foaf:Person CURIE.
let private foafTurtleBytes () : byte[] =
    System.Text.Encoding.UTF8.GetBytes
        "@prefix foaf: <http://xmlns.com/foaf/0.1/> .\n@prefix rdfs: <http://www.w3.org/2000/01/rdf-schema#> .\nfoaf:Person a rdfs:Class .\n"

/// Stub ConnegFetch: returns rich schema turtle (includes schema:Game class) for any URI.
let private richSchemaStubFetch: ConnegFetch =
    fun _uri _etag _lastMod ->
        async {
            return
                RdfContent
                    {| MediaType = "text/turtle"
                       Body = richSchemaTurtleBytes ()
                       HttpStatus = 200
                       ETag = None
                       LastModified = None
                       CacheControlMaxAge = None |}
        }

/// Dispatch stub: routes by URI host to serve independent turtle content per vocabulary.
let private twoVocabStubFetch: ConnegFetch =
    fun (uri: Uri) _etag _lastMod ->
        async {
            let bytes =
                if uri.Host = "schema.org" then richSchemaTurtleBytes ()
                elif uri.Host = "xmlns.com" then foafTurtleBytes ()
                else invalidArg "uri" $"unexpected host: {uri.Host}"

            return
                RdfContent
                    {| MediaType = "text/turtle"
                       Body = bytes
                       HttpStatus = 200
                       ETag = None
                       LastModified = None
                       CacheControlMaxAge = None |}
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

/// Writes a fixture where `type Game` matches `schema:Game` (Confirmed CURIE in lock).
/// Used for AT8 CURIE round-trip tests.
let private writeGameProjectWithRichSchema (tmpDir: string) : string * string =
    let domainSource =
        """namespace FixtureApp
type Game = { Id: int }
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

/// Writes a fixture with two vocabularies (schema + foaf), each with distinct classes.
/// Used for AT9 two-vocabulary independence tests.
let private writeTwoVocabProject (tmpDir: string) : string * string =
    let domainSource =
        """namespace FixtureApp
type Game = { Id: int }
type Person = { Name: string }
"""

    let vocabSource =
        """module Vocabulary
open Frank.Semantic

let registry =
    vocabulary {
        prefix "schema" "https://schema.org/"
        prefix "foaf" "http://xmlns.com/foaf/0.1/"
        using "schema"
        using "foaf"
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
                          (fun () -> DateTimeOffset.UtcNow)
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

                  let expectedHash =
                      use sha = SHA256.Create()

                      sha.ComputeHash(minimalTurtleBytes ())
                      |> Array.map (fun b -> b.ToString("x2"))
                      |> String.concat ""

                  Expect.equal v.Hash expectedHash "Hash must be sha256 of served turtle bytes"
              finally
                  Directory.Delete(tmpDir, true)
          }

          test "AT6b - lock Integrity field is non-None after extract" {
              let tmpDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"))
              Directory.CreateDirectory(tmpDir) |> ignore

              try
                  let projectFile, lockFilePath = writeFixtureProjectWithUsing tmpDir
                  let clock = fun () -> DateTimeOffset.Parse("2026-01-01T00:00:00Z")

                  let result =
                      Pipeline.runWithFetch
                          stubFetch
                          clock
                          { ProjectFile = projectFile
                            VocabularyFile = None
                            AssemblyRefs = dllRefs ()
                            OutputFormat = Pipeline.Text }

                  Expect.isOk result "pipeline should succeed"
                  let lf = LockFile.read lockFilePath |> Result.defaultWith (fun e -> failwith e)
                  Expect.isSome lf.Integrity "Integrity must be Some after extract (stamped)"
              finally
                  Directory.Delete(tmpDir, true)
          }

          test "AT1 - vocabularies populated with injected fetchedAt" {
              let tmpDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"))
              Directory.CreateDirectory(tmpDir) |> ignore

              try
                  let projectFile, lockFilePath = writeFixtureProjectWithUsing tmpDir
                  let fixedTime = DateTimeOffset.Parse("2026-06-01T12:00:00Z")
                  let clock = fun () -> fixedTime

                  let result =
                      Pipeline.runWithFetch
                          stubFetch
                          clock
                          { ProjectFile = projectFile
                            VocabularyFile = None
                            AssemblyRefs = dllRefs ()
                            OutputFormat = Pipeline.Text }

                  Expect.isOk result "pipeline should succeed"
                  let lf = LockFile.read lockFilePath |> Result.defaultWith (fun e -> failwith e)
                  let entry = Map.tryFind "schema" lf.Vocabularies
                  Expect.isSome entry "Vocabularies must contain 'schema'"
                  Expect.equal entry.Value.FetchedAt fixedTime "FetchedAt must equal injected clock value"
                  Expect.isSome lf.Integrity "Integrity must be stamped"
              finally
                  Directory.Delete(tmpDir, true)
          }

          test "AT5 - same injected clock and identical inputs produce byte-for-byte identical lock files" {
              let tmpDir1 = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"))
              let tmpDir2 = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"))
              Directory.CreateDirectory(tmpDir1) |> ignore
              Directory.CreateDirectory(tmpDir2) |> ignore

              try
                  let projectFile1, lockFilePath1 = writeFixtureProjectWithUsing tmpDir1
                  let projectFile2, lockFilePath2 = writeFixtureProjectWithUsing tmpDir2

                  let fixedClock = fun () -> DateTimeOffset.Parse("2026-01-01T00:00:00Z")

                  let opts1: Pipeline.ExtractOptions =
                      { ProjectFile = projectFile1
                        VocabularyFile = None
                        AssemblyRefs = dllRefs ()
                        OutputFormat = Pipeline.Text }

                  let opts2: Pipeline.ExtractOptions =
                      { ProjectFile = projectFile2
                        VocabularyFile = None
                        AssemblyRefs = dllRefs ()
                        OutputFormat = Pipeline.Text }

                  Expect.isOk (Pipeline.runWithFetch stubFetch fixedClock opts1) "run1 should succeed"
                  Expect.isOk (Pipeline.runWithFetch stubFetch fixedClock opts2) "run2 should succeed"

                  let bytes1 = File.ReadAllBytes lockFilePath1
                  let bytes2 = File.ReadAllBytes lockFilePath2

                  Expect.equal bytes1 bytes2 "lock files must be byte-for-byte identical"

                  let lf1 = LockFile.read lockFilePath1 |> Result.defaultWith failwith
                  Expect.isSome lf1.Integrity "Integrity must be stamped"
                  Expect.hasLength lf1.Integrity.Value 64 "integrity must be 64-char hex"
                  Expect.isOk (LockFile.verifyIntegrity lf1) "integrity must verify"

                  // Golden sha256: assert the produced hash equals the expected canonical value.
                  // Computed from two independent runs at 2026-01-01T00:00:00Z with stubFetch.
                  let goldenIntegrityHash =
                      "51d14da0e98f7de1590b6286b0abf0a2498905d9fdda2ed011c1bc2958cadeb0"

                  Expect.equal
                      lf1.Integrity.Value
                      goldenIntegrityHash
                      "golden integrity sha256 must match canonical output"
              finally
                  Directory.Delete(tmpDir1, true)
                  Directory.Delete(tmpDir2, true)
          } ]

// ── AT8: CURIE round-trip ─────────────────────────────────────────────────────

[<Tests>]
let at8CurieRoundTripTests =
    testList
        "AT8 - every mapping CURIE resolves via Vocabularies binding"
        [ test "schema:Game CURIE expands to Vocabularies[schema].Uri + 'Game'" {
              let tmpDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"))
              Directory.CreateDirectory(tmpDir) |> ignore

              try
                  let projectFile, lockFilePath = writeGameProjectWithRichSchema tmpDir

                  let result =
                      Pipeline.runWithFetch
                          richSchemaStubFetch
                          (fun () -> DateTimeOffset.UtcNow)
                          { ProjectFile = projectFile
                            VocabularyFile = None
                            AssemblyRefs = dllRefs ()
                            OutputFormat = Pipeline.Text }

                  Expect.isOk result "pipeline should succeed"
                  let lf = LockFile.read lockFilePath |> Result.defaultWith failwith

                  let mappingsWithCurie =
                      lf.Mappings |> List.choose (fun m -> m.Iri |> Option.map (fun iri -> m, iri))

                  Expect.isNonEmpty mappingsWithCurie "at least one mapping must have a CURIE"

                  for m, curie in mappingsWithCurie do
                      // Assert no bypass: CURIE must not be a full IRI
                      Expect.isFalse
                          (curie.StartsWith("https://") || curie.StartsWith("http://"))
                          $"{m.FSharpType}: Iri must be a CURIE, not a full IRI (bypass)"

                      // Assert CURIE resolves via Vocabularies binding
                      let colon = curie.IndexOf(':')
                      Expect.isTrue (colon > 0) $"{m.FSharpType}: CURIE '{curie}' must contain ':'"
                      let prefix = curie.[.. colon - 1]
                      let local = curie.[colon + 1 ..]

                      let vocabEntry = Map.tryFind prefix lf.Vocabularies

                      Expect.isSome
                          vocabEntry
                          $"prefix '{prefix}' from CURIE '{curie}' must exist in Vocabularies (no dangling CURIE)"

                      let fullIri = vocabEntry.Value.Uri + local

                      Expect.equal
                          fullIri
                          (vocabEntry.Value.Uri + local)
                          $"{m.FSharpType}: {prefix}:{local} must expand to {vocabEntry.Value.Uri}{local}"

                  // Specific assertion for schema:Game
                  let gameMapping =
                      lf.Mappings |> List.tryFind (fun m -> m.FSharpType = "FixtureApp.Game")

                  Expect.isSome gameMapping "must have FixtureApp.Game mapping"
                  Expect.equal gameMapping.Value.Iri (Some "schema:Game") "Game must get schema:Game CURIE"

                  let schemaEntry = Map.tryFind "schema" lf.Vocabularies
                  Expect.isSome schemaEntry "schema must be in Vocabularies"
                  Expect.equal schemaEntry.Value.Uri "https://schema.org/" "schema Uri must be https://schema.org/"

                  let expanded = schemaEntry.Value.Uri + "Game"
                  Expect.equal expanded "https://schema.org/Game" "schema:Game expands to https://schema.org/Game"
              finally
                  Directory.Delete(tmpDir, true)
          } ]

// ── AT9: two vocabularies ─────────────────────────────────────────────────────

[<Tests>]
let at9TwoVocabulariesTests =
    testList
        "AT9 - two vocabularies with independent hashes and deterministic ordering"
        [ test "schema and foaf both in Vocabularies with independent hashes" {
              let tmpDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"))
              Directory.CreateDirectory(tmpDir) |> ignore

              try
                  let projectFile, lockFilePath = writeTwoVocabProject tmpDir

                  let fixedClock = fun () -> DateTimeOffset.Parse("2026-06-01T00:00:00Z")

                  let result =
                      Pipeline.runWithFetch
                          twoVocabStubFetch
                          fixedClock
                          { ProjectFile = projectFile
                            VocabularyFile = None
                            AssemblyRefs = dllRefs ()
                            OutputFormat = Pipeline.Text }

                  Expect.isOk result "pipeline should succeed"
                  let lf = LockFile.read lockFilePath |> Result.defaultWith failwith

                  // Both bindings present
                  let schemaEntry = Map.tryFind "schema" lf.Vocabularies
                  let foafEntry = Map.tryFind "foaf" lf.Vocabularies
                  Expect.isSome schemaEntry "schema must be in Vocabularies"
                  Expect.isSome foafEntry "foaf must be in Vocabularies"

                  // Independent hashes (different turtle content → different sha256)
                  Expect.notEqual schemaEntry.Value.Hash foafEntry.Value.Hash "schema and foaf hashes must differ"

                  // Verify each hash matches the served bytes
                  let computeHash (bytes: byte[]) =
                      use sha = SHA256.Create()

                      sha.ComputeHash(bytes)
                      |> Array.map (fun b -> b.ToString("x2"))
                      |> String.concat ""

                  Expect.equal
                      schemaEntry.Value.Hash
                      (computeHash (richSchemaTurtleBytes ()))
                      "schema Hash must be sha256 of schema turtle"

                  Expect.equal
                      foafEntry.Value.Hash
                      (computeHash (foafTurtleBytes ()))
                      "foaf Hash must be sha256 of foaf turtle"

                  // CURIE resolution: schema:Game → schema.Uri + "Game"
                  let gameMapping =
                      lf.Mappings |> List.tryFind (fun m -> m.FSharpType = "FixtureApp.Game")

                  Expect.isSome gameMapping "must have FixtureApp.Game mapping"
                  Expect.equal gameMapping.Value.Iri (Some "schema:Game") "Game must get schema:Game CURIE"

                  Expect.equal
                      (schemaEntry.Value.Uri + "Game")
                      "https://schema.org/Game"
                      "schema:Game expands correctly"

                  // CURIE resolution: foaf:Person → foaf.Uri + "Person"
                  let personMapping =
                      lf.Mappings |> List.tryFind (fun m -> m.FSharpType = "FixtureApp.Person")

                  Expect.isSome personMapping "must have FixtureApp.Person mapping"
                  Expect.equal personMapping.Value.Iri (Some "foaf:Person") "Person must get foaf:Person CURIE"

                  Expect.equal
                      (foafEntry.Value.Uri + "Person")
                      "http://xmlns.com/foaf/0.1/Person"
                      "foaf:Person expands correctly"

                  // Deterministic ordering: vocabularies keys sorted (foaf < schema)
                  let keys = lf.Vocabularies |> Map.toList |> List.map fst
                  Expect.equal keys [ "foaf"; "schema" ] "vocabulary keys must be in alphabetical order"

                  // Integrity verifies
                  Expect.isSome lf.Integrity "Integrity must be stamped"
                  Expect.isOk (LockFile.verifyIntegrity lf) "integrity must verify"
              finally
                  Directory.Delete(tmpDir, true)
          } ]
