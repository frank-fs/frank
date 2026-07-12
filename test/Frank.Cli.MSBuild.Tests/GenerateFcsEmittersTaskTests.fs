module Frank.Cli.MSBuild.Tests.GenerateFcsEmittersTaskTests

open System
open System.IO
open Expecto
open Microsoft.Build.Framework
open Frank.Semantic
open Frank.Semantic.LockFile
open Frank.Cli.MSBuild
open Frank.Cli.MSBuild.Tests.Fixtures
open Frank.Cli.MSBuild.Tests.StubBuildEngine
open Frank.TestSupport.TempDir

let private frankSemanticDll =
    typeof<Frank.Semantic.VocabularyRegistry>.Assembly.Location

let private fsharpCoreDll = typeof<Microsoft.FSharp.Core.Unit>.Assembly.Location

let private sdkRefs () : string list =
    let checker = FSharp.Compiler.CodeAnalysis.FSharpChecker.Create()
    let src = FSharp.Compiler.Text.SourceText.ofString "let x = 1"

    let opts, _ =
        checker.GetProjectOptionsFromScript(
            "/tmp/frank_sdk_probe_fcs.fsx",
            src,
            assumeDotNetFramework = false,
            useSdkRefs = true
        )
        |> Async.RunSynchronously

    opts.OtherOptions
    |> Array.choose (fun o ->
        if o.StartsWith("-r:", StringComparison.Ordinal) then Some(o.[3..])
        else None)
    |> Array.toList

/// F# source with both a domain type and a vocabulary CE binding.
/// Covers LinkedData (seeAlso), Semantic (EquivalentClass), Validation (types),
/// and Provenance (provClass) in a single source file.
let private writeAllVocabSource (dir: string) : string =
    let path = Path.Combine(dir, "VocabAll.fs")

    let source =
        """namespace FcsFixture

open Frank.Semantic

type Widget =
    { id: int
      label: string }

module AllVocab =

    let registry =
        vocabulary {
            prefix "ex" "https://example.org/"
            using "ex"
            seeAlso typeof<Widget> "ex:Widget"
            provClass typeof<Widget> Entity
        }
"""

    File.WriteAllText(path, source)
    path

/// Lock file with one confirmed mapping.
let private allEmittersLock: LockFile =
    { SchemaVersion = 1
      Generated = DateTimeOffset.Parse("2025-01-01T00:00:00Z")
      Integrity = None
      Vocabularies =
        Map.ofList
            [ "ex",
              { v1Empty with
                  Uri = "https://example.org/"
                  FetchedAt = DateTimeOffset.Parse("2025-01-01T00:00:00Z")
                  Hash = "sha256:fixture-ex" } ]
      DeclaredPrefixes = Map.empty
      Mappings =
        [ { FSharpType = "FcsFixture.Widget"
            Iri = Some "ex:Widget"
            Confidence = 1.0
            Source = Convention
            Status = Confirmed
            Alternates = []
            Rt = None
            Shape =
              MappingShape.Record
                  [ { Name = "id"
                      Iri = Some "ex:id"
                      Confidence = 1.0
                      Source = Convention
                      Status = Confirmed }
                    { Name = "label"
                      Iri = Some "ex:label"
                      Confidence = 1.0
                      Source = Convention
                      Status = Confirmed } ] } ] }

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
    (srcPath: string)
    (refs: string list)
    : GenerateFcsEmittersTask =
    let task = GenerateFcsEmittersTask()
    task.BuildEngine <- engine
    task.LockFilePath <- lockPath
    task.OutputPath <- outDir
    task.SourceFiles <- [| makeTaskItem srcPath |]
    task.AssemblyRefs <- refs |> List.map makeTaskItem |> Array.ofList
    task.VocabularyBinding <- "FcsFixture.AllVocab.registry"
    task.LinkedDataModuleName <- "FcsFixture.GeneratedLinkedData"
    task.SemanticsModuleName <- "FcsFixture.GeneratedSemantics"
    task.ValidationModuleName <- "FcsFixture.GeneratedValidation"
    task.ProvenanceModuleName <- "FcsFixture.GeneratedProvenance"
    task

let private collectErrors (engine: StubBuildEngine) =
    engine.Errors |> List.map (fun e -> e.Message) |> String.concat "; "

// ── AC1a: FCS-once counting seam ─────────────────────────────────────────────

[<Tests>]
let fcsOnceTests =
    testList
        "GenerateFcsEmittersTask — AC1a FCS-once counting seam (#386)"
        [ test "FcsPassCount = 1 after executing all four emitters" {
              withTempDir (fun dir ->
                  let outDir = Path.Combine(dir, "obj")
                  let engine = StubBuildEngine()
                  let lockPath = writeLockFile dir allEmittersLock
                  let srcPath = writeAllVocabSource dir
                  let refs = frankSemanticDll :: fsharpCoreDll :: sdkRefs ()

                  let task = makeTask engine lockPath outDir srcPath refs
                  task.HasLinkedData <- true
                  task.HasSemantic <- true
                  task.HasValidation <- true
                  task.HasProvenance <- true

                  let result = task.Execute()
                  let errMsgs = collectErrors engine

                  Expect.isTrue result $"Execute must succeed; errors: {errMsgs}"
                  Expect.equal task.FcsPassCount 1 "Exactly one FCS ParseAndCheckProject call for all four emitters")
          }

          test "FcsPassCount = 0 when no HasX flags are set (task is a no-op)" {
              withTempDir (fun dir ->
                  let outDir = Path.Combine(dir, "obj")
                  let engine = StubBuildEngine()
                  let lockPath = writeLockFile dir allEmittersLock
                  let srcPath = writeAllVocabSource dir
                  let refs = frankSemanticDll :: fsharpCoreDll :: sdkRefs ()

                  let task = makeTask engine lockPath outDir srcPath refs

                  let result = task.Execute()
                  Expect.isTrue result "Execute returns true when all HasX=false (no-op)"
                  Expect.equal task.FcsPassCount 0 "No FCS call when all emitters are gated out")
          }

          test "FcsPassCount = 1 when only HasLinkedData is set (single emitter)" {
              withTempDir (fun dir ->
                  let outDir = Path.Combine(dir, "obj")
                  let engine = StubBuildEngine()
                  let lockPath = writeLockFile dir allEmittersLock
                  let srcPath = writeAllVocabSource dir
                  let refs = frankSemanticDll :: fsharpCoreDll :: sdkRefs ()

                  let task = makeTask engine lockPath outDir srcPath refs
                  task.HasLinkedData <- true

                  let result = task.Execute()
                  let errMsgs = collectErrors engine

                  Expect.isTrue result $"Execute must succeed; errors: {errMsgs}"
                  Expect.equal task.FcsPassCount 1 "Still exactly one FCS call for a single emitter")
          } ]

// ── AC2: byte-identical output ────────────────────────────────────────────────

[<Tests>]
let byteIdenticalTests =
    testList
        "GenerateFcsEmittersTask — AC2 byte-identical output vs individual tasks (#386)"
        [ test "GeneratedLinkedData.fs byte-identical to GenerateLinkedDataTask output" {
              withTempDir (fun dir ->
                  let outDir1 = Path.Combine(dir, "individual")
                  let outDir2 = Path.Combine(dir, "consolidated")
                  let lockPath = writeLockFile dir allEmittersLock
                  let srcPath = writeAllVocabSource dir
                  let refs = frankSemanticDll :: fsharpCoreDll :: sdkRefs ()

                  let engine1 = StubBuildEngine()
                  let individual = GenerateLinkedDataTask()
                  individual.BuildEngine <- engine1
                  individual.LockFilePath <- lockPath
                  individual.OutputPath <- outDir1
                  individual.ModuleName <- "FcsFixture.GeneratedLinkedData"
                  individual.SourceFiles <- [| makeTaskItem srcPath |]
                  individual.AssemblyRefs <- refs |> List.map makeTaskItem |> Array.ofList
                  individual.VocabularyBinding <- "FcsFixture.AllVocab.registry"
                  let r1 = individual.Execute()
                  let errMsgs1 = collectErrors engine1
                  Expect.isTrue r1 $"Individual task must succeed; errors: {errMsgs1}"

                  let engine2 = StubBuildEngine()
                  let task = makeTask engine2 lockPath outDir2 srcPath refs
                  task.HasLinkedData <- true
                  let r2 = task.Execute()
                  let errMsgs2 = collectErrors engine2
                  Expect.isTrue r2 $"Consolidated task must succeed; errors: {errMsgs2}"

                  let golden = File.ReadAllText(Path.Combine(outDir1, "GeneratedLinkedData.fs"))
                  let actual = File.ReadAllText(Path.Combine(outDir2, "GeneratedLinkedData.fs"))
                  Expect.equal actual golden "GeneratedLinkedData.fs must be byte-identical to individual task output")
          } ]

// ── AC3: per-package gating ───────────────────────────────────────────────────

[<Tests>]
let gatingTests =
    testList
        "GenerateFcsEmittersTask — AC3 per-package gating (#386)"
        [ test "only HasLinkedData=true: only GeneratedLinkedData.fs written" {
              withTempDir (fun dir ->
                  let outDir = Path.Combine(dir, "obj")
                  let engine = StubBuildEngine()
                  let lockPath = writeLockFile dir allEmittersLock
                  let srcPath = writeAllVocabSource dir
                  let refs = frankSemanticDll :: fsharpCoreDll :: sdkRefs ()

                  let task = makeTask engine lockPath outDir srcPath refs
                  task.HasLinkedData <- true

                  let result = task.Execute()
                  Expect.isTrue result "Execute must succeed"

                  Expect.isTrue
                      (File.Exists(Path.Combine(outDir, "GeneratedLinkedData.fs")))
                      "GeneratedLinkedData.fs written when HasLinkedData"

                  Expect.isFalse
                      (File.Exists(Path.Combine(outDir, "GeneratedSemantics.fs")))
                      "GeneratedSemantics.fs NOT written when HasSemantic=false"

                  Expect.isFalse
                      (File.Exists(Path.Combine(outDir, "GeneratedValidation.fs")))
                      "GeneratedValidation.fs NOT written when HasValidation=false"

                  Expect.isFalse
                      (File.Exists(Path.Combine(outDir, "GeneratedProvenance.fs")))
                      "GeneratedProvenance.fs NOT written when HasProvenance=false")
          }

          test "only HasSemantic=true: only GeneratedSemantics.fs written" {
              withTempDir (fun dir ->
                  let outDir = Path.Combine(dir, "obj")
                  let engine = StubBuildEngine()
                  let lockPath = writeLockFile dir allEmittersLock
                  let srcPath = writeAllVocabSource dir
                  let refs = frankSemanticDll :: fsharpCoreDll :: sdkRefs ()

                  let task = makeTask engine lockPath outDir srcPath refs
                  task.HasSemantic <- true

                  let result = task.Execute()
                  Expect.isTrue result "Execute must succeed"

                  Expect.isFalse
                      (File.Exists(Path.Combine(outDir, "GeneratedLinkedData.fs")))
                      "GeneratedLinkedData.fs NOT written when HasLinkedData=false"

                  Expect.isTrue
                      (File.Exists(Path.Combine(outDir, "GeneratedSemantics.fs")))
                      "GeneratedSemantics.fs written when HasSemantic"

                  Expect.isFalse
                      (File.Exists(Path.Combine(outDir, "GeneratedValidation.fs")))
                      "GeneratedValidation.fs NOT written when HasValidation=false"

                  Expect.isFalse
                      (File.Exists(Path.Combine(outDir, "GeneratedProvenance.fs")))
                      "GeneratedProvenance.fs NOT written when HasProvenance=false")
          } ]

// ── AC4: Validation fold-in ───────────────────────────────────────────────────

[<Tests>]
let validationFoldInTests =
    testList
        "GenerateFcsEmittersTask — AC4 Validation fold-in, zero separate Extractor call (#386)"
        [ test "GeneratedValidation.fs byte-identical to GenerateValidationTask output" {
              withTempDir (fun dir ->
                  let outDir1 = Path.Combine(dir, "individual")
                  let outDir2 = Path.Combine(dir, "consolidated")
                  let lockPath = writeLockFile dir allEmittersLock
                  let srcPath = writeAllVocabSource dir
                  let refs = frankSemanticDll :: fsharpCoreDll :: sdkRefs ()

                  let engine1 = StubBuildEngine()
                  let individual = GenerateValidationTask()
                  individual.BuildEngine <- engine1
                  individual.LockFilePath <- lockPath
                  individual.OutputPath <- outDir1
                  individual.ModuleName <- "FcsFixture.GeneratedValidation"
                  individual.SourceFiles <- [| makeTaskItem srcPath |]
                  individual.AssemblyRefs <- refs |> List.map makeTaskItem |> Array.ofList
                  individual.VocabularyBinding <- "FcsFixture.AllVocab.registry"
                  let r1 = individual.Execute()
                  let errMsgs1 = collectErrors engine1
                  Expect.isTrue r1 $"Individual task must succeed; errors: {errMsgs1}"

                  let engine2 = StubBuildEngine()
                  let task = makeTask engine2 lockPath outDir2 srcPath refs
                  task.HasValidation <- true
                  let r2 = task.Execute()
                  let errMsgs2 = collectErrors engine2
                  Expect.isTrue r2 $"Consolidated task must succeed; errors: {errMsgs2}"

                  let golden = File.ReadAllText(Path.Combine(outDir1, "GeneratedValidation.fs"))
                  let actual = File.ReadAllText(Path.Combine(outDir2, "GeneratedValidation.fs"))
                  Expect.equal actual golden "GeneratedValidation.fs must be byte-identical")
          }

          test "FcsPassCount = 1 when HasValidation=true (no separate Extractor FCS call)" {
              withTempDir (fun dir ->
                  let outDir = Path.Combine(dir, "obj")
                  let engine = StubBuildEngine()
                  let lockPath = writeLockFile dir allEmittersLock
                  let srcPath = writeAllVocabSource dir
                  let refs = frankSemanticDll :: fsharpCoreDll :: sdkRefs ()

                  let task = makeTask engine lockPath outDir srcPath refs
                  task.HasValidation <- true

                  let result = task.Execute()
                  let errMsgs = collectErrors engine

                  Expect.isTrue result $"Execute must succeed; errors: {errMsgs}"
                  Expect.equal task.FcsPassCount 1 "Exactly one FCS call — Validation fold-in uses shared typecheck")
          } ]
