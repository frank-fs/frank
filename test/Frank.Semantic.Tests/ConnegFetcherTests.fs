module Frank.Semantic.Tests.ConnegFetcherTests

open System
open System.Net
open System.Net.Http
open System.Text
open System.Threading.Tasks
open Expecto
open Frank.Semantic

// ── Stub infrastructure ───────────────────────────────────────────────────────

/// Bind an HttpListener on a random port without TOCTOU.
/// Tries up to 20 random ports; raises invalidOp if none succeed.
let private bindHttpListener () : HttpListener * int =
    let mutable result = ValueNone
    let mutable attempt = 0

    while attempt < 20 && result.IsNone do
        let port = Random.Shared.Next(40000, 60000)
        let l = new HttpListener()
        l.Prefixes.Add $"http://localhost:{port}/"

        try
            l.Start()
            result <- ValueSome(l, port)
        with _ ->
            (l :> IDisposable).Dispose()
            attempt <- attempt + 1

    match result with
    | ValueNone -> invalidOp "could not bind HttpListener after 20 attempts"
    | ValueSome r -> r

/// Run a background loop that serves up to maxRequests requests using handler,
/// then stops. Returns a background Task (fire-and-forget; caller stops listener to clean up).
let private startServing (listener: HttpListener) (maxRequests: int) (handler: HttpListenerContext -> unit) : Task =
    Task.Run(fun () ->
        let mutable count = 0

        while count < maxRequests do
            try
                let ctx = listener.GetContextAsync().GetAwaiter().GetResult()

                try
                    handler ctx
                with _ ->
                    ()

                count <- count + 1
            with _ ->
                count <- maxRequests)

/// Execute test f with a loopback stub that serves up to maxRequests via handler.
let private withStub (maxRequests: int) (handler: HttpListenerContext -> unit) (f: Uri -> Async<'T>) : Async<'T> =
    async {
        let listener, port = bindHttpListener ()
        let baseUri = Uri $"http://localhost:{port}/"
        let _ = startServing listener maxRequests handler

        try
            return! f baseUri
        finally
            try
                listener.Stop()
            with _ ->
                ()

            (listener :> IDisposable).Dispose()
    }

let private makeClient () : HttpClient =
    let handler = new HttpClientHandler()
    handler.AllowAutoRedirect <- false
    new HttpClient(handler)

/// Write bytes as a simple HTTP response with the given content-type and status.
let private respond (ctx: HttpListenerContext) (status: int) (contentType: string) (body: byte[]) : unit =
    ctx.Response.StatusCode <- status
    ctx.Response.ContentType <- contentType
    ctx.Response.ContentLength64 <- int64 body.Length
    use stream = ctx.Response.OutputStream
    stream.Write(body, 0, body.Length)

/// Write a redirect response with a Location header.
let private respondRedirect (ctx: HttpListenerContext) (status: int) (location: string) : unit =
    ctx.Response.StatusCode <- status
    ctx.Response.RedirectLocation <- location
    ctx.Response.ContentLength64 <- 0L
    ctx.Response.OutputStream.Close()

// ── Turtle fixture with dynamic namespace ─────────────────────────────────────

/// Turtle body declaring Game and Player as rdfs:Class in the given namespace.
let private turtleWithNs (ns: string) : byte[] =
    sprintf
        "@prefix ex: <%s> .\n@prefix rdfs: <http://www.w3.org/2000/01/rdf-schema#> .\n<%sGame> a rdfs:Class .\n<%sPlayer> a rdfs:Class .\n"
        ns
        ns
        ns
    |> Encoding.UTF8.GetBytes

let private htmlBytes = Encoding.UTF8.GetBytes "<html><body>not rdf</body></html>"

// ── A-C1: Content negotiation ─────────────────────────────────────────────────

[<Tests>]
let connegTests =
    testList
        "RdfConneg — A-C1 content negotiation"
        [
          // Stub serves Turtle ONLY when Accept contains text/turtle.
          // Falsifiable: if rdfFetch sent Accept: */* it would get HTML and fail.
          testAsync "stub requires RDF Accept; fetcher sends it and gets Validated=true" {
              let handler (ctx: HttpListenerContext) =
                  let accept = ctx.Request.Headers.Get "Accept"
                  let wantsRdf = accept <> null && accept.Contains "text/turtle"

                  if wantsRdf then
                      let body = turtleWithNs (ctx.Request.Url.GetLeftPart(UriPartial.Authority) + "/")
                      respond ctx 200 "text/turtle" body
                  else
                      respond ctx 200 "text/html" htmlBytes

              do!
                  withStub 1 handler (fun baseUri ->
                      async {
                          use client = makeClient ()
                          let fetch = RdfConneg.rdfFetch client
                          let! fetchResult = fetch baseUri None None
                          let evidence = RdfConneg.buildEvidence baseUri DateTimeOffset.UtcNow fetchResult

                          match evidence with
                          | Updated ev ->
                              Expect.equal ev.Validated.IsValidated true "IsValidated"
                              Expect.equal ev.MediaType (Some "text/turtle") "MediaType"

                              let terms = ev.Terms |> Option.defaultValue Set.empty
                              Expect.isFalse terms.IsEmpty "Terms must be non-empty"
                          | Unchanged -> failtest "expected Updated, got Unchanged"
                          | Undereferenceable r -> failtest $"expected Updated, got Undereferenceable: {r}"
                      })
          }

          // Stub always returns HTML regardless of Accept header.
          // rdfFetch must reject the HTML response and yield Undereferenceable.
          testAsync "stub returns HTML always; evidence is Undereferenceable (non-RDF)" {
              let handler (ctx: HttpListenerContext) = respond ctx 200 "text/html" htmlBytes

              do!
                  withStub 1 handler (fun baseUri ->
                      async {
                          use client = makeClient ()
                          let fetch = RdfConneg.rdfFetch client
                          let! fetchResult = fetch baseUri None None
                          let evidence = RdfConneg.buildEvidence baseUri DateTimeOffset.UtcNow fetchResult

                          match evidence with
                          | Undereferenceable reason ->
                              Expect.stringContains reason "text/html" "reason mentions content-type"
                          | Updated _ -> failtest "expected Undereferenceable, got Updated"
                          | Unchanged -> failtest "expected Undereferenceable, got Unchanged"
                      })
          } ]

// ── A-C2: 303 / httpRange-14 ──────────────────────────────────────────────────

[<Tests>]
let redirectTests =
    testList
        "RdfConneg — A-C2 303 httpRange-14"
        [ testAsync "stub 303s vocab IRI → /desc; fetcher follows and parses RDF" {
              let handler (ctx: HttpListenerContext) =
                  let path = ctx.Request.Url.AbsolutePath

                  if path = "/" then
                      respondRedirect ctx 303 "/desc"
                  else
                      let ns = ctx.Request.Url.GetLeftPart(UriPartial.Authority) + "/"
                      let body = turtleWithNs ns
                      respond ctx 200 "text/turtle" body

              do!
                  withStub 2 handler (fun baseUri ->
                      async {
                          use client = makeClient ()
                          let fetch = RdfConneg.rdfFetch client
                          let! fetchResult = fetch baseUri None None
                          let evidence = RdfConneg.buildEvidence baseUri DateTimeOffset.UtcNow fetchResult

                          match evidence with
                          | Updated ev ->
                              Expect.equal ev.Validated.IsValidated true "IsValidated after 303"
                              Expect.equal ev.MediaType (Some "text/turtle") "MediaType"
                          | Unchanged -> failtest "expected Updated, got Unchanged"
                          | Undereferenceable r ->
                              failtest $"expected Updated after following 303, got Undereferenceable: {r}"
                      })
          } ]

// ── A-C3: Conditional request / 304 ──────────────────────────────────────────

[<Tests>]
let conditionalTests =
    testList
        "RdfConneg — A-C3 conditional / 304"
        [ testAsync "fetcher sends If-None-Match; stub returns 304; evidence is Unchanged" {
              // Capture the If-None-Match header received by the stub.
              let receivedIfNoneMatch = ref ""

              let handler (ctx: HttpListenerContext) =
                  let inm = ctx.Request.Headers.Get "If-None-Match"
                  receivedIfNoneMatch.Value <- if inm <> null then inm else ""

                  if inm <> null then
                      ctx.Response.StatusCode <- 304
                      ctx.Response.ContentLength64 <- 0L
                      ctx.Response.OutputStream.Close()
                  else
                      let ns = ctx.Request.Url.GetLeftPart(UriPartial.Authority) + "/"
                      respond ctx 200 "text/turtle" (turtleWithNs ns)

              do!
                  withStub 2 handler (fun baseUri ->
                      async {
                          use client = makeClient ()
                          let fetch = RdfConneg.rdfFetch client

                          // First fetch — no ETag → 200 + Turtle
                          let! first = fetch baseUri None None
                          let ev1 = RdfConneg.buildEvidence baseUri DateTimeOffset.UtcNow first

                          let priorETag =
                              match ev1 with
                              | Updated e -> e.ETag
                              | _ -> None

                          // Use a synthetic ETag if the stub didn't send one
                          let testETag = priorETag |> Option.defaultValue "\"test-etag\""

                          // Second fetch — send If-None-Match → expect 304
                          let! second = fetch baseUri (Some testETag) None
                          let ev2 = RdfConneg.buildEvidence baseUri DateTimeOffset.UtcNow second

                          // Prove the conditional was sent (stub must have received it)
                          Expect.equal receivedIfNoneMatch.Value testETag "stub received If-None-Match"

                          // 304 → Unchanged (no re-parse, no new hash)
                          match ev2 with
                          | Unchanged -> ()
                          | Updated _ -> failtest "expected Unchanged (304) but got Updated (re-parse)"
                          | Undereferenceable r -> failtest $"expected Unchanged, got Undereferenceable: {r}"
                      })
          } ]

// ── Redirect cap ─────────────────────────────────────────────────────────────

[<Tests>]
let redirectCapTests =
    testList
        "RdfConneg — redirect cap (Holzmann #10)"
        [ testAsync "looping redirect stub → cap hit at maxRedirectHops" {
              // Stub always redirects to itself — triggers the hop cap.
              // The fetcher makes maxRedirectHops + 1 requests before returning cap-hit.
              let handler (ctx: HttpListenerContext) = respondRedirect ctx 302 "/"

              do!
                  withStub (RdfConneg.maxRedirectHops + 2) handler (fun baseUri ->
                      async {
                          use client = makeClient ()
                          let fetch = RdfConneg.rdfFetch client
                          let! fetchResult = fetch baseUri None None
                          let evidence = RdfConneg.buildEvidence baseUri DateTimeOffset.UtcNow fetchResult

                          match evidence with
                          | Undereferenceable reason -> Expect.stringContains reason "cap" "reason mentions cap"
                          | Updated _ -> failtest "expected Undereferenceable (cap hit), got Updated"
                          | Unchanged -> failtest "expected Undereferenceable (cap hit), got Unchanged"
                      })
          } ]
