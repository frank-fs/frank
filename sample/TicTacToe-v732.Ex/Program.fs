module TicTacToe.Program

open System.IO
open System.Text.Json.Nodes
open Microsoft.AspNetCore.Http
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

let private findDescriptorHref (id: string) =
    TicTacToe.GeneratedDiscovery.discoveryConfig.AlpsDescriptors
    |> List.tryFind (fun d -> d.Id = id)
    |> Option.bind (fun d -> d.Href)
    |> Option.defaultWith (fun () -> invalidOp $"ALPS descriptor '{id}' not found in discoveryConfig")

let private agentIri = findDescriptorHref "agent"
let private squareIri = findDescriptorHref "square"

let private isLdJson (ctx: HttpContext) =
    let ct = ctx.Request.ContentType
    ct <> null && ct.Contains("application/ld+json")

let private parseMoveFromDoc (isLd: bool) (doc: JsonNode) =
    if isLd then
        let pos =
            doc.[squareIri] |> Option.ofObj |> Option.map (fun n -> n.GetValue<string>())

        let plr =
            doc.[agentIri] |> Option.ofObj |> Option.map (fun n -> n.GetValue<string>())

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
        let position, player = parseMoveFromDoc ld doc

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
        get gameHandler
    }

let private movesResource =
    resource "/games/{id}/moves" {
        name "GameMoves"

        relation (
            (TicTacToe.GeneratedSemantics.iri TicTacToe.GeneratedSemantics.SemanticResource.MoveRequest).AbsoluteUri
        )

        post moveHandler
    }

[<EntryPoint>]
let main args =
    webHost args {
        useDiscoveryWith TicTacToe.GeneratedDiscovery.discoveryConfig
        resource homeResource
        resource gameResource
        resource movesResource
    }

    0
