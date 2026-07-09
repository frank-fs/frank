module Frank.Cli.Tests.RefreshCliTests

open System
open System.Diagnostics
open System.IO
open System.Net
open System.Security.Cryptography
open System.Text
open System.Threading.Tasks
open Expecto
open Frank.Semantic
open Frank.Semantic.LockFile
open Frank.TestSupport.TempDir

// ── Stub Turtle served on loopback ────────────────────────────────────────────

// Fixed Turtle body served by the local stub.
// The SHA-256 of these bytes ≠ "sha256:STALE" → drift is deterministic.
let private stubTurtleBytes: byte[] =
    Encoding.UTF8.GetBytes
        "@prefix ex: <http://example.org/vocab#> .\nex:Game a <http://www.w3.org/2000/01/rdf-schema#Class> .\n"

// ── Helpers ───────────────────────────────────────────────────────────────────

/// Frank.Cli.dll co-located with the test DLL.
let private frankCliDll: string =
    let testDir =
        Path.GetDirectoryName(Reflection.Assembly.GetExecutingAssembly().Location)

    Path.Combine(testDir, "Frank.Cli.dll")

/// Bind an HttpListener on a random port without a prior TcpListener (no TOCTOU).
/// Tries up to 20 random ports; raises invalidOp if none succeed.
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

/// Serve stubTurtleBytes to a single request within capMs, then stop and dispose.
/// GetContextAsync is started before Task.Run so the listener is ready immediately.
/// If no request arrives within capMs the listener is cleaned up deterministically.
let private serveOnce (listener: HttpListener) (capMs: int) : Task =
    let ctxTask = listener.GetContextAsync()

    Task.Run(fun () ->
        if ctxTask.Wait(capMs) then
            try
                let ctx = ctxTask.Result
                ctx.Response.ContentType <- "text/turtle"
                ctx.Response.StatusCode <- 200
                ctx.Response.ContentLength64 <- int64 stubTurtleBytes.Length
                use stream = ctx.Response.OutputStream
                stream.Write(stubTurtleBytes, 0, stubTurtleBytes.Length)
            with _ ->
                ()

        try
            listener.Stop()
        with _ ->
            ()

        (listener :> IDisposable).Dispose())

/// Build a lock whose recorded hash ("sha256:STALE") will never match the
/// real SHA-256 of stubTurtleBytes — drift is guaranteed every run.
let private staleLockFor (port: int) : LockFile =
    { SchemaVersion = 1
      Generated = DateTimeOffset.Parse("2025-01-01T00:00:00Z")
      Integrity = None
      Vocabularies =
        Map.ofList
            [ "vocab",
              { v1Empty with
                  Uri = $"http://localhost:{port}/vocab.ttl"
                  FetchedAt = DateTimeOffset.Parse("2025-01-01T00:00:00Z")
                  Hash = "sha256:STALE" } ]
      DeclaredPrefixes = Map.empty
      Mappings = [] }

/// Run `dotnet <frankCliDll> <args>` and return (exitCode, stdout, stderr).
/// Bound by capMs (Holzmann rule 10); kills process and raises on timeout.
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

/// SHA-256 hex of stubTurtleBytes (used for the "no drift" case).
let private stubTurtleSha256 () : string =
    use sha = SHA256.Create()
    sha.ComputeHash(stubTurtleBytes)
    |> Array.map (fun b -> b.ToString("x2"))
    |> String.concat ""

/// Lock where the recorded hash MATCHES stubTurtleBytes → no drift on refresh.
let private freshLockFor (port: int) : LockFile =
    { SchemaVersion = 1
      Generated = DateTimeOffset.Parse("2025-01-01T00:00:00Z")
      Integrity = None
      Vocabularies =
        Map.ofList
            [ "vocab",
              { v1Empty with
                  Uri = $"http://localhost:{port}/vocab.ttl"
                  FetchedAt = DateTimeOffset.Parse("2025-01-01T00:00:00Z")
                  Hash = stubTurtleSha256 () } ]
      DeclaredPrefixes = Map.empty
      Mappings = [] }

/// Serve stubTurtleBytes, capturing the URL of the first incoming request.
let private serveOnceCapturingUrl (listener: HttpListener) (capMs: int) : Task * Task<string> =
    let ctxTask = listener.GetContextAsync()
    let urlTcs = TaskCompletionSource<string>()

    let serving =
        Task.Run(fun () ->
            if ctxTask.Wait(capMs) then
                try
                    let ctx = ctxTask.Result
                    urlTcs.TrySetResult(ctx.Request.Url.AbsoluteUri) |> ignore
                    ctx.Response.ContentType <- "text/turtle"
                    ctx.Response.StatusCode <- 200
                    ctx.Response.ContentLength64 <- int64 stubTurtleBytes.Length
                    use stream = ctx.Response.OutputStream
                    stream.Write(stubTurtleBytes, 0, stubTurtleBytes.Length)
                with _ ->
                    urlTcs.TrySetResult("") |> ignore
            else
                urlTcs.TrySetResult("") |> ignore

            try
                listener.Stop()
            with _ ->
                ()

            (listener :> IDisposable).Dispose())

    serving, urlTcs.Task

// ── Tests ─────────────────────────────────────────────────────────────────────

[<Tests>]
let refreshCliTests =
    testList
        "AT4 — frank semantic refresh CLI path: exit 2 + drift on stderr"
        [ test "frank semantic refresh exits 2 on drift and writes drift to stderr not stdout" {
              withTempDir (fun dir ->
                  // Listener bound BEFORE subprocess launch; port derived from the listener itself.
                  let capMs = 5_000
                  let listener, port = bindHttpListener ()
                  let serving = serveOnce listener capMs
                  let lockPath = Path.Combine(dir, "semantic-mappings.lock.json")
                  LockFile.write lockPath (staleLockFor port)

                  let exitCode, stdout, stderr =
                      runCli [| "semantic"; "refresh"; "--lock-file"; lockPath |] capMs

                  serving.Wait(1_000) |> ignore

                  Expect.equal exitCode 2 $"refresh must exit 2 when drift detected; stderr:\n{stderr}"

                  Expect.stringContains stderr "drift: content hash changed" "drift message must appear on STDERR"

                  Expect.isFalse (stdout.Contains "vocabulary hash drift") "drift message must NOT appear on stdout")
          }

          test "AT2 - unchanged vocab: refresh exits 0 and stub receives GET for exactly the recorded URL" {
              withTempDir (fun dir ->
                  let capMs = 5_000
                  let listener, port = bindHttpListener ()
                  let serving, urlTask = serveOnceCapturingUrl listener capMs
                  let lockPath = Path.Combine(dir, "semantic-mappings.lock.json")
                  let lock = freshLockFor port
                  LockFile.write lockPath lock

                  let exitCode, _stdout, stderr =
                      runCli [| "semantic"; "refresh"; "--lock-file"; lockPath |] capMs

                  serving.Wait(1_000) |> ignore

                  Expect.equal exitCode 0 $"refresh must exit 0 when no drift; stderr:\n{stderr}"

                  // The stub received a request for exactly the URL recorded in Vocabularies
                  let recordedUrl = lock.Vocabularies.["vocab"].Uri
                  let requestedUrl = urlTask.Result

                  Expect.equal requestedUrl recordedUrl "stub must receive GET for the URL recorded in Vocabularies")
          }

          test "AT2 - unreachable vocab URL: refresh exits 1 (error, not drift)" {
              withTempDir (fun dir ->
                  // Bind and immediately close a listener to get a port that's not serving.
                  let l, port = bindHttpListener ()
                  l.Stop()
                  (l :> IDisposable).Dispose()

                  let lockPath = Path.Combine(dir, "semantic-mappings.lock.json")
                  LockFile.write lockPath (staleLockFor port)

                  let capMs = 5_000
                  let exitCode, _stdout, stderr =
                      runCli [| "semantic"; "refresh"; "--lock-file"; lockPath |] capMs

                  Expect.equal exitCode 1 $"unreachable URL must exit 1 (error); stderr:\n{stderr}")
          } ]

[<Tests>]
let at6AcceptFinalizeIntegrityTests =
    testList
        "AT6 - accept and finalize stamp Integrity on the written lock"
        [ test "frank semantic finalize: written lock has Integrity=Some and verifyIntegrity=Ok" {
              withTempDir (fun dir ->
                  let lockPath = Path.Combine(dir, "semantic-mappings.lock.json")

                  let draftLock: LockFile =
                      { SchemaVersion = 1
                        Generated = DateTimeOffset.Parse("2025-01-01T00:00:00Z")
                        Integrity = None
                        Vocabularies = Map.empty
                        DeclaredPrefixes = Map.empty
                        Mappings =
                          [ { FSharpType = "MyApp.Draft"
                              Iri = None
                              Confidence = 0.0
                              Source = Convention
                              Status = Proposed
                              Alternates = []
                              Rt = None
                              Shape = MappingShape.Record [] } ] }

                  LockFile.write lockPath draftLock

                  let capMs = 10_000

                  let exitCode, _stdout, stderr =
                      runCli [| "semantic"; "finalize"; "--lock-file"; lockPath |] capMs

                  Expect.equal exitCode 0 $"finalize must exit 0; stderr:\n{stderr}"

                  let lf = LockFile.read lockPath |> Result.defaultWith failwith
                  Expect.isSome lf.Integrity "Integrity must be Some after finalize"
                  Expect.isOk (LockFile.verifyIntegrity lf) "Integrity must verify after finalize")
          }

          test "frank semantic accept: written lock has Integrity=Some and verifyIntegrity=Ok" {
              withTempDir (fun dir ->
                  let lockPath = Path.Combine(dir, "semantic-mappings.lock.json")
                  let resolvedPath = Path.Combine(dir, "resolved.json")

                  let draftLock: LockFile =
                      { SchemaVersion = 1
                        Generated = DateTimeOffset.Parse("2025-01-01T00:00:00Z")
                        Integrity = None
                        Vocabularies = Map.empty
                        DeclaredPrefixes = Map.empty
                        Mappings =
                          [ { FSharpType = "MyApp.Order"
                              Iri = None
                              Confidence = 0.0
                              Source = Convention
                              Status = Proposed
                              Alternates = []
                              Rt = None
                              Shape = MappingShape.Record [] } ] }

                  LockFile.write lockPath draftLock

                  let resolvedJson =
                      """{"schemaVersion":1,"resolved":[{"fsharpType":"MyApp.Order","iri":"schema:Order","fields":[]}]}"""

                  File.WriteAllText(resolvedPath, resolvedJson)

                  let capMs = 10_000

                  let exitCode, _stdout, stderr =
                      runCli [| "semantic"; "accept"; "--lock-file"; lockPath; "--input"; resolvedPath |] capMs

                  Expect.equal exitCode 0 $"accept must exit 0; stderr:\n{stderr}"

                  let lf = LockFile.read lockPath |> Result.defaultWith failwith
                  Expect.isSome lf.Integrity "Integrity must be Some after accept"
                  Expect.isOk (LockFile.verifyIntegrity lf) "Integrity must verify after accept")
          } ]
