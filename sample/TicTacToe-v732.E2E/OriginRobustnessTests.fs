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

    /// Send a raw HTTP/1.1 POST with an explicit (possibly malformed) Host header value,
    /// bypassing HttpClient's own Host-header validation entirely. Returns the response
    /// status code.
    member private this.PostWithRawHost
        (baseUrl: string, path: string, hostHeaderValue: string, jsonBody: string)
        : Task<int> =
        task {
            let uri = Uri baseUrl
            use client = new TcpClient()
            do! client.ConnectAsync(uri.Host, uri.Port)
            use stream = client.GetStream()
            let bodyBytes = Encoding.UTF8.GetBytes jsonBody

            let request =
                $"POST {path} HTTP/1.1\r\n"
                + $"Host: {hostHeaderValue}\r\n"
                + "Content-Type: application/json\r\n"
                + $"Content-Length: {bodyBytes.Length}\r\n"
                + "Connection: close\r\n"
                + "\r\n"
                + jsonBody

            let requestBytes = Encoding.Latin1.GetBytes request
            do! stream.WriteAsync(requestBytes, 0, requestBytes.Length)
            use reader = new StreamReader(stream, Encoding.Latin1)
            let! response = reader.ReadToEndAsync()
            let statusLine = response.Split("\r\n").[0]
            return statusLine.Split(' ').[1] |> int
        }

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
