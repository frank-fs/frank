namespace TicTacToe.E2E

open System
open System.Collections.Generic
open System.Text.Json
open System.Threading.Tasks
open Microsoft.Playwright
open Microsoft.Playwright.NUnit
open NUnit.Framework

/// The v7.3.2 target: a naive client navigates entirely via HTTP discovery.
/// The ONLY hardcoded value is the base URL. Game URL comes from JSON Home,
/// the ALPS profile from Link rel=profile, field IRIs from the ALPS document,
/// and the move URL from Link rel=makeMove. No path or IRI is constructed by
/// the client. These fail until the semantic layer is built — that is the point.
[<TestFixture>]
type SemanticTests() =
    inherit PlaywrightTest()

    member this.NewContext() : Task<IAPIRequestContext> =
        this.Playwright.APIRequest.NewContextAsync(APIRequestNewContextOptions(BaseURL = Server.Url()))

    /// Collect Link header values (Playwright may split or combine them).
    static member private LinkRels(resp: IAPIResponse) : IDictionary<string, string> =
        let rels = Dictionary<string, string>()

        let raw =
            resp.Headers
            |> Seq.filter (fun kv -> kv.Key.ToLowerInvariant() = "link")
            |> Seq.map (fun kv -> kv.Value)
            |> String.concat ", "

        for part in raw.Split(',') do
            let seg = part.Trim()

            if seg.Contains "<" && seg.Contains ">" && seg.Contains "rel=" then
                let url = seg.Substring(seg.IndexOf '<' + 1, seg.IndexOf '>' - seg.IndexOf '<' - 1)

                let relRaw = seg.Substring(seg.IndexOf "rel=" + 4).Trim().Trim('"', '\'', ';', ' ')

                let rel = relRaw.Split(';').[0].Trim().Trim('"', '\'')
                rels.[rel] <- url

        rels

    [<Test>]
    member this.``AT-S1 JSON Home advertises a game resource``() =
        task {
            use! ctx = this.NewContext()
            let! resp = ctx.GetAsync("/", APIRequestContextOptions(Headers = dict [ "Accept", "application/json-home" ]))
            Assert.That(resp.Status, Is.EqualTo 200)
            let! json = resp.JsonAsync()
            let resources = json.Value.GetProperty("resources")

            let hasTemplate =
                resources.EnumerateObject()
                |> Seq.exists (fun r ->
                    let v = r.Value
                    let mutable t = Unchecked.defaultof<JsonElement>
                    v.TryGetProperty("href-template", &t) || v.TryGetProperty("href", &t))

            Assert.That(hasTemplate, Is.True, "JSON Home has no game resource href/href-template")
        }

    [<Test>]
    member this.``AT-S2 game response carries Link rel=profile to ALPS``() =
        task {
            use! ctx = this.NewContext()
            let! resp = ctx.GetAsync("/games/at-s2")
            let rels = SemanticTests.LinkRels resp
            Assert.That(rels.ContainsKey "profile", Is.True, "no Link rel=profile on game response")
        }

    [<Test>]
    member this.``AT-S3 ALPS profile describes position and agent fields``() =
        task {
            use! ctx = this.NewContext()
            let! game = ctx.GetAsync("/games/at-s3")
            let alpsUrl = (SemanticTests.LinkRels game).["profile"]
            let! alps = ctx.GetAsync(alpsUrl)
            Assert.That(alps.Status, Is.EqualTo 200)
            let! body = alps.TextAsync()
            Assert.That(body.Contains "urn:frank:", Is.False, "ALPS leaks urn:frank: IRIs")
            // descriptors should reference the move's position + agent fields
            Assert.That(body.ToLowerInvariant().Contains "position", Is.True, "ALPS missing position descriptor")
            Assert.That(body.ToLowerInvariant().Contains "agent", Is.True, "ALPS missing agent descriptor")
        }

    [<Test>]
    member this.``AT-S4 invalid move is rejected 422 with vocabulary IRIs and no urn:frank``() =
        task {
            use! ctx = this.NewContext()

            let badMove =
                {| ``@type`` = "https://schema.org/Action"
                   position = "NotASquare"
                   agent = "X" |}

            let! resp =
                ctx.PostAsync(
                    "/games/at-s4/moves",
                    APIRequestContextOptions(
                        Headers = dict [ "Content-Type", "application/ld+json" ],
                        DataObject = badMove
                    )
                )

            Assert.That(resp.Status, Is.EqualTo 422)
            let! body = resp.TextAsync()
            Assert.That(body.Contains "urn:frank:", Is.False, "422 body leaks urn:frank: IRIs")
            Assert.That(body.Contains "schema.org", Is.True, "422 body lacks vocabulary IRIs")
        }

    [<Test>]
    member this.``AT-S5 game state negotiates JSON-LD and Turtle``() =
        task {
            use! ctx = this.NewContext()

            let contentType (resp: IAPIResponse) =
                match resp.Headers.TryGetValue "content-type" with
                | true, v -> v
                | _ -> ""

            let! ld =
                ctx.GetAsync("/games/at-s5", APIRequestContextOptions(Headers = dict [ "Accept", "application/ld+json" ]))

            Assert.That(ld.Status, Is.EqualTo 200, "ld+json not negotiated")
            Assert.That(contentType(ld).Contains "ld+json", Is.True, "ld+json Accept did not yield ld+json")

            let! ttl =
                ctx.GetAsync("/games/at-s5", APIRequestContextOptions(Headers = dict [ "Accept", "text/turtle" ]))

            Assert.That(ttl.Status, Is.EqualTo 200, "turtle not negotiated")
            Assert.That(contentType(ttl).Contains "turtle", Is.True, "turtle Accept did not yield turtle")
        }

    /// Discover the game URL (from JSON Home) and the position/agent field IRIs
    /// (from the ALPS profile) using nothing but HTTP responses.
    member private this.Discover(ctx: IAPIRequestContext, gameId: string) =
        task {
            let! home = ctx.GetAsync("/", APIRequestContextOptions(Headers = dict [ "Accept", "application/json-home" ]))
            let! homeJson = home.JsonAsync()
            let resources = homeJson.Value.GetProperty("resources")

            let template =
                resources.EnumerateObject()
                |> Seq.pick (fun r ->
                    let v = r.Value
                    let mutable t = Unchecked.defaultof<JsonElement>

                    if v.TryGetProperty("href-template", &t) then Some(t.GetString())
                    elif v.TryGetProperty("href", &t) then Some(t.GetString())
                    else None)

            let gameUrl =
                let openIdx = template.IndexOf '{'

                if openIdx < 0 then
                    template
                else
                    let close = template.IndexOf '}'
                    template.Substring(0, openIdx) + Uri.EscapeDataString gameId + template.Substring(close + 1)

            let! game0 = ctx.GetAsync(gameUrl)
            let alpsUrl = (SemanticTests.LinkRels game0).["profile"]
            let! alps = ctx.GetAsync(alpsUrl)
            let! alpsBody = alps.TextAsync()
            use alpsDoc = JsonDocument.Parse alpsBody

            let fieldIri (name: string) =
                let mutable found = None

                let rec walk (el: JsonElement) =
                    match el.ValueKind with
                    | JsonValueKind.Object ->
                        let mutable idEl = Unchecked.defaultof<JsonElement>
                        let mutable hrefEl = Unchecked.defaultof<JsonElement>

                        if
                            el.TryGetProperty("id", &idEl)
                            && idEl.GetString() = name
                            && el.TryGetProperty("href", &hrefEl)
                        then
                            found <- Some(hrefEl.GetString())

                        for p in el.EnumerateObject() do
                            walk p.Value
                    | JsonValueKind.Array -> for item in el.EnumerateArray() do walk item
                    | _ -> ()

                walk alpsDoc.RootElement
                found

            let positionIri = fieldIri "position" |> Option.defaultWith (fun () -> failwith "ALPS missing position IRI")
            let agentIri = fieldIri "agent" |> Option.defaultWith (fun () -> failwith "ALPS missing agent IRI")
            return gameUrl, positionIri, agentIri
        }

    /// One naive client posts a single move for its role, using the move URL
    /// discovered from the live game response (never constructed).
    member private _.PlayTurn(ctx: IAPIRequestContext, gameUrl, positionIri, agentIri, role) =
        task {
            let! state = ctx.GetAsync(gameUrl)
            let! sJson = state.JsonAsync()
            let root = sJson.Value
            let moveUrl = (SemanticTests.LinkRels state).["makeMove"]

            let emptyPos =
                root.GetProperty("squares").EnumerateObject()
                |> Seq.find (fun p -> p.Value.ValueKind = JsonValueKind.Null)

            let body = Dictionary<string, obj>()
            body.["@type"] <- "https://schema.org/Action"
            body.[positionIri] <- emptyPos.Name
            body.[agentIri] <- role

            let! _ =
                ctx.PostAsync(
                    moveUrl,
                    APIRequestContextOptions(Headers = dict [ "Content-Type", "application/ld+json" ], DataObject = body)
                )

            return ()
        }

    [<Test>]
    member this.``AT-S6 two naive clients complete a full game via discovery only``() =
        task {
            let gameId = "at-s6"
            // two independent, firewalled clients — one per role
            use! ctxX = this.NewContext()
            use! ctxO = this.NewContext()

            let! (gameUrl, positionIri, agentIri) = this.Discover(ctxX, gameId)
            let! _ = this.Discover(ctxO, gameId) // O discovers independently too

            let mutable finished = false
            let mutable turn = 0

            while not finished && turn < 9 do
                let! state = ctxX.GetAsync(gameUrl)
                let! sJson = state.JsonAsync()
                let status = sJson.Value.GetProperty("status").GetString()

                if status = "Won" || status = "Draw" then
                    finished <- true
                else
                    let player = sJson.Value.GetProperty("currentPlayer").GetString()
                    let activeCtx = if player = "X" then ctxX else ctxO
                    do! this.PlayTurn(activeCtx, gameUrl, positionIri, agentIri, player)
                    turn <- turn + 1

            Assert.That(finished, Is.True, "two naive clients could not finish the game via discovery")
        }
