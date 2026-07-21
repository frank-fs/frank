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

    /// Well-known schema.org prefix fallback (#394): neither prefix walker below
    /// dereferences the remote schema.org @context element, so both seed this to
    /// recognize schema:-compacted keys. A "schema" entry declared inline in the
    /// served @context still overrides the seed.
    static let schemaOrgPrefix = "https://schema.org/"

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

    /// Return the given JSON property (`propertyName`, e.g. "href" or "type") of the
    /// ALPS descriptor whose id matches localId, or None. Searches nested descriptors
    /// recursively; depth bounded by ALPS document structure. Shared implementation for
    /// AlpsDescriptorHrefByLocalId ("href") and AlpsDescriptorTypeByLocalId ("type") —
    /// the two were a near-verbatim copy differing only in property name (#400 /simplify
    /// Fix 5).
    static member private AlpsDescriptorPropertyByLocalId
        (alpsBody: string, localId: string, propertyName: string)
        : string option =
        use doc = JsonDocument.Parse alpsBody
        let mutable alpsEl = Unchecked.defaultof<JsonElement>
        let mutable descriptorEl = Unchecked.defaultof<JsonElement>

        let matchProperty (d: JsonElement) : string option =
            let mutable idEl = Unchecked.defaultof<JsonElement>
            let mutable propEl = Unchecked.defaultof<JsonElement>

            if
                d.TryGetProperty("id", &idEl)
                && idEl.GetString() = localId
                && d.TryGetProperty(propertyName, &propEl)
            then
                propEl.GetString() |> Option.ofObj
            else
                None

        let rec findIn (arr: JsonElement) : string option =
            arr.EnumerateArray()
            |> Seq.tryPick (fun d ->
                match matchProperty d with
                | Some v -> Some v
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

    /// Return the href of the ALPS descriptor whose id matches localId, or None.
    static member private AlpsDescriptorHrefByLocalId(alpsBody: string, localId: string) : string option =
        SemanticTests.AlpsDescriptorPropertyByLocalId(alpsBody, localId, "href")

    /// Return the ALPS "type" of the descriptor whose id matches localId, or None.
    /// #400 AC2: the live-derived counterpart to AlpsDescriptorHrefByLocalId — reads the
    /// served (post-reconciliation) Type, not the codegen-time fallback baked at build time.
    static member private AlpsDescriptorTypeByLocalId(alpsBody: string, localId: string) : string option =
        SemanticTests.AlpsDescriptorPropertyByLocalId(alpsBody, localId, "type")

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
        prefixes.["schema"] <- schemaOrgPrefix
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
        result.["schema"] <- schemaOrgPrefix
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

            // ── Corrected model: move is POST on the game resource, not a separate moves resource ──
            // (1) No resource whose href-template ends /moves — the phantom resource is gone.
            let movesResourceEntry =
                resources.EnumerateObject()
                |> Seq.tryFind (fun r ->
                    let mutable tmpl = Unchecked.defaultof<JsonElement>

                    r.Value.TryGetProperty("href-template", &tmpl)
                    && (tmpl.GetString() |> Option.ofObj |> Option.exists (fun s -> s.Contains "/moves")))

            Assert.That(
                movesResourceEntry.IsNone,
                Is.True,
                "Phantom moves resource (/games/{id}/moves) must be absent from JSON Home — POST is on the game resource"
            )

            // (2) The game resource entry's hints.allow must include GET, OPTIONS, and POST.
            let gameEntry =
                resources.EnumerateObject()
                |> Seq.tryFind (fun r ->
                    let mutable tmpl = Unchecked.defaultof<JsonElement>
                    let mutable hints = Unchecked.defaultof<JsonElement>
                    let mutable allow = Unchecked.defaultof<JsonElement>

                    r.Value.TryGetProperty("href-template", &tmpl)
                    && (tmpl.GetString()
                        |> Option.ofObj
                        |> Option.exists (fun s -> s.Contains "/games/{id}"))
                    && r.Value.TryGetProperty("hints", &hints)
                    && hints.TryGetProperty("allow", &allow)
                    && allow.EnumerateArray() |> Seq.exists (fun m -> m.GetString() = "GET")
                    && allow.EnumerateArray() |> Seq.exists (fun m -> m.GetString() = "POST"))

            Assert.That(
                gameEntry.IsSome,
                Is.True,
                "Game resource entry must carry hints.allow ⊇ {GET, POST} — move is a POST transition on the game"
            )

            let mutable hintsEl2 = Unchecked.defaultof<JsonElement>
            let mutable allowEl2 = Unchecked.defaultof<JsonElement>

            let allowContainsOptions =
                gameEntry.IsSome
                && gameEntry.Value.Value.TryGetProperty("hints", &hintsEl2)
                && hintsEl2.TryGetProperty("allow", &allowEl2)
                && allowEl2.EnumerateArray() |> Seq.exists (fun m -> m.GetString() = "OPTIONS")

            Assert.That(
                allowContainsOptions,
                Is.True,
                "Game resource hints.allow must include OPTIONS (RFC 7231 §7.4.1)"
            )

            // (3) The game resource's href-vars must have an 'id' key that is an absolute IRI
            // (schema:identifier). This coverage existed for the old moves resource entry and
            // is re-pointed at the game resource — the id var travels with the game template now.
            let mutable hrefVarsEl = Unchecked.defaultof<JsonElement>
            let mutable idVarEl = Unchecked.defaultof<JsonElement>

            let hrefVarsPresent =
                gameEntry.IsSome
                && gameEntry.Value.Value.TryGetProperty("href-vars", &hrefVarsEl)
                && hrefVarsEl.TryGetProperty("id", &idVarEl)

            Assert.That(hrefVarsPresent, Is.True, "Game resource entry must carry href-vars with an 'id' key")
            let idVarValue = idVarEl.GetString()
            Assert.That(idVarValue, Is.Not.Empty, "href-vars.id must not be empty")
            Assert.That(idVarValue.StartsWith "http", Is.True, "href-vars.id must be an absolute IRI")

            Assert.That(
                idVarValue,
                Is.EqualTo "https://schema.org/identifier",
                "href-vars.id must equal schema:identifier"
            )
        }

    // ── AT-S2: OPTIONS yields Allow ⊇ {GET,OPTIONS,POST} + Link rel=describedby → ALPS ──
    // ALPS MoveAction must carry rt = https://schema.org/Game — move is a POST transition
    // on the game resource, not a separate resource. OPTIONS must be in Allow (RFC 7231 §7.4.1).
    [<Test>]
    member this.``AT-S2 OPTIONS carries Allow and Link rel=describedby to ALPS``() =
        task {
            use! ctx = this.NewContext()
            let! resp = this.Options(ctx, "/games/at-s2")
            Assert.That(resp.Headers.ContainsKey "allow", Is.True, "OPTIONS missing Allow header")

            let allowStr = resp.Headers.["allow"]
            Assert.That(allowStr.Contains "GET", Is.True, "Allow must include GET")
            Assert.That(allowStr.Contains "OPTIONS", Is.True, "Allow must include OPTIONS (RFC 7231 §7.4.1)")

            Assert.That(
                allowStr.Contains "POST",
                Is.True,
                "Allow must include POST — move is a POST transition on the game"
            )

            let rels = SemanticTests.LinkRels resp
            Assert.That(rels.ContainsKey "describedby", Is.True, "OPTIONS missing Link rel=describedby")

            let alpsUrl = rels.["describedby"]
            let! alpsResp = ctx.GetAsync(alpsUrl)
            let! alpsBody = alpsResp.TextAsync()
            use alpsDoc = JsonDocument.Parse alpsBody
            let mutable alpsEl = Unchecked.defaultof<JsonElement>
            let mutable descriptorEl = Unchecked.defaultof<JsonElement>

            Assert.That(
                alpsDoc.RootElement.TryGetProperty("alps", &alpsEl)
                && alpsEl.TryGetProperty("descriptor", &descriptorEl),
                Is.True,
                "ALPS document must have alps.descriptor array"
            )

            let moveActionRt =
                descriptorEl.EnumerateArray()
                |> Seq.tryPick (fun d ->
                    let mutable typeEl = Unchecked.defaultof<JsonElement>
                    let mutable rtEl = Unchecked.defaultof<JsonElement>

                    if
                        d.TryGetProperty("type", &typeEl)
                        && typeEl.GetString() = "unsafe"
                        && d.TryGetProperty("rt", &rtEl)
                    then
                        rtEl.GetString() |> Option.ofObj
                    else
                        None)

            Assert.That(moveActionRt.IsSome, Is.True, "ALPS must have an unsafe descriptor with rt")

            Assert.That(
                moveActionRt.Value,
                Is.EqualTo "https://schema.org/Game",
                "ALPS MoveAction rt must be schema:Game — move is anchored on the game resource"
            )
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
                    "/games/at-s4",
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
            use stub = SchemaOrgStub.Start()

            let toStub (iri: string) =
                iri.Replace("https://schema.org", stub.BaseUrl.TrimEnd('/'))

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
            // schema.org term IRIs — rewritten to the loopback stub for the GET
            // so the default suite passes offline. ALL assertions use the REAL
            // schema.org IRI (recognition thesis unchanged). A hallucinated local-name
            // 404s from the stub, so the load-bearing check is preserved.
            for iri in descriptorHrefs |> List.filter (fun u -> u.StartsWith "https://schema.org/") do
                let! r = httpClient.GetAsync(toStub iri)

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

            // seeAlso targets — game graph has none; loop is vacuous but not removed for structure.
            // schema.org seeAlso URIs are rewritten to the stub; non-schema.org URIs are unchanged.
            for seeAlsoUri in SemanticTests.SeeAlsoUris ldBody do
                let fetchUri =
                    if seeAlsoUri.StartsWith "https://schema.org/" then
                        toStub seeAlsoUri
                    else
                        seeAlsoUri

                let! r = httpClient.GetAsync fetchUri

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

    // ── #400/#411 AC2: ex: server's MoveAction ALPS Type is genuinely live-derived ──
    //
    // TicTacToe-v732.Ex had no Frank.OpenApi reference before #400 — its POST
    // /games/{id} move handler carried no IAcceptsMetadata, so #397's HTTP-method
    // reconciliation had no live signal to correlate MoveAction's ALPS Type against
    // (its own ClassIri, ex:MoveAction, is never itself a declared route relation —
    // only ex:Game is). The served Type fell back, unreconciled, to the codegen-time
    // default. #400 closed this gap: the ex: sample's POST declares
    // `accepts typeof<MoveRequest>` (Frank.OpenApi's HandlerDefinition), giving
    // Frank.Discovery's HTTP-method correlation a live IAcceptsMetadata signal to match.
    // #411 replaced #400's correlation SOURCE — Frank.Discovery now reads Frank's own
    // composed Endpoint[] directly (via the narrow ResourceEndpointDataSource
    // WebHostBuilder.Run registers), not IApiDescriptionGroupCollectionProvider/
    // AddEndpointsApiExplorer() — while the reconciled ALPS Type semantics this test
    // asserts on are unchanged.
    //
    // Falsifiability (adversarial-review finding, confirmed live): MoveRequest is a
    // module-nested F# type (`module TicTacToe.Model` → `MoveRequest`), so its CLR
    // reflection FullName is '+'-nested ("TicTacToe.Model+MoveRequest") while codegen's
    // FCS-derived RequestClrTypeName is '.'-separated ("TicTacToe.Model.MoveRequest") —
    // these never compared equal before DiscoveryMiddleware.methodsByRequestType's
    // normalizeClrTypeFullName fix, so reconciliation silently no-op'd. The codegen-time
    // default is unconditionally "semantic" regardless of Rt (DiscoveryEmitter.fs's
    // alpsTypeDefault ignores its descriptor argument entirely) — MoveRequest.rt is
    // "ex:Game" in the ex: sample's lock file (see .frank/semantic-mappings.lock.json),
    // so this assertion does NOT falsify whether Rt happens to be present; it falsifies
    // whether live reconciliation against Frank's own Endpoint[] actually ran and
    // overrode the always-"semantic" codegen default to the live POST's real "unsafe"
    // classification. A reverted/broken correlation path (confirmed by temporarily
    // disabling normalizeClrTypeFullName and re-running this exact test against the live
    // server) serves "semantic" here, not "unsafe" — this assertion genuinely falsifies
    // MoveAction's reconciliation specifically, not via Game as an indirect proxy.
    // Game's own Type is checked too — reconciled from the live GET, WRONG under its
    // own codegen default ("semantic", same alpsTypeDefault) — proving reconciliation
    // runs at all (the coarser ClassIri/relation path), independent of the
    // RequestClrTypeName-specific fix MoveAction's assertion targets. Both are grounded
    // in an independently observed OPTIONS Allow header.
    [<Test>]
    member this.``AT-S9 ex: server's ALPS Types are genuinely live-derived from Frank's own Endpoint[] (ResourceEndpointDataSource)``
        ()
        =
        task {
            use! ctx = this.Playwright.APIRequest.NewContextAsync(APIRequestNewContextOptions(BaseURL = ExServer.Url()))
            let gameId = "at-s9"

            let! opts = this.Options(ctx, sprintf "/games/%s" gameId)
            let rels = SemanticTests.LinkRels opts
            Assert.That(rels.ContainsKey "describedby", Is.True, "ex: server missing Link rel=describedby")
            let alpsUrl = rels.["describedby"]
            let! alpsResp = ctx.GetAsync alpsUrl
            Assert.That(alpsResp.Status, Is.EqualTo 200, "ex: server ALPS not 200")
            let! alpsBody = alpsResp.TextAsync()

            let moveActionType =
                SemanticTests.AlpsDescriptorTypeByLocalId(alpsBody, "MoveAction")

            Assert.That(
                moveActionType,
                Is.EqualTo(Some "unsafe"),
                "MoveAction must be served as 'unsafe', reconciled from the live POST /games/{id}'s IAcceptsMetadata (#400) — overriding the always-'semantic' codegen default (alpsTypeDefault, Rt-independent), not left unresolved"
            )

            let gameType = SemanticTests.AlpsDescriptorTypeByLocalId(alpsBody, "Game")

            Assert.That(
                gameType,
                Is.EqualTo(Some "safe"),
                "Game must be served as 'safe', reconciled from the live GET on the same route — the always-'semantic' codegen default (alpsTypeDefault, Rt-independent) would be WRONG if reconciliation were not genuinely running against live data"
            )

            // Ground both classifications in an independently observed live fact: the
            // same route really does serve both GET and POST.
            let allow =
                opts.Headers
                |> Seq.tryFind (fun kv -> kv.Key.ToLowerInvariant() = "allow")
                |> Option.map (fun kv -> kv.Value)
                |> Option.defaultValue ""

            Assert.That(
                allow.Contains "GET",
                Is.True,
                "OPTIONS Allow must list GET — grounds the Game=safe classification"
            )

            Assert.That(
                allow.Contains "POST",
                Is.True,
                "OPTIONS Allow must list POST — grounds the MoveAction=unsafe classification"
            )
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

    // ── compacted-CURIE ld+json POST ─────────────────────────────────────────────
    // Proves parseMoveFromDoc expands @context prefix mappings so a natural
    // compacted-CURIE body ("ttt:square"/"schema:agent") is accepted alongside
    // the existing fully-expanded IRI key form used by AT-S6/AT-S8.
    [<Test>]
    member this.``compacted CURIE ld+json POST applies the move and returns 200``() =
        task {
            use! ctx = this.NewContext()
            let originBase = (Server.Url()).TrimEnd('/')

            // Unique game ID prevents state pollution from prior test runs.
            let gameId = "curie-" + Guid.NewGuid().ToString("N").[..7]

            // GET creates the game before the move is posted.
            let! _ = ctx.GetAsync(sprintf "/games/%s" gameId)

            // Natural compacted-CURIE body — the @context defines prefix expansions.
            // parseMoveFromDoc must resolve ttt:square and schema:agent before lookup.
            let body =
                sprintf
                    """{"@context":{"schema":"https://schema.org/","ttt":"%s/tictactoe#"},"@type":"schema:MoveAction","ttt:square":"TopLeft","schema:agent":"X"}"""
                    originBase

            let! resp =
                ctx.PostAsync(
                    sprintf "/games/%s" gameId,
                    APIRequestContextOptions(Headers = dict [ "Content-Type", "application/ld+json" ], Data = body)
                )

            Assert.That(resp.Status, Is.EqualTo 200, "compacted-CURIE ld+json POST should return 200")
            let! json = resp.TextAsync()
            Assert.That(json.Contains "\"TopLeft\"", Is.True, "move not applied — TopLeft missing from game state")
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

    // ── SchemaOrgStub smoke — load-bearing: unknown paths must 404 ──────────────
    [<Test>]
    member _.``SchemaOrgStub serves known terms 200 and unknown paths 404``() =
        task {
            use client = new HttpClient()
            use stub = SchemaOrgStub.Start()
            let! r200 = client.GetAsync(stub.BaseUrl + "/MoveAction")

            Assert.That(int r200.StatusCode, Is.EqualTo 200, "stub must serve known term MoveAction as 200")

            let! r404 = client.GetAsync(stub.BaseUrl + "/BogusTerm")

            Assert.That(
                int r404.StatusCode,
                Is.EqualTo 404,
                "stub must serve unknown path as 404 — hallucinated IRIs must still fail"
            )
        }

    // ── AT-S6-live: opt-in live-network deref (excluded from default suite) ─────
    [<Test>]
    [<Category("LiveNetwork")>]
    [<Explicit("requires outbound egress to schema.org")>]
    member _.``AT-S6-live schema.org term IRIs dereference on the real web``() =
        task {
            let terms =
                [ "MoveAction"
                  "agent"
                  "Game"
                  "result"
                  "CompletedActionStatus"
                  "FailedActionStatus" ]

            for term in terms do
                let! r = httpClient.GetAsync(sprintf "https://schema.org/%s" term)

                Assert.That(
                    int r.StatusCode,
                    Is.InRange(200, 299),
                    sprintf "schema.org live deref failed: https://schema.org/%s" term
                )
        }

    // ── AT-S10: .Ex sample never leaks a placeholder domain (#415) ──────────────
    //
    // Mirrors RdfVerificationTests.AT-R7's methodology (absence check paired with a
    // positive real-origin presence check, never absence-only — a false-green would
    // pass an absence-only check even if some OTHER garbage domain leaked) but scoped
    // to the .Ex sample and covering every live surface the #415 thesis names together
    // in one standing regression test: /ex (Turtle), OPTIONS /games/{id} (Link headers),
    // the ALPS profile reached via the OPTIONS describedby Link, and JSON Home. Turns
    // the manual `curl` verification from the #415 fix into a persisting guard — Vocabulary.
    // Ex.fs's ex:/ttt: prefixes are declared-only identity keys (RFC 2606 ".invalid"),
    // never themselves served; every IRI must instead resolve host-relative against the
    // live ExServer origin (EmitterShared.declaredOnlyBases + hrefFor, #396/#415).
    [<Test>]
    member this.``AT-S10 .Ex sample serves no placeholder domain across /ex, OPTIONS, ALPS, and JSON Home``() =
        task {
            use! ctx = this.Playwright.APIRequest.NewContextAsync(APIRequestNewContextOptions(BaseURL = ExServer.Url()))

            let exOriginBase = (ExServer.Url()).TrimEnd('/')
            let gameIri = exOriginBase + "/ex#Game"

            let assertNoPlaceholder (label: string) (body: string) =
                Assert.That(body.Contains "example.org", Is.False, sprintf "%s must not contain example.org" label)

                Assert.That(
                    body.Contains "tictactoe.invalid",
                    Is.False,
                    sprintf "%s must not contain the un-relativized declared-only identity domain" label
                )

            // ── OPTIONS /games/{id}: Link headers carry no placeholder domain ────
            let gameId = "at-s10"
            let! opts = this.Options(ctx, sprintf "/games/%s" gameId)
            Assert.That(opts.Status, Is.EqualTo 200, "OPTIONS /games/{id} not 200")
            let rels = SemanticTests.LinkRels opts
            Assert.That(rels.ContainsKey "describedby", Is.True, ".Ex server missing Link rel=describedby")
            Assert.That(rels.ContainsKey "type", Is.True, ".Ex server missing Link rel=type")

            let linkHeaderRaw =
                opts.Headers
                |> Seq.filter (fun kv -> kv.Key.ToLowerInvariant() = "link")
                |> Seq.map (fun kv -> kv.Value)
                |> String.concat ", "

            assertNoPlaceholder "OPTIONS Link headers" linkHeaderRaw

            // Positive check (not absence-only): the type Link, resolved against the live
            // origin, is EXACTLY the real ex:Game IRI — not merely free of "example.org".
            let typeIri =
                let h = rels.["type"]
                if h.StartsWith "/" then exOriginBase + h else h

            Assert.That(typeIri, Is.EqualTo gameIri, "OPTIONS Link rel=type must resolve to the real ex:Game IRI")

            // ── GET /ex: Turtle body carries no placeholder domain, and DOES carry the
            // real-origin ex:Game IRI (positive presence, not absence-only). ──────────
            let! exResp = ctx.GetAsync "/ex"
            Assert.That(exResp.Status, Is.EqualTo 200, "GET /ex not 200")
            let! exBody = exResp.TextAsync()
            assertNoPlaceholder "GET /ex Turtle body" exBody

            Assert.That(
                exBody.Contains gameIri,
                Is.True,
                sprintf "GET /ex must contain the real-origin ex:Game IRI '%s', got: %s" gameIri exBody
            )

            // ── ALPS profile (reached via the discovered describedby Link, never
            // hardcoded): no placeholder domain, real-origin ex:Game IRI present. ────
            let! alpsResp = ctx.GetAsync rels.["describedby"]
            Assert.That(alpsResp.Status, Is.EqualTo 200, "ALPS profile not 200")
            let! alpsBody = alpsResp.TextAsync()
            assertNoPlaceholder "ALPS profile body" alpsBody

            Assert.That(
                alpsBody.Contains gameIri,
                Is.True,
                sprintf "ALPS profile must contain the real-origin ex:Game IRI '%s', got: %s" gameIri alpsBody
            )

            // ── JSON Home: no placeholder domain anywhere (#415 — the leak this AC
            // specifically closes: the resource key used to be the un-relativized,
            // un-dereferenceable identity IRI). ──────────────────────────────────────
            let! homeResp =
                ctx.GetAsync("/", APIRequestContextOptions(Headers = dict [ "Accept", "application/json-home" ]))

            Assert.That(homeResp.Status, Is.EqualTo 200, "JSON Home not 200")
            let! homeBody = homeResp.TextAsync()
            assertNoPlaceholder "JSON Home body" homeBody

            // Positive check (not absence-only): JSON Home serves the relation as its own
            // host-relative AlpsDescriptor.Href — "/ex#Game" — never the un-relativized
            // absolute identity key (confirmed live: DiscoveryMiddleware.classIriHrefMap
            // resolves it, #415). An empty/broken `{"resources":{}}` response is free of
            // "example.org"/"tictactoe.invalid" too — this positive assertion is what
            // actually falsifies that false-green.
            use homeDoc = JsonDocument.Parse homeBody
            let resources = homeDoc.RootElement.GetProperty "resources"

            let gameEntry =
                resources.EnumerateObject()
                |> Seq.tryFind (fun p -> p.Name = "/ex#Game")
                |> Option.defaultWith (fun () ->
                    failwith (
                        sprintf
                            "JSON Home must key the Game resource by the host-relative '/ex#Game' href, got: %s"
                            homeBody
                    ))

            let hrefVars = gameEntry.Value.GetProperty "href-vars"

            Assert.That(
                hrefVars.GetProperty("id").GetString(),
                Is.EqualTo "/ex#identifier",
                "JSON Home href-vars.id must be the host-relative '/ex#identifier' meaning IRI"
            )
        }
