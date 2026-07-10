module Frank.Cli.Tests.ValidateCliTests

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

// ── Stub bodies ───────────────────────────────────────────────────────────────

let private stubTurtleBytes: byte[] =
    Encoding.UTF8.GetBytes
        "@prefix ex: <http://localhost/vocab#> .\nex:Game a <http://www.w3.org/2000/01/rdf-schema#Class> .\n"

let private htmlBytes: byte[] =
    Encoding.UTF8.GetBytes "<html><body>not rdf</body></html>"

// ── Helpers (duplicated from RefreshCliTests for isolation) ───────────────────

let private frankCliDll: string =
    let testDir =
        Path.GetDirectoryName(Reflection.Assembly.GetExecutingAssembly().Location)

    Path.Combine(testDir, "Frank.Cli.dll")

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

// ── Lock builders ─────────────────────────────────────────────────────────────

/// Lock with a single Owned=true entry pointing at the given port.
/// FetchedAt is set far in the past so SLA forces a probe.
/// v2 locks must be stamped; verifyIfStamped rejects unstamped v2 (M3).
let private ownedLockFor (port: int) : LockFile =
    withIntegrity
        { SchemaVersion = 2
          Generated = DateTimeOffset.Parse("2025-01-01T00:00:00Z")
          Integrity = None
          Vocabularies =
            Map.ofList
                [ "vocab",
                  { v1Empty with
                      Uri = $"http://localhost:{port}/"
                      FetchedAt = DateTimeOffset.Parse("2020-01-01T00:00:00Z")
                      Hash = "sha256:INITIAL"
                      Owned = true } ]
          DeclaredPrefixes = Map.empty
          Mappings = [] }

/// Lock with a single Owned=false entry — validate must SKIP unowned entries.
/// v2 locks must be stamped; verifyIfStamped rejects unstamped v2 (M3).
let private unownedLockFor (port: int) : LockFile =
    withIntegrity
        { SchemaVersion = 2
          Generated = DateTimeOffset.Parse("2025-01-01T00:00:00Z")
          Integrity = None
          Vocabularies =
            Map.ofList
                [ "vocab",
                  { v1Empty with
                      Uri = $"http://localhost:{port}/"
                      FetchedAt = DateTimeOffset.Parse("2020-01-01T00:00:00Z")
                      Hash = "sha256:INITIAL"
                      Owned = false } ]
          DeclaredPrefixes = Map.empty
          Mappings = [] }

// ── A-C7 tests ────────────────────────────────────────────────────────────────

[<Tests>]
let validateCliTests =
    testSequenced
    <| testList
        "A-C7 — frank semantic validate: self-hosted endpoint RDF conneg"
        [ test "owned entry serves RDF Turtle → exit 0, Validated=true in updated lock" {
              withTempDir (fun dir ->
                  let capMs = 30_000
                  let listener, port = bindHttpListener ()
                  let serving = serveRequest listener capMs "text/turtle" 200 stubTurtleBytes
                  let lockPath = Path.Combine(dir, "semantic-mappings.lock.json")
                  LockFile.write lockPath (ownedLockFor port)

                  let exitCode, _stdout, stderr =
                      runCli [| "semantic"; "validate"; "--lock-file"; lockPath |] capMs

                  serving.Wait(1_000) |> ignore

                  Expect.equal exitCode 0 $"validate must exit 0 when RDF served; stderr:\n{stderr}"

                  let lf = LockFile.read lockPath |> Result.defaultWith failwith
                  let entry = lf.Vocabularies.["vocab"]
                  Expect.isTrue entry.Validated.IsValidated "Validated=true after successful conneg")
          }

          test "owned entry serves HTML (non-RDF) → exit 2 (LyingIri), Validated=false in updated lock" {
              withTempDir (fun dir ->
                  let capMs = 30_000
                  let listener, port = bindHttpListener ()
                  let serving = serveRequest listener capMs "text/html" 200 htmlBytes
                  let lockPath = Path.Combine(dir, "semantic-mappings.lock.json")
                  LockFile.write lockPath (ownedLockFor port)

                  let exitCode, _stdout, stderr =
                      runCli [| "semantic"; "validate"; "--lock-file"; lockPath |] capMs

                  serving.Wait(1_000) |> ignore

                  Expect.equal exitCode 2 $"validate must exit 2 on lying IRI (HTML served); stderr:\n{stderr}"

                  let lf = LockFile.read lockPath |> Result.defaultWith failwith
                  let entry = lf.Vocabularies.["vocab"]
                  Expect.isFalse entry.Validated.IsValidated "Validated=false after lying IRI")
          }

          test "unowned entries are not validated → exit 0, lock unchanged" {
              withTempDir (fun dir ->
                  // Port not serving — if validate probes it, serveRequest would be needed.
                  // If unowned entries are correctly skipped, no HTTP request arrives.
                  let l, port = bindHttpListener ()
                  l.Stop()
                  (l :> IDisposable).Dispose()

                  let lockPath = Path.Combine(dir, "semantic-mappings.lock.json")
                  let lock = unownedLockFor port
                  LockFile.write lockPath lock

                  let capMs = 30_000

                  let exitCode, _stdout, stderr =
                      runCli [| "semantic"; "validate"; "--lock-file"; lockPath |] capMs

                  Expect.equal exitCode 0 $"validate must exit 0 when no owned entries; stderr:\n{stderr}"

                  let lf = LockFile.read lockPath |> Result.defaultWith failwith
                  Expect.equal lf.Vocabularies.["vocab"].Hash "sha256:INITIAL" "unowned hash unchanged")
          }

          test "owned entry returns 503 → exit 1 (transient), Validated unchanged" {
              withTempDir (fun dir ->
                  let capMs = 30_000
                  let listener, port = bindHttpListener ()

                  let serving =
                      serveRequest listener capMs "text/plain" 503 (Encoding.UTF8.GetBytes "service unavailable")

                  let lockPath = Path.Combine(dir, "semantic-mappings.lock.json")
                  let priorValidated =
                      { IsValidated = true
                        Reason = None
                        LastChecked = None }

                  let lock =
                      { (ownedLockFor port) with
                          Vocabularies =
                            Map.ofList
                                [ "vocab",
                                  { v1Empty with
                                      Uri = $"http://localhost:{port}/"
                                      FetchedAt = DateTimeOffset.Parse("2020-01-01T00:00:00Z")
                                      Hash = "sha256:CURRENT"
                                      Owned = true
                                      Validated = priorValidated } ] }

                  LockFile.write lockPath lock

                  let exitCode, _stdout, stderr =
                      runCli [| "semantic"; "validate"; "--lock-file"; lockPath |] capMs

                  serving.Wait(1_000) |> ignore

                  Expect.equal exitCode 1 $"validate must exit 1 on transient error; stderr:\n{stderr}"

                  let lf = LockFile.read lockPath |> Result.defaultWith failwith
                  let entry = lf.Vocabularies.["vocab"]
                  Expect.isTrue entry.Validated.IsValidated "Validated UNCHANGED on transient (503)")
          } ]
