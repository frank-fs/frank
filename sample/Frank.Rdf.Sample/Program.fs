module Sample.Rdf.Program

open System.Text
open System.IO
open Microsoft.AspNetCore.Http
open Frank.Builder
open Frank.Rdf

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
// `/games/{id}` url. "application/json" is registered first, so it's also
// what a request with no `Accept` header (or an unparseable one) gets --
// see NegotiateBuilder.fsi. Each representation does its own game lookup
// and 404 handling, independently -- matching Frank.OpenApi.Sample's
// `getProductNegotiated` pattern (deliberately not factored out).
let private getGame =
    negotiate {
        accepts "application/json" (fun (ctx: HttpContext) -> task {
            let id = string ctx.Request.RouteValues.["id"]

            match games.TryGetValue id with
            | true, name ->
                do! ctx.Response.WriteAsJsonAsync({ id = id; name = name; numberOfPlayers = 2 })
            | false, _ ->
                ctx.Response.StatusCode <- 404
                do! ctx.Response.WriteAsJsonAsync({| error = $"no game with id {id}" |})
        })

        accepts "application/ld+json" (fun (ctx: HttpContext) -> task {
            let id = string ctx.Request.RouteValues.["id"]

            match games.TryGetValue id with
            | true, name ->
                let baseUri = $"{ctx.Request.Scheme}://{ctx.Request.Host}"
                use writer = new StreamWriter(ctx.Response.Body, Encoding.UTF8, leaveOpen = true)
                Doc.writeJsonLd (gameDoc baseUri id name) writer
                do! writer.FlushAsync()
            | false, _ -> ctx.Response.StatusCode <- 404
        })
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
