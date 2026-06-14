module Frank.Cli.MSBuild.Tests.ValidateLockFileTaskTests

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

let private makeTask (engine: StubBuildEngine) (lockPath: string) : ValidateLockFileTask =
    let task = ValidateLockFileTask()
    task.BuildEngine <- engine
    task.LockFilePath <- lockPath
    task

[<Tests>]
let validateTests =
    testList
        "ValidateLockFileTask"
        [ test "all-confirmed lock: Execute returns true, no errors logged" {
              withTempDir (fun dir ->
                  let engine = StubBuildEngine()
                  let lockPath = writeLockFile dir confirmedLock
                  let task = makeTask engine lockPath
                  let result = task.Execute()
                  Expect.isTrue result "Execute should return true"
                  Expect.isEmpty engine.Errors "no errors should be logged")
          }

          test "lock with proposed entry: Execute returns false, MS001 logged" {
              withTempDir (fun dir ->
                  let engine = StubBuildEngine()
                  let lockPath = writeLockFile dir proposedLock
                  let task = makeTask engine lockPath
                  let result = task.Execute()
                  Expect.isFalse result "Execute should return false"
                  Expect.isNonEmpty engine.Errors "at least one error logged"
                  Expect.contains engine.ErrorCodes "MS001" "MS001 error code present"

                  let msg = engine.Errors |> List.map (fun e -> e.Message) |> String.concat ""
                  Expect.stringContains msg "proposed/unresolved" "error mentions proposed/unresolved")
          }

          test "lock with proposed mapping and unresolved field: count covers both" {
              withTempDir (fun dir ->
                  let engine = StubBuildEngine()
                  let lockPath = writeLockFile dir proposedLock
                  let task = makeTask engine lockPath
                  let result = task.Execute()
                  Expect.isFalse result "Execute should return false"
                  let msg = engine.Errors |> List.map (fun e -> e.Message) |> String.concat ""
                  Expect.stringContains msg "2 proposed/unresolved" "count includes mapping + field")
          }

          test "missing lock file: Execute returns false, error logged" {
              let engine = StubBuildEngine()
              let task = makeTask engine "/nonexistent/path/lock.json"
              let result = task.Execute()
              Expect.isFalse result "Execute should return false for missing file"
              Expect.isNonEmpty engine.Errors "error logged for missing file"
          }

          test "garbage JSON lock file: Execute returns false, error logged" {
              withTempDir (fun dir ->
                  let engine = StubBuildEngine()
                  let garbagePath = Path.Combine(dir, "bad.lock.json")
                  File.WriteAllText(garbagePath, "not json {{{ }")
                  let task = makeTask engine garbagePath
                  let result = task.Execute()
                  Expect.isFalse result "Execute should return false for bad JSON"
                  Expect.isNonEmpty engine.Errors "error logged for bad JSON")
          } ]
