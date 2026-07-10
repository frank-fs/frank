module Frank.Cli.Tests.FinalizeCliTests

open System
open System.Diagnostics
open System.IO
open System.Net
open System.Text
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

    let stdoutTask: Task<string> = proc.StandardOutput.ReadToEndAsync()
    let stderrTask: Task<string> = proc.StandardError.ReadToEndAsync()
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

let private serveRequest
    (listener: HttpListener)
    (capMs: int)
    (contentType: string)
    (status: int)
    (body: byte[])
    : Task =
    let ctxTask = listener.GetContextAsync()

    Task.Run(fun () ->
        if ctxTask.Wait(capMs) then
            try
                let ctx = ctxTask.Result
                ctx.Response.ContentType <- contentType
                ctx.Response.StatusCode <- status
                ctx.Response.ContentLength64 <- int64 body.Length
                use stream = ctx.Response.OutputStream
                stream.Write(body, 0, body.Length)
            with _ ->
                ()

        try
            listener.Stop()
        with _ ->
            ()

        (listener :> IDisposable).Dispose())

let private bindHttpListener () : HttpListener * int =
    let mutable result = ValueNone
    let mutable attempt = 0

    while attempt < 20 && result.IsNone do
        let port = Random.Shared.Next(40000, 60000)
        let l = new HttpListener()
        l.Prefixes.Add($"http://localhost:{port}/")

        try
            l.Start()
            result <- ValueSome(l, port)
        with _ ->
            (l :> IDisposable).Dispose()
            attempt <- attempt + 1

    match result with
    | ValueNone -> invalidOp "could not bind HttpListener after 20 attempts"
    | ValueSome r -> r

let private stubTurtleBytes (port: int) : byte[] =
    Encoding.UTF8.GetBytes
        $"@prefix ex: <http://localhost:{port}/vocab#> .\nex:Game a <http://www.w3.org/2000/01/rdf-schema#Class> .\n"

// ── Lock builders ─────────────────────────────────────────────────────────────

let private draftLockWithVocabs (ownedUri: string) (externalUri: string) : LockFile =
    withIntegrity
        { SchemaVersion = 2
          Generated = DateTimeOffset.Parse("2025-01-01T00:00:00Z")
          Integrity = None
          Vocabularies =
            Map.ofList
                [ "local",
                  { v1Empty with
                      Uri = ownedUri
                      FetchedAt = DateTimeOffset.Parse("2024-01-01T00:00:00Z")
                      Hash = "sha256:LOCALVOCAB"
                      Owned = false }
                  "external",
                  { v1Empty with
                      Uri = externalUri
                      FetchedAt = DateTimeOffset.Parse("2024-01-01T00:00:00Z")
                      Hash = "sha256:EXTERNAL"
                      Owned = false } ]
          DeclaredPrefixes = Map.empty
          Mappings = [] }

// ── H1 --base-uri tests ───────────────────────────────────────────────────────

[<Tests>]
let finalizeCliTests =
    testList
        "H1 — frank semantic finalize --base-uri Owned stamping"
        [ test "--base-uri stamps Owned=true on self-hosted, false on external" {
              withTempDir (fun dir ->
                  let lockPath = Path.Combine(dir, "semantic-mappings.lock.json")

                  LockFile.write lockPath (draftLockWithVocabs "https://example.org/vocab#" "https://schema.org/")

                  let exitCode, _stdout, stderr =
                      runCli
                          [| "semantic"
                             "finalize"
                             "--base-uri"
                             "https://example.org"
                             "--lock-file"
                             lockPath |]
                          5_000

                  Expect.equal exitCode 0 $"finalize must exit 0; stderr:\n{stderr}"

                  let lf = LockFile.read lockPath |> Result.defaultWith failwith
                  let local = lf.Vocabularies.["local"]
                  let ext = lf.Vocabularies.["external"]
                  Expect.isTrue local.Owned "self-hosted (example.org) must be Owned=true"
                  Expect.isFalse ext.Owned "external (schema.org) must be Owned=false")
          }

          test "no --base-uri → all Owned=false" {
              withTempDir (fun dir ->
                  let lockPath = Path.Combine(dir, "semantic-mappings.lock.json")

                  LockFile.write lockPath (draftLockWithVocabs "https://example.org/vocab#" "https://schema.org/")

                  let exitCode, _stdout, stderr =
                      runCli [| "semantic"; "finalize"; "--lock-file"; lockPath |] 5_000

                  Expect.equal exitCode 0 $"finalize without --base-uri must exit 0; stderr:\n{stderr}"

                  let lf = LockFile.read lockPath |> Result.defaultWith failwith

                  for KeyValue(key, entry) in lf.Vocabularies do
                      Expect.isFalse entry.Owned $"without --base-uri, {key} must stay Owned=false")
          }

          test "malformed --base-uri → exit 1, no lock laundering" {
              withTempDir (fun dir ->
                  let lockPath = Path.Combine(dir, "semantic-mappings.lock.json")

                  LockFile.write lockPath (draftLockWithVocabs "https://example.org/vocab#" "https://schema.org/")

                  let originalLf = LockFile.read lockPath |> Result.defaultWith failwith

                  let exitCode, _stdout, stderr =
                      runCli [| "semantic"; "finalize"; "--base-uri"; "not-a-uri"; "--lock-file"; lockPath |] 5_000

                  Expect.equal exitCode 1 $"malformed --base-uri must exit 1; stderr:\n{stderr}"
                  Expect.isTrue (stderr.Contains "not-a-uri") "error must mention the bad value"

                  let lf = LockFile.read lockPath |> Result.defaultWith failwith
                  Expect.equal lf.Integrity originalLf.Integrity "lock must not be rewritten on bad --base-uri")
          }

          test "end-to-end: finalize --base-uri then validate sees owned entry (closes inert-path gap)" {
              withTempDir (fun dir ->
                  let listener, port = bindHttpListener ()
                  let capMs = 8_000
                  let serving = serveRequest listener capMs "text/turtle" 200 (stubTurtleBytes port)

                  let lockPath = Path.Combine(dir, "semantic-mappings.lock.json")

                  LockFile.write lockPath (draftLockWithVocabs $"http://localhost:{port}/" "https://schema.org/")

                  let finalizeExit, _, fErr =
                      runCli
                          [| "semantic"
                             "finalize"
                             "--base-uri"
                             $"http://localhost:{port}"
                             "--lock-file"
                             lockPath |]
                          capMs

                  Expect.equal finalizeExit 0 $"finalize must exit 0; stderr:\n{fErr}"

                  let afterFinalize = LockFile.read lockPath |> Result.defaultWith failwith
                  Expect.isTrue afterFinalize.Vocabularies.["local"].Owned "local must be Owned=true after finalize"

                  let validateExit, vOut, vErr =
                      runCli [| "semantic"; "validate"; "--lock-file"; lockPath |] capMs

                  serving.Wait(1_000) |> ignore

                  Expect.equal
                      validateExit
                      0
                      $"validate must exit 0 when owned entry serves valid RDF; stderr:\n{vErr}"

                  Expect.isFalse
                      (vOut.Contains "no owned vocabulary entries")
                      "validate must not skip when Owned=true entries exist"

                  Expect.isTrue
                      (vOut.Contains "Validated=true")
                      "validate must probe and confirm owned entry (Validated=true in output)")
          } ]
