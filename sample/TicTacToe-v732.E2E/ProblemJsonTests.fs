namespace TicTacToe.E2E

open System
open System.Diagnostics
open System.IO
open System.Net.Http
open System.Text
open System.Threading
open System.Threading.Tasks
open Microsoft.Playwright
open Microsoft.Playwright.NUnit
open NUnit.Framework

/// #436: performMove's `JsonNode.Parse body` had no guard — a malformed or empty request
/// body threw JsonException that escaped to Kestrel, producing an unhandled-exception 500
/// (Constitution rule 7 violation). This proves a clean 4xx application/problem+json
/// response instead.
///
/// The shared ServerFixture instance (Server.Url()) inherits the test runner's own console
/// for its child process's stdout/stderr — that stream can't be captured retroactively
/// (redirection must be configured on ProcessStartInfo before Process.Start). To make "zero
/// Kestrel unhandled-exception log entries" independently falsifiable evidence rather than
/// merely inferred from the HTTP response, this fixture starts its OWN dedicated server
/// instance with output redirected, exercised only by the malformed-body tests below.
[<TestFixture>]
type MalformedBodyLogTests() =
    let mutable proc: Process option = None
    let mutable url = ""
    let output = StringBuilder()
    let outputLock = obj ()

    let findAppProject () =
        let rec up (dir: DirectoryInfo) =
            let candidate =
                Path.Combine(dir.FullName, "sample", "TicTacToe-v732", "TicTacToe.v732.fsproj")

            if File.Exists candidate then
                candidate
            elif isNull dir.Parent then
                failwith "TicTacToe.v732.fsproj not found walking up from test output"
            else
                up dir.Parent

        up (DirectoryInfo(AppContext.BaseDirectory))

    let appendOutput (line: string) =
        if not (isNull line) then
            lock outputLock (fun () -> output.AppendLine line |> ignore)

    let waitUntilReady (baseUrl: string) =
        use client = new HttpClient()
        let deadline = DateTime.UtcNow.AddSeconds 60.0
        let mutable ready = false

        while not ready && DateTime.UtcNow < deadline do
            try
                let resp = client.GetAsync(baseUrl + "/").Result
                ready <- resp.IsSuccessStatusCode
            with _ ->
                () // connection refused while the host boots — expected

            if not ready then
                Thread.Sleep 500

        if not ready then
            failwith "TicTacToe server (log-capture instance) did not become ready within 60s"

    [<OneTimeSetUp>]
    member _.StartServer() =
        let app = findAppProject ()
        let port = 15330
        let baseUrl = sprintf "http://localhost:%d" port
        let psi = ProcessStartInfo("dotnet")
        psi.ArgumentList.Add "run"
        psi.ArgumentList.Add "--project"
        psi.ArgumentList.Add app
        psi.ArgumentList.Add "--urls"
        psi.ArgumentList.Add baseUrl
        psi.EnvironmentVariables.["DOTNET_SYSTEM_GLOBALIZATION_INVARIANT"] <- "1"
        psi.UseShellExecute <- false
        psi.RedirectStandardOutput <- true
        psi.RedirectStandardError <- true
        let p = Process.Start psi
        p.OutputDataReceived.Add(fun args -> appendOutput args.Data)
        p.ErrorDataReceived.Add(fun args -> appendOutput args.Data)
        p.BeginOutputReadLine()
        p.BeginErrorReadLine()
        proc <- Some p
        url <- baseUrl
        waitUntilReady baseUrl

    [<OneTimeTearDown>]
    member _.StopServer() =
        match proc with
        | Some p ->
            (try
                p.Kill true
             with _ ->
                 ())

            p.Dispose()
        | None -> ()

    member private _.CapturedOutput() =
        lock outputLock (fun () -> output.ToString())

    member private this.Post(path: string, body: string) : Task<HttpResponseMessage> =
        task {
            use client = new HttpClient()
            let content = new StringContent(body, Encoding.UTF8, "application/json")
            return! client.PostAsync(url + path, content)
        }

    [<Test>]
    member this.``POST with an empty body returns a clean 4xx, not an unhandled 500, and logs no unhandled exception``
        ()
        =
        task {
            use! created = (new HttpClient()).GetAsync(url + "/games/e2e-log-empty")
            Assert.That(created.StatusCode |> int, Is.EqualTo 200)

            use! resp = this.Post("/games/e2e-log-empty", "")
            Assert.That(resp.StatusCode |> int, Is.LessThan 500, "empty body must not surface as a 5xx")
            Assert.That(resp.StatusCode |> int, Is.GreaterThanOrEqualTo 400)

            let captured = this.CapturedOutput()

            Assert.That(
                captured,
                Does.Not.Contain("Unhandled exception"),
                $"server output must contain no unhandled-exception log entry; captured:\n{captured}"
            )

            Assert.That(
                captured,
                Does.Not.Contain("JsonException"),
                $"server output must contain no JsonException escape; captured:\n{captured}"
            )
        }

    [<Test>]
    member this.``POST with a truncated malformed body returns a clean 4xx, not an unhandled 500, and logs no unhandled exception``
        ()
        =
        task {
            use! created = (new HttpClient()).GetAsync(url + "/games/e2e-log-malformed")
            Assert.That(created.StatusCode |> int, Is.EqualTo 200)

            use! resp = this.Post("/games/e2e-log-malformed", "{")
            Assert.That(resp.StatusCode |> int, Is.LessThan 500, "malformed body must not surface as a 5xx")
            Assert.That(resp.StatusCode |> int, Is.GreaterThanOrEqualTo 400)

            let captured = this.CapturedOutput()

            Assert.That(
                captured,
                Does.Not.Contain("Unhandled exception"),
                $"server output must contain no unhandled-exception log entry; captured:\n{captured}"
            )

            Assert.That(
                captured,
                Does.Not.Contain("JsonException"),
                $"server output must contain no JsonException escape; captured:\n{captured}"
            )
        }

    /// #436 adversarial-gate finding: a body of literal `null` is VALID JSON —
    /// JsonNode.Parse "null" returns a null JsonNode rather than throwing — so the
    /// original guard's `try ... with :? JsonException` never caught it. The `Some doc`
    /// branch then called `doc.["position"]` on a null reference, escaping as an
    /// unhandled NullReferenceException 500. This must now be rejected at the parse-shape
    /// guard, same as malformed JSON.
    [<Test>]
    member this.``POST with a null body returns a clean 4xx, not an unhandled 500, and logs no unhandled exception``() =
        task {
            use! created = (new HttpClient()).GetAsync(url + "/games/e2e-log-null")
            Assert.That(created.StatusCode |> int, Is.EqualTo 200)

            use! resp = this.Post("/games/e2e-log-null", "null")
            Assert.That(resp.StatusCode |> int, Is.LessThan 500, "null body must not surface as a 5xx")
            Assert.That(resp.StatusCode |> int, Is.GreaterThanOrEqualTo 400)

            let captured = this.CapturedOutput()

            Assert.That(
                captured,
                Does.Not.Contain("Unhandled exception"),
                $"server output must contain no unhandled-exception log entry; captured:\n{captured}"
            )

            Assert.That(
                captured,
                Does.Not.Contain("NullReferenceException"),
                $"server output must contain no NullReferenceException escape; captured:\n{captured}"
            )
        }

    /// #436 adversarial-gate finding: a valid-but-non-object body (e.g. a bare JSON number)
    /// parses to a JsonValue, which isn't string-indexable — indexing it by "position" throws
    /// InvalidOperationException, not JsonException, so it also escaped the original guard.
    [<Test>]
    member this.``POST with a non-object JSON body (bare number) returns a clean 4xx, not an unhandled 500, and logs no unhandled exception``
        ()
        =
        task {
            use! created = (new HttpClient()).GetAsync(url + "/games/e2e-log-scalar")
            Assert.That(created.StatusCode |> int, Is.EqualTo 200)

            use! resp = this.Post("/games/e2e-log-scalar", "42")
            Assert.That(resp.StatusCode |> int, Is.LessThan 500, "non-object body must not surface as a 5xx")
            Assert.That(resp.StatusCode |> int, Is.GreaterThanOrEqualTo 400)

            let captured = this.CapturedOutput()

            Assert.That(
                captured,
                Does.Not.Contain("Unhandled exception"),
                $"server output must contain no unhandled-exception log entry; captured:\n{captured}"
            )

            Assert.That(
                captured,
                Does.Not.Contain("InvalidOperationException"),
                $"server output must contain no InvalidOperationException escape; captured:\n{captured}"
            )
        }

/// Response-shape assertions against the shared ServerFixture instance (Server.Url()) —
/// no dedicated log-capture needed here, only the wire-level content-type/body shape.
[<TestFixture>]
type ProblemJsonTests() =
    inherit PlaywrightTest()

    member this.NewContext() : Task<IAPIRequestContext> =
        this.Playwright.APIRequest.NewContextAsync(APIRequestNewContextOptions(BaseURL = Server.Url()))

    member private this.AssertProblemJson
        (resp: IAPIResponse, expectedStatus: int)
        : Task<System.Text.Json.JsonElement> =
        task {
            Assert.That(resp.Status, Is.EqualTo expectedStatus)

            Assert.That(
                resp.Headers.["content-type"],
                Does.StartWith "application/problem+json",
                "response must be served as application/problem+json"
            )

            let! json = resp.JsonAsync()
            let root = json.Value
            Assert.That(root.GetProperty("type").GetString(), Is.EqualTo "about:blank")
            Assert.That(root.GetProperty("title").GetString(), Is.Not.Empty)
            Assert.That(root.GetProperty("status").GetInt32(), Is.EqualTo expectedStatus)
            return root
        }

    [<Test>]
    member this.``a malformed body against the schema: sample returns 400 application/problem+json``() =
        task {
            use! ctx = this.NewContext()
            let! _ = ctx.GetAsync("/games/e2e-problemjson-malformed") // GET creates the game

            // DataByte, not Data/DataString: Playwright's string-typed Data/DataString options
            // silently re-encode a string that ISN'T itself valid JSON as a quoted JSON string
            // literal (confirmed by capturing the live server's own exception: a bare Data = "{"
            // arrived server-side as the 3-byte JSON string "\"{\"" — a syntactically VALID
            // JSON value, never exercising the malformed-body guard this test exists to prove).
            // DataByte sends the exact raw bytes on the wire, matching what a real malformed
            // client request looks like.
            let! resp =
                ctx.PostAsync(
                    "/games/e2e-problemjson-malformed",
                    APIRequestContextOptions(
                        Headers = dict [ "Content-Type", "application/json" ],
                        DataByte = System.Text.Encoding.UTF8.GetBytes "{"
                    )
                )

            let! _ = this.AssertProblemJson(resp, 400)
            ()
        }

    [<Test>]
    member this.``an empty body against the schema: sample returns 400 application/problem+json``() =
        task {
            use! ctx = this.NewContext()
            let! _ = ctx.GetAsync("/games/e2e-problemjson-empty") // GET creates the game

            let! resp =
                ctx.PostAsync(
                    "/games/e2e-problemjson-empty",
                    APIRequestContextOptions(Headers = dict [ "Content-Type", "application/json" ], DataByte = [||])
                )

            let! _ = this.AssertProblemJson(resp, 400)
            ()
        }

    /// #436 adversarial-gate finding: `null` is valid JSON (JsonNode.Parse doesn't throw for
    /// it), so it bypassed the original JsonException-only guard and NRE'd downstream. Must
    /// now be rejected at the same parse-shape guard as malformed JSON.
    [<Test>]
    member this.``a null body against the schema: sample returns 400 application/problem+json``() =
        task {
            use! ctx = this.NewContext()
            let! _ = ctx.GetAsync("/games/e2e-problemjson-null") // GET creates the game

            let! resp =
                ctx.PostAsync(
                    "/games/e2e-problemjson-null",
                    APIRequestContextOptions(
                        Headers = dict [ "Content-Type", "application/json" ],
                        DataByte = System.Text.Encoding.UTF8.GetBytes "null"
                    )
                )

            let! _ = this.AssertProblemJson(resp, 400)
            ()
        }

    /// #436 adversarial-gate finding: a valid-but-non-object body (bare array) parses fine
    /// but isn't string-indexable — indexing it by "position" throws, not JsonException.
    [<Test>]
    member this.``a non-object JSON body (bare array) against the schema: sample returns 400 application/problem+json``
        ()
        =
        task {
            use! ctx = this.NewContext()
            let! _ = ctx.GetAsync("/games/e2e-problemjson-array") // GET creates the game

            let! resp =
                ctx.PostAsync(
                    "/games/e2e-problemjson-array",
                    APIRequestContextOptions(
                        Headers = dict [ "Content-Type", "application/json" ],
                        DataByte = System.Text.Encoding.UTF8.GetBytes "[]"
                    )
                )

            let! _ = this.AssertProblemJson(resp, 400)
            ()
        }

    [<Test>]
    member this.``an unparseable move returns 400 application/problem+json``() =
        task {
            use! ctx = this.NewContext()
            let! _ = ctx.GetAsync("/games/e2e-problemjson-unparseable") // GET creates the game

            let! resp =
                ctx.PostAsync(
                    "/games/e2e-problemjson-unparseable",
                    APIRequestContextOptions(
                        DataObject =
                            {| position = "NotASquare"
                               player = "X" |}
                    )
                )

            let! root = this.AssertProblemJson(resp, 400)
            Assert.That(root.GetProperty("title").GetString(), Is.EqualTo "Bad Request")
        }

    [<Test>]
    member this.``a body missing position or player returns 400 application/problem+json``() =
        task {
            use! ctx = this.NewContext()
            let! _ = ctx.GetAsync("/games/e2e-problemjson-missing") // GET creates the game

            let! resp =
                ctx.PostAsync(
                    "/games/e2e-problemjson-missing",
                    APIRequestContextOptions(DataObject = {| position = "TopLeft" |})
                )

            let! root = this.AssertProblemJson(resp, 400)
            Assert.That(root.GetProperty("title").GetString(), Is.EqualTo "Bad Request")
        }

    [<Test>]
    member this.``an out-of-turn move returns 409 application/problem+json``() =
        task {
            use! ctx = this.NewContext()
            let! _ = ctx.GetAsync("/games/e2e-problemjson-conflict") // GET creates the game

            let! first =
                ctx.PostAsync(
                    "/games/e2e-problemjson-conflict",
                    APIRequestContextOptions(DataObject = {| position = "TopLeft"; player = "X" |})
                )

            Assert.That(first.Status, Is.EqualTo 200)

            let! second =
                ctx.PostAsync(
                    "/games/e2e-problemjson-conflict",
                    APIRequestContextOptions(
                        DataObject =
                            {| position = "TopCenter"
                               player = "X" |}
                    )
                )

            let! root = this.AssertProblemJson(second, 409)
            Assert.That(root.GetProperty("title").GetString(), Is.EqualTo "Conflict")
        }

    [<Test>]
    member this.``a move against a not-yet-created game returns 404 application/problem+json``() =
        task {
            use! ctx = this.NewContext()

            // No GET first — store.Update returns None for a game id never created.
            let! resp =
                ctx.PostAsync(
                    "/games/e2e-problemjson-notfound",
                    APIRequestContextOptions(DataObject = {| position = "TopLeft"; player = "X" |})
                )

            let! root = this.AssertProblemJson(resp, 404)
            Assert.That(root.GetProperty("title").GetString(), Is.EqualTo "Not Found")
        }
