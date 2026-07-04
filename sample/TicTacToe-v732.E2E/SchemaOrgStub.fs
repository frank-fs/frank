namespace TicTacToe.E2E

open System
open System.Net
open System.Text
open System.Threading.Tasks

/// Implementation details — not part of the public surface.
module private SchemaOrgStubImpl =

    /// Known schema.org local-names served as 200; everything else → 404.
    /// Derived from the ALPS descriptors the TicTacToe sample actually emits.
    /// Keeping this set exact makes the deref load-bearing: a hallucinated or
    /// misspelled IRI still fails even when the stub is in use.
    let knownTerms =
        Set.ofList
            [ "ActionStatusType"
              "ActiveActionStatus"
              "agent"
              "CompletedActionStatus"
              "FailedActionStatus"
              "Game"
              "identifier"
              "item"
              "ItemList"
              "MoveAction"
              "numberOfItems"
              "result"
              "actionStatus" ]

    /// Bind an HttpListener on a random port without a prior TcpListener.
    /// Tries up to 20 random ports; raises invalidOp on exhaustion (Holzmann 10).
    let bindHttpListener () : HttpListener * int =
        let mutable result = ValueNone
        let mutable attempt = 0

        while attempt < 20 && result.IsNone do
            let port = Random.Shared.Next(40000, 60000)
            let l = new HttpListener()
            l.Prefixes.Add(sprintf "http://localhost:%d/" port)

            try
                l.Start()
                result <- ValueSome(l, port)
            with :? HttpListenerException ->
                (l :> IDisposable).Dispose()
                attempt <- attempt + 1

        match result with
        | ValueNone -> invalidOp "could not bind HttpListener after 20 attempts"
        | ValueSome r -> r

    /// Write a minimal response for the given request context.
    /// 200 for known schema.org local-names, 404 otherwise.
    let serveRequest (ctx: HttpListenerContext) : unit =
        let localName = ctx.Request.Url.LocalPath.TrimStart('/')
        let statusCode = if knownTerms.Contains localName then 200 else 404
        let body = Encoding.UTF8.GetBytes(sprintf "<!-- schema.org stub: %s -->" localName)
        ctx.Response.StatusCode <- statusCode
        ctx.Response.ContentType <- "text/html"
        ctx.Response.ContentLength64 <- int64 body.Length
        use stream = ctx.Response.OutputStream
        stream.Write(body, 0, body.Length)

    /// Accept loop — serves up to requestCap requests, then stops (cap-hit behavior:
    /// stop serving). Exits silently on HttpListenerException / ObjectDisposedException
    /// (listener-stopped case only — Holzmann 7, constitution rule 7).
    let acceptLoop (listener: HttpListener) (requestCap: int) : Task =
        Task.Run(fun () ->
            let mutable count = 0
            let mutable active = true

            while active && count < requestCap do
                try
                    let ctx = listener.GetContext()
                    count <- count + 1
                    serveRequest ctx
                with
                | :? HttpListenerException -> active <- false
                | :? ObjectDisposedException -> active <- false)

/// Loopback HttpListener stub that serves the schema.org term set that AT-S6
/// legitimately discovers as 200; unknown paths return 404, keeping the deref
/// load-bearing. Dispose to stop accepting new requests.
type SchemaOrgStub private (listener: HttpListener, port: int) =

    /// Base URL of the loopback stub, e.g. http://localhost:49321
    member _.BaseUrl = sprintf "http://localhost:%d" port

    /// Create and start a new stub listener on a random port.
    /// The accept loop runs in the background; bounded to 100 requests (Holzmann 10).
    static member Start() : SchemaOrgStub =
        let listener, port = SchemaOrgStubImpl.bindHttpListener ()
        SchemaOrgStubImpl.acceptLoop listener 100 |> ignore
        new SchemaOrgStub(listener, port)

    interface IDisposable with
        member _.Dispose() =
            listener.Stop()
            (listener :> IDisposable).Dispose()
