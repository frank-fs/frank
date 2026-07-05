module Frank.Cli.MSBuild.Tests.ValidateLockFileTaskTests

open System
open System.IO
open Expecto
open Frank.Cli.MSBuild
open Frank.Cli.MSBuild.Tests.Fixtures
open Frank.Cli.MSBuild.Tests.StubBuildEngine
open Frank.Semantic
open Frank.Semantic.LockFile
open Frank.TestSupport.TempDir

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
                                    Rt = None
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
                                    Rt = None
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
                                  Rt = None
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
                                  Rt = None
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
                                  Rt = None
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
          }

          test "AT3 - stamped lock with valid integrity: Execute returns true, zero integrity warnings" {
              withTempDir (fun dir ->
                  let engine = StubBuildEngine()
                  let stamped = LockFile.withIntegrity confirmedLock
                  let lockPath = writeLockFile dir stamped
                  let task = makeTask engine lockPath
                  let result = task.Execute()
                  Expect.isTrue result "Execute should return true"
                  Expect.isEmpty engine.Errors "no errors"
                  Expect.isEmpty engine.Warnings "no integrity warnings for valid stamped lock")
          }

          test "AT4 - hand-edited IRI changes hash: exactly one FRANKSEM-INTEGRITY warning, build proceeds" {
              withTempDir (fun dir ->
                  let engine = StubBuildEngine()
                  let stamped = LockFile.withIntegrity confirmedLock
                  // Tamper with one IRI after stamping to simulate hand-edit
                  let tampered =
                      { stamped with
                          Mappings =
                              stamped.Mappings
                              |> List.map (fun m -> { m with Iri = Some "schema:CreativeWork" }) }

                  let lockPath = writeLockFile dir tampered
                  let task = makeTask engine lockPath
                  let result = task.Execute()
                  Expect.isTrue result "Execute should return true (build proceeds)"
                  Expect.isEmpty engine.Errors "no MS001 error (all mappings are confirmed)"
                  Expect.hasLength engine.Warnings 1 "exactly one FRANKSEM-INTEGRITY warning"
                  Expect.contains engine.WarningCodes "FRANKSEM-INTEGRITY" "warning code is FRANKSEM-INTEGRITY"
                  Expect.equal
                      engine.WarningMessages.[0]
                      "lock appears hand-edited; regenerate"
                      "warning message must be exact text")
          }

          test "AT4b - canonical-equivalent reformat (CRLF LF reindent): no integrity warning" {
              withTempDir (fun dir ->
                  let engine = StubBuildEngine()
                  let stamped = LockFile.withIntegrity confirmedLock
                  let lockPath = writeLockFile dir stamped
                  // Read the raw JSON, replace LF with CRLF, write back — canonical form strips whitespace
                  let raw = File.ReadAllText(lockPath)
                  let reformatted = raw.Replace("\n", "\r\n")
                  File.WriteAllText(lockPath, reformatted)
                  let task = makeTask engine lockPath
                  let result = task.Execute()
                  Expect.isTrue result "Execute should return true"
                  Expect.isEmpty engine.Warnings "CRLF reformat must not trigger integrity warning")
          }

          test "AT7 - Integrity=None: warning 'lock is unstamped; regenerate', build proceeds" {
              withTempDir (fun dir ->
                  let engine = StubBuildEngine()
                  let unstamped = { confirmedLock with Integrity = None }
                  let lockPath = writeLockFile dir unstamped
                  let task = makeTask engine lockPath
                  let result = task.Execute()
                  Expect.isTrue result "Execute should return true (build proceeds)"
                  Expect.isEmpty engine.Errors "no MS001 error"
                  Expect.isNonEmpty engine.Warnings "at least one warning for unstamped lock"
                  Expect.contains engine.WarningCodes "FRANKSEM-INTEGRITY" "warning code is FRANKSEM-INTEGRITY"
                  Expect.equal
                      engine.WarningMessages.[0]
                      "lock is unstamped; regenerate"
                      "warning message must be exact text")
          } ]
