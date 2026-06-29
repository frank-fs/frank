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
