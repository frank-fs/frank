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
        if o.StartsWith("-r:", StringComparison.Ordinal) then
            Some(o.[3..])
        else
            None)
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

          test "FcsPassCount = 1 when no HasX flags (Semantics emits unconditionally, A2 #386)" {
              withTempDir (fun dir ->
                  let outDir = Path.Combine(dir, "obj")
                  let engine = StubBuildEngine()
                  let lockPath = writeLockFile dir allEmittersLock
                  let srcPath = writeAllVocabSource dir
                  let refs = frankSemanticDll :: fsharpCoreDll :: sdkRefs ()

                  let task = makeTask engine lockPath outDir srcPath refs

                  let result = task.Execute()
                  let errMsgs = collectErrors engine

                  Expect.isTrue result $"Execute returns true (Semantics unconditional); errors: {errMsgs}"
                  Expect.equal task.FcsPassCount 1 "One FCS call even with all HasX=false (Semantics emits unconditionally)"

                  Expect.isTrue
                      (File.Exists(Path.Combine(outDir, "GeneratedSemantics.fs")))
                      "GeneratedSemantics.fs written even with no HasX flags (lock-only, A2)")
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

/// Data specification for a single byte-identity cross-check.
type private ByIdCase =
    { FileName: string
      RunIndividual: StubBuildEngine -> string -> string -> string -> string list -> bool
      SetConsolidatedFlag: GenerateFcsEmittersTask -> unit }

let private runByteIdentityTest (c: ByIdCase) =
    withTempDir (fun dir ->
        let outDir1 = Path.Combine(dir, "individual")
        let outDir2 = Path.Combine(dir, "consolidated")
        let lockPath = writeLockFile dir allEmittersLock
        let srcPath = writeAllVocabSource dir
        let refs = frankSemanticDll :: fsharpCoreDll :: sdkRefs ()

        let engine1 = StubBuildEngine()
        let r1 = c.RunIndividual engine1 lockPath outDir1 srcPath refs
        let errMsgs1 = collectErrors engine1
        Expect.isTrue r1 $"Individual task must succeed; errors: {errMsgs1}"

        let engine2 = StubBuildEngine()
        let task = makeTask engine2 lockPath outDir2 srcPath refs
        c.SetConsolidatedFlag task
        let r2 = task.Execute()
        let errMsgs2 = collectErrors engine2
        Expect.isTrue r2 $"Consolidated task must succeed; errors: {errMsgs2}"

        let golden = File.ReadAllText(Path.Combine(outDir1, c.FileName))
        let actual = File.ReadAllText(Path.Combine(outDir2, c.FileName))
        Expect.equal actual golden $"{c.FileName} must be byte-identical to individual task output")

let private byIdCases: ByIdCase list =
    [ { FileName = "GeneratedLinkedData.fs"
        RunIndividual =
            fun engine lockPath outDir srcPath refs ->
                let t = GenerateLinkedDataTask()
                t.BuildEngine <- engine
                t.LockFilePath <- lockPath
                t.OutputPath <- outDir
                t.ModuleName <- "FcsFixture.GeneratedLinkedData"
                t.SourceFiles <- [| makeTaskItem srcPath |]
                t.AssemblyRefs <- refs |> List.map makeTaskItem |> Array.ofList
                t.VocabularyBinding <- "FcsFixture.AllVocab.registry"
                t.Execute()
        SetConsolidatedFlag = fun t -> t.HasLinkedData <- true }
      { FileName = "GeneratedSemantics.fs"
        RunIndividual =
            fun engine lockPath outDir srcPath refs ->
                let t = GenerateSemanticsTask()
                t.BuildEngine <- engine
                t.LockFilePath <- lockPath
                t.OutputPath <- outDir
                t.ModuleName <- "FcsFixture.GeneratedSemantics"
                t.SourceFiles <- [| makeTaskItem srcPath |]
                t.AssemblyRefs <- refs |> List.map makeTaskItem |> Array.ofList
                t.VocabularyBinding <- "FcsFixture.AllVocab.registry"
                t.Execute()
        // Semantics emits unconditionally in the consolidated task; no flag needed.
        SetConsolidatedFlag = fun _ -> () }
      { FileName = "GeneratedProvenance.fs"
        RunIndividual =
            fun engine lockPath outDir srcPath refs ->
                let t = GenerateProvenanceTask()
                t.BuildEngine <- engine
                t.LockFilePath <- lockPath
                t.OutputPath <- outDir
                t.ModuleName <- "FcsFixture.GeneratedProvenance"
                t.SourceFiles <- [| makeTaskItem srcPath |]
                t.AssemblyRefs <- refs |> List.map makeTaskItem |> Array.ofList
                t.VocabularyBinding <- "FcsFixture.AllVocab.registry"
                t.Execute()
        SetConsolidatedFlag = fun t -> t.HasProvenance <- true } ]

[<Tests>]
let byteIdenticalTests =
    testList
        "GenerateFcsEmittersTask — AC2 byte-identical output vs individual tasks (#386)"
        (byIdCases
         |> List.map (fun c ->
             test $"{c.FileName} byte-identical to individual task output" { runByteIdentityTest c }))

// ── AC3: per-package gating ───────────────────────────────────────────────────

[<Tests>]
let gatingTests =
    testList
        "GenerateFcsEmittersTask — AC3 per-package gating (#386)"
        [ test "HasLinkedData=true: GeneratedLinkedData.fs + GeneratedSemantics.fs written; others skipped" {
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

                  Expect.isTrue
                      (File.Exists(Path.Combine(outDir, "GeneratedSemantics.fs")))
                      "GeneratedSemantics.fs ALWAYS written (unconditional, A2)"

                  Expect.isFalse
                      (File.Exists(Path.Combine(outDir, "GeneratedValidation.fs")))
                      "GeneratedValidation.fs NOT written when HasValidation=false"

                  Expect.isFalse
                      (File.Exists(Path.Combine(outDir, "GeneratedProvenance.fs")))
                      "GeneratedProvenance.fs NOT written when HasProvenance=false")
          }

          test "no HasX flags: only GeneratedSemantics.fs written (unconditional, A2)" {
              withTempDir (fun dir ->
                  let outDir = Path.Combine(dir, "obj")
                  let engine = StubBuildEngine()
                  let lockPath = writeLockFile dir allEmittersLock
                  let srcPath = writeAllVocabSource dir
                  let refs = frankSemanticDll :: fsharpCoreDll :: sdkRefs ()

                  let task = makeTask engine lockPath outDir srcPath refs

                  let result = task.Execute()
                  Expect.isTrue result "Execute must succeed"

                  Expect.isTrue
                      (File.Exists(Path.Combine(outDir, "GeneratedSemantics.fs")))
                      "GeneratedSemantics.fs written even with no HasX flags (lock-only, A2)"

                  Expect.isFalse
                      (File.Exists(Path.Combine(outDir, "GeneratedLinkedData.fs")))
                      "GeneratedLinkedData.fs NOT written when HasLinkedData=false"

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
