namespace TicTacToe.E2E

open System
open System.Diagnostics
open System.IO
open System.Net.Http
open System.Threading
open NUnit.Framework

/// Shared server endpoint for the E2E run. Test-harness state — the app is a
/// black box reached only over HTTP (no ProjectReference).
module Server =
    let mutable private url = ""
    let mutable internal proc: Process option = None
    let internal setUrl u = url <- u
    let Url () = url

/// Second server instance using the ex: vocabulary (FRANK_VOCAB=ex). Used by
/// AT-S7 to prove falsifiability — the schema.org client fails against it while
/// the discovery-only client still navigates it successfully.
module ExServer =
    let mutable private url = ""
    let mutable internal proc: Process option = None
    let internal setUrl u = url <- u
    let Url () = url

[<SetUpFixture>]
type ServerFixture() =

    let findAppProject () =
        let rec up (dir: DirectoryInfo) =
            let candidate =
                Path.Combine(dir.FullName, "sample", "TicTacToe-v732", "TicTacToe.v732.fsproj")

            if File.Exists candidate then candidate
            elif isNull dir.Parent then failwith "TicTacToe.v732.fsproj not found walking up from test output"
            else up dir.Parent

        up (DirectoryInfo(AppContext.BaseDirectory))

    let waitUntilReady (url: string) =
        use client = new HttpClient()
        let deadline = DateTime.UtcNow.AddSeconds 60.0
        let mutable ready = false

        while not ready && DateTime.UtcNow < deadline do
            try
                let resp = client.GetAsync(url + "/").Result
                ready <- resp.IsSuccessStatusCode
            with _ ->
                () // connection refused while the host boots — expected

            if not ready then
                Thread.Sleep 500

        if not ready then
            failwith "TicTacToe server did not become ready within 60s"

    let startSampleOn (port: int) (extraEnv: (string * string) list) (app: string) =
        let url = sprintf "http://localhost:%d" port
        let psi = ProcessStartInfo("dotnet")
        psi.ArgumentList.Add "run"
        psi.ArgumentList.Add "--project"
        psi.ArgumentList.Add app
        psi.ArgumentList.Add "--urls"
        psi.ArgumentList.Add url
        psi.EnvironmentVariables.["DOTNET_SYSTEM_GLOBALIZATION_INVARIANT"] <- "1"

        for (k, v) in extraEnv do
            psi.EnvironmentVariables.[k] <- v

        psi.UseShellExecute <- false
        url, Process.Start psi

    [<OneTimeSetUp>]
    member _.StartServer() =
        let app = findAppProject ()
        let schemaUrl, schemaProc = startSampleOn 15321 [] app
        Server.proc <- Some schemaProc
        Server.setUrl schemaUrl
        waitUntilReady schemaUrl
        let exUrl, exProc = startSampleOn 15322 [ "FRANK_VOCAB", "ex" ] app
        ExServer.proc <- Some exProc
        ExServer.setUrl exUrl
        waitUntilReady exUrl

    [<OneTimeTearDown>]
    member _.StopServer() =
        for p in [ Server.proc; ExServer.proc ] |> List.choose id do
            (try p.Kill true with _ -> ())
            p.Dispose()
