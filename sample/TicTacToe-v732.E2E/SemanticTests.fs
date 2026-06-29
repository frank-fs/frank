namespace TicTacToe.E2E

open System
open System.Collections.Generic
open System.Text.Json
open System.Threading.Tasks
open Microsoft.Playwright
open Microsoft.Playwright.NUnit
open NUnit.Framework

/// v7.3.2 Track B acceptance criteria (spec §6), expressed as falsifiable HTTP
/// pairs against the live TicTacToe server. The naive client navigates via
/// JSON Home + ALPS + content negotiation + SHACL validation — no hardcoded API
/// knowledge beyond the base URL. State-dependent affordances are Track A and
/// out of scope here. These fail until the semantic layer is built.
///
/// Scope: this file is the CAPSTONE (spec §6#6, issue #333). Provenance (§6#3),
/// composition (§6#5), and negative tests (vocab swap / build gate / hash drift)
/// live in separate units (issues #331/#332/#334), not here.
[<TestFixture>]
type SemanticTests() =
    inherit PlaywrightTest()

    member this.NewContext() : Task<IAPIRequestContext> =
        this.Playwright.APIRequest.NewContextAsync(APIRequestNewContextOptions(BaseURL = Server.Url()))

    /// rel -> url from one or more Link headers (RFC 8288).
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

                let rel =
                    seg.Substring(seg.IndexOf "rel=" + 4).Trim().Split(';').[0].Trim().Trim('"', '\'')

                rels.[rel] <- url

        rels

    member private this.Options(ctx: IAPIRequestContext, url: string) =
        ctx.FetchAsync(url, APIRequestContextOptions(Method = "OPTIONS"))

    /// Collect all ALPS descriptor href values from an ALPS JSON body.
    /// The document is small and bounded, so recursion terminates.
    static member private AlpsDescriptorHrefs(alpsBody: string) : string list =
        use doc = JsonDocument.Parse alpsBody
        let acc = System.Collections.Generic.List<string>()

        let rec walk (el: JsonElement) =
            match el.ValueKind with
            | JsonValueKind.Object ->
                let mutable hrefEl = Unchecked.defaultof<JsonElement>

                if el.TryGetProperty("href", &hrefEl) then
                    let v = hrefEl.GetString()

                    if not (isNull v) && v.StartsWith "http" then
                        acc.Add v

                for p in el.EnumerateObject() do
                    walk p.Value
            | JsonValueKind.Array ->
                for item in el.EnumerateArray() do
                    walk item
            | _ -> ()

        walk doc.RootElement
        acc |> Seq.toList

    /// Return the href of the ALPS descriptor whose id matches localId, or None.
    /// Navigates alps.descriptor directly — no unbounded recursion.
    static member private AlpsDescriptorHrefByLocalId(alpsBody: string, localId: string) : string option =
        use doc = JsonDocument.Parse alpsBody
        let mutable result: string option = None
        let mutable alpsEl = Unchecked.defaultof<JsonElement>
        let mutable descriptorEl = Unchecked.defaultof<JsonElement>

        if doc.RootElement.TryGetProperty("alps", &alpsEl)
           && alpsEl.TryGetProperty("descriptor", &descriptorEl) then
            for d in descriptorEl.EnumerateArray() do
                let mutable idEl = Unchecked.defaultof<JsonElement>
                let mutable hrefEl = Unchecked.defaultof<JsonElement>

                if d.TryGetProperty("id", &idEl)
                   && idEl.GetString() = localId
                   && d.TryGetProperty("href", &hrefEl) then
                    result <- hrefEl.GetString() |> Option.ofObj

        result

    /// Extract rdfs:seeAlso target URIs from a JSON-LD @graph body.
    static member private SeeAlsoUris(ldBody: string) : string list =
        use doc = JsonDocument.Parse ldBody
        let acc = System.Collections.Generic.List<string>()
        let seeAlsoKey = "http://www.w3.org/2000/01/rdf-schema#seeAlso"
        let mutable graph = Unchecked.defaultof<JsonElement>

        if doc.RootElement.TryGetProperty("@graph", &graph) then
            for node in graph.EnumerateArray() do
                let mutable sa = Unchecked.defaultof<JsonElement>

                if node.TryGetProperty(seeAlsoKey, &sa) then
                    for target in sa.EnumerateArray() do
                        let mutable idEl = Unchecked.defaultof<JsonElement>

                        if target.TryGetProperty("@id", &idEl) then
                            acc.Add(idEl.GetString())

        acc |> Seq.toList

    // ── AT-S1: JSON Home is a resource directory ────────────────────────────────
    [<Test>]
    member this.``AT-S1 JSON Home lists resources with vocabulary-mapped rels``() =
        task {
            use! ctx = this.NewContext()

            let! resp =
                ctx.GetAsync("/", APIRequestContextOptions(Headers = dict [ "Accept", "application/json-home" ]))

            Assert.That(resp.Status, Is.EqualTo 200)
            let! json = resp.JsonAsync()
            let resources = json.Value.GetProperty("resources")
            Assert.That(resources.EnumerateObject() |> Seq.isEmpty |> not, "JSON Home has no resources")
            // rels ARE vocabulary terms (absolute IRIs), never urn:frank:
            let relsAreVocab =
                resources.EnumerateObject() |> Seq.exists (fun r -> r.Name.StartsWith "http")

            Assert.That(relsAreVocab, Is.True, "JSON Home rels are not vocabulary IRIs")
            let! body = resp.TextAsync()
            Assert.That(body.Contains "urn:frank:", Is.False, "JSON Home leaks urn:frank: rels")
        }

    // ── AT-S2: OPTIONS yields Allow + Link rel=describedby → ALPS ────────────────
    [<Test>]
    member this.``AT-S2 OPTIONS carries Allow and Link rel=describedby to ALPS``() =
        task {
            use! ctx = this.NewContext()
            let! resp = this.Options(ctx, "/games/at-s2")
            Assert.That(resp.Headers.ContainsKey "allow", Is.True, "OPTIONS missing Allow header")
            let rels = SemanticTests.LinkRels resp
            Assert.That(rels.ContainsKey "describedby", Is.True, "OPTIONS missing Link rel=describedby")
        }

    // ── AT-S3: ALPS descriptors cite vocabulary IRIs, never urn:frank: ───────────
    [<Test>]
    member this.``AT-S3 ALPS profile descriptors reference schema.org IRIs``() =
        task {
            use! ctx = this.NewContext()
            let! opts = this.Options(ctx, "/games/at-s3")
            let alpsUrl = (SemanticTests.LinkRels opts).["describedby"]
            let! alps = ctx.GetAsync(alpsUrl)
            Assert.That(alps.Status, Is.EqualTo 200)
            let! body = alps.TextAsync()
            Assert.That(body.Contains "urn:frank:", Is.False, "ALPS leaks urn:frank: IRIs")
            Assert.That(body.Contains "schema.org", Is.True, "ALPS descriptors cite no schema.org IRIs")
        }

    // ── AT-S4: invalid move → 422 ValidationReport citing vocabulary IRIs ────────
    [<Test>]
    member this.``AT-S4 invalid move returns 422 ValidationReport with vocabulary IRIs``() =
        task {
            use! ctx = this.NewContext()

            let badMove =
                {| ``@type`` = "https://schema.org/MoveAction"
                   ``https://schema.org/agent`` = "X"
                   ``https://example.org/tictactoe#square`` = "NotASquare" |}

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
            Assert.That(body.Contains "ValidationReport", Is.True, "422 body is not a W3C SHACL ValidationReport")
            // sh:resultPath carries the ttt:square IRI; dotNetRdf sh:sourceShape points to the
            // blank-node property shape, so schema:MoveAction does not appear in the report.
            Assert.That(body.Contains "example.org/tictactoe", Is.True, "422 ValidationReport cites no vocabulary IRIs")
        // valid-move → 200 is covered by the capstone (AT-S6) using discovered IRIs.
        }

    // ── AT-S5: content negotiation — all three formats ───────────────────────────
    [<Test>]
    member this.``AT-S5 game negotiates JSON-LD with external schema.org @context``() =
        task {
            use! ctx = this.NewContext()

            // ── ld+json: external @context + seeAlso outbound link ──────────────
            let! ld =
                ctx.GetAsync(
                    "/games/at-s5",
                    APIRequestContextOptions(Headers = dict [ "Accept", "application/ld+json" ])
                )

            Assert.That(ld.Status, Is.EqualTo 200, "ld+json not negotiated")

            let contentType =
                match ld.Headers.TryGetValue "content-type" with
                | true, v -> v
                | _ -> ""

            Assert.That(contentType.Contains "ld+json", Is.True, "ld+json Accept did not yield ld+json")
            let! body = ld.TextAsync()
            Assert.That(body.Contains "@context", Is.True, "JSON-LD body lacks @context")
            Assert.That(body.Contains "schema.org", Is.True, "@context does not reference external schema.org")
            // declared outbound link (vocabulary CE: seeAlso wikidata:Q11907)
            Assert.That(
                body.Contains "seeAlso" || body.Contains "wikidata",
                Is.True,
                "JSON-LD lacks rdfs:seeAlso outbound link"
            )

            // ── application/json: compact JSON game state ────────────────────────
            let! json =
                ctx.GetAsync(
                    "/games/at-s5",
                    APIRequestContextOptions(Headers = dict [ "Accept", "application/json" ])
                )

            Assert.That(json.Status, Is.EqualTo 200, "application/json not negotiated")

            let jsonCt =
                match json.Headers.TryGetValue "content-type" with
                | true, v -> v
                | _ -> ""

            Assert.That(
                jsonCt.Contains "json" && not (jsonCt.Contains "ld+json"),
                Is.True,
                "application/json Accept did not yield compact JSON (got: " + jsonCt + ")"
            )

            let! jsonBody = json.TextAsync()
            Assert.That(jsonBody.Contains "status", Is.True, "compact JSON body lacks game status field")

            // ── text/turtle: vocabulary graph in Turtle ──────────────────────────
            let! turtle =
                ctx.GetAsync(
                    "/games/at-s5",
                    APIRequestContextOptions(Headers = dict [ "Accept", "text/turtle" ])
                )

            Assert.That(turtle.Status, Is.EqualTo 200, "text/turtle not negotiated")

            let! turtleBody = turtle.TextAsync()
            Assert.That(turtleBody.Contains "@prefix", Is.True, "text/turtle body is not Turtle syntax")
        }

    // ── AT-S6: agent-simulator — follow links, verify term set, deref, play ──────
    //
    // Proves semantic understanding via absolute IRI recognition, not spelling.
    // The client selects inputs by href (https://schema.org/agent, ttt:square),
    // asserts the expected term set is present, dereferences every URI it receives,
    // then plays a full two-player game to a terminal state.
    [<Test>]
    member this.``AT-S6 naive client plays a full game via discovery only``() =
        task {
            use! ctx = this.NewContext()
            let testBase = Server.Url()
            let gameId = "at-s6"

            // ── Phase 1: JSON Home ──────────────────────────────────────────────
            let! home =
                ctx.GetAsync("/", APIRequestContextOptions(Headers = dict [ "Accept", "application/json-home" ]))

            Assert.That(home.Status, Is.EqualTo 200, "JSON Home not 200")
            let! homeJson = home.JsonAsync()
            let resources = homeJson.Value.GetProperty "resources"

            let templateFor (verb: string) =
                resources.EnumerateObject()
                |> Seq.tryPick (fun r ->
                    let mutable hints = Unchecked.defaultof<JsonElement>
                    let mutable allow = Unchecked.defaultof<JsonElement>
                    let mutable tmpl = Unchecked.defaultof<JsonElement>

                    let hasVerb =
                        r.Value.TryGetProperty("hints", &hints)
                        && hints.TryGetProperty("allow", &allow)
                        && allow.EnumerateArray() |> Seq.exists (fun m -> m.GetString() = verb)

                    if hasVerb && r.Value.TryGetProperty("href-template", &tmpl) then
                        Some(tmpl.GetString())
                    else
                        None)

            let expand (tpl: string) =
                let o = tpl.IndexOf '{'

                if o < 0 then
                    tpl
                else
                    tpl.Substring(0, o) + Uri.EscapeDataString gameId + tpl.Substring(tpl.IndexOf '}' + 1)

            let gameUrl =
                templateFor "GET"
                |> Option.map expand
                |> Option.defaultWith (fun () -> failwith "JSON Home: no GET game template")

            let moveUrl =
                templateFor "POST"
                |> Option.map expand
                |> Option.defaultWith (fun () -> failwith "JSON Home: no POST moves template")

            // ── Phase 2: Follow links, assert each resolves ─────────────────────
            let! gameResp = ctx.GetAsync gameUrl
            Assert.That(gameResp.Status, Is.EqualTo 200, sprintf "Game resource '%s' not 200" gameUrl)

            let! opts = this.Options(ctx, gameUrl)
            Assert.That(opts.Headers.ContainsKey "allow", Is.True, "OPTIONS missing Allow header")
            let rels = SemanticTests.LinkRels opts
            Assert.That(rels.ContainsKey "describedby", Is.True, "OPTIONS missing Link rel=describedby")
            let alpsUrl = rels.["describedby"]

            let! alpsResp = ctx.GetAsync alpsUrl
            Assert.That(alpsResp.Status, Is.EqualTo 200, sprintf "ALPS profile '%s' not 200" alpsUrl)
            let! alpsBody = alpsResp.TextAsync()

            // ── Phase 3: Collect hrefs; assert expected semantic term set ────────
            let descriptorHrefs = SemanticTests.AlpsDescriptorHrefs alpsBody

            for href in descriptorHrefs do
                Assert.That(
                    href.StartsWith "http",
                    Is.True,
                    sprintf "ALPS descriptor href is not an absolute IRI: %s" href
                )

            let hrefSet = Set.ofList descriptorHrefs

            let expectedTerms =
                [ "https://schema.org/MoveAction"
                  "https://schema.org/agent"
                  "https://example.org/tictactoe#square"
                  "https://schema.org/Game"
                  "https://schema.org/result" ]

            for term in expectedTerms do
                Assert.That(
                    hrefSet.Contains term,
                    Is.True,
                    sprintf "Expected semantic term absent from ALPS: %s" term
                )

            // ── Phase 4: Dereference every URI the client received ───────────────
            // schema.org term IRIs — dereference live over the network
            for iri in descriptorHrefs |> List.filter (fun u -> u.StartsWith "https://schema.org/") do
                let! r = ctx.GetAsync iri
                Assert.That(r.Status, Is.InRange(200, 299), sprintf "schema.org IRI not dereferenceable: %s" iri)

            // domain ttt: term — rebase to test host (strip fragment, swap origin)
            for tttIri in descriptorHrefs |> List.filter (fun u -> u.Contains "example.org/tictactoe") do
                let baseIri = tttIri.Split('#').[0]
                let tttPath = Uri(baseIri).AbsolutePath
                let! r = ctx.GetAsync(testBase + tttPath)

                Assert.That(
                    r.Status,
                    Is.EqualTo 200,
                    sprintf "ttt vocab resource not served at %s%s" testBase tttPath
                )

                let! tttBody = r.TextAsync()
                // Turtle uses prefixed form (ttt:square); JSON-LD uses full IRI
                Assert.That(
                    tttBody.Contains "ttt:square" || tttBody.Contains "tictactoe#square",
                    Is.True,
                    "ttt vocab body does not reference the square term"
                )

            // seeAlso targets from game's ld+json — dereference live
            let! ldGame =
                ctx.GetAsync(
                    gameUrl,
                    APIRequestContextOptions(Headers = dict [ "Accept", "application/ld+json" ])
                )

            let! ldBody = ldGame.TextAsync()

            Assert.That(
                ldBody.Contains "@context" && ldBody.Contains "schema.org",
                Is.True,
                "Game not available as external-context JSON-LD"
            )

            for seeAlsoUri in SemanticTests.SeeAlsoUris ldBody do
                let! r = ctx.GetAsync seeAlsoUri
                Assert.That(r.Status, Is.InRange(200, 299), sprintf "seeAlso target did not resolve: %s" seeAlsoUri)

            // ── Phase 5: Identify inputs by absolute IRI — NOT by field name ────
            // A meaningless mapping (schema:position, schema:Action) would not
            // have these IRIs and the test would fail here.
            let agentIri =
                descriptorHrefs
                |> List.tryFind (fun h -> h = "https://schema.org/agent")
                |> Option.defaultWith (fun () -> failwith "ALPS missing agent IRI (https://schema.org/agent)")

            let squareIri =
                descriptorHrefs
                |> List.tryFind (fun h -> h.Contains "tictactoe#square")
                |> Option.defaultWith (fun () -> failwith "ALPS missing square IRI (tictactoe#square)")

            let classIri =
                descriptorHrefs
                |> List.tryFind (fun h -> h = "https://schema.org/MoveAction")
                |> Option.defaultWith (fun () -> failwith "ALPS missing MoveAction class IRI")

            // ── Phase 6: Illegal move → 422 citing vocab IRI ────────────────────
            let illegal = Dictionary<string, obj>()
            illegal.["@type"] <- classIri
            illegal.[squareIri] <- "NotASquare"
            illegal.[agentIri] <- "X"

            let! bad =
                ctx.PostAsync(
                    moveUrl,
                    APIRequestContextOptions(
                        Headers = dict [ "Content-Type", "application/ld+json" ],
                        DataObject = illegal
                    )
                )

            Assert.That(bad.Status, Is.EqualTo 422, "SHACL did not reject illegal move")
            let! badBody = bad.TextAsync()

            Assert.That(
                badBody.Contains "schema.org" || badBody.Contains "tictactoe",
                Is.True,
                "422 ValidationReport cites no vocab IRI"
            )

            // ── Phase 7: Play full game via discovered IRIs ──────────────────────
            // Keys and @type come from ALPS — no hardcoded field names or URLs.
            // Legal squares come from the game-state's validMoves array.
            let mutable finished = false
            let mutable turn = 0

            while not finished && turn < 9 do
                let! stateResp = ctx.GetAsync gameUrl
                let! stateJson = stateResp.JsonAsync()
                let root = stateJson.Value
                let status = root.GetProperty("status").GetString()

                if status = "Won" || status = "Draw" then
                    finished <- true
                else
                    let player = root.GetProperty("currentPlayer").GetString()

                    let square =
                        root.GetProperty("validMoves").EnumerateArray()
                        |> Seq.map (fun v -> v.GetString())
                        |> Seq.head

                    let moveBody = Dictionary<string, obj>()
                    moveBody.["@type"] <- classIri
                    moveBody.[agentIri] <- player
                    moveBody.[squareIri] <- square

                    let! _ =
                        ctx.PostAsync(
                            moveUrl,
                            APIRequestContextOptions(
                                Headers = dict [ "Content-Type", "application/ld+json" ],
                                DataObject = moveBody
                            )
                        )

                    turn <- turn + 1

            Assert.That(finished, Is.True, "Naive client could not finish the game via discovery")
        }

    /// Parse prov:Activity count and prov:startedAtTime values from a compacted PROV-O JSON-LD body.
    /// The body uses the prov: prefix alias so activity nodes carry "@type":"prov:Activity".
    /// Returns (activityCount, startedAtTimes in graph-walk order).
    static member private ParseProvenanceActivities(body: string) : int * DateTimeOffset list =
        use doc = JsonDocument.Parse body
        let timestamps = System.Collections.Generic.List<DateTimeOffset>()
        let mutable activityCount = 0

        let isActivity (el: JsonElement) =
            let mutable typeEl = Unchecked.defaultof<JsonElement>

            if el.TryGetProperty("@type", &typeEl) then
                match typeEl.ValueKind with
                | JsonValueKind.String -> typeEl.GetString() = "prov:Activity"
                | JsonValueKind.Array ->
                    typeEl.EnumerateArray()
                    |> Seq.exists (fun t -> t.ValueKind = JsonValueKind.String && t.GetString() = "prov:Activity")
                | _ -> false
            else
                false

        let tryAddTimestamp (el: JsonElement) =
            let mutable tsEl = Unchecked.defaultof<JsonElement>

            if el.TryGetProperty("prov:startedAtTime", &tsEl) then
                let mutable valEl = Unchecked.defaultof<JsonElement>

                let raw =
                    match tsEl.ValueKind with
                    | JsonValueKind.Object when tsEl.TryGetProperty("@value", &valEl) -> valEl.GetString()
                    | JsonValueKind.String -> tsEl.GetString()
                    | _ -> null

                if not (isNull raw) then
                    match DateTimeOffset.TryParse raw with
                    | true, dt -> timestamps.Add dt
                    | _ -> ()

        let mutable graphEl = Unchecked.defaultof<JsonElement>
        let root = doc.RootElement

        let nodes =
            if root.TryGetProperty("@graph", &graphEl) then
                graphEl.EnumerateArray() |> Seq.toList
            else
                [ root ]

        for node in nodes do
            if isActivity node then
                activityCount <- activityCount + 1
                tryAddTimestamp node

        activityCount, timestamps |> Seq.toList

    // ── AT-S7: vocab-swap negative — hardcoded schema.org fails, discovery survives ──
    //
    // The ex: server serves the same game but with ALPS descriptors in the
    // https://example.org/ex# namespace instead of schema.org. A client that
    // hardcodes schema.org IRIs as POST body keys gets a 400 (wrong keys). The
    // discovery navigator finds IRIs by their ALPS descriptor local id (vocab-neutral),
    // reads whatever href the server advertises, and still completes a full game.
    [<Test>]
    member this.``AT-S7 vocab-swap — hardcoded schema.org client fails, discovery client succeeds``() =
        task {
            use! ctx =
                this.Playwright.APIRequest.NewContextAsync(
                    APIRequestNewContextOptions(BaseURL = ExServer.Url())
                )

            let gameId = "at-s7"

            // ── Phase 1: Follow links to the ex: ALPS profile ──────────────────
            let! opts = this.Options(ctx, sprintf "/games/%s" gameId)
            let rels = SemanticTests.LinkRels opts
            Assert.That(rels.ContainsKey "describedby", Is.True, "ex: server missing Link rel=describedby")
            let alpsUrl = rels.["describedby"]
            let! alpsResp = ctx.GetAsync alpsUrl
            Assert.That(alpsResp.Status, Is.EqualTo 200, "ex: server ALPS not 200")
            let! alpsBody = alpsResp.TextAsync()

            // ── Phase 2: Hardcoded schema.org client would fail here ────────────
            // The ALPS must contain NO schema.org IRIs — a client that hardcodes
            // "https://schema.org/agent" as a POST key would get no matching
            // descriptor and post an empty/wrong body.
            Assert.That(
                alpsBody.Contains "schema.org",
                Is.False,
                "ex: server ALPS still references schema.org — hardcoded schema.org client would not fail"
            )

            Assert.That(
                alpsBody.Contains "example.org/ex",
                Is.True,
                "ex: server ALPS does not contain ex: namespace IRIs"
            )

            // ── Phase 3: Discovery navigator — find IRIs by local name ──────────
            // Vocab-neutral: looks up ALPS descriptor by id (local name), reads
            // whatever href the server chose. Works for schema: OR ex: servers.
            let agentIri =
                SemanticTests.AlpsDescriptorHrefByLocalId(alpsBody, "agent")
                |> Option.defaultWith (fun () -> failwith "ALPS missing descriptor id='agent'")

            let squareIri =
                SemanticTests.AlpsDescriptorHrefByLocalId(alpsBody, "square")
                |> Option.defaultWith (fun () -> failwith "ALPS missing descriptor id='square'")

            let classIri =
                SemanticTests.AlpsDescriptorHrefByLocalId(alpsBody, "MoveAction")
                |> Option.defaultWith (fun () -> failwith "ALPS missing descriptor id='MoveAction'")

            // Confirm the server actually served ex: IRIs (not schema.org).
            Assert.That(agentIri.Contains "example.org/ex", Is.True, "agentIri not in ex: namespace")
            Assert.That(squareIri.Contains "example.org/ex", Is.True, "squareIri not in ex: namespace")
            Assert.That(classIri.Contains "example.org/ex", Is.True, "classIri not in ex: namespace")

            // ── Phase 4: Navigate JSON Home for game and move URLs ──────────────
            let! home =
                ctx.GetAsync("/", APIRequestContextOptions(Headers = dict [ "Accept", "application/json-home" ]))

            Assert.That(home.Status, Is.EqualTo 200, "JSON Home not 200")
            let! homeJson = home.JsonAsync()
            let resources = homeJson.Value.GetProperty "resources"

            let templateFor (verb: string) =
                resources.EnumerateObject()
                |> Seq.tryPick (fun r ->
                    let mutable hints = Unchecked.defaultof<JsonElement>
                    let mutable allow = Unchecked.defaultof<JsonElement>
                    let mutable tmpl = Unchecked.defaultof<JsonElement>

                    let hasVerb =
                        r.Value.TryGetProperty("hints", &hints)
                        && hints.TryGetProperty("allow", &allow)
                        && allow.EnumerateArray() |> Seq.exists (fun m -> m.GetString() = verb)

                    if hasVerb && r.Value.TryGetProperty("href-template", &tmpl) then
                        Some(tmpl.GetString())
                    else
                        None)

            let expand (tpl: string) =
                let o = tpl.IndexOf '{'

                if o < 0 then
                    tpl
                else
                    tpl.Substring(0, o) + Uri.EscapeDataString gameId + tpl.Substring(tpl.IndexOf '}' + 1)

            let gameUrl =
                templateFor "GET"
                |> Option.map expand
                |> Option.defaultWith (fun () -> failwith "JSON Home: no GET game template")

            let moveUrl =
                templateFor "POST"
                |> Option.map expand
                |> Option.defaultWith (fun () -> failwith "JSON Home: no POST moves template")

            // ── Leg A: hardcoded schema.org client breaks against ex: server ───
            // A client that hardcodes schema.org IRIs as body keys cannot find the
            // ex: descriptor hrefs in the body — the handler returns 400 because
            // squareIri/agentIri keys are absent. This is a DEMONSTRATED failure,
            // not just an assertion about the ALPS content.
            let legABody = Dictionary<string, obj>()
            legABody.["@type"] <- "https://schema.org/MoveAction"
            legABody.["https://schema.org/agent"] <- "X"
            legABody.["https://example.org/tictactoe#square"] <- "TopLeft"

            let! legAResp =
                ctx.PostAsync(
                    moveUrl,
                    APIRequestContextOptions(
                        Headers = dict [ "Content-Type", "application/ld+json" ],
                        DataObject = legABody
                    )
                )

            let! legAText = legAResp.TextAsync()

            Assert.That(
                legAResp.Status,
                Is.InRange(400, 499),
                sprintf
                    "Leg A: hardcoded schema.org client expected 4xx from ex: server but got %d — body: %s"
                    legAResp.Status
                    legAText
            )

            // ── Phase 5: Play full game using discovered ex: IRIs ───────────────
            // POST bodies keyed by the ex: IRIs read from ALPS — no hardcoded values.
            let mutable finished = false
            let mutable turn = 0

            while not finished && turn < 9 do
                let! stateResp = ctx.GetAsync gameUrl
                let! stateJson = stateResp.JsonAsync()
                let root = stateJson.Value
                let status = root.GetProperty("status").GetString()

                if status = "Won" || status = "Draw" then
                    finished <- true
                else
                    let player = root.GetProperty("currentPlayer").GetString()

                    let square =
                        root.GetProperty("validMoves").EnumerateArray()
                        |> Seq.map (fun v -> v.GetString())
                        |> Seq.head

                    let moveBody = Dictionary<string, obj>()
                    moveBody.["@type"] <- classIri
                    moveBody.[agentIri] <- player
                    moveBody.[squareIri] <- square

                    let! _ =
                        ctx.PostAsync(
                            moveUrl,
                            APIRequestContextOptions(
                                Headers = dict [ "Content-Type", "application/ld+json" ],
                                DataObject = moveBody
                            )
                        )

                    turn <- turn + 1

            Assert.That(finished, Is.True, "Discovery client could not finish game against ex: server")
        }

    // ── AT-S8: provenance complete-capture audit ───────────────────────────────────
    //
    // Plays a full game (via the AT-S6 navigator), logging every posted (agent, square).
    // After terminal state the test follows the DISCOVERED has_provenance Link header
    // (NOT hardcoded) to the lineage and proves COMPLETE capture:
    //   (1) Count: exactly one prov:Activity per posted move — no dropped or fabricated move.
    //   (2) Attribution: each activity carries prov:wasAssociatedWith (the HTTP agent).
    //       NOTE: the HTTP agent is "anonymous" for all moves (no auth); game-level player
    //       (X/O) and square attribution are NOT in the current ProvenanceRecord because the
    //       middleware does not extract request-body content. This is a documented design gap.
    //   (3) Order: prov:startedAtTime values are monotonically increasing — play order preserved.
    //       A reordered or dropped activity would break the count or timestamp sequence.
    //   (4) Terminal outcome: the observed final game state is Won or Draw, proving the
    //       full session reached a terminal state while the lineage was being recorded.
    // Falsifiability: a lineage that drops a move → count mismatch; a duplicate/fabricated
    // move → count mismatch; scrambled timestamps → order assertion fails.
    [<Test>]
    member this.``AT-S8 provenance captures every move with order and terminal outcome``() =
        task {
            use! ctx = this.NewContext()
            let gameId = "at-s8"

            // ── Phase 1: Discover game/moves URLs from JSON Home ──────────────
            let! home =
                ctx.GetAsync("/", APIRequestContextOptions(Headers = dict [ "Accept", "application/json-home" ]))

            Assert.That(home.Status, Is.EqualTo 200, "JSON Home not 200")
            let! homeJson = home.JsonAsync()
            let resources = homeJson.Value.GetProperty "resources"

            let templateFor (verb: string) =
                resources.EnumerateObject()
                |> Seq.tryPick (fun r ->
                    let mutable hints = Unchecked.defaultof<JsonElement>
                    let mutable allow = Unchecked.defaultof<JsonElement>
                    let mutable tmpl = Unchecked.defaultof<JsonElement>

                    let hasVerb =
                        r.Value.TryGetProperty("hints", &hints)
                        && hints.TryGetProperty("allow", &allow)
                        && allow.EnumerateArray() |> Seq.exists (fun m -> m.GetString() = verb)

                    if hasVerb && r.Value.TryGetProperty("href-template", &tmpl) then
                        Some(tmpl.GetString())
                    else
                        None)

            let expand (tpl: string) =
                let o = tpl.IndexOf '{'

                if o < 0 then
                    tpl
                else
                    tpl.Substring(0, o) + Uri.EscapeDataString gameId + tpl.Substring(tpl.IndexOf '}' + 1)

            let gameUrl =
                templateFor "GET"
                |> Option.map expand
                |> Option.defaultWith (fun () -> failwith "JSON Home: no GET game template")

            let moveUrl =
                templateFor "POST"
                |> Option.map expand
                |> Option.defaultWith (fun () -> failwith "JSON Home: no POST moves template")

            // ── Phase 2: Discover class/agent/square IRIs from ALPS ─────────
            let! opts = this.Options(ctx, gameUrl)
            let rels = SemanticTests.LinkRels opts
            Assert.That(rels.ContainsKey "describedby", Is.True, "OPTIONS missing Link rel=describedby")
            let alpsUrl = rels.["describedby"]
            let! alpsResp = ctx.GetAsync alpsUrl
            Assert.That(alpsResp.Status, Is.EqualTo 200, "ALPS not 200")
            let! alpsBody = alpsResp.TextAsync()

            let agentIri =
                SemanticTests.AlpsDescriptorHrefs alpsBody
                |> List.tryFind (fun h -> h = "https://schema.org/agent")
                |> Option.defaultWith (fun () -> failwith "ALPS missing agent IRI")

            let squareIri =
                SemanticTests.AlpsDescriptorHrefs alpsBody
                |> List.tryFind (fun h -> h.Contains "tictactoe#square")
                |> Option.defaultWith (fun () -> failwith "ALPS missing square IRI")

            let classIri =
                SemanticTests.AlpsDescriptorHrefs alpsBody
                |> List.tryFind (fun h -> h = "https://schema.org/MoveAction")
                |> Option.defaultWith (fun () -> failwith "ALPS missing MoveAction class IRI")

            // ── Phase 3: Play game, log (agent,square) per move, capture link ─
            let moveLog = System.Collections.Generic.List<string * string>()
            let mutable finished = false
            let mutable turn = 0
            let mutable provenanceResourceUrl = ""
            let provRel = "http://www.w3.org/ns/prov#has_provenance"

            while not finished && turn < 9 do
                let! stateResp = ctx.GetAsync gameUrl
                let! stateJson = stateResp.JsonAsync()
                let root = stateJson.Value
                let status = root.GetProperty("status").GetString()

                if status = "Won" || status = "Draw" then
                    finished <- true
                else
                    let player = root.GetProperty("currentPlayer").GetString()

                    let square =
                        root.GetProperty("validMoves").EnumerateArray()
                        |> Seq.map (fun v -> v.GetString())
                        |> Seq.head

                    let moveBody = Dictionary<string, obj>()
                    moveBody.["@type"] <- classIri
                    moveBody.[agentIri] <- player
                    moveBody.[squareIri] <- square

                    let! moveResp =
                        ctx.PostAsync(
                            moveUrl,
                            APIRequestContextOptions(
                                Headers = dict [ "Content-Type", "application/ld+json" ],
                                DataObject = moveBody
                            )
                        )

                    Assert.That(moveResp.Status, Is.EqualTo 200, sprintf "Move %d failed" (turn + 1))

                    // Capture has_provenance link from the move response (discovered, not hardcoded).
                    let moveRels = SemanticTests.LinkRels moveResp
                    Assert.That(moveRels.ContainsKey provRel, Is.True, "Move response missing has_provenance Link")
                    provenanceResourceUrl <- moveRels.[provRel]

                    moveLog.Add(player, square)
                    turn <- turn + 1

            Assert.That(finished, Is.True, "Game did not finish via discovery")
            Assert.That(moveLog.Count, Is.GreaterThan 0, "No moves were logged")

            // provenanceResourceUrl is the DISCOVERED moves resource URI — NOT hardcoded.
            Assert.That(provenanceResourceUrl, Is.Not.Empty, "has_provenance link never captured")

            // ── Phase 4: Fetch lineage via the discovered resource URL ────────
            // has_provenance link points to the moves resource URI; /provenance?resource=<that URI>
            // returns all captured records for that resource.
            let lineageQuery =
                sprintf "/provenance?resource=%s" (Uri.EscapeDataString provenanceResourceUrl)

            let! lineageResp = ctx.GetAsync lineageQuery
            Assert.That(lineageResp.Status, Is.EqualTo 200, "Provenance lineage endpoint not 200")
            let! lineageBody = lineageResp.TextAsync()

            // ── Phase 5: Assert COMPLETE CAPTURE ─────────────────────────────
            let activityCount, timestamps = SemanticTests.ParseProvenanceActivities lineageBody

            // (1) Count: exactly one prov:Activity per posted move.
            // Fails if any move was dropped (count too low) or fabricated (count too high).
            Assert.That(
                activityCount,
                Is.EqualTo moveLog.Count,
                sprintf "INCOMPLETE CAPTURE: activity count %d != moves posted %d" activityCount moveLog.Count
            )

            // (2) Attribution: every activity carries prov:wasAssociatedWith.
            // The HTTP agent is "anonymous" in this unauthenticated sample; game-level
            // player (X/O) attribution requires request-body capture (current design gap).
            Assert.That(
                lineageBody.Contains "prov:wasAssociatedWith",
                Is.True,
                "Activities lack prov:wasAssociatedWith — agent attribution missing from lineage"
            )

            // (3) Order: prov:startedAtTime values must be in non-decreasing order.
            // Activities are sequential (one per turn); any reordering or gap would surface here.
            Assert.That(
                timestamps.Length,
                Is.EqualTo moveLog.Count,
                sprintf "Timestamp count %d != move count %d" timestamps.Length moveLog.Count
            )

            let inOrder =
                timestamps
                |> List.pairwise
                |> List.forall (fun (a, b) -> a <= b)

            Assert.That(inOrder, Is.True, "REORDERED: activity timestamps not in ascending order")

            // (4) Terminal outcome: the recorded final game state must be Won or Draw.
            let! finalResp = ctx.GetAsync gameUrl
            Assert.That(finalResp.Status, Is.EqualTo 200)
            let! finalJson = finalResp.JsonAsync()
            let finalStatus = finalJson.Value.GetProperty("status").GetString()

            Assert.That(
                finalStatus = "Won" || finalStatus = "Draw",
                Is.True,
                sprintf "Game did not reach terminal state: '%s'" finalStatus
            )
        }
