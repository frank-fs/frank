module Frank.Cli.MSBuild.Tests.GenerateValidationTaskTests

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

let private frankSemanticDll =
    typeof<Frank.Semantic.VocabularyRegistry>.Assembly.Location

let private fsharpCoreDll = typeof<Microsoft.FSharp.Core.Unit>.Assembly.Location

/// SDK ref assembly paths via FCS probe — required by extractTypeInfosFromSources
/// which uses --noframework mode. Same technique as ExtractorTests.sdkRefs().
let private sdkRefs () : string list =
    let checker = FSharp.Compiler.CodeAnalysis.FSharpChecker.Create()
    let src = FSharp.Compiler.Text.SourceText.ofString "let x = 1"

    let opts, _ =
        checker.GetProjectOptionsFromScript(
            "/tmp/frank_sdk_probe.fsx",
            src,
            assumeDotNetFramework = false,
            useSdkRefs = true
        )
        |> Async.RunSynchronously

    opts.OtherOptions
    |> Array.choose (fun o ->
        if o.StartsWith("-r:", System.StringComparison.Ordinal) then
            Some(o.[3..])
        else
            None)
    |> Array.toList

/// Write a .fs file that both defines a domain type AND binds the vocab registry.
/// The domain type (TicTacToe.MoveAction) matches the mappings in validationLock.
/// extractTypeInfosFromSources will discover it; evalRegistry will find the binding.
let private writeVocabAndDomainSource (dir: string) : string =
    let path = Path.Combine(dir, "VocabAndDomain.fs")

    let source =
        """namespace TicTacToe

open Frank.Semantic

type MoveAction =
    { position: int
      notes: string option }

module CliValidationVocab =

    let registry =
        vocabulary {
            prefix "schema" "https://schema.org/"
            using "schema"
            seeAlso typeof<MoveAction> "schema:MoveAction"
        }
"""

    File.WriteAllText(path, source)
    path

/// Lock file mapping TicTacToe.MoveAction → schema:MoveAction with one shaped field.
let private validationLock: LockFile =
    { confirmedLock with
        Vocabularies =
            Map.ofList
                [ "schema",
                  { Uri = "https://schema.org/"
                    FetchedAt = DateTimeOffset.Parse("2025-01-01T00:00:00Z")
                    Hash = "sha256:abc" } ]
        Mappings =
            [ { FSharpType = "TicTacToe.MoveAction"
                Iri = Some "schema:MoveAction"
                Confidence = 1.0
                Source = Convention
                Status = Confirmed
                Alternates = []
                Shape =
                  MappingShape.Record
                      [ { Name = "position"
                          Iri = Some "schema:position"
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
    (sourceFiles: string list)
    (assemblyRefs: string list)
    : GenerateValidationTask =
    let task = GenerateValidationTask()
    task.BuildEngine <- engine
    task.LockFilePath <- lockPath
    task.OutputPath <- outDir
    task.ModuleName <- "CliTest.GeneratedValidation"
    task.SourceFiles <- sourceFiles |> List.map makeTaskItem |> Array.ofList
    task.AssemblyRefs <- assemblyRefs |> List.map makeTaskItem |> Array.ofList
    task

// ── tests ─────────────────────────────────────────────────────────────────────

[<Tests>]
let validationTests =
    testList
        "GenerateValidationTask"
        [ test "end-to-end: evalRegistry + extractTypeInfos + ValidationEmitter writes GeneratedValidation.fs" {
              withTempDir (fun dir ->
                  let outDir = Path.Combine(dir, "obj")
                  let engine = StubBuildEngine()
                  let lockPath = writeLockFile dir validationLock
                  let srcPath = writeVocabAndDomainSource dir
                  let refs = frankSemanticDll :: fsharpCoreDll :: sdkRefs ()
                  let task = makeTask engine lockPath outDir [ srcPath ] refs
                  task.VocabularyBinding <- "TicTacToe.CliValidationVocab.registry"

                  let result = task.Execute()
                  let errMsgs = engine.Errors |> List.map (fun e -> e.Message) |> String.concat "; "

                  Expect.isTrue result ("Execute returned false; errors: " + errMsgs)
                  Expect.isEmpty engine.Errors "no errors logged"

                  let outFile = Path.Combine(outDir, "GeneratedValidation.fs")
                  Expect.isTrue (File.Exists outFile) "GeneratedValidation.fs written"

                  let source = File.ReadAllText outFile
                  Expect.stringContains source "RecordShape" "RecordShape DU case present"
                  Expect.stringContains source "Shapes.toShapesGraph" "interpreter call present"
                  Expect.isFalse (source.Contains "urn:frank:") "no urn:frank: in output"
                  Expect.stringContains source "Some XsdInteger" "int field produces XsdInteger datatype")
          }

          test "GeneratedFile output property set to written path" {
              withTempDir (fun dir ->
                  let outDir = Path.Combine(dir, "obj")
                  let engine = StubBuildEngine()
                  let lockPath = writeLockFile dir validationLock
                  let srcPath = writeVocabAndDomainSource dir
                  let refs = frankSemanticDll :: fsharpCoreDll :: sdkRefs ()
                  let task = makeTask engine lockPath outDir [ srcPath ] refs
                  task.VocabularyBinding <- "TicTacToe.CliValidationVocab.registry"
                  task.Execute() |> ignore
                  let expected = Path.Combine(outDir, "GeneratedValidation.fs")
                  Expect.equal task.GeneratedFile expected "GeneratedFile property matches written path")
          }

          test "missing lock file: Execute returns false, error logged" {
              withTempDir (fun dir ->
                  let engine = StubBuildEngine()
                  let srcPath = writeVocabAndDomainSource dir
                  let refs = frankSemanticDll :: fsharpCoreDll :: sdkRefs ()
                  let task = makeTask engine "/nonexistent/lock.json" dir [ srcPath ] refs
                  task.VocabularyBinding <- "TicTacToe.CliValidationVocab.registry"
                  let result = task.Execute()
                  Expect.isFalse result "Execute returns false on missing lock"
                  Expect.isNonEmpty engine.Errors "error logged for missing lock")
          }

          test "bad vocab source: Execute returns false, FCS diagnostic surfaced" {
              withTempDir (fun dir ->
                  let outDir = Path.Combine(dir, "obj")
                  let engine = StubBuildEngine()
                  let lockPath = writeLockFile dir validationLock
                  let badPath = Path.Combine(dir, "Bad.fs")
                  File.WriteAllText(badPath, "this is not valid fsharp !!!")
                  let refs = frankSemanticDll :: fsharpCoreDll :: sdkRefs ()
                  let task = makeTask engine lockPath outDir [ badPath ] refs
                  task.VocabularyBinding <- "TicTacToe.CliValidationVocab.registry"
                  let result = task.Execute()
                  Expect.isFalse result "Execute returns false on bad vocab source"
                  Expect.isNonEmpty engine.Errors "FCS diagnostic error logged")
          }

          test "no child processes spawned: no StartProcess member on GenerateValidationTask" {
              let taskType = typeof<GenerateValidationTask>

              let hasStartProcess =
                  taskType.GetMethods()
                  |> Array.exists (fun m -> m.Name.Contains("StartProcess") || m.Name.Contains("Process.Start"))

              Expect.isFalse hasStartProcess "no StartProcess member on GenerateValidationTask"
          }

          test "FCS compile gate: emitted GeneratedValidation.fs compiles against Frank.Semantic/Frank.Validation" {
              withTempDir (fun dir ->
                  let outDir = Path.Combine(dir, "obj")
                  let engine = StubBuildEngine()
                  let lockPath = writeLockFile dir validationLock
                  let srcPath = writeVocabAndDomainSource dir
                  let refs = frankSemanticDll :: fsharpCoreDll :: sdkRefs ()
                  let task = makeTask engine lockPath outDir [ srcPath ] refs
                  task.VocabularyBinding <- "TicTacToe.CliValidationVocab.registry"
                  let result = task.Execute()

                  Expect.isTrue result "task must succeed before compile gate runs"

                  let outFile = Path.Combine(outDir, "GeneratedValidation.fs")
                  let emittedSrc = File.ReadAllText outFile

                  let checker =
                      FSharp.Compiler.CodeAnalysis.FSharpChecker.Create(keepAssemblyContents = false)

                  let primaryText = FSharp.Compiler.Text.SourceText.ofString emittedSrc
                  let tmpFile = Path.Combine(outDir, "Generated.fs")
                  File.WriteAllText(tmpFile, emittedSrc)

                  let scriptOpts, _ =
                      checker.GetProjectOptionsFromScript(
                          tmpFile,
                          primaryText,
                          assumeDotNetFramework = false,
                          useSdkRefs = true
                      )
                      |> Async.RunSynchronously

                  let extraRefs =
                      [ typeof<Frank.Semantic.ShapeDecl>.Assembly
                        typeof<Frank.Validation.ValidationConfig>.Assembly
                        typeof<VDS.RDF.Shacl.ShapesGraph>.Assembly
                        typeof<VDS.RDF.IGraph>.Assembly ]
                      |> List.filter (fun a -> not (String.IsNullOrEmpty a.Location))
                      |> List.map (fun a -> $"-r:{a.Location}")
                      |> Array.ofList

                  let opts =
                      { scriptOpts with
                          SourceFiles = [| tmpFile |]
                          OtherOptions = Array.append scriptOpts.OtherOptions extraRefs }

                  let results = checker.ParseAndCheckProject(opts) |> Async.RunSynchronously

                  let errors =
                      results.Diagnostics
                      |> Array.filter (fun d ->
                          d.Severity = FSharp.Compiler.Diagnostics.FSharpDiagnosticSeverity.Error)
                      |> Array.map (fun d -> d.ToString())
                      |> Array.toList

                  Expect.isEmpty errors $"emitted GeneratedValidation.fs compiles cleanly; errors: {errors}")
          }

          test "FCS compile gate is non-vacuous: corrupted source produces errors" {
              withTempDir (fun dir ->
                  let outDir = Path.Combine(dir, "obj")
                  let engine = StubBuildEngine()
                  let lockPath = writeLockFile dir validationLock
                  let srcPath = writeVocabAndDomainSource dir
                  let refs = frankSemanticDll :: fsharpCoreDll :: sdkRefs ()
                  let task = makeTask engine lockPath outDir [ srcPath ] refs
                  task.VocabularyBinding <- "TicTacToe.CliValidationVocab.registry"
                  let result = task.Execute()
                  Expect.isTrue result "task must succeed to have emitted source"

                  let outFile = Path.Combine(outDir, "GeneratedValidation.fs")
                  let goodSrc = File.ReadAllText outFile

                  let corruptedSrc = goodSrc.Replace("Path =", "PathZZZ =")
                  Expect.isFalse (corruptedSrc = goodSrc) "corruption must change the source"

                  let checker =
                      FSharp.Compiler.CodeAnalysis.FSharpChecker.Create(keepAssemblyContents = false)

                  let corruptedText = FSharp.Compiler.Text.SourceText.ofString corruptedSrc
                  let tmpFile = Path.Combine(outDir, "Corrupted.fs")
                  File.WriteAllText(tmpFile, corruptedSrc)

                  let scriptOpts, _ =
                      checker.GetProjectOptionsFromScript(
                          tmpFile,
                          corruptedText,
                          assumeDotNetFramework = false,
                          useSdkRefs = true
                      )
                      |> Async.RunSynchronously

                  let extraRefs =
                      [ typeof<Frank.Semantic.ShapeDecl>.Assembly
                        typeof<Frank.Validation.ValidationConfig>.Assembly
                        typeof<VDS.RDF.Shacl.ShapesGraph>.Assembly
                        typeof<VDS.RDF.IGraph>.Assembly ]
                      |> List.filter (fun a -> not (String.IsNullOrEmpty a.Location))
                      |> List.map (fun a -> $"-r:{a.Location}")
                      |> Array.ofList

                  let opts =
                      { scriptOpts with
                          SourceFiles = [| tmpFile |]
                          OtherOptions = Array.append scriptOpts.OtherOptions extraRefs }

                  let results = checker.ParseAndCheckProject(opts) |> Async.RunSynchronously

                  let errors =
                      results.Diagnostics
                      |> Array.filter (fun d ->
                          d.Severity = FSharp.Compiler.Diagnostics.FSharpDiagnosticSeverity.Error)
                      |> Array.toList

                  Expect.isNonEmpty errors "gate must report errors on corrupted source (mutation proof)")
          }

          test "determinism: two runs produce byte-identical output" {
              withTempDir (fun dir ->
                  let outDir1 = Path.Combine(dir, "obj1")
                  let outDir2 = Path.Combine(dir, "obj2")
                  let lockPath = writeLockFile dir validationLock
                  let srcPath = writeVocabAndDomainSource dir
                  let refs = frankSemanticDll :: fsharpCoreDll :: sdkRefs ()

                  let run outDir =
                      let engine = StubBuildEngine()
                      let task = makeTask engine lockPath outDir [ srcPath ] refs
                      task.VocabularyBinding <- "TicTacToe.CliValidationVocab.registry"
                      task.Execute() |> ignore
                      File.ReadAllText(Path.Combine(outDir, "GeneratedValidation.fs"))

                  let a = run outDir1
                  let b = run outDir2
                  Expect.equal a b "two runs are byte-identical")
          } ]
