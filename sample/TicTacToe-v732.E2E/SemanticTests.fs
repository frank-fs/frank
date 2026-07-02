namespace TicTacToe.E2E

open System
open System.Collections.Generic
open System.Net.Http
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

    static let httpClient =
        let c = new HttpClient()
        c.DefaultRequestHeaders.Add("User-Agent", "Frank-E2E-Test/1.0")
        c

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

                    if not (isNull v) then
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
    /// Searches nested descriptors recursively; depth bounded by ALPS document structure.
    static member private AlpsDescriptorHrefByLocalId(alpsBody: string, localId: string) : string option =
        use doc = JsonDocument.Parse alpsBody
        let mutable alpsEl = Unchecked.defaultof<JsonElement>
        let mutable descriptorEl = Unchecked.defaultof<JsonElement>

        let matchHref (d: JsonElement) : string option =
            let mutable idEl = Unchecked.defaultof<JsonElement>
            let mutable hrefEl = Unchecked.defaultof<JsonElement>

            if
                d.TryGetProperty("id", &idEl)
                && idEl.GetString() = localId
                && d.TryGetProperty("href", &hrefEl)
            then
                hrefEl.GetString() |> Option.ofObj
            else
                None

        let rec findIn (arr: JsonElement) : string option =
            arr.EnumerateArray()
            |> Seq.tryPick (fun d ->
                match matchHref d with
                | Some h -> Some h
                | None ->
                    let mutable nestedEl = Unchecked.defaultof<JsonElement>

                    if d.TryGetProperty("descriptor", &nestedEl) then
                        findIn nestedEl
                    else
                        None)

        if
            doc.RootElement.TryGetProperty("alps", &alpsEl)
            && alpsEl.TryGetProperty("descriptor", &descriptorEl)
        then
            findIn descriptorEl
        else
            None

    /// Find the non-agent input of the ALPS 'unsafe' (action) descriptor by role.
    /// Returns the origin-resolved absolute IRI of the nested field whose href is NOT agentIri.
    /// Relative hrefs are resolved using originBase. Returns None when no such field exists.
    static member private FindMoveInputByRole(alpsBody: string, agentIri: string, originBase: string) : string option =
        use doc = JsonDocument.Parse alpsBody
        let mutable alpsEl = Unchecked.defaultof<JsonElement>
        let mutable descriptorEl = Unchecked.defaultof<JsonElement>

        let resolveHref (h: string) =
            if h.StartsWith "/" then originBase + h else h

        let fieldHref (d: JsonElement) : string option =
            let mutable hEl = Unchecked.defaultof<JsonElement>

            if d.TryGetProperty("href", &hEl) then
                hEl.GetString() |> Option.ofObj |> Option.map resolveHref
            else
                None

        if
            not (
                doc.RootElement.TryGetProperty("alps", &alpsEl)
                && alpsEl.TryGetProperty("descriptor", &descriptorEl)
            )
        then
            None
        else
            descriptorEl.EnumerateArray()
            |> Seq.tryPick (fun d ->
                let mutable typeEl = Unchecked.defaultof<JsonElement>
                let mutable nestedEl = Unchecked.defaultof<JsonElement>
                let isAction = d.TryGetProperty("type", &typeEl) && typeEl.GetString() = "unsafe"

                if not isAction || not (d.TryGetProperty("descriptor", &nestedEl)) then
                    None
                else
                    nestedEl.EnumerateArray()
                    |> Seq.tryPick (fun field ->
                        match fieldHref field with
                        | Some h when h <> agentIri -> Some h
                        | _ -> None))

    /// Extract a literal string from a JSON element: plain string, @value object, or @value in array.
    static member private TryExtractLiteral(el: JsonElement) : string option =
        match el.ValueKind with
        | JsonValueKind.String -> Some(el.GetString())
        | JsonValueKind.Array ->
            el.EnumerateArray()
            |> Seq.tryPick (fun v ->
                match v.ValueKind with
                | JsonValueKind.String -> Some(v.GetString())
                | JsonValueKind.Object ->
                    let mutable valEl = Unchecked.defaultof<JsonElement>

                    if v.TryGetProperty("@value", &valEl) then
                        Some(valEl.GetString())
                    else
                        None
                | _ -> None)
        | JsonValueKind.Object ->
            let mutable valEl = Unchecked.defaultof<JsonElement>

            if el.TryGetProperty("@value", &valEl) then
                Some(valEl.GetString())
            else
                None
        | _ -> None

    /// Extract all literal strings from a JSON element (handles arrays and single values).
    static member private ExtractAllLiterals(el: JsonElement) : string list =
        match el.ValueKind with
        | JsonValueKind.String -> [ el.GetString() ]
        | JsonValueKind.Array ->
            [ for v in el.EnumerateArray() do
                  match v.ValueKind with
                  | JsonValueKind.String -> yield v.GetString()
                  | JsonValueKind.Object ->
                      let mutable valEl = Unchecked.defaultof<JsonElement>

                      if v.TryGetProperty("@value", &valEl) then
                          yield valEl.GetString()
                  | _ -> () ]
        | JsonValueKind.Object ->
            let mutable valEl = Unchecked.defaultof<JsonElement>

            if el.TryGetProperty("@value", &valEl) then
                [ valEl.GetString() ]
            else
                []
        | _ -> []

    /// Try reading a property on a node by full IRI key first, then its compacted CURIE.
    static member private TryGetByEitherKey
        (node: JsonElement, fullKey: string, prefixes: System.Collections.Generic.Dictionary<string, string>)
        : JsonElement option =
        let mutable el = Unchecked.defaultof<JsonElement>

        if node.TryGetProperty(fullKey, &el) then
            Some el
        else
            match SemanticTests.TryCompactIri(fullKey, prefixes) with
            | Some compact ->
                let mutable el2 = Unchecked.defaultof<JsonElement>

                if node.TryGetProperty(compact, &el2) then
                    Some el2
                else
                    None
            | None -> None

    /// True when the node's @id (expanded against context base) equals the target IRI.
    static member private NodeMatchesIri
        (
            node: JsonElement,
            targetIri: string,
            prefixes: System.Collections.Generic.Dictionary<string, string>,
            contextBase: string
        ) : bool =
        let mutable idEl = Unchecked.defaultof<JsonElement>

        node.TryGetProperty("@id", &idEl)
        && SemanticTests.ExpandIri(idEl.GetString(), prefixes, contextBase) = targetIri

    /// Common skeleton: parse body once, extract prefixes+base from the same document,
    /// walk @graph, find the game node, look up fullKey via TryGetByEitherKey.
    /// Returns a Clone'd JsonElement so the caller owns the lifetime, or None when absent.
    static member private TryReadGameNodeProperty
        (ldBody: string, gameIri: string, fullKey: string)
        : JsonElement option =
        use doc = JsonDocument.Parse ldBody
        let prefixes = System.Collections.Generic.Dictionary<string, string>()
        let mutable contextBase = ""
        let mutable ctxEl = Unchecked.defaultof<JsonElement>

        if doc.RootElement.TryGetProperty("@context", &ctxEl) then
            let scanObject (el: JsonElement) =
                if el.ValueKind = JsonValueKind.Object then
                    for prop in el.EnumerateObject() do
                        if prop.Name = "@base" && prop.Value.ValueKind = JsonValueKind.String then
                            contextBase <- prop.Value.GetString()
                        elif not (prop.Name.StartsWith "@") && prop.Value.ValueKind = JsonValueKind.String then
                            prefixes.[prop.Name] <- prop.Value.GetString()

            match ctxEl.ValueKind with
            | JsonValueKind.Array ->
                for item in ctxEl.EnumerateArray() do
                    scanObject item
            | _ -> scanObject ctxEl

        let mutable graphEl = Unchecked.defaultof<JsonElement>

        if not (doc.RootElement.TryGetProperty("@graph", &graphEl)) then
            None
        else
            graphEl.EnumerateArray()
            |> Seq.tryPick (fun node ->
                if not (SemanticTests.NodeMatchesIri(node, gameIri, prefixes, contextBase)) then
                    None
                else
                    SemanticTests.TryGetByEitherKey(node, fullKey, prefixes))
            |> Option.map (fun el -> el.Clone())

    /// Parse schema:actionStatus IRI from the game's JSON-LD @graph body.
    /// Handles both expanded (full IRI key + array value) and compacted (CURIE key + object value) forms.
    /// Returns the expanded actionStatus IRI, or "" when not found.
    static member private ParseActionStatus(ldBody: string, gameIri: string) : string =
        let prefixes = SemanticTests.ParseContextPrefixes ldBody

        let tryGetId (el: JsonElement) =
            let mutable idEl = Unchecked.defaultof<JsonElement>

            if el.TryGetProperty("@id", &idEl) then
                Some(SemanticTests.ExpandIri(idEl.GetString(), prefixes, "https://schema.org/"))
            else
                None

        let extractStatus (statusEl: JsonElement) =
            match statusEl.ValueKind with
            | JsonValueKind.Array -> statusEl.EnumerateArray() |> Seq.tryPick tryGetId
            | JsonValueKind.Object -> tryGetId statusEl
            | _ -> None

        SemanticTests.TryReadGameNodeProperty(ldBody, gameIri, "https://schema.org/actionStatus")
        |> Option.bind extractStatus
        |> Option.defaultValue ""

    /// Parse ttt:currentPlayer literal from the game's JSON-LD @graph body.
    /// Handles both expanded (full IRI key, @value wrapper) and compacted (CURIE key, plain string) forms.
    static member private ParseCurrentPlayer(ldBody: string, gameIri: string, originBase: string) : string =
        let fullKey = originBase + "/tictactoe#currentPlayer"

        SemanticTests.TryReadGameNodeProperty(ldBody, gameIri, fullKey)
        |> Option.bind SemanticTests.TryExtractLiteral
        |> Option.defaultValue ""

    /// Parse ttt:validMoves values from the game's JSON-LD @graph body.
    /// Dual-form: handles IRI nodes ({"@id":"ttt:TopLeft"} — emitted after MINOR-6)
    /// and plain string literals (backward compat / provenance graph form).
    /// Returns the local name of each move (e.g. "TopLeft") for use as POST body values.
    static member private ParseValidMoves(ldBody: string, gameIri: string, originBase: string) : string list =
        let fullKey = originBase + "/tictactoe#validMoves"
        let prefixes = SemanticTests.ParseContextPrefixes ldBody
        let contextBase = SemanticTests.ExtractContextBase ldBody

        let iriLocalName (iri: string) : string =
            let hashIdx = iri.LastIndexOf '#'

            if hashIdx >= 0 then
                iri.Substring(hashIdx + 1)
            else
                let colonIdx = iri.LastIndexOf ':'
                if colonIdx >= 0 then iri.Substring(colonIdx + 1) else iri

        let extractMoveId (el: JsonElement) : string option =
            match el.ValueKind with
            | JsonValueKind.String -> Some(el.GetString())
            | JsonValueKind.Object ->
                let mutable v = Unchecked.defaultof<JsonElement>

                if el.TryGetProperty("@id", &v) then
                    Some(iriLocalName (SemanticTests.ExpandIri(v.GetString(), prefixes, contextBase)))
                elif el.TryGetProperty("@value", &v) then
                    Some(v.GetString())
                else
                    None
            | _ -> None

        let extractAllMoves (el: JsonElement) : string list =
            match el.ValueKind with
            | JsonValueKind.Array ->
                [ for item in el.EnumerateArray() do
                      match extractMoveId item with
                      | Some s -> yield s
                      | None -> () ]
            | _ ->
                match extractMoveId el with
                | Some s -> [ s ]
                | None -> []

        SemanticTests.TryReadGameNodeProperty(ldBody, gameIri, fullKey)
        |> Option.map extractAllMoves
        |> Option.defaultValue []

    /// Parse prefix→expansion mappings from a JSON-LD @context.
    /// Skips keywords (@base, @vocab, etc.) and non-string values.
    static member private ParseContextPrefixes(body: string) : System.Collections.Generic.Dictionary<string, string> =
        use doc = JsonDocument.Parse body
        let result = System.Collections.Generic.Dictionary<string, string>()
        let mutable ctxEl = Unchecked.defaultof<JsonElement>

        if doc.RootElement.TryGetProperty("@context", &ctxEl) then
            let addFromObject (el: JsonElement) =
                if el.ValueKind = JsonValueKind.Object then
                    for prop in el.EnumerateObject() do
                        if not (prop.Name.StartsWith "@") && prop.Value.ValueKind = JsonValueKind.String then
                            result.[prop.Name] <- prop.Value.GetString()

            match ctxEl.ValueKind with
            | JsonValueKind.Array ->
                for item in ctxEl.EnumerateArray() do
                    addFromObject item
            | _ -> addFromObject ctxEl

        result

    /// Extract @base from @context. Returns "" when absent.
    static member private ExtractContextBase(body: string) : string =
        use doc = JsonDocument.Parse body
        let mutable ctxEl = Unchecked.defaultof<JsonElement>

        if not (doc.RootElement.TryGetProperty("@context", &ctxEl)) then
            ""
        else
            let tryBase (el: JsonElement) =
                let mutable baseEl = Unchecked.defaultof<JsonElement>

                if el.ValueKind = JsonValueKind.Object && el.TryGetProperty("@base", &baseEl) then
                    baseEl.GetString() |> Option.ofObj
                else
                    None

            match ctxEl.ValueKind with
            | JsonValueKind.Array -> ctxEl.EnumerateArray() |> Seq.tryPick tryBase |> Option.defaultValue ""
            | _ -> tryBase ctxEl |> Option.defaultValue ""

    /// Compute the compacted CURIE form of a full IRI using a prefix map.
    /// Returns None when no registered prefix covers the IRI.
    static member private TryCompactIri
        (fullIri: string, prefixes: System.Collections.Generic.Dictionary<string, string>)
        : string option =
        prefixes
        |> Seq.tryPick (fun kv ->
            if
                kv.Value.Length > 0
                && fullIri.StartsWith kv.Value
                && fullIri.Length > kv.Value.Length
            then
                Some(kv.Key + ":" + fullIri.Substring kv.Value.Length)
            else
                None)

    /// Expand a (possibly relative or compacted) IRI to an absolute IRI.
    /// Handles: full http/https → as-is; /path → scheme+authority + path;
    /// prefix:local (prefix in map) → expansion + local; relative path → base + "/" + path.
    static member private ExpandIri
        (iri: string, prefixes: System.Collections.Generic.Dictionary<string, string>, base': string)
        : string =
        if iri.StartsWith "http" then
            iri
        elif iri.StartsWith "/" && base'.Length > 0 then
            let baseUri = Uri base'
            baseUri.Scheme + "://" + baseUri.Authority + iri
        else
            let colonIdx = iri.IndexOf ':'

            if colonIdx > 0 then
                let prefix = iri.Substring(0, colonIdx)
                let local = iri.Substring(colonIdx + 1)

                match prefixes.TryGetValue prefix with
                | true, expansion -> expansion + local
                | _ -> iri
            else
                base'.TrimEnd '/' + "/" + iri

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

            // ── #9: move resource href-vars {id} must resolve to an absolute IRI ──
            // The move resource (/games/{id}/moves) inherits {id} from the Game
            // resource's path segment. Its href-vars must NOT contain "" for id.
            let moveResource =
                resources.EnumerateObject()
                |> Seq.tryFind (fun r ->
                    let mutable tmpl = Unchecked.defaultof<JsonElement>

                    r.Value.TryGetProperty("href-template", &tmpl)
                    && (tmpl.GetString() |> Option.ofObj |> Option.exists (fun s -> s.Contains "/moves")))

            Assert.That(moveResource.IsSome, Is.True, "Move resource not found in JSON Home")
            let mutable moveHrefVars = Unchecked.defaultof<JsonElement>

            Assert.That(
                moveResource.Value.Value.TryGetProperty("href-vars", &moveHrefVars),
                Is.True,
                "Move resource missing href-vars"
            )

            let mutable idVar = Unchecked.defaultof<JsonElement>
            Assert.That(moveHrefVars.TryGetProperty("id", &idVar), Is.True, "href-vars missing 'id' key")

            Assert.That(
                idVar.GetString(),
                Is.Not.Empty,
                "href-vars 'id' must not be empty — {id} in /games/{id}/moves must resolve to schema:identifier"
            )

            Assert.That(
                idVar.GetString().StartsWith "http",
                Is.True,
                "href-vars 'id' must be an absolute IRI (schema:identifier)"
            )
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
            let originBase = (Server.Url()).TrimEnd('/')
            let squareIri = originBase + "/tictactoe#square"

            let badMove = Dictionary<string, obj>()
            badMove.["@type"] <- "https://schema.org/MoveAction"
            badMove.["https://schema.org/agent"] <- "X"
            badMove.[squareIri] <- "NotASquare"

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
            Assert.That(body.Contains "tictactoe", Is.True, "422 ValidationReport must cite tictactoe path")
            Assert.That(body.Contains "example.org", Is.False, "422 ValidationReport must not cite example.org")
        // valid-move → 200 is covered by the capstone (AT-S6) using discovered IRIs.
        }

    // ── AT-S5: content negotiation — all three formats ───────────────────────────
    // The game endpoint serves its OWN instance graph (not the global ontology).
    // JSON-LD and Turtle must carry the game's triples (schema:actionStatus, ttt: terms).
    // No example.org in any RDF response — all term IRIs are host-resolved.
    [<Test>]
    member this.``AT-S5 game negotiates JSON-LD with external schema.org @context``() =
        task {
            use! ctx = this.NewContext()
            let originBase = (Server.Url()).TrimEnd('/')

            // ── ld+json: game's OWN graph — schema:actionStatus + ttt: terms ─────
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
            // Game's own triples: actionStatus (per-instance, not global ontology)
            Assert.That(
                body.Contains "actionStatus",
                Is.True,
                "JSON-LD game graph lacks schema:actionStatus — serving global ontology instead of game instance"
            )

            Assert.That(
                body.Contains "ActiveActionStatus" || body.Contains "CompletedActionStatus",
                Is.True,
                "JSON-LD game graph lacks a schema:ActionStatus individual"
            )
            // ttt: terms are host-resolved (no example.org)
            Assert.That(body.Contains "tictactoe#", Is.True, "JSON-LD game graph lacks host-resolved ttt: terms")

            Assert.That(
                body.Contains "example.org",
                Is.False,
                "JSON-LD game graph must not contain example.org — IRIs must be host-resolved"
            )

            // ── application/json: compact JSON game state ────────────────────────
            let! json =
                ctx.GetAsync("/games/at-s5", APIRequestContextOptions(Headers = dict [ "Accept", "application/json" ]))

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

            // ── text/turtle: game instance graph in Turtle ───────────────────────
            let! turtle =
                ctx.GetAsync("/games/at-s5", APIRequestContextOptions(Headers = dict [ "Accept", "text/turtle" ]))

            Assert.That(turtle.Status, Is.EqualTo 200, "text/turtle not negotiated")

            let! turtleBody = turtle.TextAsync()
            Assert.That(turtleBody.Contains "@prefix", Is.True, "text/turtle body is not Turtle syntax")

            Assert.That(
                turtleBody.Contains "actionStatus",
                Is.True,
                "Turtle game graph lacks schema:actionStatus — serving global ontology instead of game instance"
            )

            Assert.That(
                turtleBody.Contains "example.org",
                Is.False,
                "Turtle game graph must not contain example.org — IRIs must be host-resolved"
            )

            ignore originBase
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
                    tpl.Substring(0, o)
                    + Uri.EscapeDataString gameId
                    + tpl.Substring(tpl.IndexOf '}' + 1)

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

            // ── Phase 3: Collect hrefs; resolve relative hrefs to origin-absolute ──
            let originBase = testBase.TrimEnd('/')

            let descriptorHrefs =
                SemanticTests.AlpsDescriptorHrefs alpsBody
                |> List.map (fun href -> if href.StartsWith "/" then originBase + href else href)

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
                  originBase + "/tictactoe#square"
                  "https://schema.org/Game"
                  "https://schema.org/result" ]

            for term in expectedTerms do
                Assert.That(hrefSet.Contains term, Is.True, sprintf "Expected semantic term absent from ALPS: %s" term)

            // ── Phase 4: Dereference every URI the client received ───────────────
            // schema.org term IRIs — dereference live using HttpClient (host network,
            // avoids Playwright DNS sandbox flake; follows redirects by default).
            // A network failure or non-2xx FAILS the test — no swallowing.
            for iri in descriptorHrefs |> List.filter (fun u -> u.StartsWith "https://schema.org/") do
                let! r = httpClient.GetAsync iri

                Assert.That(
                    int r.StatusCode,
                    Is.InRange(200, 299),
                    sprintf "schema.org IRI not dereferenceable: %s" iri
                )

            // local vocab term — dereference against the test server (origin-resolved)
            for localHref in descriptorHrefs |> List.filter (fun u -> u.StartsWith originBase) do
                let path = Uri(localHref).AbsolutePath
                let! r = ctx.GetAsync(originBase + path)

                Assert.That(
                    r.Status,
                    Is.EqualTo 200,
                    sprintf "local vocab resource not served at %s" (originBase + path)
                )

                let! tttBody = r.TextAsync()

                Assert.That(
                    tttBody.Contains "ttt:square" || tttBody.Contains "tictactoe#square",
                    Is.True,
                    "vocab resource body does not reference the term"
                )

                Assert.That(
                    tttBody.Contains "example.org",
                    Is.False,
                    "vocab resource body must not contain example.org — term IRIs must be host-resolved, not example.org"
                )

            // game's ld+json — must be the game instance graph, not the global ontology
            let! ldGame =
                ctx.GetAsync(gameUrl, APIRequestContextOptions(Headers = dict [ "Accept", "application/ld+json" ]))

            let! ldBody = ldGame.TextAsync()

            Assert.That(
                ldBody.Contains "@context" && ldBody.Contains "schema.org",
                Is.True,
                "Game not available as external-context JSON-LD"
            )

            // Game instance graph: schema:actionStatus and ttt: terms must be present
            Assert.That(
                ldBody.Contains "actionStatus",
                Is.True,
                "Game ld+json lacks schema:actionStatus — serving global ontology instead of game instance"
            )

            Assert.That(ldBody.Contains "tictactoe#", Is.True, "Game ld+json lacks host-resolved ttt: terms")

            Assert.That(
                ldBody.Contains "example.org",
                Is.False,
                "Game ld+json must not contain example.org — IRIs must be host-resolved"
            )

            // seeAlso targets — game graph has none; loop is vacuous but not removed for structure
            for seeAlsoUri in SemanticTests.SeeAlsoUris ldBody do
                let! r = httpClient.GetAsync seeAlsoUri

                Assert.That(
                    int r.StatusCode,
                    Is.InRange(200, 299),
                    sprintf "seeAlso target did not resolve: %s" seeAlsoUri
                )

            // ── Phase 5: Identify inputs by absolute IRI and structural role ────
            // agentIri is found by its well-known schema.org IRI.
            // squareIri is found by ROLE — the MoveAction nested field that is NOT
            // the agent — so the client survives term renames (AC2).
            let agentIri =
                descriptorHrefs
                |> List.tryFind (fun h -> h = "https://schema.org/agent")
                |> Option.defaultWith (fun () -> failwith "ALPS missing agent IRI (https://schema.org/agent)")

            let squareIri =
                SemanticTests.FindMoveInputByRole(alpsBody, agentIri, originBase)
                |> Option.defaultWith (fun () -> failwith "ALPS MoveAction has no non-agent nested descriptor")

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
            // State is read from the game's RDF graph via schema:actionStatus and ttt: terms.
            // No hardcoded field names ("status"/"Won"/"Draw") — IRIs only.
            let gameAbsoluteIri = originBase + gameUrl
            let completedStatusIri = "https://schema.org/CompletedActionStatus"
            let failedStatusIri = "https://schema.org/FailedActionStatus"
            let mutable finished = false
            let mutable turn = 0

            while not finished && turn < 9 do
                let! stateResp =
                    ctx.GetAsync(gameUrl, APIRequestContextOptions(Headers = dict [ "Accept", "application/ld+json" ]))

                let! ldStateBody = stateResp.TextAsync()
                let actionStatus = SemanticTests.ParseActionStatus(ldStateBody, gameAbsoluteIri)

                if actionStatus = completedStatusIri || actionStatus = failedStatusIri then
                    finished <- true
                else
                    let player =
                        SemanticTests.ParseCurrentPlayer(ldStateBody, gameAbsoluteIri, originBase)

                    let validMoves =
                        SemanticTests.ParseValidMoves(ldStateBody, gameAbsoluteIri, originBase)

                    Assert.That(
                        player,
                        Is.Not.Empty,
                        sprintf "Turn %d: ttt:currentPlayer not found in game ld+json" turn
                    )

                    Assert.That(
                        validMoves,
                        Is.Not.Empty,
                        sprintf "Turn %d: ttt:validMoves not found in game ld+json" turn
                    )

                    let square = validMoves |> List.head

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

                    Assert.That(
                        moveResp.Status,
                        Is.EqualTo 200,
                        sprintf "Phase 7: move %d returned %d — expected 200" (turn + 1) moveResp.Status
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

    /// For each prov:Activity in the lineage (sorted by prov:startedAtTime), return (player, square)
    /// from the IRI-keyed body attributes emitted by the provenance middleware.
    /// squareIri: the origin-resolved absolute IRI for the square property (e.g. http://localhost:PORT/tictactoe#square).
    static member private ParseMoveAttributes(body: string, squareIri: string) : (string * string) list =
        use doc = JsonDocument.Parse body
        let agentFullIri = "https://schema.org/agent"
        let prefixes = SemanticTests.ParseContextPrefixes body

        let isActivity (el: JsonElement) =
            let mutable t = Unchecked.defaultof<JsonElement>

            if not (el.TryGetProperty("@type", &t)) then
                false
            else
                match t.ValueKind with
                | JsonValueKind.String -> t.GetString() = "prov:Activity"
                | JsonValueKind.Array ->
                    t.EnumerateArray()
                    |> Seq.exists (fun x -> x.ValueKind = JsonValueKind.String && x.GetString() = "prov:Activity")
                | _ -> false

        /// Extract a string value from a property element: plain string, @value, or @id fragment.
        /// For @id: tries # fragment first (full IRI), then : local name (compacted CURIE).
        let extractStr (p: JsonElement) : string option =
            match p.ValueKind with
            | JsonValueKind.String -> Some(p.GetString())
            | JsonValueKind.Object ->
                let mutable v = Unchecked.defaultof<JsonElement>

                if p.TryGetProperty("@value", &v) then
                    Some(v.GetString())
                elif p.TryGetProperty("@id", &v) then
                    let s = v.GetString()
                    let hashIdx = s.LastIndexOf '#'

                    if hashIdx >= 0 then
                        Some(s.Substring(hashIdx + 1))
                    else
                        let colonIdx = s.LastIndexOf ':'

                        if colonIdx >= 0 then
                            Some(s.Substring(colonIdx + 1))
                        else
                            Some s
                else
                    None
            | _ -> None

        /// Try reading a property by full IRI key, then by its compacted CURIE (via @context).
        let tryGetStr (el: JsonElement) (fullKey: string) : string option =
            SemanticTests.TryGetByEitherKey(el, fullKey, prefixes) |> Option.bind extractStr

        let tryGetTimestamp (el: JsonElement) : DateTimeOffset option =
            let mutable ts = Unchecked.defaultof<JsonElement>

            if not (el.TryGetProperty("prov:startedAtTime", &ts)) then
                None
            else
                let mutable v = Unchecked.defaultof<JsonElement>

                let raw =
                    if ts.ValueKind = JsonValueKind.String then
                        ts.GetString()
                    elif ts.ValueKind = JsonValueKind.Object && ts.TryGetProperty("@value", &v) then
                        v.GetString()
                    else
                        null

                match DateTimeOffset.TryParse raw with
                | true, dt -> Some dt
                | _ -> None

        let mutable graphEl = Unchecked.defaultof<JsonElement>
        let root = doc.RootElement

        let nodes =
            if root.TryGetProperty("@graph", &graphEl) then
                graphEl.EnumerateArray() |> Seq.toList
            else
                [ root ]

        let acc = System.Collections.Generic.List<DateTimeOffset * string * string>()

        for node in nodes do
            if isActivity node then
                match tryGetStr node agentFullIri, tryGetStr node squareIri, tryGetTimestamp node with
                | Some a, Some s, Some t -> acc.Add(t, a, s)
                | _ -> ()

        acc
        |> Seq.toList
        |> List.sortBy (fun (t, _, _) -> t)
        |> List.map (fun (_, a, s) -> (a, s))

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
            use! ctx = this.Playwright.APIRequest.NewContextAsync(APIRequestNewContextOptions(BaseURL = ExServer.Url()))

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

            Assert.That(alpsBody.Contains "/ex#", Is.True, "ex: server ALPS does not contain ex: namespace IRIs")

            // ── Phase 3: Discovery navigator — find IRIs by role and local name ──
            // Vocab-neutral: looks up descriptors without hardcoding vocab IRIs.
            // squareIri is found by ROLE (not by local-name) so the client survives
            // the ex:square → ex:cell rename (AC2+AC3).
            let exOriginBase = (ExServer.Url()).TrimEnd('/')

            let agentIri =
                SemanticTests.AlpsDescriptorHrefByLocalId(alpsBody, "agent")
                |> Option.map (fun h -> if h.StartsWith "/" then exOriginBase + h else h)
                |> Option.defaultWith (fun () -> failwith "ALPS missing descriptor id='agent'")

            let classIri =
                SemanticTests.AlpsDescriptorHrefByLocalId(alpsBody, "MoveAction")
                |> Option.map (fun h -> if h.StartsWith "/" then exOriginBase + h else h)
                |> Option.defaultWith (fun () -> failwith "ALPS missing descriptor id='MoveAction'")

            // Role-based: the nested MoveAction field that is NOT the agent (AC2+AC3).
            // Must NOT use a literal "square" or "ex:square" lookup — proves rename resilience.
            let squareIri =
                SemanticTests.FindMoveInputByRole(alpsBody, agentIri, exOriginBase)
                |> Option.defaultWith (fun () -> failwith "ALPS MoveAction has no non-agent nested descriptor")

            // Confirm the server actually served ex: IRIs (not schema.org).
            Assert.That(agentIri.Contains "/ex#", Is.True, "agentIri not in ex: namespace")
            Assert.That(squareIri.Contains "/ex#", Is.True, "squareIri not in ex: namespace")
            Assert.That(classIri.Contains "/ex#", Is.True, "classIri not in ex: namespace")
            // AC3: the renamed term (ex:cell) must be exercised — discovered by role, not by name.
            Assert.That(
                squareIri.Contains "cell",
                Is.True,
                "squareIri does not contain 'cell' — ex:square → ex:cell rename was not exercised by role-based selection"
            )

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
                    tpl.Substring(0, o)
                    + Uri.EscapeDataString gameId
                    + tpl.Substring(tpl.IndexOf '}' + 1)

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

                    let! moveResp =
                        ctx.PostAsync(
                            moveUrl,
                            APIRequestContextOptions(
                                Headers = dict [ "Content-Type", "application/ld+json" ],
                                DataObject = moveBody
                            )
                        )

                    Assert.That(
                        moveResp.Status,
                        Is.EqualTo 200,
                        sprintf "AT-S7 move %d returned %d — expected 200" (turn + 1) moveResp.Status
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
    //   (2) Attribution: each activity carries prov:wasAssociatedWith (the HTTP agent) AND
    //       the per-move player (X/O) and square from the POST body, verified in order.
    //       A dropped, reordered, or mis-attributed move fails here.
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
                    tpl.Substring(0, o)
                    + Uri.EscapeDataString gameId
                    + tpl.Substring(tpl.IndexOf '}' + 1)

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

            let s8OriginBase = (Server.Url()).TrimEnd('/')

            let s8Hrefs =
                SemanticTests.AlpsDescriptorHrefs alpsBody
                |> List.map (fun href -> if href.StartsWith "/" then s8OriginBase + href else href)

            let agentIri =
                s8Hrefs
                |> List.tryFind (fun h -> h = "https://schema.org/agent")
                |> Option.defaultWith (fun () -> failwith "ALPS missing agent IRI")

            let squareIri =
                s8Hrefs
                |> List.tryFind (fun h -> h.Contains "tictactoe#square")
                |> Option.defaultWith (fun () -> failwith "ALPS missing square IRI")

            let classIri =
                s8Hrefs
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

            // provenanceResourceUrl is the DISCOVERED provenance document URL — NOT hardcoded.
            Assert.That(provenanceResourceUrl, Is.Not.Empty, "has_provenance link never captured")

            // ── Phase 4: Follow the discovered has_provenance Link directly ───
            // PROV-AQ §4.1: the Link target IS the provenance document URL.
            // Do NOT reconstruct the URL — use the discovered link as-is.
            let! lineageResp = ctx.GetAsync provenanceResourceUrl
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

            // (2) Attribution: HTTP agent association present AND per-move player/square match in order.
            Assert.That(
                lineageBody.Contains "prov:wasAssociatedWith",
                Is.True,
                "Activities lack prov:wasAssociatedWith — HTTP agent attribution missing"
            )

            let moveAttributes = SemanticTests.ParseMoveAttributes(lineageBody, squareIri)

            Assert.That(
                moveAttributes.Length,
                Is.EqualTo moveLog.Count,
                sprintf
                    "Attribution: captured move-attribute count %d != posted move count %d"
                    moveAttributes.Length
                    moveLog.Count
            )

            for i in 0 .. moveLog.Count - 1 do
                let loggedPlayer, loggedSquare = moveLog.[i]
                let capturedPlayer, capturedSquare = moveAttributes.[i]

                Assert.That(
                    capturedPlayer,
                    Is.EqualTo loggedPlayer,
                    sprintf "Attribution[%d]: captured player '%s' != logged player '%s'" i capturedPlayer loggedPlayer
                )

                Assert.That(
                    capturedSquare,
                    Is.EqualTo loggedSquare,
                    sprintf "Attribution[%d]: captured square '%s' != logged square '%s'" i capturedSquare loggedSquare
                )

            // (3) Order: prov:startedAtTime values must be in non-decreasing order.
            // Activities are sequential (one per turn); any reordering or gap would surface here.
            Assert.That(
                timestamps.Length,
                Is.EqualTo moveLog.Count,
                sprintf "Timestamp count %d != move count %d" timestamps.Length moveLog.Count
            )

            let inOrder = timestamps |> List.pairwise |> List.forall (fun (a, b) -> a <= b)

            Assert.That(inOrder, Is.True, "REORDERED: activity timestamps not in ascending order")

            // (4) Terminal outcome: cross-check the final game state against the DISCOVERED
            // outcome IRI from ALPS ('Won' case descriptor href). Not hardcoded.
            // Note: game-loop state reads (player/validMoves) still use compact JSON via
            // GetProperty — scoped to AT-S6 per plan; reported here for maintainer visibility.
            let discoveredCompletedIri =
                SemanticTests.AlpsDescriptorHrefByLocalId(alpsBody, "Won")
                |> Option.map (fun h -> if h.StartsWith "/" then s8OriginBase + h else h)
                |> Option.defaultWith (fun () ->
                    failwith "ALPS missing 'Won' case descriptor — was outcome emitted by DiscoveryEmitter?")

            let! finalLdResp =
                ctx.GetAsync(gameUrl, APIRequestContextOptions(Headers = dict [ "Accept", "application/ld+json" ]))

            Assert.That(finalLdResp.Status, Is.EqualTo 200, "Final ld+json fetch not 200")
            let! finalLdBody = finalLdResp.TextAsync()
            let gameAbsoluteIri8 = s8OriginBase + gameUrl

            let finalActionStatus =
                SemanticTests.ParseActionStatus(finalLdBody, gameAbsoluteIri8)

            Assert.That(
                finalActionStatus,
                Is.EqualTo discoveredCompletedIri,
                sprintf
                    "Terminal outcome IRI '%s' does not match ALPS-discovered CompletedActionStatus '%s'"
                    finalActionStatus
                    discoveredCompletedIri
            )
        }

    // ── AT-C2-15: vocab turtle carries rdfs:label for each of the 9 board cells ──
    [<Test>]
    member this.``AT-C2-15 ttt vocab turtle has rdfs:label for all 9 cell individuals``() =
        task {
            use! ctx = this.NewContext()

            let! resp = ctx.GetAsync("/tictactoe", APIRequestContextOptions(Headers = dict [ "Accept", "text/turtle" ]))

            Assert.That(resp.Status, Is.EqualTo 200, "vocab endpoint not 200")
            let! body = resp.TextAsync()

            let cells =
                [ "TopLeft"
                  "TopCenter"
                  "TopRight"
                  "MiddleLeft"
                  "MiddleCenter"
                  "MiddleRight"
                  "BottomLeft"
                  "BottomCenter"
                  "BottomRight" ]

            // Stronger: each cell must have its OWN rdfs:label in its subject block.
            // Split on Turtle statement terminators (". ") to get per-subject blocks.
            // A block that contains the cell IRI fragment AND "label" means that cell
            // carries its own label — dropping any one label causes this assertion to fail.
            let cellHasOwnLabel (turtleBody: string) (cell: string) =
                turtleBody.Split([| ".\n"; ". \n" |], StringSplitOptions.None)
                |> Array.exists (fun block -> block.Contains cell && block.Contains "label")

            for cell in cells do
                Assert.That(
                    cellHasOwnLabel body cell,
                    Is.True,
                    sprintf "cell '%s' missing rdfs:label in its own Turtle subject block" cell
                )
        }
