module TicTacToe.Program

open System
open System.IO
open System.Text.Json.Nodes
open Microsoft.AspNetCore.Http
open VDS.RDF
open VDS.RDF.Writing
open Frank
open Frank.Builder
open Frank.Discovery
open Frank.LinkedData
open Frank.OpenApi
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

/// The full, un-relativized class IRI DiscoveryEmitter baked for a top-level ALPS
/// descriptor (AlpsDescriptor.ClassIri — never itself relativized, #397/#398/#411's
/// correlation-key invariant). `relation`'s CE argument must equal this EXACT value so
/// DiscoveryMiddleware's relation→ClassIri correlation (rel="type" Link header scoping,
/// JSON Home grouping) keeps matching. Sourced from Discovery's own generated config —
/// not from SemanticModelEmitter.SemanticResource.*.Iri, which is now genuinely
/// host-relative for declared-only prefixes like ex: (#415) and cannot serve as an
/// always-absolute correlation key.
let private findDescriptorClassIri (id: string) =
    TicTacToe.GeneratedDiscovery.discoveryConfig.AlpsDescriptors
    |> List.tryPick (fun d -> if d.Id = id then d.ClassIri else None)
    |> Option.defaultWith (fun () -> invalidOp $"ALPS descriptor '{id}' has no ClassIri in discoveryConfig")

let private gameClassIri = findDescriptorClassIri "Game"

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

/// Parse and apply the move body against `id`, once `origin` is known-valid — the SAME
/// origin-validation discipline handleAlpsProfile already applies before minting any
/// origin-resolved href (#398 /simplify item 7).
let private performMove (ctx: HttpContext) (origin: string) (id: string) =
    task {
        use reader = new StreamReader(ctx.Request.Body)
        let! body = reader.ReadToEndAsync()
        let doc = JsonNode.Parse body
        let ld = isLdJson ctx
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

/// A malformed Host header cannot mint resolvable hrefs (resolveHref's Uri(Uri origin, _)
/// throws for it) — reject with 400 before ever calling parseMoveFromDoc/resolveHref,
/// matching handleAlpsProfile's own behavior instead of crashing with an unhandled
/// UriFormatException (#398 /simplify item 7).
let private moveHandler (ctx: HttpContext) =
    task {
        match Frank.OriginValidation.tryValidateOrigin ctx.Request with
        | None ->
            ctx.Response.StatusCode <- 400
            do! ctx.Response.WriteAsync("""{"title":"Malformed Host header"}""")
        | Some origin ->
            let id = routeId ctx
            do! performMove ctx origin id
    }

/// Turtle serialization shared by every declared-only-vocab route — mirrors the
/// schema: sample's identical helper (Program.fs). `graph.BaseUri` is already set by
/// GeneratedLinkedData.graphFor (via Ontology.toGraph's baseUri resolution), so no
/// hand-rolled "@base <origin>" injection is needed here (#415: that ad hoc mechanism
/// is deleted, not reimplemented).
let private buildTurtleBody (graph: IGraph) : string =
    use sw = new System.IO.StringWriter()
    let writer = CompressingTurtleWriter()
    writer.Save(graph, sw :> System.IO.TextWriter)
    sw.ToString()

/// Per-request factory for the app's declared-vocabulary-mapping ontology (every
/// resource→vocab class/property mapping declared in Vocabulary.Ex.fs), rebased against
/// the real request origin — the SAME shared mechanism (EmitterShared.declaredOnlyBases +
/// Ontology.toGraph's baseUri resolution via Frank.UriResolution.resolveAgainst) Discovery
/// and LinkedData already use elsewhere (#415). Never bakes in a codegen-time placeholder
/// domain — ex.ttl's hand-authored, hand-rebased duplicate of this same information is
/// deleted (#415 AC4).
let private exVocabularyGraphFactory (ctx: HttpContext) : IGraph =
    let origin = Uri $"{ctx.Request.Scheme}://{ctx.Request.Host}"
    TicTacToe.GeneratedLinkedData.graphFor origin

/// LinkedDataConfig.JsonLdContext is a fixed string (no per-request factory), so this is
/// computed once at module load — mirrors the schema: sample's identical pattern.
/// jsonLdContextFor's baseUri is never used to rebase ContextBases entries (ex: declares
/// no `using` prefixes, so ContextBases is always empty here) — see Ontology.
/// toJsonLdContext's own contract.
let private exVocabularyJsonLdContext =
    TicTacToe.GeneratedLinkedData.jsonLdContextFor (Uri "http://placeholder.invalid")

let private homeResource =
    resource "/" {
        name "Home"
        get homeHandler
    }

let private gameResource =
    resource "/games/{id}" {
        name "Game"
        entryPoint
        relation gameClassIri
        get gameHandler

        // #400 AC2: accepts typeof<MoveRequest> stamps IAcceptsMetadata on this POST —
        // without it, Frank.Discovery's live HTTP-method correlation has no way to
        // resolve MoveAction's ALPS Type (its own ClassIri, ex:MoveAction, is never a
        // declared route relation) and the codegen Rt-based fallback survives
        // unreconciled. Mirrors the schema: sample's identical pattern (Program.fs).
        post (
            handler {
                handle moveHandler
                accepts typeof<MoveRequest>
            }
        )
    }

let private exVocabResource =
    resource "/ex" {
        name "ExVocabulary"

        linkedDataGraphWith
            { Graph = Unchecked.defaultof<IGraph>
              JsonLdContext = exVocabularyJsonLdContext
              GraphFactory = Some exVocabularyGraphFactory
              VocabularyUri = None }

        get (fun (ctx: HttpContext) ->
            task {
                let graph = exVocabularyGraphFactory ctx
                ctx.Response.ContentType <- "text/turtle"
                do! ctx.Response.WriteAsync(buildTurtleBody graph)
            })
    }

[<EntryPoint>]
let main args =
    webHost args {
        useDiscoveryWith TicTacToe.GeneratedDiscovery.discoveryConfig
        useLinkedData
        resource homeResource
        resource gameResource
        resource exVocabResource
    }

    0
