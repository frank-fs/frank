module TicTacToe.Program

open System
open System.Text.Json
open Microsoft.AspNetCore.Builder
open Microsoft.AspNetCore.Http
open Microsoft.Extensions.DependencyInjection
open VDS.RDF.Shacl
open Frank.Discovery
open Frank.Discovery.DiscoveryMiddleware
open Frank.LinkedData.LinkedDataMiddleware
open Frank.Validation.ValidationMiddleware
open Frank.Provenance.ProvenanceMiddleware
open Frank.Provenance.ProvenanceStore
open TicTacToe.Types
open TicTacToe.Vocabulary

// JSON serialization helpers
let private opts = JsonSerializerOptions(PropertyNamingPolicy = JsonNamingPolicy.CamelCase)

let private boardToJson (board: Cell[,]) =
    [| for r in 0..2 ->
        [| for c in 0..2 ->
            match board.[r, c] with
            | Empty -> null :> obj
            | Taken X -> "X" :> obj
            | Taken O -> "O" :> obj |] |]

let private gameToJson (game: Game) =
    {| id = game.Id
       board = boardToJson game.Board
       currentPlayer = string game.CurrentPlayer
       status = string game.Status |}

[<EntryPoint>]
let main args =
    let shapesGraph = buildShapesGraph ()
    let graph = buildGraph ()
    let discoveryConfig = buildDiscoveryConfig ()

    let builder = WebApplication.CreateBuilder(args)
    builder.Services.AddRouting() |> ignore
    builder.Services.AddSingleton<ShapesGraph>(shapesGraph) |> ignore
    builder.Services.AddSingleton<LinkedDataConfig>({ Graph = graph; JsonLdContext = jsonLdContext }) |> ignore
    builder.Services.AddSingleton<DiscoveryConfig>(discoveryConfig) |> ignore
    builder.Services.AddSingleton<ProvenanceConfig>(provenanceConfig) |> ignore
    builder.Services.AddSingleton<ProvenanceStore>(provenanceConfig.Store) |> ignore

    let rdfTypes = [| "application/ld+json"; "text/turtle"; "application/rdf+xml" |]
    let isRdfAccept (ctx: HttpContext) =
        ctx.Request.Headers.ContainsKey("Accept") &&
        rdfTypes |> Array.exists (fun t -> ctx.Request.Headers["Accept"].ToString().Contains(t))
    let isLdJsonPost (ctx: HttpContext) =
        ctx.Request.Method = "POST" &&
        ctx.Request.Headers.ContainsKey("Content-Type") &&
        ctx.Request.Headers["Content-Type"].ToString().Contains("application/ld+json")

    let app = builder.Build()
    app.UseRouting() |> ignore
    // ValidationMiddleware only on ld+json POST requests to /games/**
    app.UseWhen(
        (fun ctx -> ctx.Request.Path.StartsWithSegments(PathString("/games")) && isLdJsonPost ctx),
        fun branch -> branch.UseMiddleware<ValidationMiddleware>() |> ignore) |> ignore
    // LinkedDataMiddleware only when client explicitly requests an RDF format
    app.UseWhen(isRdfAccept,
        fun branch -> branch.UseMiddleware<LinkedDataMiddleware>() |> ignore) |> ignore
    app.UseMiddleware<ProvenanceMiddleware>() |> ignore
    app.UseMiddleware<OptionsEnricherMiddleware>() |> ignore

    // GET / — JSON Home or plain text
    app.MapGet("/", Func<HttpContext, Threading.Tasks.Task>(fun ctx ->
        task {
            let accept = if ctx.Request.Headers.ContainsKey("Accept") then ctx.Request.Headers["Accept"].ToString() else ""
            if accept.Contains("application/json-home") then
                let resources = buildDiscoveryConfig ()
                let entries =
                    resources.DescribedByLinks
                    |> Map.toList
                    |> List.map (fun (typeName, links) ->
                        let rel =
                            match links with
                            | link :: _ ->
                                let s = link.IndexOf('<') + 1
                                let e = link.IndexOf('>')
                                if s > 0 && e > s then link.[s..e-1] else typeName
                            | [] -> typeName
                        { Frank.Discovery.JsonHomeSerializer.Relation = rel
                          Frank.Discovery.JsonHomeSerializer.Href = "/games/{gameId}"
                          Frank.Discovery.JsonHomeSerializer.Allow = ["GET";"HEAD";"POST";"OPTIONS"] })
                ctx.Response.ContentType <- "application/json-home+json"
                do! ctx.Response.WriteAsync(Frank.Discovery.JsonHomeSerializer.serialize entries)
            else
                do! ctx.Response.WriteAsync("Frank TicTacToe v7.3.2")
        } :> Threading.Tasks.Task)) |> ignore

    // GET /alps/{slug} — ALPS profile
    app.MapGet("/alps/{slug}", Func<HttpContext, Threading.Tasks.Task>(fun ctx ->
        task {
            ctx.Response.ContentType <- "application/alps+json"
            do! ctx.Response.WriteAsync(Frank.Discovery.AlpsSerializer.serialize discoveryConfig.AlpsDescriptors)
        } :> Threading.Tasks.Task)) |> ignore

    // GET /games/{id} — game state (JSON, JSON-LD, Turtle handled by LinkedDataMiddleware)
    app.MapGet("/games/{id}", Func<HttpContext, Threading.Tasks.Task>(fun ctx ->
        task {
            let id = ctx.Request.RouteValues.["id"] :?> string
            let game = GameStore.getOrCreate id
            ctx.Response.ContentType <- "application/json"
            do! ctx.Response.WriteAsync(JsonSerializer.Serialize(gameToJson game, opts))
        } :> Threading.Tasks.Task)) |> ignore

    // POST /games/{id}/moves — make a move
    app.MapPost("/games/{id}/moves", Func<HttpContext, Threading.Tasks.Task>(fun ctx ->
        task {
            let id = ctx.Request.RouteValues.["id"] :?> string
            use reader = new System.IO.StreamReader(ctx.Request.Body)
            let! body = reader.ReadToEndAsync()
            let jsonDoc = JsonDocument.Parse(body)

            let getInt (iri: string) =
                try
                    let mutable el = Unchecked.defaultof<JsonElement>
                    if jsonDoc.RootElement.TryGetProperty(iri, &el) then
                        Some (el.GetInt32())
                    else None
                with _ -> None

            let getString (iri: string) =
                try
                    let mutable el = Unchecked.defaultof<JsonElement>
                    if jsonDoc.RootElement.TryGetProperty(iri, &el) then
                        Some (el.GetString())
                    else None
                with _ -> None

            let row = getInt RowIndexIri
            let col = getInt ColIndexIri
            let playerStr = getString AgentIri

            match row, col, playerStr with
            | None, _, _ | _, None, _ ->
                ctx.Response.StatusCode <- 422
                do! ctx.Response.WriteAsync("""{"type": "https://schema.org/rowIndex", "title": "Missing required move fields"}""")
            | _, _, None ->
                ctx.Response.StatusCode <- 422
                do! ctx.Response.WriteAsync("""{"type": "https://schema.org/agent", "title": "Missing player"}""")
            | Some r, Some c, Some p ->
                let player = if p = "X" then X else O
                let game = GameStore.getOrCreate id
                match Game.applyMove game r c player with
                | Error msg ->
                    ctx.Response.StatusCode <- 409
                    do! ctx.Response.WriteAsync($"""{{ "title": "{msg}" }}""")
                | Ok newGame ->
                    GameStore.update newGame |> ignore
                    ctx.Response.ContentType <- "application/json"
                    do! ctx.Response.WriteAsync(JsonSerializer.Serialize(gameToJson newGame, opts))
        } :> Threading.Tasks.Task)) |> ignore

    app.Run()
    0
