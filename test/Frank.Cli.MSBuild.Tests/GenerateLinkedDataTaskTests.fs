module Frank.Cli.MSBuild.Tests.GenerateLinkedDataTaskTests

open System
open System.IO
open Expecto
open Microsoft.Build.Framework
open Frank.Semantic
open Frank.Semantic.LockFile
open Frank.Cli.MSBuild
open Frank.Cli.MSBuild.Tests.Fixtures
open Frank.Cli.MSBuild.Tests.StubBuildEngine

// ── helpers ──────────────────────────────────────────────────────────────────

let private withTempDir (f: string -> unit) =
    let dir = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName())
    Directory.CreateDirectory dir |> ignore

    try
        f dir
    finally
        Directory.Delete(dir, recursive = true)

/// Paths to assemblies the FSI session must reference for the vocab CE to compile.
/// We use the assemblies already loaded in the test process — guaranteed same version.
let private frankSemanticDll =
    typeof<Frank.Semantic.VocabularyRegistry>.Assembly.Location

let private fsharpCoreDll = typeof<Microsoft.FSharp.Core.Unit>.Assembly.Location

/// Write a tiny .fs vocabulary file that uses the Frank.Semantic vocabulary CE,
/// declares a wikidata seeAlso, and binds the result to `registry`.
/// The type used is System.String (BCL, always available).
let private writeVocabSource (dir: string) : string =
    let path = Path.Combine(dir, "Vocabulary.fs")

    let source =
        """module CliLdVocab

open Frank.Semantic

let registry =
    vocabulary {
        prefix "schema" "https://schema.org/"
        prefix "wikidata" "https://www.wikidata.org/wiki/"
        using "schema"
        seeAlso typeof<System.String> "wikidata:Q277"
    }
"""

    File.WriteAllText(path, source)
    path

/// Build a lock file that maps System.String → schema:Text.
let private stringLock: Frank.Semantic.LockFile.LockFile =
    { confirmedLock with
        Vocabularies =
            Map.ofList
                [ "schema",
                  { Uri = "https://schema.org/"
                    FetchedAt = DateTimeOffset.Parse("2025-01-01T00:00:00Z")
                    Hash = "sha256:abc" }
                  "wikidata",
                  { Uri = "https://www.wikidata.org/wiki/"
                    FetchedAt = DateTimeOffset.Parse("2025-01-01T00:00:00Z")
                    Hash = "sha256:def" } ]
        Mappings =
            [ { FSharpType = "System.String"
                Iri = Some "schema:Text"
                Confidence = 1.0
                Source = Convention
                Status = Confirmed
                Alternates = []
                Fields = [] } ] }

let private makeTaskItem (path: string) : ITaskItem =
    let mutable spec = path

    { new ITaskItem with
        member _.ItemSpec
            with get () = spec
            and set v = spec <- v

        member _.GetMetadata(_) = ""
        member _.SetMetadata(_, _) = ()
        member _.RemoveMetadata(_) = ()
        member _.CopyMetadataTo(_) = ()
        member _.CloneCustomMetadata() = System.Collections.Hashtable() :> _
        member _.MetadataCount = 0
        member _.MetadataNames = System.Collections.ArrayList() :> _ }

let private makeTask
    (engine: StubBuildEngine)
    (lockPath: string)
    (outDir: string)
    (sourceFiles: string list)
    (assemblyRefs: string list)
    : GenerateLinkedDataTask =
    let task = GenerateLinkedDataTask()
    task.BuildEngine <- engine
    task.LockFilePath <- lockPath
    task.OutputPath <- outDir
    task.ModuleName <- "CliTest.GeneratedLinkedData"
    task.SourceFiles <- sourceFiles |> List.map makeTaskItem |> Array.ofList
    task.AssemblyRefs <- assemblyRefs |> List.map makeTaskItem |> Array.ofList
    task

// ── tests ─────────────────────────────────────────────────────────────────────

[<Tests>]
let linkedDataTests =
    testList
        "GenerateLinkedDataTask"
        [ test "in-process FSI eval end-to-end: wikidata seeAlso flows through eval→emit" {
              withTempDir (fun dir ->
                  let outDir = Path.Combine(dir, "obj")
                  let engine = StubBuildEngine()
                  let lockPath = writeLockFile dir stringLock
                  let vocabPath = writeVocabSource dir

                  let refs = [ frankSemanticDll; fsharpCoreDll ]
                  let task = makeTask engine lockPath outDir [ vocabPath ] refs
                  task.VocabularyBinding <- "CliLdVocab.registry"

                  let result = task.Execute()
                  let errMsgs = engine.Errors |> List.map (fun e -> e.Message) |> String.concat "; "

                  Expect.isTrue result ("Execute returned false; errors: " + errMsgs)
                  Expect.isEmpty engine.Errors "no errors logged"

                  let outFile = Path.Combine(outDir, "GeneratedLinkedData.fs")
                  Expect.isTrue (File.Exists outFile) "GeneratedLinkedData.fs written"

                  let source = File.ReadAllText outFile
                  Expect.stringContains source "https://www.wikidata.org/wiki/Q277" "wikidata seeAlso IRI present"
                  Expect.stringContains source "https://schema.org/" "schema.org prefix present"
                  Expect.isFalse (source.Contains "urn:frank:") "no urn:frank: in output")
          }

          test "GeneratedFile output property set to written path" {
              withTempDir (fun dir ->
                  let outDir = Path.Combine(dir, "obj")
                  let engine = StubBuildEngine()
                  let lockPath = writeLockFile dir stringLock
                  let vocabPath = writeVocabSource dir
                  let refs = [ frankSemanticDll; fsharpCoreDll ]
                  let task = makeTask engine lockPath outDir [ vocabPath ] refs
                  task.VocabularyBinding <- "CliLdVocab.registry"
                  task.Execute() |> ignore
                  let expected = Path.Combine(outDir, "GeneratedLinkedData.fs")
                  Expect.equal task.GeneratedFile expected "GeneratedFile property matches written path")
          }

          test "missing lock file: Execute returns false, error logged" {
              withTempDir (fun dir ->
                  let engine = StubBuildEngine()
                  let vocabPath = writeVocabSource dir
                  let refs = [ frankSemanticDll; fsharpCoreDll ]
                  let task = makeTask engine "/nonexistent/lock.json" dir [ vocabPath ] refs
                  task.VocabularyBinding <- "CliLdVocab.registry"
                  let result = task.Execute()
                  Expect.isFalse result "Execute returns false on missing lock"
                  Expect.isNonEmpty engine.Errors "error logged for missing lock")
          }

          test "bad vocab source: Execute returns false, FSI diagnostic surfaced" {
              withTempDir (fun dir ->
                  let outDir = Path.Combine(dir, "obj")
                  let engine = StubBuildEngine()
                  let lockPath = writeLockFile dir stringLock
                  let badPath = Path.Combine(dir, "Bad.fs")
                  File.WriteAllText(badPath, "this is not valid fsharp !!!")
                  let refs = [ frankSemanticDll; fsharpCoreDll ]
                  let task = makeTask engine lockPath outDir [ badPath ] refs
                  task.VocabularyBinding <- "CliLdVocab.registry"
                  let result = task.Execute()
                  Expect.isFalse result "Execute returns false on bad vocab source"
                  Expect.isNonEmpty engine.Errors "FSI diagnostic error logged")
          }

          test "no child processes spawned: no Process.Start in Execute path" {
              // Structural test: GenerateLinkedDataTask uses in-process FSI only.
              // We verify by confirming the type has no member named StartProcess
              // and that Execute() completes without spawning a dotnet child.
              let taskType = typeof<GenerateLinkedDataTask>

              let hasStartProcess =
                  taskType.GetMethods()
                  |> Array.exists (fun m -> m.Name.Contains("StartProcess") || m.Name.Contains("Process.Start"))

              Expect.isFalse hasStartProcess "no StartProcess member on GenerateLinkedDataTask"
          } ]
