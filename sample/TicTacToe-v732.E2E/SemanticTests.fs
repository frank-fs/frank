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

    // ── AT-S5: JSON-LD content negotiation with external @context ────────────────
    [<Test>]
    member this.``AT-S5 game negotiates JSON-LD with external schema.org @context``() =
        task {
            use! ctx = this.NewContext()

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
        }

    // ── AT-S6: capstone — naive client plays a full game via discovery only ──────
    [<Test>]
    member this.``AT-S6 naive client plays a full game via discovery only``() =
        task {
            use! ctx = this.NewContext()
            let gameId = "at-s6"

            // 1. JSON Home → game resource (GET) and moves resource (POST) templates
            let! home =
                ctx.GetAsync("/", APIRequestContextOptions(Headers = dict [ "Accept", "application/json-home" ]))

            let! homeJson = home.JsonAsync()
            let resources = homeJson.Value.GetProperty("resources")

            let templateWith (verb: string) =
                resources.EnumerateObject()
                |> Seq.tryPick (fun r ->
                    let v = r.Value
                    let mutable hints = Unchecked.defaultof<JsonElement>
                    let mutable tmpl = Unchecked.defaultof<JsonElement>

                    let allowsVerb =
                        v.TryGetProperty("hints", &hints)
                        && (let mutable allow = Unchecked.defaultof<JsonElement>

                            hints.TryGetProperty("allow", &allow)
                            && allow.EnumerateArray() |> Seq.exists (fun m -> m.GetString() = verb))

                    if allowsVerb && v.TryGetProperty("href-template", &tmpl) then
                        Some(tmpl.GetString())
                    else
                        None)

            let expand (template: string) =
                let o = template.IndexOf '{'

                if o < 0 then
                    template
                else
                    template.Substring(0, o)
                    + Uri.EscapeDataString gameId
                    + template.Substring(template.IndexOf '}' + 1)

            let gameUrl =
                templateWith "GET"
                |> Option.map expand
                |> Option.defaultWith (fun () -> failwith "JSON Home: no GET game resource")

            let moveUrl =
                templateWith "POST"
                |> Option.map expand
                |> Option.defaultWith (fun () -> failwith "JSON Home: no POST moves resource")

            // 2. OPTIONS game → ALPS profile → field IRIs (schema.org)
            let! opts = this.Options(ctx, gameUrl)
            let alpsUrl = (SemanticTests.LinkRels opts).["describedby"]
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
                    | JsonValueKind.Array ->
                        for item in el.EnumerateArray() do
                            walk item
                    | _ -> ()

                walk alpsDoc.RootElement
                found

            let positionIri =
                fieldIri "position"
                |> Option.defaultWith (fun () -> failwith "ALPS missing position IRI")

            let agentIri =
                fieldIri "agent"
                |> Option.defaultWith (fun () -> failwith "ALPS missing agent IRI")

            // 3. capstone also reads JSON-LD responses (external @context) ...
            let! ldState =
                ctx.GetAsync(gameUrl, APIRequestContextOptions(Headers = dict [ "Accept", "application/ld+json" ]))

            let! ldBody = ldState.TextAsync()

            Assert.That(
                ldBody.Contains "@context" && ldBody.Contains "schema.org",
                Is.True,
                "capstone: game not available as external-context JSON-LD"
            )

            // ... and an illegal move is rejected by SHACL with a schema.org IRI in the report
            let invalid = Dictionary<string, obj>()
            invalid.["@type"] <- "https://schema.org/Action"
            invalid.[positionIri] <- "NotASquare"
            invalid.[agentIri] <- "X"

            let! bad =
                ctx.PostAsync(
                    moveUrl,
                    APIRequestContextOptions(
                        Headers = dict [ "Content-Type", "application/ld+json" ],
                        DataObject = invalid
                    )
                )

            Assert.That(bad.Status, Is.EqualTo 422, "capstone: SHACL did not reject an illegal move")
            let! badBody = bad.TextAsync()
            Assert.That(badBody.Contains "schema.org", Is.True, "capstone: SHACL error does not cite a schema.org IRI")

            // 4. play until the game ends, reading state, posting discovered IRIs
            let mutable finished = false
            let mutable turn = 0

            while not finished && turn < 9 do
                let! state = ctx.GetAsync(gameUrl)
                let! sJson = state.JsonAsync()
                let root = sJson.Value
                let status = root.GetProperty("status").GetString()

                if status = "Won" || status = "Draw" then
                    finished <- true
                else
                    let player = root.GetProperty("currentPlayer").GetString()

                    let emptyPos =
                        root.GetProperty("squares").EnumerateObject()
                        |> Seq.find (fun p -> p.Value.ValueKind = JsonValueKind.Null)

                    let body = Dictionary<string, obj>()
                    body.["@type"] <- "https://schema.org/Action"
                    body.[positionIri] <- emptyPos.Name
                    body.[agentIri] <- player

                    let! _ =
                        ctx.PostAsync(
                            moveUrl,
                            APIRequestContextOptions(
                                Headers = dict [ "Content-Type", "application/ld+json" ],
                                DataObject = body
                            )
                        )

                    turn <- turn + 1

            Assert.That(finished, Is.True, "naive client could not finish the game via discovery")
        }
