namespace TicTacToe.E2E

open System
open System.IO
open System.Net.Sockets
open System.Text
open System.Threading.Tasks
open Microsoft.Playwright
open Microsoft.Playwright.NUnit
open NUnit.Framework

/// #398 /simplify item 7: moveHandler built `origin` via naive string interpolation
/// ($"{ctx.Request.Scheme}://{ctx.Request.Host}") and passed it straight to
/// DiscoveryMiddleware.resolveHref, which throws an unhandled UriFormatException for a
/// Host header Uri.TryCreate can't parse — the exact failure mode handleAlpsProfile
/// already guards against via Frank.OriginValidation.tryValidateOrigin (log + 400).
///
/// An empty Host header is a genuine, reachable trigger: Kestrel's own HTTP/1.1 framing
/// validation 400s almost every other malformed Host value before the request ever
/// reaches app code, but accepts an empty one — yet Uri.TryCreate rejects "http://"
/// (empty authority) as not a valid absolute URI. HttpClient's own Host-header setter
/// additionally refuses to send arbitrary malformed Host values at all (FormatException,
/// client-side, before the request is even written to the wire), so proving this gap
/// requires driving the live server over a raw TCP socket — bypassing HttpClient's
/// client-side validation — exactly as a real non-.NET HTTP client could.
[<TestFixture>]
type OriginRobustnessTests() =
    inherit PlaywrightTest()

    /// Send a raw HTTP/1.1 request with an explicit (possibly malformed, possibly merely
    /// disallowed) Host header value, bypassing HttpClient's own Host-header validation
    /// entirely. Returns the response status code.
    member private this.SendWithRawHost
        (baseUrl: string, httpMethod: string, path: string, hostHeaderValue: string, body: string option)
        : Task<int> =
        task {
            let uri = Uri baseUrl
            use client = new TcpClient()
            do! client.ConnectAsync(uri.Host, uri.Port)
            use stream = client.GetStream()

            let bodyHeaders, bodyText =
                match body with
                | Some text ->
                    let bodyBytes = Encoding.UTF8.GetBytes text
                    $"Content-Type: application/json\r\nContent-Length: {bodyBytes.Length}\r\n", text
                | None -> "", ""

            let request =
                $"{httpMethod} {path} HTTP/1.1\r\n"
                + $"Host: {hostHeaderValue}\r\n"
                + bodyHeaders
                + "Connection: close\r\n"
                + "\r\n"
                + bodyText

            let requestBytes = Encoding.Latin1.GetBytes request
            do! stream.WriteAsync(requestBytes, 0, requestBytes.Length)
            use reader = new StreamReader(stream, Encoding.Latin1)
            let! response = reader.ReadToEndAsync()
            let statusLine = response.Split("\r\n").[0]
            return statusLine.Split(' ').[1] |> int
        }

    /// Send a raw HTTP/1.1 POST with an explicit (possibly malformed) Host header value,
    /// bypassing HttpClient's own Host-header validation entirely. Returns the response
    /// status code.
    member private this.PostWithRawHost
        (baseUrl: string, path: string, hostHeaderValue: string, jsonBody: string)
        : Task<int> =
        this.SendWithRawHost(baseUrl, "POST", path, hostHeaderValue, Some jsonBody)

    [<Test>]
    member this.``schema: sample POST /games/{id} with an empty Host header returns 400, not an unhandled 500``() =
        task {
            use! ctx = this.Playwright.APIRequest.NewContextAsync(APIRequestNewContextOptions(BaseURL = Server.Url()))

            let! _ = ctx.GetAsync("/games/e2e-origin-robustness") // GET creates the game (real client flow)

            let! statusCode =
                this.PostWithRawHost(
                    Server.Url(),
                    "/games/e2e-origin-robustness",
                    "",
                    """{"position":"TopLeft","player":"X"}"""
                )

            Assert.That(
                statusCode,
                Is.EqualTo 400,
                "malformed (empty) Host header must be rejected gracefully with 400, not crash the request"
            )
        }

    [<Test>]
    member this.``ex: sample POST /games/{id} with an empty Host header returns 400, not an unhandled 500``() =
        task {
            use! ctx = this.Playwright.APIRequest.NewContextAsync(APIRequestNewContextOptions(BaseURL = ExServer.Url()))

            let! _ = ctx.GetAsync("/games/e2e-origin-robustness-ex") // GET creates the game

            let! statusCode =
                this.PostWithRawHost(
                    ExServer.Url(),
                    "/games/e2e-origin-robustness-ex",
                    "",
                    """{"position":"TopLeft","player":"X"}"""
                )

            Assert.That(
                statusCode,
                Is.EqualTo 400,
                "malformed (empty) Host header must be rejected gracefully with 400 — the ex: sample has no upstream Provenance guard, so this is moveHandler's OWN fix being exercised directly"
            )
        }

    /// #405 part 2: a Host header can be perfectly well-formed and still not belong to any
    /// host this deployment serves — "evil-flood-attempt.example" parses as a valid absolute
    /// URI, so Frank.OriginValidation.tryValidateOrigin (which only ever rejects a Host value
    /// Uri.TryCreate can't parse) would let it straight through to Frank's own middleware.
    /// The 400 here must instead come from ASP.NET Core's native host filtering
    /// (UseHostFiltering, configured via AllowedHosts in appsettings.json) rejecting the
    /// request BEFORE it reaches UseRouting or any Frank middleware — closing the
    /// Host-header-flood vector at its true source, independent of and complementing
    /// Frank's own bounded-cache mitigation (#405 part 1).
    [<Test>]
    member this.``schema: sample GET / with a well-formed but disallowed Host header returns 400 from ASP.NET Core host filtering``
        ()
        =
        task {
            let! statusCode = this.SendWithRawHost(Server.Url(), "GET", "/", "evil-flood-attempt.example", None)

            Assert.That(
                statusCode,
                Is.EqualTo 400,
                "a syntactically valid Host header not on AllowedHosts must be rejected by ASP.NET Core's \
                 UseHostFiltering — Frank's own OriginValidation only rejects unparseable Host values, never a \
                 valid-looking host that's merely off the allow-list, so this 400 proves framework-level filtering"
            )
        }
