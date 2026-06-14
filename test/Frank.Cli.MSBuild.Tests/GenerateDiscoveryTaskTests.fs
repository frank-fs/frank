module Frank.Cli.MSBuild.Tests.GenerateDiscoveryTaskTests

open System.IO
open Expecto
open Frank.Cli.MSBuild
open Frank.Cli.MSBuild.Tests.Fixtures
open Frank.Cli.MSBuild.Tests.StubBuildEngine

let private withTempDir (f: string -> unit) =
    let dir = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName())
    Directory.CreateDirectory dir |> ignore

    try
        f dir
    finally
        Directory.Delete(dir, recursive = true)

let private makeTask (engine: StubBuildEngine) (lockPath: string) (outDir: string) : GenerateDiscoveryTask =
    let task = GenerateDiscoveryTask()
    task.BuildEngine <- engine
    task.LockFilePath <- lockPath
    task.OutputPath <- outDir
    task.ModuleName <- "TicTacToe.GeneratedDiscovery"
    task.ProfileUri <- "/alps/tictactoe"
    task

[<Tests>]
let generateTests =
    testList
        "GenerateDiscoveryTask"
        [ test "confirmed TicTacToe lock: Execute returns true, GeneratedDiscovery.fs written" {
              withTempDir (fun dir ->
                  let outDir = Path.Combine(dir, "obj")
                  let engine = StubBuildEngine()
                  let lockPath = writeLockFile dir confirmedLock
                  let task = makeTask engine lockPath outDir
                  let result = task.Execute()
                  Expect.isTrue result "Execute should return true"
                  Expect.isEmpty engine.Errors "no errors logged"
                  let expectedPath = Path.Combine(outDir, "GeneratedDiscovery.fs")
                  Expect.isTrue (File.Exists expectedPath) "GeneratedDiscovery.fs written"
                  Expect.equal task.GeneratedFile expectedPath "GeneratedFile output property set")
          }

          test "generated file contains https://schema.org/Game" {
              withTempDir (fun dir ->
                  let outDir = Path.Combine(dir, "obj")
                  let engine = StubBuildEngine()
                  let lockPath = writeLockFile dir confirmedLock
                  let task = makeTask engine lockPath outDir
                  task.Execute() |> ignore
                  let source = File.ReadAllText(Path.Combine(outDir, "GeneratedDiscovery.fs"))
                  Expect.stringContains source "https://schema.org/Game" "schema.org/Game IRI present")
          }

          test "generated file does not contain urn:frank:" {
              withTempDir (fun dir ->
                  let outDir = Path.Combine(dir, "obj")
                  let engine = StubBuildEngine()
                  let lockPath = writeLockFile dir confirmedLock
                  let task = makeTask engine lockPath outDir
                  task.Execute() |> ignore
                  let source = File.ReadAllText(Path.Combine(outDir, "GeneratedDiscovery.fs"))
                  Expect.isFalse (source.Contains("urn:frank:")) "no urn:frank: in output")
          }

          test "GeneratedFile output property is set to the written path" {
              withTempDir (fun dir ->
                  let outDir = Path.Combine(dir, "obj")
                  let engine = StubBuildEngine()
                  let lockPath = writeLockFile dir confirmedLock
                  let task = makeTask engine lockPath outDir
                  task.Execute() |> ignore
                  Expect.isNonEmpty task.GeneratedFile "GeneratedFile must be set"
                  Expect.isTrue (task.GeneratedFile.EndsWith "GeneratedDiscovery.fs") "filename correct")
          }

          test "missing lock file: Execute returns false, error logged" {
              withTempDir (fun dir ->
                  let engine = StubBuildEngine()
                  let task = makeTask engine "/nonexistent/lock.json" dir
                  let result = task.Execute()
                  Expect.isFalse result "Execute returns false"
                  Expect.isNonEmpty engine.Errors "error logged")
          }

          test "proposed lock: DiscoveryEmitter still succeeds (validate is separate task)" {
              withTempDir (fun dir ->
                  let outDir = Path.Combine(dir, "obj")
                  let engine = StubBuildEngine()
                  let lockPath = writeLockFile dir proposedLock
                  let task = makeTask engine lockPath outDir
                  let result = task.Execute()
                  Expect.isTrue result "GenerateDiscovery succeeds with proposed lock (validation is separate)")
          } ]
