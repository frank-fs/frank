module Frank.Cli.Tests.RefreshCliTests

open System
open System.Diagnostics
open System.IO
open System.Net
open System.Text
open System.Threading.Tasks
open Expecto
open Frank.Semantic
open Frank.Semantic.LockFile

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

let private withTempDir (f: string -> 'a) : 'a =
    let dir = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName())
    Directory.CreateDirectory dir |> ignore

    try
        f dir
    finally
        Directory.Delete(dir, recursive = true)

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
      Vocabularies =
        Map.ofList
            [ "vocab",
              { Uri = $"http://localhost:{port}/vocab.ttl"
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

                  Expect.stringContains stderr "vocabulary hash drift" "drift message must appear on STDERR"

                  Expect.isFalse (stdout.Contains "vocabulary hash drift") "drift message must NOT appear on stdout")
          } ]
