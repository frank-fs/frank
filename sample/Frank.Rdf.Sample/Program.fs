module Sample.Rdf.Program

open System
open System.Buffers
open System.IO
open System.Text
open Microsoft.AspNetCore.Http
open Frank.Builder
open Frank.Rdf

// TextWriter adapter for IBufferWriter<byte>, enabling direct writes to PipeWriter
// without intermediate buffering in StreamWriter.
type private PipeTextWriter(bufferWriter: IBufferWriter<byte>) =
    inherit TextWriter()
    override _.Encoding = Encoding.UTF8
    override _.Write(value: string) =
        if not (isNull value) && value.Length > 0 then
            let byteCount = Encoding.UTF8.GetByteCount(value)
            let buffer = bufferWriter.GetSpan(byteCount)
            let bytesWritten = Encoding.UTF8.GetBytes(value, buffer)
            bufferWriter.Advance(bytesWritten)

// A tiny in-memory "database" -- just enough to have more than one game to curl.
let private games = dict [ "1", "Tic-tac-toe"; "2", "Connect Four" ]

// The plain-JSON representation: no RDF at all, just the game's data as an
// ordinary DTO -- the "direct resource representation" alongside the JSON-LD one.
type GameDto = { id: string; name: string; numberOfPlayers: int }

// Shared across every game's document: who publishes this API. Built once and merged into
// each per-game document via `Doc.merge` -- a genuine reason to merge (facts every resource
// wants to assert), demonstrating Doc.merge for real rather than contriving one.
let private publisher = Node.Iri "https://frank-fs.github.io/#organization"

let private publisherDoc =
    rdf {
        prefix "schema" "https://schema.org/"
        about (describe publisher { typ "schema:Organization"; propertyString "schema:name" "Frank" })
    }

let private gameDoc (baseUri: string) (id: string) (name: string) : Doc =
    let gameUri = Node.Iri $"{baseUri}/games/{id}"
    let players = Node.Iri $"{baseUri}/games/{id}#players"

    let doc =
        rdf {
            prefix "schema" "https://schema.org/"

            about (
                describe gameUri {
                    typ "schema:Game"
                    propertyString "schema:name" name
                    propertyNode "schema:numberOfPlayers" players
                    propertyNode "schema:publisher" publisher
                }
            )

            about (describe players { typ "schema:QuantitativeValue"; propertyInt "schema:value" 2 })
        }

    Doc.merge doc publisherDoc

// `negotiate { }` picks a representation by `Accept`, both serving the SAME
// `/games/{id}` url. Each representation becomes its own `RouteEndpoint`; which one runs is
// decided at the routing layer by `FrankProducesMatcherPolicy`, using the RFC 9110 §12.5.1
// matching in `MediaTypeNegotiation` (src/Frank/MediaTypeNegotiation.fs) -- no dispatching
// function inside the handler. "application/json" is registered first, so it's also what a
// request with no `Accept` header (or an unparseable one) gets. Each representation does its
// own game lookup and 404 handling, independently -- matching Frank.OpenApi.Sample's
// `getProductNegotiated` pattern (deliberately not factored out).
//
// Each handler below is wrapped in an explicit `RequestDelegate(...)` rather than passed as a
// bare lambda. Both handlers write their own response directly and return no value, so a bare
// `fun (ctx: HttpContext) -> task { ... }` with no final `return` infers as
// `HttpContext -> Task<unit>`, which F# overload resolution silently prefers over
// `NegotiateBuilder.Accepts`'s `RequestDelegate` overload (a direct type match beats the
// implicit function-to-delegate conversion the `RequestDelegate` overload needs). That used to
// misroute the handler through the auto-format (`viaOutputFormatter`) overload, which tries to
// set `ContentType` and serialize a return value *after* the handler already wrote the response
// itself -- throwing `InvalidOperationException: Headers are read-only, response has already
// started` and aborting the transfer mid-stream. That's the bug that shipped here once
// (https://github.com/frank-fs/frank/issues/492); `NegotiateBuilder` now carries a dedicated
// `HttpContext -> Task<unit>` overload that dispatches such handlers directly, so a bare lambda
// no longer reintroduces it. The explicit wrapper is kept as the unambiguous form -- it names
// the intent (self-writing handler) rather than relying on overload-resolution order. The
// matcher policy sets `Content-Type` from the winning representation's media type during
// endpoint selection, before this handler runs.
let private getGame =
    negotiate {
        accepts "application/json" (RequestDelegate(fun (ctx: HttpContext) -> task {
            let id = string ctx.Request.RouteValues.["id"]

            match games.TryGetValue id with
            | true, name ->
                do! ctx.Response.WriteAsJsonAsync({ id = id; name = name; numberOfPlayers = 2 })
            | false, _ ->
                ctx.Response.StatusCode <- 404
                do! ctx.Response.WriteAsJsonAsync({| error = $"no game with id {id}" |})
        }))

        accepts "application/ld+json" (RequestDelegate(fun (ctx: HttpContext) -> task {
            let id = string ctx.Request.RouteValues.["id"]

            match games.TryGetValue id with
            | true, name ->
                let baseUri = $"{ctx.Request.Scheme}://{ctx.Request.Host}"
                use writer = new PipeTextWriter(ctx.Response.BodyWriter)
                Doc.writeJsonLd (gameDoc baseUri id name) writer
                do! ctx.Response.BodyWriter.FlushAsync()
            | false, _ -> ctx.Response.StatusCode <- 404
        }))
    }

let private gameResource =
    resource "/games/{id}" {
        link (fun (ctx: HttpContext) ->
            Seq.singleton {
                Target = string ctx.Request.Path
                Rel = "alternate"
                Params = [ "type", "application/ld+json" ]
            })

        get getGame
    }

[<EntryPoint>]
let main args =
    webHost args {
        useDefaults
        resource gameResource
    }

    0
