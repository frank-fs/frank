module Frank.Cli.MSBuild.Tests.ValidateLockFileTaskTests

open System
open System.IO
open Expecto
open Frank.Cli.MSBuild
open Frank.Cli.MSBuild.Tests.Fixtures
open Frank.Cli.MSBuild.Tests.StubBuildEngine
open Frank.Semantic
open Frank.Semantic.LockFile

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
                  Expect.stringContains msg "2 undecided" "count includes mapping + field")
          }

          test "AT5 excluded mapping passes: Execute returns true, no MS001" {
              withTempDir (fun dir ->
                  let engine = StubBuildEngine()

                  let excludedLock: LockFile =
                      { confirmedLock with
                          Mappings =
                              confirmedLock.Mappings
                              @ [ { FSharpType = "TicTacToe.Internal"
                                    Iri = None
                                    Confidence = 0.0
                                    Source = Convention
                                    Status = Excluded
                                    Alternates = []
                                    Shape = MappingShape.Record [] } ] }

                  let lockPath = writeLockFile dir excludedLock
                  let task = makeTask engine lockPath
                  let result = task.Execute()
                  Expect.isTrue result "Execute should return true"
                  Expect.isEmpty engine.Errors "no errors should be logged")
          }

          test "AT5b excluded mapping with proposed field passes: Execute returns true" {
              withTempDir (fun dir ->
                  let engine = StubBuildEngine()

                  let excludedWithProposedField: LockFile =
                      { confirmedLock with
                          Mappings =
                              confirmedLock.Mappings
                              @ [ { FSharpType = "TicTacToe.Internal"
                                    Iri = None
                                    Confidence = 0.0
                                    Source = Convention
                                    Status = Excluded
                                    Alternates = []
                                    Shape =
                                      MappingShape.Record
                                          [ { Name = "privateField"
                                              Iri = None
                                              Confidence = 0.3
                                              Source = Llm
                                              Status = Proposed } ] } ] }

                  let lockPath = writeLockFile dir excludedWithProposedField
                  let task = makeTask engine lockPath
                  let result = task.Execute()
                  Expect.isTrue result "Execute should return true"
                  Expect.isEmpty engine.Errors "fields of excluded mappings are ignored")
          }

          test "AT4 proposed mapping fails: Execute returns false, MS001 logged" {
              withTempDir (fun dir ->
                  let engine = StubBuildEngine()

                  let proposedMappingLock: LockFile =
                      { confirmedLock with
                          Mappings =
                              [ { FSharpType = "TicTacToe.Draft"
                                  Iri = Some "schema:Thing"
                                  Confidence = 0.6
                                  Source = Llm
                                  Status = Proposed
                                  Alternates = []
                                  Shape = MappingShape.Record [] } ] }

                  let lockPath = writeLockFile dir proposedMappingLock
                  let task = makeTask engine lockPath
                  let result = task.Execute()
                  Expect.isFalse result "Execute should return false"
                  Expect.contains engine.ErrorCodes "MS001" "MS001 error code present")
          }

          test "AT4b unresolved mapping fails: Execute returns false, MS001 logged" {
              withTempDir (fun dir ->
                  let engine = StubBuildEngine()

                  let unresolvedMappingLock: LockFile =
                      { confirmedLock with
                          Mappings =
                              [ { FSharpType = "TicTacToe.Ambiguous"
                                  Iri = None
                                  Confidence = 0.0
                                  Source = Convention
                                  Status = Unresolved
                                  Alternates = []
                                  Shape = MappingShape.Record [] } ] }

                  let lockPath = writeLockFile dir unresolvedMappingLock
                  let task = makeTask engine lockPath
                  let result = task.Execute()
                  Expect.isFalse result "Execute should return false"
                  Expect.contains engine.ErrorCodes "MS001" "MS001 error code present")
          }

          test "confirmed mapping with proposed field fails: Execute returns false, MS001 logged" {
              withTempDir (fun dir ->
                  let engine = StubBuildEngine()

                  let confirmedWithProposedField: LockFile =
                      { confirmedLock with
                          Mappings =
                              [ { FSharpType = "TicTacToe.Game"
                                  Iri = Some "schema:Game"
                                  Confidence = 1.0
                                  Source = Convention
                                  Status = Confirmed
                                  Alternates = []
                                  Shape =
                                    MappingShape.Record
                                        [ { Name = "pendingField"
                                            Iri = None
                                            Confidence = 0.4
                                            Source = Llm
                                            Status = Proposed } ] } ] }

                  let lockPath = writeLockFile dir confirmedWithProposedField
                  let task = makeTask engine lockPath
                  let result = task.Execute()
                  Expect.isFalse result "Execute should return false"
                  Expect.contains engine.ErrorCodes "MS001" "MS001 error code present")
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
