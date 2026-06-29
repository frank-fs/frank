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

// When FRANK_VOCAB=ex the server uses the ex: namespace throughout ALPS and the
// move handler — the discovery client reads whatever IRIs the server advertises,
// while a hardcoded schema.org client fails (AT-S7 falsifiability lever).
let private isExVocab =
    Environment.GetEnvironmentVariable "FRANK_VOCAB" = "ex"

let private exPrefix = "https://example.org/ex#"

let private activeAgentIri =
    if isExVocab then exPrefix + "agent" else "https://schema.org/agent"

let private activeSquareIri =
    if isExVocab then exPrefix + "square" else "https://example.org/tictactoe#square"

let private isLdJson (ctx: HttpContext) =
    let ct = ctx.Request.ContentType
    ct <> null && ct.Contains("application/ld+json")

let private parseMoveFromDoc (isLd: bool) (doc: JsonNode) =
    if isLd then
        let pos =
            doc.[activeSquareIri] |> Option.ofObj |> Option.map (fun n -> n.GetValue<string>())

        let plr =
            doc.[activeAgentIri] |> Option.ofObj |> Option.map (fun n -> n.GetValue<string>())

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

let private tttVocabTtl =
    let path = Path.Combine(AppContext.BaseDirectory, "vocab", "ttt.ttl")
    File.ReadAllText(path)

let private tttVocabGraph =
    let g = new Graph()
    let parser = TurtleParser()
    use reader = new StringReader(tttVocabTtl)
    parser.Load(g, reader)
    g

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

        post (
            handler {
                handle moveHandler
                accepts typeof<MoveRequest>
                produces typeof<Move> 200
            }
        )
    }

let private tttVocabResource =
    resource "/tictactoe" {
        name "TttVocabulary"
        linkedDataGraph tttVocabGraph """{"@context":{"ttt":"https://example.org/tictactoe#"}}"""

        get (fun (ctx: HttpContext) ->
            task {
                ctx.Response.ContentType <- "text/turtle"
                do! ctx.Response.WriteAsync(tttVocabTtl)
            })
    }

let private exDiscoveryConfig: DiscoveryConfig =
    { ProfileUri = "/alps/tictactoe.v732"
      HomeRoute = "/"
      AlpsDescriptors =
        [ { Id = "ActionStatusType"
            Type = "semantic"
            Doc = None
            Href = Some(exPrefix + "ActionStatusType") }
          { Id = "Game"
            Type = "semantic"
            Doc = None
            Href = Some(exPrefix + "Game") }
          { Id = "identifier"
            Type = "semantic"
            Doc = None
            Href = Some(exPrefix + "identifier") }
          { Id = "result"
            Type = "semantic"
            Doc = None
            Href = Some(exPrefix + "result") }
          { Id = "ItemList"
            Type = "semantic"
            Doc = None
            Href = Some(exPrefix + "ItemList") }
          { Id = "item"
            Type = "semantic"
            Doc = None
            Href = Some(exPrefix + "item") }
          { Id = "numberOfItems"
            Type = "semantic"
            Doc = None
            Href = Some(exPrefix + "numberOfItems") }
          { Id = "MoveAction"
            Type = "semantic"
            Doc = None
            Href = Some(exPrefix + "MoveAction") }
          { Id = "square"
            Type = "semantic"
            Doc = None
            Href = Some(exPrefix + "square") }
          { Id = "agent"
            Type = "semantic"
            Doc = None
            Href = Some(exPrefix + "agent") } ]
      DescribedByLinks =
        [ sprintf "<%sActionStatusType>; rel=\"type\"" exPrefix
          sprintf "<%sGame>; rel=\"type\"" exPrefix
          sprintf "<%sItemList>; rel=\"type\"" exPrefix
          sprintf "<%sMoveAction>; rel=\"type\"" exPrefix ] }

[<EntryPoint>]
let main args =
    let discoConfig =
        if isExVocab then exDiscoveryConfig
        else TicTacToe.GeneratedDiscovery.discoveryConfig

    webHost args {
        useProvenance
        useValidation
        useDiscoveryWith discoConfig
        useLinkedData
        resource homeResource
        resource gameResource
        resource movesResource
        resource tttVocabResource
    }

    0
