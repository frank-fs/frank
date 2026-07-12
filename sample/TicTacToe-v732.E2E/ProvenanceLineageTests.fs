namespace TicTacToe.E2E

open System
open System.Net.Http
open System.Text
open System.Threading.Tasks
open NUnit.Framework
open VDS.RDF
open VDS.RDF.Parsing
open VDS.RDF.Query
open VDS.RDF.Query.Datasets

/// AT-P1..P6: Provenance lineage — dereferenceable activity IRIs + prov:wasDerivedFrom
/// version chain. All assertions use dotNetRDF (real-RDF), not string match.
/// Plays 3 scripted moves (non-terminal) and verifies the PROV-O lineage graph.
[<TestFixture>]
type ProvenanceLineageTests() =

    static let N = 3
    static let scriptedMoves = [| "X", "TopLeft"; "O", "TopCenter"; "X", "MiddleLeft" |]
    static let provNs = "http://www.w3.org/ns/prov#"
    static let rdfTypeIri = "http://www.w3.org/1999/02/22-rdf-syntax-ns#type"
    static let agentBodyIri = "https://schema.org/agent"

    let mutable httpClient: HttpClient = Unchecked.defaultof<_>
    let mutable baseUrl = ""
    let mutable gameIri = ""
    let mutable gameUrl = ""
    let mutable squareIri = ""
    let mutable batchBody = ""

    // ── Pure helpers ──────────────────────────────────────────────────────────

    // Parse JSON-LD into a single merged graph via TripleStore (JSON-LD is multi-graph).
    static let parseJsonLd (body: string) : IGraph =
        use store = new TripleStore()
        use reader = new System.IO.StringReader(body)
        (new JsonLdParser()).Load(store :> ITripleStore, reader)
        let g = new Graph()

        for ng in store.Graphs do
            g.Merge(ng) |> ignore

        g :> IGraph

    // Returns AbsoluteUri for URI nodes, Value for literals. Covers both storage forms.
    static let nodeStr (n: INode) : string option =
        match n with
        | :? ILiteralNode as l -> Some l.Value
        | :? IUriNode as u -> Some u.Uri.AbsoluteUri
        | _ -> None

    // Extracts the local name after the last '#' or '/' in an IRI.
    static let localFragment (iri: string) : string =
        let i = iri.LastIndexOfAny([| '#'; '/' |])

        if i >= 0 && i < iri.Length - 1 then
            iri.Substring(i + 1)
        else
            iri

    static let uriStr (n: INode) : string option =
        match n with
        | :? IUriNode as u -> Some u.Uri.AbsoluteUri
        | _ -> None

    static let subjectsByType (g: IGraph) (typeIri: string) : string list =
        let rdfType = g.CreateUriNode(UriFactory.Create rdfTypeIri)
        let typeNode = g.CreateUriNode(UriFactory.Create typeIri)

        g.GetTriplesWithPredicateObject(rdfType, typeNode)
        |> Seq.choose (fun t -> uriStr t.Subject)
        |> Seq.toList

    static let triplesByPred (g: IGraph) (predIri: string) : Triple list =
        g.GetTriplesWithPredicate(g.CreateUriNode(UriFactory.Create predIri))
        |> Seq.toList

    static let hasBlankNode (g: IGraph) : bool =
        g.Triples
        |> Seq.exists (fun t -> t.Subject :? IBlankNode || t.Object :? IBlankNode)

    static let hasUrnNode (g: IGraph) : bool =
        g.Triples
        |> Seq.exists (fun t ->
            let isUrn (n: INode) =
                match n with
                | :? IUriNode as u -> u.Uri.Scheme = "urn"
                | _ -> false

            isUrn t.Subject || isUrn t.Object)

    // Walk wasDerivedFrom chain; returns IRIs in leaf→root order.
    // Bounded by cap=100 (Rule 10: every loop must have an explicit cap).
    static let walkChain (g: IGraph) : string list =
        let fwdMap =
            triplesByPred g (provNs + "wasDerivedFrom")
            |> List.choose (fun t ->
                match uriStr t.Subject, uriStr t.Object with
                | Some s, Some o -> Some(s, o)
                | _ -> None)
            |> Map.ofList

        let objectSet = fwdMap |> Map.toSeq |> Seq.map snd |> Set.ofSeq

        let leafCandidates =
            fwdMap
            |> Map.toSeq
            |> Seq.map fst
            |> Seq.filter (fun s -> not (Set.contains s objectSet))
            |> Seq.toList

        match leafCandidates with
        | [ leaf ] ->
            let mutable acc = [ leaf ]
            let mutable cur = leaf
            let mutable cap = 100

            while Map.containsKey cur fwdMap && cap > 0 do
                cur <- fwdMap.[cur]
                acc <- cur :: acc
                cap <- cap - 1

            List.rev acc
        | _ -> []

    // No activity may prov:used the entity it generates (cycle defect fix AT-P2).
    static let assertNoCycle (wgbEdges: Triple list) (usedEdges: Triple list) =
        let generatedByMap =
            wgbEdges
            |> List.choose (fun t ->
                match uriStr t.Subject, uriStr t.Object with
                | Some e, Some a -> Some(e, a)
                | _ -> None)
            |> Map.ofList

        for usedT in usedEdges do
            match uriStr usedT.Subject, uriStr usedT.Object with
            | Some actIri, Some usedIri ->
                match Map.tryFind usedIri generatedByMap with
                | Some generatingAct ->
                    Assert.That(
                        actIri,
                        Is.Not.EqualTo generatingAct,
                        sprintf "CYCLE: activity %s prov:used the entity it generated" actIri
                    )
                | None -> ()
            | _ -> ()

    // Extract (player, squareNodeStr) from the activity that generated stateIri.
    // squarePredIri is the predicate IRI for the square property.
    // Square value may be IriNode (via PropertyClassRanges) — nodeStr handles both forms.
    static let extractMove (g: IGraph) (squarePredIri: string) (stateIri: string) : (string * string) option =
        let stateNode = g.CreateUriNode(UriFactory.Create stateIri)
        let wgbPred = g.CreateUriNode(UriFactory.Create(provNs + "wasGeneratedBy"))

        match g.GetTriplesWithSubjectPredicate(stateNode, wgbPred) |> Seq.tryHead with
        | None -> None
        | Some wgbT ->
            let actNode = wgbT.Object
            let agentPred = g.CreateUriNode(UriFactory.Create agentBodyIri)
            let sqPred = g.CreateUriNode(UriFactory.Create squarePredIri)

            match
                g.GetTriplesWithSubjectPredicate(actNode, agentPred) |> Seq.tryHead,
                g.GetTriplesWithSubjectPredicate(actNode, sqPred) |> Seq.tryHead
            with
            | Some agT, Some sqT ->
                match nodeStr agT.Object, nodeStr sqT.Object with
                | Some pl, Some sq -> Some(pl, sq)
                | _ -> None
            | _ -> None

    static let sparqlStr (result: ISparqlResult) (var: string) : string =
        match result.[var] with
        | null -> ""
        | :? ILiteralNode as l -> l.Value
        | :? IUriNode as u -> u.Uri.AbsoluteUri
        | n -> n.ToString()

    static let runSparql (g: IGraph) (query: string) : ISparqlResult list =
        let dataset = new InMemoryDataset(g)
        let parsed = (new SparqlQueryParser()).ParseFromString query

        (new LeviathanQueryProcessor(dataset)).ProcessQuery(parsed) :?> SparqlResultSet
        |> Seq.toList

    // ── Setup: play 3 scripted moves, fetch batch ─────────────────────────────

    [<OneTimeTearDown>]
    member _.TearDown() =
        if not (isNull (box httpClient)) then
            httpClient.Dispose()

    [<OneTimeSetUp>]
    member _.SetupGame() : Task =
        task {
            httpClient <- new HttpClient()
            httpClient.DefaultRequestHeaders.Add("User-Agent", "Frank-E2E-Prov/1.0")
            baseUrl <- (Server.Url()).TrimEnd('/')
            let gameId = "prov-lin-" + Guid.NewGuid().ToString("N").[..7]
            gameUrl <- "/games/" + gameId
            gameIri <- baseUrl + gameUrl
            squareIri <- baseUrl + "/tictactoe#square"

            // GET first — TicTacToe creates the game on GetOrCreate (GET path).
            // Update (POST path) returns 404 if the game doesn't exist yet.
            let! createResp = httpClient.GetAsync(baseUrl + gameUrl)

            Assert.That(int createResp.StatusCode, Is.EqualTo 200, "Setup: game creation GET failed")

            for (player, square) in scriptedMoves do
                let body =
                    sprintf
                        """{"@type":"%s","%s":"%s","%s":"%s"}"""
                        "https://schema.org/MoveAction"
                        agentBodyIri
                        player
                        squareIri
                        square

                use req = new HttpRequestMessage(HttpMethod.Post, baseUrl + gameUrl)
                req.Content <- new StringContent(body, Encoding.UTF8, "application/ld+json")
                let! resp = httpClient.SendAsync(req)

                Assert.That(
                    int resp.StatusCode,
                    Is.EqualTo 200,
                    sprintf "Setup: move (%s,%s) failed with %d" player square (int resp.StatusCode)
                )

            let provUrl =
                sprintf "%s/provenance?resource=%s" baseUrl (Uri.EscapeDataString gameIri)

            let! provResp = httpClient.GetAsync(provUrl)
            Assert.That(int provResp.StatusCode, Is.EqualTo 200, "Setup: provenance batch not 200")
            let! body = provResp.Content.ReadAsStringAsync()
            batchBody <- body
        }

    // ── AT-P1: activities are dereferenceable HTTP resources ──────────────────

    [<Test>]
    member _.``AT-P1 every activity IRI is http(s) and per-node GET returns 200``() =
        task {
            let g = parseJsonLd batchBody
            let activities = subjectsByType g (provNs + "Activity")
            Assert.That(activities, Is.Not.Empty, "No prov:Activity nodes in batch")

            for iri in activities do
                Assert.That(iri.StartsWith "http", Is.True, sprintf "Activity IRI is not http(s): %s" iri)
                Assert.That(iri.StartsWith "urn:", Is.False, sprintf "Activity IRI uses urn: scheme: %s" iri)
                let! nodeResp = httpClient.GetAsync(iri)
                Assert.That(int nodeResp.StatusCode, Is.EqualTo 200, sprintf "GET %s → not 200" iri)
                let ct = string nodeResp.Content.Headers.ContentType
                Assert.That(ct.Contains "ld+json", Is.True, sprintf "GET %s Content-Type: %s" iri ct)
                let! nodeBody = nodeResp.Content.ReadAsStringAsync()
                let ng = parseJsonLd nodeBody
                let ngActs = subjectsByType ng (provNs + "Activity")

                Assert.That(
                    List.contains iri ngActs,
                    Is.True,
                    sprintf "Per-node doc %s: no activity subject with own IRI" iri
                )

                Assert.That(
                    triplesByPred ng (provNs + "used"),
                    Is.Not.Empty,
                    sprintf "Per-node activity doc %s lacks prov:used" iri
                )

                Assert.That(
                    triplesByPred ng (provNs + "wasAssociatedWith"),
                    Is.Not.Empty,
                    sprintf "Per-node activity doc %s lacks prov:wasAssociatedWith" iri
                )

                // Strengthen: prov:startedAtTime must be typed xsd:dateTime (walkable timestamp).
                let satTs = triplesByPred ng (provNs + "startedAtTime")
                Assert.That(satTs, Is.Not.Empty, sprintf "Per-node activity doc %s lacks prov:startedAtTime" iri)

                match satTs with
                | sat :: _ ->
                    match sat.Object with
                    | :? ILiteralNode as l ->
                        let dtUri = if isNull l.DataType then "" else l.DataType.AbsoluteUri

                        Assert.That(
                            dtUri.Contains "dateTime",
                            Is.True,
                            sprintf "prov:startedAtTime in %s not xsd:dateTime (datatype: %s)" iri dtUri
                        )
                    | _ -> Assert.Fail(sprintf "prov:startedAtTime in %s is not a literal node" iri)
                | [] -> ()

                // Strengthen: prov:wasAssociatedWith object must be an http(s) URI (not blank, not urn:).
                let wawTs = triplesByPred ng (provNs + "wasAssociatedWith")

                match wawTs with
                | waw :: _ ->
                    Assert.That(
                        waw.Object :? IBlankNode,
                        Is.False,
                        sprintf "prov:wasAssociatedWith in %s: agent is a blank node" iri
                    )

                    match uriStr waw.Object with
                    | Some agentUri ->
                        Assert.That(
                            agentUri.StartsWith "http",
                            Is.True,
                            sprintf "prov:wasAssociatedWith in %s: agent IRI not http(s): %s" iri agentUri
                        )

                        Assert.That(
                            agentUri.StartsWith "urn:",
                            Is.False,
                            sprintf "prov:wasAssociatedWith in %s: agent IRI is urn: scheme: %s" iri agentUri
                        )
                    | None -> Assert.Fail(sprintf "prov:wasAssociatedWith in %s: agent is not a URI node" iri)
                | [] -> ()
        }

    // ── AT-P2: state entities + grounded linear wasDerivedFrom chain ──────────

    [<Test>]
    member _.``AT-P2 N+1 entities, N wasDerivedFrom chain, no used-cycle, per-entity deref``() =
        task {
            let g = parseJsonLd batchBody
            let entities = subjectsByType g (provNs + "Entity")
            let activities = subjectsByType g (provNs + "Activity")
            let wdfEdges = triplesByPred g (provNs + "wasDerivedFrom")
            let wgbEdges = triplesByPred g (provNs + "wasGeneratedBy")
            let usedEdges = triplesByPred g (provNs + "used")
            let specEdges = triplesByPred g (provNs + "specializationOf")
            Assert.That(entities.Length, Is.EqualTo(N + 1), "Entity count")
            Assert.That(activities.Length, Is.EqualTo N, "Activity count")
            Assert.That(wdfEdges.Length, Is.EqualTo N, "wasDerivedFrom count")
            let chain = walkChain g
            Assert.That(chain.Length, Is.EqualTo(N + 1), "Chain length")
            Assert.That(chain |> List.distinct |> List.length, Is.EqualTo(N + 1), "Chain has duplicates")
            Assert.That(wgbEdges.Length, Is.EqualTo N, "wasGeneratedBy count")
            Assert.That(usedEdges.Length, Is.EqualTo N, "used edge count")
            assertNoCycle wgbEdges usedEdges
            Assert.That(specEdges.Length, Is.EqualTo(N + 1), "specializationOf count")

            for specT in specEdges do
                match uriStr specT.Object with
                | Some obj -> Assert.That(obj, Is.EqualTo gameIri, "specializationOf target not exact game IRI")
                | None -> Assert.Fail("specializationOf object is not a URI node")

            // Map entity IRI → expected prior entity IRI (from batch wasDerivedFrom edges).
            // entity_0 is absent (it has no predecessor).
            let wdfMap =
                wdfEdges
                |> List.choose (fun t ->
                    match uriStr t.Subject, uriStr t.Object with
                    | Some s, Some o -> Some(s, o)
                    | _ -> None)
                |> Map.ofList

            for iri in entities do
                let! resp = httpClient.GetAsync(iri)
                Assert.That(int resp.StatusCode, Is.EqualTo 200, sprintf "GET entity %s → not 200" iri)
                let! nodeBody = resp.Content.ReadAsStringAsync()
                let ng = parseJsonLd nodeBody

                Assert.That(
                    triplesByPred ng (provNs + "specializationOf"),
                    Is.Not.Empty,
                    sprintf "Per-node entity doc %s lacks specializationOf" iri
                )

                // Strengthen: state_1..N per-node docs must be walkable to activity and prior state.
                match Map.tryFind iri wdfMap with
                | None ->
                    // entity_0: no predecessor — wasGeneratedBy/wasDerivedFrom are absent by design.
                    ()
                | Some priorIri ->
                    Assert.That(
                        triplesByPred ng (provNs + "wasGeneratedBy"),
                        Is.Not.Empty,
                        sprintf "Per-node state entity doc %s (k>0) lacks prov:wasGeneratedBy" iri
                    )

                    let wdfNg = triplesByPred ng (provNs + "wasDerivedFrom")

                    Assert.That(
                        wdfNg,
                        Is.Not.Empty,
                        sprintf "Per-node state entity doc %s (k>0) lacks prov:wasDerivedFrom" iri
                    )

                    // Exact prior state IRI must match the batch graph's wasDerivedFrom target.
                    match wdfNg with
                    | wdfT :: _ ->
                        match uriStr wdfT.Object with
                        | Some actualPrior ->
                            Assert.That(
                                actualPrior,
                                Is.EqualTo priorIri,
                                sprintf
                                    "Per-node state entity doc %s: wasDerivedFrom points to %s, expected %s"
                                    iri
                                    actualPrior
                                    priorIri
                            )
                        | None ->
                            Assert.Fail(sprintf "Per-node state entity doc %s: wasDerivedFrom object is not URI" iri)
                    | [] -> ()
        }

    // ── AT-P3: lineage is sufficient to replay the board ─────────────────────

    [<Test>]
    member _.``AT-P3 lineage replay sequence matches scripted moves and game state oracle``() =
        task {
            let g = parseJsonLd batchBody
            let chain = walkChain g
            // chain = leaf→root; reverse to root→leaf, then skip entity_0 (index 0).
            let stateEntities = List.rev chain |> List.tail

            let extractedMoves = stateEntities |> List.choose (extractMove g squareIri)

            Assert.That(extractedMoves.Length, Is.EqualTo N, "Extracted move count from chain")

            // squareIri = "http://…/tictactoe#square"; tttNs = "http://…/tictactoe#"
            let tttNs = squareIri.Substring(0, squareIri.LastIndexOf('#') + 1)

            for i in 0 .. N - 1 do
                let (ep, es) = extractedMoves.[i]
                let (sp, ss) = scriptedMoves.[i]
                Assert.That(ep, Is.EqualTo sp, sprintf "Move %d: player mismatch" i)
                Assert.That(es, Is.EqualTo(tttNs + ss), sprintf "Move %d: square IRI mismatch" i)

            // Replay oracle: occupied squares must no longer be in validMoves.
            // localFragment strips IRI to position name (e.g., "…#TopLeft" → "TopLeft").
            let occupiedSquares =
                extractedMoves |> List.map (snd >> localFragment) |> Set.ofList

            let! gameResp = httpClient.GetAsync(baseUrl + gameUrl)
            Assert.That(int gameResp.StatusCode, Is.EqualTo 200, "Game GET not 200")
            let! gameJson = gameResp.Content.ReadAsStringAsync()
            use doc = System.Text.Json.JsonDocument.Parse gameJson

            let validMoves =
                doc.RootElement.GetProperty("validMoves").EnumerateArray()
                |> Seq.map (fun v -> v.GetString())
                |> Set.ofSeq

            for sq in occupiedSquares do
                Assert.That(
                    Set.contains sq validMoves,
                    Is.False,
                    sprintf "Occupied square '%s' still in validMoves — replay oracle mismatch" sq
                )

            Assert.That(validMoves.Count, Is.EqualTo(9 - occupiedSquares.Count), "validMoves count mismatch")
        }

    // ── AT-P4: no dead-end IRIs anywhere ──────────────────────────────────────

    [<Test>]
    member _.``AT-P4 no urn: IRIs and no blank nodes in batch or any per-node doc``() =
        task {
            let g = parseJsonLd batchBody
            Assert.That(hasUrnNode g, Is.False, "Batch graph contains urn: IRI")
            Assert.That(hasBlankNode g, Is.False, "Batch graph contains blank node")

            let nodeIris =
                subjectsByType g (provNs + "Activity") @ subjectsByType g (provNs + "Entity")

            for iri in nodeIris do
                let! resp = httpClient.GetAsync(iri)
                let! nodeBody = resp.Content.ReadAsStringAsync()
                let ng = parseJsonLd nodeBody
                Assert.That(hasUrnNode ng, Is.False, sprintf "Per-node doc %s contains urn: IRI" iri)

                Assert.That(hasBlankNode ng, Is.False, sprintf "Per-node doc %s contains blank node" iri)
        }

    // ── AT-P5: real-RDF counts + existing has_provenance Link ─────────────────

    [<Test>]
    member _.``AT-P5 batch has N+1 entities, N activities, N wasDerivedFrom; has_provenance Link intact``() =
        task {
            let g = parseJsonLd batchBody

            Assert.That(subjectsByType g (provNs + "Entity") |> List.length, Is.EqualTo(N + 1), "Entity count in batch")

            Assert.That(subjectsByType g (provNs + "Activity") |> List.length, Is.EqualTo N, "Activity count in batch")

            Assert.That(
                triplesByPred g (provNs + "wasDerivedFrom") |> List.length,
                Is.EqualTo N,
                "wasDerivedFrom count in batch"
            )

            let! gameResp = httpClient.GetAsync(baseUrl + gameUrl)

            let linkHeader =
                gameResp.Headers
                |> Seq.tryFind (fun kv -> kv.Key.ToLowerInvariant() = "link")
                |> Option.map (fun kv -> String.concat ", " kv.Value)
                |> Option.defaultValue ""

            Assert.That(
                linkHeader.Contains "has_provenance",
                Is.True,
                "has_provenance Link header not present on game GET"
            )
        }

    // ── AT-P6: SPARQL-queryable substrate ─────────────────────────────────────

    [<Test>]
    member _.``AT-P6 lineage is SPARQL-queryable: move reconstruction, leaderboard, resource join``() =
        task {
            let g = parseJsonLd batchBody
            // squareIri = predicate IRI "http://…/tictactoe#square"
            // tttNs = "http://…/tictactoe#" — namespace for square value IRIs
            let tttNs = squareIri.Substring(0, squareIri.LastIndexOf('#') + 1)

            let moveQuery =
                sprintf
                    """PREFIX prov: <http://www.w3.org/ns/prov#>
SELECT ?player ?square WHERE {
    ?state a prov:Entity ; prov:wasGeneratedBy ?activity .
    ?activity <https://schema.org/agent> ?player ;
              <%s> ?square ;
              prov:startedAtTime ?t .
} ORDER BY ?t"""
                    squareIri

            let moveRows = runSparql g moveQuery
            Assert.That(moveRows.Length, Is.EqualTo N, "SPARQL move reconstruction: row count")

            for i in 0 .. N - 1 do
                let row = moveRows.[i]
                let (sp, ss) = scriptedMoves.[i]
                Assert.That(sparqlStr row "player", Is.EqualTo sp, sprintf "SPARQL move %d: player" i)

                Assert.That(sparqlStr row "square", Is.EqualTo(tttNs + ss), sprintf "SPARQL move %d: square IRI" i)

            let lbQuery =
                """PREFIX prov: <http://www.w3.org/ns/prov#>
SELECT ?player (COUNT(?a) AS ?moves) WHERE {
    ?a a prov:Activity . ?a <https://schema.org/agent> ?player .
} GROUP BY ?player ORDER BY DESC(?moves)"""

            let lbRows = runSparql g lbQuery
            Assert.That(lbRows.Length, Is.GreaterThan 0, "SPARQL leaderboard: no results")
            let xRow = lbRows |> List.tryFind (fun r -> sparqlStr r "player" = "X")
            let oRow = lbRows |> List.tryFind (fun r -> sparqlStr r "player" = "O")
            Assert.That(xRow.IsSome, Is.True, "SPARQL leaderboard: player X not found")
            Assert.That(oRow.IsSome, Is.True, "SPARQL leaderboard: player O not found")

            Assert.That(sparqlStr xRow.Value "moves", Is.EqualTo "2", "SPARQL leaderboard: X move count")

            Assert.That(sparqlStr oRow.Value "moves", Is.EqualTo "1", "SPARQL leaderboard: O move count")

            let rjQuery =
                sprintf
                    """PREFIX prov: <http://www.w3.org/ns/prov#>
SELECT ?s WHERE { ?s prov:specializationOf <%s> . }"""
                    gameIri

            let rjRows = runSparql g rjQuery

            Assert.That(
                rjRows.Length,
                Is.EqualTo(N + 1),
                sprintf "SPARQL resource join: expected %d entities, got %d" (N + 1) rjRows.Length
            )
        }
