module Sample.Rdf.Program

open System.Text
open System.IO
open Microsoft.AspNetCore.Http
open Frank.Builder
open Frank.Rdf

// A tiny in-memory "database" -- just enough to have more than one game to curl.
let private games = dict [ "1", "Tic-tac-toe"; "2", "Connect Four" ]

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

let private getGame: RequestDelegate =
    RequestDelegate(fun ctx ->
        task {
            let id = string ctx.Request.RouteValues.["id"]

            match games.TryGetValue id with
            | true, name ->
                let baseUri = $"{ctx.Request.Scheme}://{ctx.Request.Host}"
                ctx.Response.ContentType <- "application/ld+json"
                use writer = new StreamWriter(ctx.Response.Body, Encoding.UTF8, leaveOpen = true)
                Doc.writeJsonLd (gameDoc baseUri id name) writer
                do! writer.FlushAsync()
            | false, _ -> ctx.Response.StatusCode <- 404
        })

let private gameResource = resource "/games/{id}" { get getGame }

[<EntryPoint>]
let main args =
    webHost args {
        useDefaults
        resource gameResource
    }

    0
