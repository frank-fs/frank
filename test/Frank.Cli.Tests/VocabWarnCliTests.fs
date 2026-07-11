module Frank.Cli.Tests.VocabWarnCliTests

open System
open System.Diagnostics
open System.IO
open System.Text
open System.Text.Json
open System.Threading.Tasks
open Expecto
open Frank.Semantic
open Frank.Semantic.LockFile
open Frank.TestSupport.TempDir

// ── Helpers ───────────────────────────────────────────────────────────────────

let private frankCliDll: string =
    let testDir =
        Path.GetDirectoryName(Reflection.Assembly.GetExecutingAssembly().Location)

    Path.Combine(testDir, "Frank.Cli.dll")

let private runCli (args: string[]) (capMs: int) : int * string * string =
    let argStr = String.concat " " (args |> Array.map (fun a -> $"\"{a}\""))
    let psi = ProcessStartInfo("dotnet", $"\"{frankCliDll}\" {argStr}")
    psi.RedirectStandardOutput <- true
    psi.RedirectStandardError <- true
    psi.UseShellExecute <- false
    psi.Environment.["DOTNET_SYSTEM_GLOBALIZATION_INVARIANT"] <- "1"

    use proc = new Process()
    proc.StartInfo <- psi
    proc.Start() |> ignore

    let stdoutTask = proc.StandardOutput.ReadToEndAsync()
    let stderrTask = proc.StandardError.ReadToEndAsync()
    let allDone = Task.WhenAll([| stdoutTask :> Task; stderrTask :> Task |])
    let finished = allDone.Wait(capMs)

    if not finished then
        try
            proc.Kill()
        with _ ->
            ()

        invalidOp $"frank CLI did not complete within cap of {capMs}ms"

    proc.WaitForExit()
    proc.ExitCode, stdoutTask.Result, stderrTask.Result

// Build AT1-style fixture: ttt declared but not in Vocabularies → Undereferenceable
let private confirmedSchemaEntry =
    { v1Empty with
        Uri = "https://schema.org/"
        Validated =
            { IsValidated = true
              Reason = None
              LastChecked = None }
        FetchedAt = DateTimeOffset.UnixEpoch
        Hash = "sha256:abc" }

let private at1Lock: LockFile =
    { SchemaVersion = 2
      Generated = DateTimeOffset.UtcNow
      Integrity = None
      Vocabularies = Map.ofList [ "schema", confirmedSchemaEntry ]
      DeclaredPrefixes =
        Map.ofList
            [ "schema", "https://schema.org/"
              "ttt", "https://example.org/tictactoe#" ]
      Mappings =
        [ { FSharpType = "App.MoveRequest"
            Iri = None
            Confidence = 0.0
            Source = Convention
            Status = Unresolved
            Alternates = []
            Rt = None
            Shape =
              MappingShape.Record
                  [ { Name = "Position"
                      Iri = None
                      Confidence = 0.0
                      Source = Convention
                      Status = Unresolved } ] } ] }

// Fixture variant: field Position has a full ttt-namespace IRI in the lock.
// Proves status --format json populates type/field when the lock records a reference.
let private at1LockWithRef: LockFile =
    { at1Lock with
        Mappings =
            [ { FSharpType = "App.MoveRequest"
                Iri = None
                Confidence = 0.0
                Source = Convention
                Status = Unresolved
                Alternates = []
                Rt = None
                Shape =
                    MappingShape.Record
                        [ { Name = "Position"
                            Iri = Some "https://example.org/tictactoe#square"
                            Confidence = 0.0
                            Source = Convention
                            Status = Unresolved } ] } ] }

// resolved.json uses ttt: prefix so collectVocabWarnings encounters it
let private at1ResolvedJson =
    """{ "schemaVersion": 1, "resolved": [
         { "fsharpType": "App.MoveRequest", "iri": "schema:MoveAction", "shape": "record",
           "fields": [ { "name": "Position", "iri": "ttt:square" } ] } ] }"""

// ── Tests ─────────────────────────────────────────────────────────────────────

[<Tests>]
let vocabWarnCliTests =
    testList
        "VocabWarn CLI AT9–AT10"
        [ // AT9: --strict promotes exit code to 3; default stays 0
          testCase "AT9: accept without --strict → exit 0, warning still printed"
          <| fun () ->
              withTempDir (fun dir ->
                  let lockPath = Path.Combine(dir, "lock.json")
                  let resolvedPath = Path.Combine(dir, "resolved.json")
                  LockFile.write lockPath at1Lock
                  File.WriteAllText(resolvedPath, at1ResolvedJson, Encoding.UTF8)

                  let exitCode, _, stderr =
                      runCli
                          [| "semantic"; "accept"; "--input"; resolvedPath; "--lock-file"; lockPath |]
                          15000

                  Expect.equal exitCode 0 "AT9: no --strict → exit 0"
                  Expect.stringContains stderr "ttt" "AT9: warning about ttt still printed on stderr")

          testCase "AT9: accept with --strict → exit 3 when Undereferenceable warning present"
          <| fun () ->
              withTempDir (fun dir ->
                  let lockPath = Path.Combine(dir, "lock.json")
                  let resolvedPath = Path.Combine(dir, "resolved.json")
                  LockFile.write lockPath at1Lock
                  File.WriteAllText(resolvedPath, at1ResolvedJson, Encoding.UTF8)

                  let exitCode, _, stderr =
                      runCli
                          [| "semantic"
                             "accept"
                             "--strict"
                             "--input"
                             resolvedPath
                             "--lock-file"
                             lockPath |]
                          15000

                  Expect.equal exitCode 3 "AT9: --strict + Undereferenceable warning → exit 3"
                  Expect.notEqual exitCode 1 "AT9: exit 3 is distinct from operational-error (1)"
                  Expect.notEqual exitCode 2 "AT9: exit 3 is distinct from diff (2)"
                  Expect.stringContains stderr "ttt" "AT9: warning still printed even with --strict")

          // AT10: --format json emits structured warning records
          testCase "AT10: accept --format json emits structured VocabWarning records"
          <| fun () ->
              withTempDir (fun dir ->
                  let lockPath = Path.Combine(dir, "lock.json")
                  let resolvedPath = Path.Combine(dir, "resolved.json")
                  LockFile.write lockPath at1Lock
                  File.WriteAllText(resolvedPath, at1ResolvedJson, Encoding.UTF8)

                  let exitCode, stdout, _ =
                      runCli
                          [| "semantic"
                             "accept"
                             "--format"
                             "json"
                             "--input"
                             resolvedPath
                             "--lock-file"
                             lockPath |]
                          15000

                  Expect.equal exitCode 0 "AT10: accept json exit 0"

                  let doc = JsonDocument.Parse(stdout)
                  let root = doc.RootElement

                  let warnings = root.GetProperty("warnings")
                  Expect.isGreaterThan (warnings.GetArrayLength()) 0 "AT10: warnings array is non-empty"

                  let w =
                      seq { 0 .. warnings.GetArrayLength() - 1 }
                      |> Seq.map (fun i -> warnings.[i])
                      |> Seq.tryFind (fun el -> el.GetProperty("prefix").GetString() = "ttt")
                      |> Option.defaultWith (fun () -> failwith "AT10: no warning record for ttt prefix")

                  Expect.equal (w.GetProperty("prefix").GetString()) "ttt" "AT10: prefix field is ttt"

                  Expect.equal
                      (w.GetProperty("state").GetString())
                      "Undereferenceable"
                      "AT10: state field is Undereferenceable"

                  Expect.equal
                      (w.GetProperty("iri").GetString())
                      "https://example.org/tictactoe#"
                      "AT10: iri field is exact namespace IRI"

                  Expect.isNonEmpty (w.GetProperty("hint").GetString()) "AT10: hint field is non-empty")

          testCase "AT10: status --format json emits 6-key records; type/field null when lock has no mapping reference"
          <| fun () ->
              withTempDir (fun dir ->
                  let lockPath = Path.Combine(dir, "lock.json")
                  LockFile.write lockPath (LockFile.withIntegrity at1Lock)

                  let exitCode, stdout, _ =
                      runCli
                          [| "semantic"; "status"; "--format"; "json"; "--lock-file"; lockPath |]
                          15000

                  Expect.equal exitCode 0 "AT10: status json exit 0"

                  let arr = JsonDocument.Parse(stdout)
                  let root = arr.RootElement
                  Expect.equal root.ValueKind JsonValueKind.Array "AT10: status json output is a JSON array"
                  Expect.isGreaterThan (root.GetArrayLength()) 0 "AT10: status warnings array is non-empty"

                  let w =
                      seq { 0 .. root.GetArrayLength() - 1 }
                      |> Seq.map (fun i -> root.[i])
                      |> Seq.tryFind (fun el -> el.GetProperty("prefix").GetString() = "ttt")
                      |> Option.defaultWith (fun () ->
                          failwith "AT10: no warning record for ttt in status json")

                  Expect.equal (w.GetProperty("prefix").GetString()) "ttt" "AT10: prefix is ttt"

                  Expect.equal
                      (w.GetProperty("state").GetString())
                      "Undereferenceable"
                      "AT10: state is Undereferenceable"

                  Expect.equal
                      (w.GetProperty("iri").GetString())
                      "https://example.org/tictactoe#"
                      "AT10: iri is exact namespace IRI"

                  Expect.equal
                      (w.GetProperty("type").ValueKind)
                      JsonValueKind.Null
                      "AT10: type is null when no lock mapping references ttt namespace"

                  Expect.equal
                      (w.GetProperty("field").ValueKind)
                      JsonValueKind.Null
                      "AT10: field is null when no lock mapping references ttt namespace"

                  Expect.isNonEmpty (w.GetProperty("hint").GetString()) "AT10: hint is non-empty")

          testCase "AT10: status --format json populates type/field when lock mapping references namespace"
          <| fun () ->
              withTempDir (fun dir ->
                  let lockPath = Path.Combine(dir, "lock.json")
                  LockFile.write lockPath (LockFile.withIntegrity at1LockWithRef)

                  let exitCode, stdout, _ =
                      runCli
                          [| "semantic"; "status"; "--format"; "json"; "--lock-file"; lockPath |]
                          15000

                  Expect.equal exitCode 0 "AT10: status json exit 0"

                  let arr = JsonDocument.Parse(stdout)
                  let root = arr.RootElement
                  Expect.equal root.ValueKind JsonValueKind.Array "AT10: output is a JSON array"

                  let w =
                      seq { 0 .. root.GetArrayLength() - 1 }
                      |> Seq.map (fun i -> root.[i])
                      |> Seq.tryFind (fun el -> el.GetProperty("prefix").GetString() = "ttt")
                      |> Option.defaultWith (fun () ->
                          failwith "AT10: no warning record for ttt in status json")

                  Expect.equal (w.GetProperty("prefix").GetString()) "ttt" "AT10: prefix is ttt"

                  Expect.equal
                      (w.GetProperty("state").GetString())
                      "Undereferenceable"
                      "AT10: state is Undereferenceable"

                  Expect.equal
                      (w.GetProperty("iri").GetString())
                      "https://example.org/tictactoe#"
                      "AT10: iri is exact namespace IRI"

                  Expect.equal
                      (w.GetProperty("type").GetString())
                      "App.MoveRequest"
                      "AT10: type is FSharpType of mapping whose field references ttt namespace"

                  Expect.equal
                      (w.GetProperty("field").GetString())
                      "Position"
                      "AT10: field is name of field whose IRI references ttt namespace"

                  Expect.isNonEmpty (w.GetProperty("hint").GetString()) "AT10: hint is non-empty")

          // AT11: --format flag (standardized from --output-format) accepted by clarify and extract
          testCase "AT11: clarify --format json is accepted (standardized from --output-format)"
          <| fun () ->
              withTempDir (fun dir ->
                  let lockPath = Path.Combine(dir, "lock.json")
                  LockFile.write lockPath (LockFile.withIntegrity at1Lock)

                  let exitCode, stdout, stderr =
                      runCli
                          [| "semantic"; "clarify"; "--format"; "json"; "--lock-file"; lockPath |]
                          15000

                  Expect.equal exitCode 0 "AT11: clarify --format json exits 0"
                  Expect.isFalse (stderr.Contains("unknown")) "AT11: --format is a recognized argument"
                  let _ = JsonDocument.Parse(stdout)
                  ())

          testCase "AT11: clarify --output-format json still accepted as deprecated alias"
          <| fun () ->
              withTempDir (fun dir ->
                  let lockPath = Path.Combine(dir, "lock.json")
                  LockFile.write lockPath (LockFile.withIntegrity at1Lock)

                  let exitCode, _, stderr =
                      runCli
                          [| "semantic"; "clarify"; "--output-format"; "json"; "--lock-file"; lockPath |]
                          15000

                  Expect.equal exitCode 0 "AT11: --output-format alias still exits 0"
                  Expect.isFalse (stderr.Contains("unknown")) "AT11: --output-format is still recognized")

          testCase "AT11: extract --format json is accepted (flag recognized, project error not flag error)"
          <| fun () ->
              withTempDir (fun dir ->
                  let fakeProjPath = Path.Combine(dir, "fake.fsproj")

                  let exitCode, _, stderr =
                      runCli
                          [| "semantic"; "extract"; "--format"; "json"; "--project"; fakeProjPath |]
                          15000

                  // Should fail due to project not found (exit 1), NOT due to unknown --format flag
                  Expect.equal exitCode 1 "AT11: extract with missing project exits 1"
                  Expect.isFalse (stderr.ToLower().Contains("unknown")) "AT11: --format is recognized by extract") ]
