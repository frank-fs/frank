module Frank.Cli.MSBuild.Tests.GenerateProvenanceTaskTests

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

/// SDK ref assembly paths via FCS probe — required by VocabularyEvaluator in --noframework mode.
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

/// Write a .fs file that declares a domain type AND binds the vocab registry with provClass.
/// The domain type (TicTacToe.Agent) matches the mapping in provenanceLock.
let private writeVocabAndDomainSource (dir: string) : string =
    let path = Path.Combine(dir, "VocabAndDomain.fs")

    let source =
        """namespace TicTacToe

open Frank.Semantic

type Agent =
    { name: string }

module CliProvenanceVocab =

    let registry =
        vocabulary {
            prefix "prov" "http://www.w3.org/ns/prov#"
            using "prov"
            provClass typeof<Agent> Entity
        }
"""

    File.WriteAllText(path, source)
    path

/// Lock file with TicTacToe.Agent mapping so ResolvedModel.build produces a resource with ProvClass.
let private provenanceLock: LockFile =
    { confirmedLock with
        Vocabularies =
            Map.ofList
                [ "prov",
                  { Uri = "http://www.w3.org/ns/prov#"
                    FetchedAt = DateTimeOffset.Parse("2025-01-01T00:00:00Z")
                    Hash = "sha256:def" } ]
        Mappings =
            [ { FSharpType = "TicTacToe.Agent"
                Iri = Some "prov:Agent"
                Confidence = 1.0
                Source = Convention
                Status = Confirmed
                Alternates = []
                Shape = MappingShape.Record [] } ] }

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

/// Vocab source where OrderPlaced has BOTH provClass Activity AND a schema:OrderAction class mapping.
/// The lock (orderActionLock) maps TicTacToe.OrderPlaced → schema:OrderAction, giving ClassIri.
/// The provClass entry gives ProvClass = Activity.
/// The join in ProvenanceEmitter.entryExpr produces ("TicTacToe.OrderPlaced", ("Activity", "https://schema.org/OrderAction")).
let private writeOrderActionVocabSource (dir: string) : string =
    let path = Path.Combine(dir, "OrderActionVocab.fs")

    let source =
        """namespace TicTacToe

open Frank.Semantic

type OrderPlaced =
    { orderId: string }

module OrderActionVocab =

    let registry =
        vocabulary {
            prefix "schema" "https://schema.org/"
            using "schema"
            provClass typeof<OrderPlaced> Activity
        }
"""

    File.WriteAllText(path, source)
    path

/// Lock file with TicTacToe.OrderPlaced → schema:OrderAction.
/// Combined with provClass Activity in the vocab CE, the emitter must produce
/// ("TicTacToe.OrderPlaced", ("Activity", "https://schema.org/OrderAction")).
let private orderActionLock: LockFile =
    { confirmedLock with
        Vocabularies =
            Map.ofList
                [ "schema",
                  { Uri = "https://schema.org/"
                    FetchedAt = DateTimeOffset.Parse("2025-01-01T00:00:00Z")
                    Hash = "sha256:abc" } ]
        Mappings =
            [ { FSharpType = "TicTacToe.OrderPlaced"
                Iri = Some "schema:OrderAction"
                Confidence = 1.0
                Source = Convention
                Status = Confirmed
                Alternates = []
                Shape = MappingShape.Record [] } ] }

let private makeTask
    (engine: StubBuildEngine)
    (lockPath: string)
    (outDir: string)
    (sourceFiles: string list)
    (assemblyRefs: string list)
    : GenerateProvenanceTask =
    let task = GenerateProvenanceTask()
    task.BuildEngine <- engine
    task.LockFilePath <- lockPath
    task.OutputPath <- outDir
    task.ModuleName <- "CliTest.GeneratedProvenance"
    task.SourceFiles <- sourceFiles |> List.map makeTaskItem |> Array.ofList
    task.AssemblyRefs <- assemblyRefs |> List.map makeTaskItem |> Array.ofList
    task

// ── tests ─────────────────────────────────────────────────────────────────────

[<Tests>]
let provenanceTests =
    testList
        "GenerateProvenanceTask"
        [ test "end-to-end: evalRegistry + ProvenanceEmitter writes GeneratedProvenance.fs" {
              withTempDir (fun dir ->
                  let outDir = Path.Combine(dir, "obj")
                  let engine = StubBuildEngine()
                  let lockPath = writeLockFile dir provenanceLock
                  let srcPath = writeVocabAndDomainSource dir
                  let refs = frankSemanticDll :: fsharpCoreDll :: sdkRefs ()
                  let task = makeTask engine lockPath outDir [ srcPath ] refs
                  task.VocabularyBinding <- "TicTacToe.CliProvenanceVocab.registry"

                  let result = task.Execute()
                  let errMsgs = engine.Errors |> List.map (fun e -> e.Message) |> String.concat "; "

                  Expect.isTrue result ("Execute returned false; errors: " + errMsgs)
                  Expect.isEmpty engine.Errors "no errors logged"

                  let outFile = Path.Combine(outDir, "GeneratedProvenance.fs")
                  Expect.isTrue (File.Exists outFile) "GeneratedProvenance.fs written"

                  let source = File.ReadAllText outFile
                  Expect.stringContains source "module GeneratedProvenance" "module header present"
                  Expect.stringContains source "provClasses" "provClasses value present")
          }

          test "GeneratedFile output property set to written path" {
              withTempDir (fun dir ->
                  let outDir = Path.Combine(dir, "obj")
                  let engine = StubBuildEngine()
                  let lockPath = writeLockFile dir provenanceLock
                  let srcPath = writeVocabAndDomainSource dir
                  let refs = frankSemanticDll :: fsharpCoreDll :: sdkRefs ()
                  let task = makeTask engine lockPath outDir [ srcPath ] refs
                  task.VocabularyBinding <- "TicTacToe.CliProvenanceVocab.registry"
                  task.Execute() |> ignore
                  let expected = Path.Combine(outDir, "GeneratedProvenance.fs")
                  Expect.equal task.GeneratedFile expected "GeneratedFile property matches written path")
          }

          test "missing lock file: Execute returns false, error logged" {
              withTempDir (fun dir ->
                  let engine = StubBuildEngine()
                  let srcPath = writeVocabAndDomainSource dir
                  let refs = frankSemanticDll :: fsharpCoreDll :: sdkRefs ()
                  let task = makeTask engine "/nonexistent/lock.json" dir [ srcPath ] refs
                  task.VocabularyBinding <- "TicTacToe.CliProvenanceVocab.registry"
                  let result = task.Execute()
                  Expect.isFalse result "Execute returns false on missing lock"
                  Expect.isNonEmpty engine.Errors "error logged for missing lock")
          }

          test "bad vocab source: Execute returns false, FCS diagnostic surfaced" {
              withTempDir (fun dir ->
                  let outDir = Path.Combine(dir, "obj")
                  let engine = StubBuildEngine()
                  let lockPath = writeLockFile dir provenanceLock
                  let badPath = Path.Combine(dir, "Bad.fs")
                  File.WriteAllText(badPath, "this is not valid fsharp !!!")
                  let refs = frankSemanticDll :: fsharpCoreDll :: sdkRefs ()
                  let task = makeTask engine lockPath outDir [ badPath ] refs
                  task.VocabularyBinding <- "TicTacToe.CliProvenanceVocab.registry"
                  let result = task.Execute()
                  Expect.isFalse result "Execute returns false on bad vocab source"
                  Expect.isNonEmpty engine.Errors "FCS diagnostic error logged")
          }

          test "no child processes spawned: no StartProcess member on GenerateProvenanceTask" {
              let taskType = typeof<GenerateProvenanceTask>

              let hasStartProcess =
                  taskType.GetMethods()
                  |> Array.exists (fun m -> m.Name.Contains("StartProcess") || m.Name.Contains("Process.Start"))

              Expect.isFalse hasStartProcess "no StartProcess member on GenerateProvenanceTask"
          }

          test "determinism: two runs produce byte-identical output" {
              withTempDir (fun dir ->
                  let outDir1 = Path.Combine(dir, "obj1")
                  let outDir2 = Path.Combine(dir, "obj2")
                  let lockPath = writeLockFile dir provenanceLock
                  let srcPath = writeVocabAndDomainSource dir
                  let refs = frankSemanticDll :: fsharpCoreDll :: sdkRefs ()

                  let run outDir =
                      let engine = StubBuildEngine()
                      let task = makeTask engine lockPath outDir [ srcPath ] refs
                      task.VocabularyBinding <- "TicTacToe.CliProvenanceVocab.registry"
                      task.Execute() |> ignore
                      File.ReadAllText(Path.Combine(outDir, "GeneratedProvenance.fs"))

                  let a = run outDir1
                  let b = run outDir2
                  Expect.equal a b "two runs are byte-identical")
          }

          test "generates exact (Activity, schema IRI) entry for a provClass+classmapped type" {
              withTempDir (fun dir ->
                  let outDir = Path.Combine(dir, "obj")
                  let engine = StubBuildEngine()
                  let lockPath = writeLockFile dir orderActionLock
                  let srcPath = writeOrderActionVocabSource dir
                  let refs = frankSemanticDll :: fsharpCoreDll :: sdkRefs ()
                  let task = makeTask engine lockPath outDir [ srcPath ] refs
                  task.VocabularyBinding <- "TicTacToe.OrderActionVocab.registry"

                  let result = task.Execute()
                  let errMsgs = engine.Errors |> List.map (fun e -> e.Message) |> String.concat "; "

                  Expect.isTrue result ("Execute returned false; errors: " + errMsgs)
                  Expect.isEmpty engine.Errors "no errors logged"

                  let outFile = Path.Combine(outDir, "GeneratedProvenance.fs")
                  let source = File.ReadAllText outFile

                  Expect.stringContains source "TicTacToe.OrderPlaced" "type FullName in provClasses"
                  Expect.stringContains source "Activity" "ProvOClass Activity in provClasses"
                  Expect.stringContains source "https://schema.org/OrderAction" "resolved schema IRI in provClasses"

                  Expect.stringContains
                      source
                      "(\"Activity\", \"https://schema.org/OrderAction\")"
                      "exact (Activity, schema IRI) tuple present — proves the ClassIri join")
          } ]
