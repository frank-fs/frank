module TicTacToe.Program

open System
open System.IO
open System.Text.Json.Nodes
open Microsoft.AspNetCore.Http
open VDS.RDF
open VDS.RDF.Parsing
open VDS.RDF.Writing
open Frank
open Frank.Builder
open Frank.Discovery
open TicTacToe.Model
open TicTacToe.GameStore

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

let rec private findDescriptorHrefIn (id: string) (descriptors: Frank.Discovery.AlpsDescriptor list) : string option =
    descriptors
    |> List.tryPick (fun d ->
        if d.Id = id then
            d.Href
        else
            findDescriptorHrefIn id d.Descriptors)

let private findDescriptorHref (id: string) =
    findDescriptorHrefIn id TicTacToe.GeneratedDiscovery.discoveryConfig.AlpsDescriptors
    |> Option.defaultWith (fun () -> invalidOp $"ALPS descriptor '{id}' not found in discoveryConfig")

let private agentRelIri = findDescriptorHref "agent"
let private squareRelIri = findDescriptorHref "cell"

let private isLdJson (ctx: HttpContext) =
    let ct = ctx.Request.ContentType
    ct <> null && ct.Contains("application/ld+json")

let private parseMoveFromDoc (origin: string) (isLd: bool) (doc: JsonNode) =
    // Resolve the SAME codegen-emitted href the ALPS profile itself serves, against the
    // live request origin — DiscoveryMiddleware.resolveHref is the single source of this
    // resolution rule (#398), never reimplemented here.
    let sq = DiscoveryMiddleware.resolveHref origin squareRelIri
    let ag = DiscoveryMiddleware.resolveHref origin agentRelIri

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

let private homeHandler (ctx: HttpContext) =
    task { do! ctx.Response.WriteAsync("Frank TicTacToe v7.3.2 — ex: vocab") }

let private gameHandler (ctx: HttpContext) =
    task {
        let id = routeId ctx

        let game: Game =
            { Id = id
              Result = store.GetOrCreate id }

        do! writeJson ctx (wireJson game.Id game.Result)
    }

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

let private exVocabTtl =
    let path = Path.Combine(AppContext.BaseDirectory, "vocab", "ex.ttl")
    File.ReadAllText(path)

let private loadExVocabGraph (ctx: HttpContext) : IGraph =
    let origin = $"{ctx.Request.Scheme}://{ctx.Request.Host}"
    let g = new Graph()
    g.BaseUri <- Uri origin
    let parser = TurtleParser()
    use reader = new StringReader(exVocabTtl)
    parser.Load(g, reader)
    g :> IGraph

let private buildExTurtleBody (origin: string) (graph: IGraph) : string =
    use sw = new System.IO.StringWriter()
    let writer = CompressingTurtleWriter()
    writer.Save(graph, sw :> System.IO.TextWriter)

    if isNull (box graph.BaseUri) then
        "@base <" + origin + "> .\n" + sw.ToString()
    else
        sw.ToString()

let private homeResource =
    resource "/" {
        name "Home"
        get homeHandler
    }

let private gameResource =
    resource "/games/{id}" {
        name "Game"
        entryPoint
        relation (TicTacToe.GeneratedSemantics.SemanticResource.Game.Iri.AbsoluteUri)
        get gameHandler
        post moveHandler
    }

let private exVocabResource =
    resource "/ex" {
        name "ExVocabulary"

        get (fun (ctx: HttpContext) ->
            task {
                let origin = $"{ctx.Request.Scheme}://{ctx.Request.Host}"
                let graph = loadExVocabGraph ctx
                ctx.Response.ContentType <- "text/turtle"
                do! ctx.Response.WriteAsync(buildExTurtleBody origin graph)
            })
    }

[<EntryPoint>]
let main args =
    webHost args {
        useDiscoveryWith TicTacToe.GeneratedDiscovery.discoveryConfig
        resource homeResource
        resource gameResource
        resource exVocabResource
    }

    0
