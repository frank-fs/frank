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
        if d.Id = id then
            d.Href
        else
            findDescriptorHrefIn id d.Descriptors)

let private findDescriptorHref (id: string) =
    findDescriptorHrefIn id TicTacToe.GeneratedDiscovery.discoveryConfig.AlpsDescriptors
    |> Option.defaultWith (fun () -> invalidOp $"ALPS descriptor '{id}' not found in discoveryConfig")

let private agentRelIri = findDescriptorHref "agent"
let private squareRelIri = findDescriptorHref "square"

let private isLdJson (ctx: HttpContext) =
    let ct = ctx.Request.ContentType
    ct <> null && ct.Contains("application/ld+json")

/// Accumulate prefix→expansion pairs from a single JSON-LD @context object.
/// Skips @keyword entries and non-string values.
let private addPrefixesFromObj (acc: Map<string, string>) (o: JsonObject) : Map<string, string> =
    o
    |> Seq.fold
        (fun m kv ->
            if kv.Key.StartsWith "@" then
                m
            else
                match kv.Value with
                | :? JsonValue as jv ->
                    try
                        m |> Map.add kv.Key (jv.GetValue<string>())
                    with _ ->
                        m
                | _ -> m)
        acc

/// Extract prefix→expansion mappings from the @context of a JSON-LD document.
/// Supports both plain-object and array-of-objects @context forms.
let private extractContextPrefixes (doc: JsonNode) : Map<string, string> =
    match doc.["@context"] |> Option.ofObj with
    | None -> Map.empty
    | Some(:? JsonObject as o) -> addPrefixesFromObj Map.empty o
    | Some(:? JsonArray as arr) ->
        arr
        |> Seq.fold
            (fun acc item ->
                match item with
                | :? JsonObject as o -> addPrefixesFromObj acc o
                | _ -> acc)
            Map.empty
    | _ -> Map.empty

/// Try finding the compacted CURIE form of a full IRI as a key in doc.
/// Returns the node when the prefix map covers the IRI and that key exists.
let private tryCompactLookup (prefixes: Map<string, string>) (doc: JsonNode) (fullIri: string) : JsonNode option =
    prefixes
    |> Map.tryPick (fun pfx expansion ->
        if
            expansion.Length > 0
            && fullIri.StartsWith(expansion)
            && fullIri.Length > expansion.Length
        then
            doc.[pfx + ":" + fullIri.Substring(expansion.Length)] |> Option.ofObj
        else
            None)

/// Look up a full IRI as a body key: tries the full IRI directly, then the compacted CURIE.
let private lookupByIri (prefixes: Map<string, string>) (doc: JsonNode) (iri: string) : JsonNode option =
    doc.[iri]
    |> Option.ofObj
    |> Option.orElseWith (fun () -> tryCompactLookup prefixes doc iri)

let private parseMoveFromDoc (origin: string) (isLd: bool) (doc: JsonNode) =
    // Resolve the SAME codegen-emitted href the ALPS profile itself serves, against the
    // live request origin — DiscoveryMiddleware.resolveHref is the single source of this
    // resolution rule (#398), never reimplemented here.
    let sq = DiscoveryMiddleware.resolveHref origin squareRelIri
    let ag = DiscoveryMiddleware.resolveHref origin agentRelIri

    if isLd then
        let prefixes = extractContextPrefixes doc
        let pos = lookupByIri prefixes doc sq |> Option.map (fun n -> n.GetValue<string>())
        let plr = lookupByIri prefixes doc ag |> Option.map (fun n -> n.GetValue<string>())
        pos, plr
    else
        let pos =
            doc.["position"] |> Option.ofObj |> Option.map (fun n -> n.GetValue<string>())

        let plr =
            doc.["player"] |> Option.ofObj |> Option.map (fun n -> n.GetValue<string>())

        pos, plr

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
/// UriFormatException (#398 /simplify item 7). ProvenanceMiddleware already guards this
/// route upstream today, but moveHandler must not depend on that ordering for its own
/// correctness — defense in depth, consistent with the ex: sample which has no such guard.
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

    let actionStatusPred =
        g.CreateUriNode(UriFactory.Create(schemaPrefix + "actionStatus"))

    let statusIri =
        match result with
        | XTurn _
        | OTurn _ -> schemaPrefix + "ActiveActionStatus"
        | Won _
        | Draw _ -> schemaPrefix + "CompletedActionStatus"
        | Error _ -> schemaPrefix + "FailedActionStatus"

    g.Assert(Triple(gameSubj, actionStatusPred, g.CreateUriNode(UriFactory.Create statusIri)))
    |> ignore

    match result with
    | XTurn(_, moves) ->
        let currentPlayerPred =
            g.CreateUriNode(UriFactory.Create(tttBase + "currentPlayer"))

        g.Assert(Triple(gameSubj, currentPlayerPred, g.CreateLiteralNode "X")) |> ignore
        let validMovesPred = g.CreateUriNode(UriFactory.Create(tttBase + "validMoves"))

        for XPos pos in moves do
            g.Assert(Triple(gameSubj, validMovesPred, g.CreateUriNode(UriFactory.Create(tttBase + pos.ToString()))))
            |> ignore

    | OTurn(_, moves) ->
        let currentPlayerPred =
            g.CreateUriNode(UriFactory.Create(tttBase + "currentPlayer"))

        g.Assert(Triple(gameSubj, currentPlayerPred, g.CreateLiteralNode "O")) |> ignore
        let validMovesPred = g.CreateUriNode(UriFactory.Create(tttBase + "validMoves"))

        for OPos pos in moves do
            g.Assert(Triple(gameSubj, validMovesPred, g.CreateUriNode(UriFactory.Create(tttBase + pos.ToString()))))
            |> ignore

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
        relation (TicTacToe.GeneratedSemantics.SemanticResource.Game.Iri.AbsoluteUri)

        linkedDataGraphWith
            { LinkedDataConfig.Empty with
                JsonLdContext = """{"@context":["https://schema.org/version/latest/schemaorg-current-https.jsonld"]}"""
                GraphFactory = Some gameGraphFactory
                // #420: Game's class-level facts (rdfs:seeAlso Wikidata IRIs) live at
                // /vocabulary, never duplicated into this instance body — the naive
                // client follows this Link header, never a hardcoded path.
                VocabularyUri = Some "/vocabulary" }

        get gameHandler

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
            { LinkedDataConfig.Empty with
                JsonLdContext =
                    """{"@context":["http://www.w3.org/1999/02/22-rdf-syntax-ns#","http://www.w3.org/2000/01/rdf-schema#","http://www.w3.org/2002/07/owl#","https://schema.org/version/latest/schemaorg-current-https.jsonld"]}"""
                GraphFactory = Some loadTttVocabGraph }

        get (fun (ctx: HttpContext) ->
            task {
                let origin = $"{ctx.Request.Scheme}://{ctx.Request.Host}"
                let graph = loadTttVocabGraph ctx
                ctx.Response.ContentType <- "text/turtle"
                do! ctx.Response.WriteAsync(buildTurtleBody origin graph)
            })
    }

/// Per-request factory for the app's declared-vocabulary-mapping ontology (every
/// resource→vocab class/property mapping declared in Vocabulary.fs), rebased against the
/// real request origin — genuine, HTTP-reachable use of the generated
/// GeneratedLinkedData.graphFor function, proving it resolves the app's own (ttt:) terms
/// against the real deployed host and never bakes in a codegen-time placeholder (#396 round 5).
let private appVocabularyGraphFactory (ctx: HttpContext) : IGraph =
    let origin = Uri $"{ctx.Request.Scheme}://{ctx.Request.Host}"
    TicTacToe.GeneratedLinkedData.graphFor origin

/// GeneratedLinkedData.jsonLdContextFor's own output now genuinely covers rdf/rdfs/owl
/// (Ontology.toJsonLdContext always lists them, #396 round 6), so the prior hand-curated
/// @context string is no longer needed.
///
/// LinkedDataConfig.JsonLdContext is a fixed string (no per-request factory, unlike
/// GraphFactory), so this is computed once. jsonLdContextFor's baseUri is never used to rebase
/// ContextBases entries — Ontology.toJsonLdContext asserts every one absolute up front
/// (assertAbsolute), regardless of whether baseUri is Some or None (#396 round 7), because
/// ContextBases is built exclusively from `using` (external vocab) prefixes
/// (LinkedDataEmitter.contextBases), which must always already be absolute IRIs (schema.org
/// here), never the app's own relative ones. So the ".invalid" sentinel below is provably inert:
/// were ContextBases ever to carry a relative entry, jsonLdContextFor would throw
/// ArgumentException at this very module-load call — not silently rebase into a
/// garbage-but-valid-looking URI served in the response.
let private appVocabularyJsonLdContext =
    TicTacToe.GeneratedLinkedData.jsonLdContextFor (Uri "http://placeholder.invalid")

let private appVocabularyResource =
    resource "/vocabulary" {
        name "AppVocabulary"

        linkedDataGraphWith
            { LinkedDataConfig.Empty with
                JsonLdContext = appVocabularyJsonLdContext
                GraphFactory = Some appVocabularyGraphFactory }

        get (fun (ctx: HttpContext) ->
            task {
                let origin = $"{ctx.Request.Scheme}://{ctx.Request.Host}"
                let graph = appVocabularyGraphFactory ctx
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
        resource tttVocabResource
        resource appVocabularyResource
    }

    0
