module TicTacToe.Program

open System
open System.IO
open System.Text.Json
open System.Text.Json.Nodes
open System.Threading.Tasks
open Microsoft.AspNetCore.Builder
open Microsoft.AspNetCore.Http
open VDS.RDF
open VDS.RDF.Parsing
open VDS.RDF.Writing
open Frank
open Frank.Builder
open Frank.Discovery
open Frank.LinkedData
open Frank.OpenApi
open Frank.Provenance
open Frank.Validation
open TicTacToe.Model
open TicTacToe.GameStore

/// Single in-memory store for this sample app.
let private store = GameStore()

let private allPositions =
    [ TopLeft
      TopCenter
      TopRight
      MiddleLeft
      MiddleCenter
      MiddleRight
      BottomLeft
      BottomCenter
      BottomRight ]

let private playerName (p: Player) = p.ToString()

let private squaresJson (gameState: GameState) : JsonObject =
    let obj = JsonObject()

    for pos in allPositions do
        let value =
            match gameState.TryGetValue pos with
            | true, Taken p -> JsonValue.Create(playerName p) :> JsonNode
            | _ -> null

        obj.[pos.ToString()] <- value

    obj

let private positionsJson (positions: SquarePosition seq) : JsonArray =
    let arr = JsonArray()

    for pos in positions do
        arr.Add(JsonValue.Create(pos.ToString()))

    arr

let private getGameState (result: MoveResult) : GameState =
    match result with
    | XTurn(gs, _)
    | OTurn(gs, _)
    | Won(gs, _)
    | Error(gs, _) -> gs
    | Draw gs -> gs

/// Project a MoveResult into the wire shape the naive client reads.
let private wireJson (id: string) (result: MoveResult) : JsonObject =
    let gs = getGameState result
    let obj = JsonObject()
    obj.["id"] <- JsonValue.Create id
    obj.["squares"] <- squaresJson gs

    let status, current, winner, valid =
        match result with
        | XTurn(_, moves) -> "XTurn", Some "X", None, moves |> Array.map (fun (XPos p) -> p)
        | OTurn(_, moves) -> "OTurn", Some "O", None, moves |> Array.map (fun (OPos p) -> p)
        | Won(_, p) -> "Won", None, Some(playerName p), [||]
        | Draw _ -> "Draw", None, None, [||]
        | Error _ -> "Error", None, None, [||]

    obj.["status"] <- JsonValue.Create status

    obj.["currentPlayer"] <-
        (match current with
         | Some c -> JsonValue.Create c :> JsonNode
         | None -> null)

    obj.["winner"] <-
        (match winner with
         | Some w -> JsonValue.Create w :> JsonNode
         | None -> null)

    obj.["validMoves"] <- positionsJson valid
    obj

let private writeJson (ctx: HttpContext) (node: JsonNode) =
    ctx.Response.ContentType <- "application/json"
    ctx.Response.WriteAsync(node.ToJsonString())

let private routeId (ctx: HttpContext) =
    ctx.Request.RouteValues.["id"] :?> string

let private homeHandler (ctx: HttpContext) =
    task { do! ctx.Response.WriteAsync("Frank TicTacToe v7.3.2") }

let private gameHandler (ctx: HttpContext) =
    task {
        let id = routeId ctx

        let game: Game =
            { Id = id
              Result = store.GetOrCreate id }

        do! writeJson ctx (wireJson game.Id game.Result)
    }

let rec private findDescriptorHrefIn (id: string) (descriptors: Frank.Discovery.AlpsDescriptor list) : string option =
    descriptors
    |> List.tryPick (fun d ->
        if d.Id = id then d.Href
        else findDescriptorHrefIn id d.Descriptors)

let private findDescriptorHref (id: string) =
    findDescriptorHrefIn id TicTacToe.GeneratedDiscovery.discoveryConfig.AlpsDescriptors
    |> Option.defaultWith (fun () -> invalidOp $"ALPS descriptor '{id}' not found in discoveryConfig")

let private agentRelIri = findDescriptorHref "agent"
let private squareRelIri = findDescriptorHref "square"

let private resolveRelativeIri (origin: string) (iri: string) =
    if iri.StartsWith "/" then origin + iri else iri

let private isLdJson (ctx: HttpContext) =
    let ct = ctx.Request.ContentType
    ct <> null && ct.Contains("application/ld+json")

let private parseMoveFromDoc (origin: string) (isLd: bool) (doc: JsonNode) =
    let sq = resolveRelativeIri origin squareRelIri
    let ag = resolveRelativeIri origin agentRelIri

    if isLd then
        let pos = doc.[sq] |> Option.ofObj |> Option.map (fun n -> n.GetValue<string>())
        let plr = doc.[ag] |> Option.ofObj |> Option.map (fun n -> n.GetValue<string>())
        pos, plr
    else
        let pos =
            doc.["position"] |> Option.ofObj |> Option.map (fun n -> n.GetValue<string>())

        let plr =
            doc.["player"] |> Option.ofObj |> Option.map (fun n -> n.GetValue<string>())

        pos, plr

let private moveHandler (ctx: HttpContext) =
    task {
        let id = routeId ctx
        use reader = new StreamReader(ctx.Request.Body)
        let! body = reader.ReadToEndAsync()
        let doc = JsonNode.Parse body
        let ld = isLdJson ctx
        let origin = $"{ctx.Request.Scheme}://{ctx.Request.Host}"
        let position, player = parseMoveFromDoc origin ld doc

        match position, player with
        | Some pos, Some plr ->
            match Move.TryParse(plr, pos) with
            | None ->
                ctx.Response.StatusCode <- 400
                do! ctx.Response.WriteAsync("""{"title":"Unparseable move"}""")
            | Some move ->
                match store.Update(id, move) with
                | None -> ctx.Response.StatusCode <- 404
                | Some(Error(_, msg)) ->
                    ctx.Response.StatusCode <- 409
                    do! writeJson ctx (JsonObject(dict [ "title", (JsonValue.Create msg :> JsonNode) ]))
                | Some result -> do! writeJson ctx (wireJson id result)
        | _ ->
            ctx.Response.StatusCode <- 400
            do! ctx.Response.WriteAsync("""{"title":"Missing position or player"}""")
    }

let private tttVocabTtl =
    let path = Path.Combine(AppContext.BaseDirectory, "vocab", "ttt.ttl")
    File.ReadAllText(path)

/// Build the ttt vocabulary graph with term IRIs resolved to the request origin.
let private loadTttVocabGraph (ctx: HttpContext) : IGraph =
    let origin = $"{ctx.Request.Scheme}://{ctx.Request.Host}"
    let g = new Graph()
    g.BaseUri <- Uri origin
    let parser = TurtleParser()
    use reader = new StringReader(tttVocabTtl)
    parser.Load(g, reader)
    g :> IGraph

let private buildTurtleBody (origin: string) (graph: IGraph) : string =
    use sw = new System.IO.StringWriter()
    let writer = CompressingTurtleWriter()
    writer.Save(graph, sw :> System.IO.TextWriter)
    // When graph.BaseUri is set the writer already emitted @base; avoid duplicating it.
    if isNull (box graph.BaseUri) then
        "@base <" + origin + "> .\n" + sw.ToString()
    else
        sw.ToString()

/// Build a per-game-instance RDF graph with schema: and ttt: terms.
/// Subject = <origin>/games/<id>; predicates are host-resolved, no example.org.
let private buildGameGraph (origin: string) (id: string) (result: MoveResult) : IGraph =
    let schemaPrefix = "https://schema.org/"
    let tttBase = origin + "/tictactoe#"
    let gameIri = origin + "/games/" + id
    let g = new Graph()
    g.NamespaceMap.AddNamespace("schema", UriFactory.Create schemaPrefix)
    g.NamespaceMap.AddNamespace("ttt", UriFactory.Create tttBase)
    let gameSubj = g.CreateUriNode(UriFactory.Create gameIri)
    let identifierPred = g.CreateUriNode(UriFactory.Create(schemaPrefix + "identifier"))
    g.Assert(Triple(gameSubj, identifierPred, g.CreateLiteralNode(id))) |> ignore
    let actionStatusPred = g.CreateUriNode(UriFactory.Create(schemaPrefix + "actionStatus"))

    let statusIri =
        match result with
        | XTurn _ | OTurn _ -> schemaPrefix + "ActiveActionStatus"
        | Won _ | Draw _ -> schemaPrefix + "CompletedActionStatus"
        | Error _ -> schemaPrefix + "FailedActionStatus"

    g.Assert(Triple(gameSubj, actionStatusPred, g.CreateUriNode(UriFactory.Create statusIri))) |> ignore

    match result with
    | XTurn(_, moves) ->
        let currentPlayerPred = g.CreateUriNode(UriFactory.Create(tttBase + "currentPlayer"))
        g.Assert(Triple(gameSubj, currentPlayerPred, g.CreateLiteralNode "X")) |> ignore
        let validMovesPred = g.CreateUriNode(UriFactory.Create(tttBase + "validMoves"))

        for XPos pos in moves do
            g.Assert(Triple(gameSubj, validMovesPred, g.CreateLiteralNode(pos.ToString()))) |> ignore

    | OTurn(_, moves) ->
        let currentPlayerPred = g.CreateUriNode(UriFactory.Create(tttBase + "currentPlayer"))
        g.Assert(Triple(gameSubj, currentPlayerPred, g.CreateLiteralNode "O")) |> ignore
        let validMovesPred = g.CreateUriNode(UriFactory.Create(tttBase + "validMoves"))

        for OPos pos in moves do
            g.Assert(Triple(gameSubj, validMovesPred, g.CreateLiteralNode(pos.ToString()))) |> ignore

    | _ -> ()

    g :> IGraph

/// Per-request game graph factory: reads route id, looks up game state, builds instance graph.
let private gameGraphFactory (ctx: HttpContext) : IGraph =
    let id = ctx.Request.RouteValues.["id"] :?> string
    let origin = $"{ctx.Request.Scheme}://{ctx.Request.Host}"
    buildGameGraph origin id (store.GetOrCreate id)

let private homeResource =
    resource "/" {
        name "Home"
        get homeHandler
    }

let private gameResource =
    resource "/games/{id}" {
        name "Game"
        entryPoint
        relation ((TicTacToe.GeneratedSemantics.iri TicTacToe.GeneratedSemantics.SemanticResource.Game).AbsoluteUri)

        linkedDataGraphWith
            { Graph = Unchecked.defaultof<IGraph>
              JsonLdContext = """{"@context":["https://schema.org"]}"""
              GraphFactory = Some gameGraphFactory }

        get gameHandler
    }

let private movesResource =
    resource "/games/{id}/moves" {
        name "GameMoves"

        relation (
            (TicTacToe.GeneratedSemantics.iri TicTacToe.GeneratedSemantics.SemanticResource.MoveRequest).AbsoluteUri
        )

        post (
            handler {
                handle moveHandler
                accepts typeof<MoveRequest>
                produces typeof<MoveResult> 200
            }
        )
    }

let private tttVocabResource =
    resource "/tictactoe" {
        name "TttVocabulary"

        linkedDataGraphWith
            { Graph = Unchecked.defaultof<IGraph>
              JsonLdContext = """{"@context":{}}"""
              GraphFactory = Some loadTttVocabGraph }

        get (fun (ctx: HttpContext) ->
            task {
                let origin = $"{ctx.Request.Scheme}://{ctx.Request.Host}"
                let graph = loadTttVocabGraph ctx
                ctx.Response.ContentType <- "text/turtle"
                do! ctx.Response.WriteAsync(buildTurtleBody origin graph)
            })
    }

[<EntryPoint>]
let main args =
    webHost args {
        useProvenance
        useValidation
        useDiscoveryWith TicTacToe.GeneratedDiscovery.discoveryConfig
        useLinkedData
        resource homeResource
        resource gameResource
        resource movesResource
        resource tttVocabResource
    }

    0
