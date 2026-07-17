namespace TicTacToe.E2E

open System
open System.IO
open System.Text.Json
open System.Collections.Generic
open System.Threading.Tasks
open Microsoft.Playwright
open Microsoft.Playwright.NUnit
open NUnit.Framework
open VDS.RDF
open VDS.RDF.Parsing
open VDS.RDF.JsonLd
open Newtonsoft.Json.Linq

/// RDF-level verification tests for the v7.3.2 semantic outputs.
/// Parses served bodies using dotNetRDF + SHACL, replacing string-match
/// assertions with real graph queries. Proves false-green risk of the
/// existing Contains checks (AT-R5).
///
/// Offline strict loader (Option A): stubs the canonical https-current schema.org
/// context document (https://schema.org/version/latest/schemaorg-current-https.jsonld)
/// with a minimal {"@context":{"schema":"https://schema.org/"}} document — the one
/// substantive fact it supplies when fetched live (#394); throws for any other
/// remote URI.
[<TestFixture>]
type RdfVerificationTests() =
    inherit PlaywrightTest()

    static let canonicalSchemaOrgContextUrl =
        "https://schema.org/version/latest/schemaorg-current-https.jsonld"

    // ── Strict offline document loader ───────────────────────────────────────
    // Simulates "if the remote vocab document were reachable": stubs each canonical
    // external context document with the one substantive prefix mapping it supplies
    // when fetched live (#394) — without ever touching the real network. Any other
    // remote URI throws.
    static let strictLoader: Func<Uri, JsonLdLoaderOptions, RemoteDocument> =
        Func<Uri, JsonLdLoaderOptions, RemoteDocument>(fun uri _ ->
            let stub (contextJson: string) =
                let doc = RemoteDocument()
                doc.Document <- JObject.Parse contextJson
                doc.DocumentUrl <- uri
                doc

            match uri.ToString() with
            | s when s = canonicalSchemaOrgContextUrl -> stub """{"@context":{"schema":"https://schema.org/"}}"""
            // /vocabulary's @context (GeneratedLinkedData.jsonLdContextFor, #396 round 6) lists
            // the bare schema.org base IRI (LinkedDataEmitter.contextBases's `using "schema"`
            // ContextBases entry, trailing slash trimmed in the served string) — distinct from
            // /games' hand-curated canonical versioned URL above. System.Uri's own AbsoluteUri
            // reinstates the trailing slash for an authority-only URI, so the loader sees
            // "https://schema.org/", not the served "https://schema.org" text.
            | "https://schema.org/" -> stub """{"@context":{"schema":"https://schema.org/"}}"""
            | "http://www.w3.org/1999/02/22-rdf-syntax-ns#" ->
                stub """{"@context":{"rdf":"http://www.w3.org/1999/02/22-rdf-syntax-ns#"}}"""
            | "http://www.w3.org/2000/01/rdf-schema#" ->
                stub """{"@context":{"rdfs":"http://www.w3.org/2000/01/rdf-schema#"}}"""
            | "http://www.w3.org/2002/07/owl#" -> stub """{"@context":{"owl":"http://www.w3.org/2002/07/owl#"}}"""
            | s -> invalidOp (sprintf "strictOfflineLoader: blocked remote URI '%s'" s))

    // ── Core parsing helpers ─────────────────────────────────────────────────

    /// Shared parse: merges all named graphs from a JSON-LD document into one flat IGraph
    /// using the given document loader.
    static member private ParseJsonLdWith
        (body: string, loader: Func<Uri, JsonLdLoaderOptions, RemoteDocument>)
        : IGraph =
        let opts = JsonLdProcessorOptions()
        opts.DocumentLoader <- loader
        let parser = JsonLdParser(opts)
        use store = new TripleStore()
        use reader = new StringReader(body)
        parser.Load(store :> ITripleStore, reader)
        let merged = new Graph()

        for g in store.Graphs do
            merged.Merge(g) |> ignore

        merged :> IGraph

    /// Parse a JSON-LD body into an IGraph using the strict offline loader.
    static member private ParseJsonLd(body: string) : IGraph =
        RdfVerificationTests.ParseJsonLdWith(body, strictLoader)

    /// Parse a JSON-LD body into an IGraph using dotNetRDF's real, live-network loader.
    static member private ParseJsonLdLive(body: string) : IGraph =
        RdfVerificationTests.ParseJsonLdWith(
            body,
            Func<Uri, JsonLdLoaderOptions, RemoteDocument>(fun uri o -> DefaultDocumentLoader.LoadJson(uri, o))
        )

    // ── Graph query helpers ──────────────────────────────────────────────────

    /// All triples in graph whose predicate matches predIri.
    static member private TriplesWithPred(g: IGraph, predIri: string) : Triple seq =
        g.GetTriplesWithPredicate(Uri predIri)

    // ── Context inspection helpers ───────────────────────────────────────────

    /// True if the JSON-LD body's @context array contains exactly the literal
    /// canonical https-current schema.org context URL (not a relative ref, not
    /// bare https://schema.org, not example.org).
    static member private HasCanonicalSchemaOrgRef(body: string) : bool =
        use doc = JsonDocument.Parse body
        let mutable ctxEl = Unchecked.defaultof<JsonElement>

        if not (doc.RootElement.TryGetProperty("@context", &ctxEl)) then
            false
        else
            match ctxEl.ValueKind with
            | JsonValueKind.Array ->
                ctxEl.EnumerateArray()
                |> Seq.exists (fun el ->
                    el.ValueKind = JsonValueKind.String
                    && el.GetString() = canonicalSchemaOrgContextUrl)
            | _ -> false

    member this.NewContext() : Task<IAPIRequestContext> =
        this.Playwright.APIRequest.NewContextAsync(APIRequestNewContextOptions(BaseURL = Server.Url()))

    // ── AT-R1: JSON-LD parses to real triples ────────────────────────────────

    [<Test>]
    member this.``AT-R1 game JSON-LD parses to real schema-org triples``() =
        task {
            use! ctx = this.NewContext()
            let gameId = "at-r1"

            let! resp =
                ctx.GetAsync(
                    sprintf "/games/%s" gameId,
                    APIRequestContextOptions(Headers = dict [ "Accept", "application/ld+json" ])
                )

            Assert.That(resp.Status, Is.EqualTo 200, "GET game ld+json not 200")

            let contentType =
                match resp.Headers.TryGetValue "content-type" with
                | true, v -> v
                | _ -> ""

            Assert.That(
                contentType,
                Is.EqualTo "application/ld+json",
                sprintf "Content-Type must be exactly application/ld+json, got: %s" contentType
            )

            let! body = resp.TextAsync()

            // @context must contain the canonical https-current schema.org context URL
            Assert.That(
                RdfVerificationTests.HasCanonicalSchemaOrgRef body,
                Is.True,
                "body @context must contain the canonical https-current schema.org context URL"
            )

            // Parse as real RDF
            use g = RdfVerificationTests.ParseJsonLd body

            // Must find triple with predicate = full IRI https://schema.org/actionStatus
            let actionStatusTriples =
                RdfVerificationTests.TriplesWithPred(g, "https://schema.org/actionStatus")
                |> Seq.toList

            Assert.That(
                actionStatusTriples,
                Is.Not.Empty,
                "IGraph has no triple with predicate https://schema.org/actionStatus"
            )

            // Object must be the expected schema.org ActionStatusType IRI
            let objNode = actionStatusTriples.[0].Object

            match objNode with
            | :? IUriNode as u ->
                Assert.That(
                    u.Uri.AbsoluteUri.StartsWith "https://schema.org/",
                    Is.True,
                    sprintf "actionStatus object IRI must be under https://schema.org/, got: %s" u.Uri.AbsoluteUri
                )

                Assert.That(
                    u.Uri.AbsoluteUri,
                    Is.EqualTo "https://schema.org/ActiveActionStatus",
                    sprintf "fresh game must have ActiveActionStatus, got: %s" u.Uri.AbsoluteUri
                )
            | _ -> Assert.Fail(sprintf "actionStatus object is not a URI node: %A" objNode)

            // No triple whose predicate or object URI is under http://schema.org/ (wrong scheme)
            let wrongSchemePrefix = "http://schema.org/"

            for triple in g.Triples do
                let checkNode (n: INode) =
                    match n with
                    | :? IUriNode as u ->
                        Assert.That(
                            u.Uri.AbsoluteUri.StartsWith wrongSchemePrefix,
                            Is.False,
                            sprintf "Triple has URI under wrong scheme http://schema.org/: %s" u.Uri.AbsoluteUri
                        )
                    | _ -> ()

                checkNode triple.Predicate
                checkNode triple.Object
        }

    // ── AT-R2: 422 is a real SHACL ValidationReport ──────────────────────────

    [<Test>]
    member this.``AT-R2 422 body is a real SHACL ValidationReport``() =
        task {
            use! ctx = this.NewContext()
            let originBase = Server.Url().TrimEnd('/')
            let squareIri = originBase + "/tictactoe#square"
            let expectedPath = squareIri

            let badMove = Dictionary<string, obj>()
            badMove.["@type"] <- "https://schema.org/MoveAction"
            badMove.["https://schema.org/agent"] <- "X"
            badMove.[squareIri] <- "NotASquare"

            let! resp =
                ctx.PostAsync(
                    "/games/at-r2",
                    APIRequestContextOptions(
                        Headers = dict [ "Content-Type", "application/ld+json" ],
                        DataObject = badMove
                    )
                )

            Assert.That(resp.Status, Is.EqualTo 422, "invalid move must yield 422")

            let contentType =
                match resp.Headers.TryGetValue "content-type" with
                | true, v -> v
                | _ -> ""

            // Must carry application/ld+json AND profile=shacl
            Assert.That(
                contentType.Contains "application/ld+json",
                Is.True,
                sprintf "422 Content-Type must contain application/ld+json, got: %s" contentType
            )

            Assert.That(
                contentType.Contains "profile=\"http://www.w3.org/ns/shacl#\"",
                Is.True,
                sprintf "422 Content-Type must carry profile=shacl, got: %s" contentType
            )

            let! body = resp.TextAsync()
            use g = RdfVerificationTests.ParseJsonLd body

            // Parse as SHACL ValidationReport
            let report = VDS.RDF.Shacl.Validation.Report.Parse(g)
            Assert.That(report.Conforms, Is.False, "SHACL report must not conform for invalid move")

            // Assert ≥1 result with expected resultPath and Violation severity
            let shResultPath = "http://www.w3.org/ns/shacl#resultPath"
            let shSeverity = "http://www.w3.org/ns/shacl#resultSeverity"
            let shViolation = "http://www.w3.org/ns/shacl#Violation"
            let shConstraintComponent = "http://www.w3.org/ns/shacl#sourceConstraintComponent"

            let resultPathTriples =
                RdfVerificationTests.TriplesWithPred(g, shResultPath) |> Seq.toList

            let hasExpectedPath =
                resultPathTriples
                |> List.exists (fun t ->
                    match t.Object with
                    | :? IUriNode as u -> u.Uri.AbsoluteUri = expectedPath
                    | _ -> false)

            Assert.That(
                hasExpectedPath,
                Is.True,
                sprintf "No sh:resultPath triple with IRI '%s' — expected tictactoe#square path" expectedPath
            )

            let hasSeverityViolation =
                RdfVerificationTests.TriplesWithPred(g, shSeverity)
                |> Seq.exists (fun t ->
                    match t.Object with
                    | :? IUriNode as u -> u.Uri.AbsoluteUri = shViolation
                    | _ -> false)

            Assert.That(hasSeverityViolation, Is.True, "No sh:resultSeverity = sh:Violation triple")

            let hasConstraintComponent =
                RdfVerificationTests.TriplesWithPred(g, shConstraintComponent)
                |> Seq.isEmpty
                |> not

            Assert.That(hasConstraintComponent, Is.True, "No sh:sourceConstraintComponent triple")

            // No example.org URIs anywhere
            for triple in g.Triples do
                let check (n: INode) =
                    match n with
                    | :? IUriNode as u ->
                        Assert.That(
                            u.Uri.Host.Contains "example.org",
                            Is.False,
                            sprintf "Triple has example.org URI: %s" u.Uri.AbsoluteUri
                        )
                    | _ -> ()

                check triple.Subject
                check triple.Predicate
                check triple.Object
        }

    // AT-R3 removed: provenance real-RDF verification superseded by ProvenanceLineageTests.fs (#391),
    // which covers AT-P1..P6 with dereferenceable activity IRIs, wasDerivedFrom chain, and SPARQL queries.

    // ── AT-R4: JSON-LD @context compaction round-trip ────────────────────────

    [<Test>]
    member this.``AT-R4 JSON-LD context compaction round-trip offline``() =
        task {
            use! ctx = this.NewContext()

            let! resp =
                ctx.GetAsync(
                    "/games/at-r4",
                    APIRequestContextOptions(Headers = dict [ "Accept", "application/ld+json" ])
                )

            Assert.That(resp.Status, Is.EqualTo 200, "GET game ld+json not 200")
            let! body = resp.TextAsync()

            // Expand via strict loader — proves offline completion
            let opts = JsonLdProcessorOptions()
            opts.DocumentLoader <- strictLoader

            let expanded =
                JsonLdProcessor.Expand(JToken.Parse body, opts, System.Collections.Generic.List())

            let expandedJson = expanded.ToString()

            // Full IRI must appear as a property key in the expanded output
            Assert.That(
                expandedJson.Contains "\"https://schema.org/actionStatus\"",
                Is.True,
                "Expanded JSON-LD must contain full IRI https://schema.org/actionStatus as a key"
            )

            // CURIE form must NOT appear as a property key in the expanded output
            Assert.That(
                expandedJson.Contains "\"schema:actionStatus\"",
                Is.False,
                "Expanded JSON-LD must not contain CURIE 'schema:actionStatus' as a key"
            )

            // Compact against the served @context
            use contextDoc = JsonDocument.Parse body

            let ctxToken =
                JToken.Parse(contextDoc.RootElement.GetProperty("@context").GetRawText())

            let compacted = JsonLdProcessor.Compact(expanded, ctxToken, opts)
            let compactedJson = compacted.ToString()

            // After compaction: CURIE form must appear in the @graph nodes
            // We check the @graph portion specifically to avoid matching context definitions
            let mutable graphEl = Unchecked.defaultof<JsonElement>
            use compactedDoc = JsonDocument.Parse compactedJson

            let graphJson =
                if compactedDoc.RootElement.TryGetProperty("@graph", &graphEl) then
                    graphEl.GetRawText()
                else
                    compactedJson

            Assert.That(
                graphJson.Contains "\"schema:actionStatus\"",
                Is.True,
                "Compacted @graph must contain 'schema:actionStatus' CURIE key"
            )

            // Full IRI as a property KEY must NOT appear in the @graph (may appear in @context definitions)
            Assert.That(
                graphJson.Contains "\"https://schema.org/actionStatus\"",
                Is.False,
                "Compacted @graph must not contain full IRI 'https://schema.org/actionStatus' as a key"
            )
        }

    // ── AT-R5: guard — RDF layer catches what string-match misses ────────────

    [<Test>]
    member this.``AT-R5 RDF layer catches non-conformant output that string-match misses``() =
        task {
            use! ctx = this.NewContext()

            let! resp =
                ctx.GetAsync(
                    "/games/at-r5",
                    APIRequestContextOptions(Headers = dict [ "Accept", "application/ld+json" ])
                )

            Assert.That(resp.Status, Is.EqualTo 200, "GET game ld+json not 200")
            let! originalBody = resp.TextAsync()

            // ── (a) Structural mutation: remove @context ──────────────────────
            // body.Contains "actionStatus" must still be true on the mutated body
            // but the IGraph must yield zero https://schema.org/actionStatus triples.
            let bodyWithoutContext =
                let node = System.Text.Json.Nodes.JsonNode.Parse originalBody
                node.AsObject().Remove "@context" |> ignore
                node.ToJsonString()

            Assert.That(
                bodyWithoutContext.Contains "actionStatus",
                Is.True,
                "(a) sanity: body.Contains 'actionStatus' must still match after removing @context"
            )

            // Now parse: without @context the compacted CURIEs cannot be expanded
            use g_a = RdfVerificationTests.ParseJsonLd bodyWithoutContext

            let actionStatusTriples_a =
                RdfVerificationTests.TriplesWithPred(g_a, "https://schema.org/actionStatus")
                |> Seq.toList

            Assert.That(
                actionStatusTriples_a,
                Is.Empty,
                "(a) IGraph must have zero https://schema.org/actionStatus triples when @context is removed — string-match misses this"
            )

            // ── (b) Semantic mutation: rewrite actionStatus object to example.org ──
            // body.Contains "actionStatus" must still be true (key survives)
            // but the object IRI must NOT be https://schema.org/ActiveActionStatus.
            let bodyWithMutatedStatus =
                // The compacted actionStatus value uses the schema: prefix.
                // Replace the specific @id value to an example.org IRI while keeping the key.
                let candidates =
                    [ "schema:ActiveActionStatus"
                      "schema:CompletedActionStatus"
                      "schema:FailedActionStatus" ]

                candidates
                |> List.tryPick (fun candidate ->
                    if originalBody.Contains candidate then
                        Some(originalBody.Replace(candidate, "https://example.org/ActiveActionStatus"))
                    else
                        None)
                |> Option.defaultWith (fun () ->
                    // Fallback: no compacted form found; try replacing full IRI if present
                    originalBody.Replace(
                        "https://schema.org/ActiveActionStatus",
                        "https://example.org/ActiveActionStatus"
                    ))

            Assert.That(
                bodyWithMutatedStatus.Contains "actionStatus",
                Is.True,
                "(b) sanity: body.Contains 'actionStatus' must still match after mutating the object"
            )

            use g_b = RdfVerificationTests.ParseJsonLd bodyWithMutatedStatus

            let actionStatusTriples_b =
                RdfVerificationTests.TriplesWithPred(g_b, "https://schema.org/actionStatus")
                |> Seq.toList

            // The predicate triple itself should still exist (key was not removed)
            Assert.That(
                actionStatusTriples_b,
                Is.Not.Empty,
                "(b) IGraph must still have the schema:actionStatus predicate after object mutation"
            )

            // But the object must now be the example.org IRI, NOT schema.org/Active...
            let objUri_b = actionStatusTriples_b.[0].Object

            match objUri_b with
            | :? IUriNode as u ->
                Assert.That(
                    u.Uri.AbsoluteUri,
                    Is.Not.EqualTo "https://schema.org/ActiveActionStatus",
                    "(b) exact-object-IRI assertion must fail on mutated body — string-match missed this"
                )

                Assert.That(
                    u.Uri.Host.Contains "example.org",
                    Is.True,
                    sprintf "(b) mutated object must be under example.org, got: %s" u.Uri.AbsoluteUri
                )
            | _ -> Assert.Fail(sprintf "(b) actionStatus object is not a URI node after mutation: %A" objUri_b)
        }

    // ── AT-R6: /tictactoe vocab JSON-LD resolves all external prefixes ───────
    //
    // GET /tictactoe was never exercised via Accept: application/ld+json (#394 review):
    // its graph registers rdf/rdfs/owl/schema alongside ttt, so after the origin-filter
    // fix ALL FOUR must be covered by JsonLdContext or their compact IRIs are undefined.
    // Proves it via the same strict offline loader used by AT-R1/AT-R4.
    [<Test>]
    member this.``AT-R6 ttt vocabulary JSON-LD resolves rdf, rdfs, owl and schema externally``() =
        task {
            use! ctx = this.NewContext()

            let! resp =
                ctx.GetAsync("/tictactoe", APIRequestContextOptions(Headers = dict [ "Accept", "application/ld+json" ]))

            Assert.That(resp.Status, Is.EqualTo 200, "GET /tictactoe ld+json not 200")
            let! body = resp.TextAsync()
            use g = RdfVerificationTests.ParseJsonLd body

            let assertPredicateResolves (predIri: string) (label: string) =
                Assert.That(
                    RdfVerificationTests.TriplesWithPred(g, predIri) |> Seq.isEmpty |> not,
                    Is.True,
                    sprintf "IGraph has no triple with predicate %s (%s)" predIri label
                )

            // rdf:type (via ttl "a" shorthand)
            assertPredicateResolves "http://www.w3.org/1999/02/22-rdf-syntax-ns#type" "rdf"
            // rdfs:label
            assertPredicateResolves "http://www.w3.org/2000/01/rdf-schema#label" "rdfs"
            // rdfs:domain, whose object is owl/schema-typed — confirms owl:Class and schema:MoveAction
            // objects resolve to full IRIs, not undefined CURIE strings.
            let domainTriples =
                RdfVerificationTests.TriplesWithPred(g, "http://www.w3.org/2000/01/rdf-schema#domain")
                |> Seq.toList

            Assert.That(domainTriples, Is.Not.Empty, "IGraph has no rdfs:domain triple")

            let domainObjIri =
                match domainTriples.[0].Object with
                | :? IUriNode as u -> u.Uri.AbsoluteUri
                | other ->
                    Assert.Fail(sprintf "rdfs:domain object is not a URI node: %A" other)
                    |> ignore
                    |> string

            Assert.That(
                domainObjIri,
                Is.EqualTo "https://schema.org/MoveAction",
                "rdfs:domain object must resolve to full schema.org IRI, not stay an undefined 'schema:MoveAction' CURIE"
            )

            let classTriples =
                RdfVerificationTests.TriplesWithPred(g, "http://www.w3.org/1999/02/22-rdf-syntax-ns#type")
                |> Seq.filter (fun t ->
                    match t.Object with
                    | :? IUriNode as u -> u.Uri.AbsoluteUri = "http://www.w3.org/2002/07/owl#Class"
                    | _ -> false)
                |> Seq.toList

            Assert.That(
                classTriples,
                Is.Not.Empty,
                "No rdf:type triple resolving to full IRI http://www.w3.org/2002/07/owl#Class — owl: prefix undefined"
            )
        }

    // ── AT-R7: /vocabulary — GeneratedLinkedData.graphFor genuinely wired at request time ──
    //
    // #396 round 5: GeneratedLinkedData.graphFor/jsonLdContextFor were dead code baking a fake
    // example.org placeholder into an unused module. /vocabulary wires graphFor into a real,
    // per-request HTTP endpoint (Frank.LinkedData.LinkedDataConfig.GraphFactory). Proves the
    // served ttt:square term resolves against the REAL request origin, never example.org, in
    // both text/turtle and application/ld+json.
    [<Test>]
    member this.``AT-R7 /vocabulary ttt:square resolves to the real origin, never example.org``() =
        task {
            use! ctx = this.NewContext()
            let originBase = Server.Url().TrimEnd('/')
            let squareIri = originBase + "/tictactoe#square"

            let! turtleResp =
                ctx.GetAsync("/vocabulary", APIRequestContextOptions(Headers = dict [ "Accept", "text/turtle" ]))

            Assert.That(turtleResp.Status, Is.EqualTo 200, "GET /vocabulary text/turtle not 200")
            let! turtleBody = turtleResp.TextAsync()

            Assert.That(
                turtleBody.Contains "example.org",
                Is.False,
                "Turtle body must never contain example.org (#396 round 5)"
            )

            // Not just absence of example.org — the real server origin must actually be present,
            // exactly, as ttt:square's subject IRI (weak-assertion false-green otherwise: a
            // graphFor emitting some other garbage host would still pass an absence-only check).
            Assert.That(
                turtleBody.Contains squareIri,
                Is.True,
                sprintf "Turtle body must contain the real-origin ttt:square IRI '%s', got: %s" squareIri turtleBody
            )

            let! ldJsonResp =
                ctx.GetAsync(
                    "/vocabulary",
                    APIRequestContextOptions(Headers = dict [ "Accept", "application/ld+json" ])
                )

            Assert.That(ldJsonResp.Status, Is.EqualTo 200, "GET /vocabulary ld+json not 200")
            let! ldJsonBody = ldJsonResp.TextAsync()

            Assert.That(
                ldJsonBody.Contains "example.org",
                Is.False,
                "ld+json body must never contain example.org (#396 round 5)"
            )

            use g = RdfVerificationTests.ParseJsonLd ldJsonBody

            let domainTriples =
                RdfVerificationTests.TriplesWithPred(g, "http://www.w3.org/2000/01/rdf-schema#domain")
                |> Seq.toList

            let squareSubjects =
                domainTriples
                |> List.choose (fun t ->
                    match t.Subject with
                    | :? IUriNode as u when u.Uri.AbsoluteUri.EndsWith "tictactoe#square" -> Some u.Uri.AbsoluteUri
                    | _ -> None)

            // Exact match against the real server origin (mirrors AT-R2's originBase/squareIri
            // pattern) — an absence-only check would still pass if graphFor emitted a different
            // garbage host or an empty authority instead of the real origin.
            Assert.That(
                squareSubjects,
                Is.EqualTo [ squareIri ],
                sprintf "ttt:square subject IRI must be exactly the real request origin, got: %A" squareSubjects
            )
        }

    // ── AT-R-live: live-network expansion against the real schema.org context ────
    //
    // Opt-in tier (#394). Expands the served body with the DEFAULT real-HTTP JSON-LD
    // document loader (no stub) — proves the served @context actually resolves on
    // the real web, not merely offline via the strict loader above. A thrown
    // JsonLdProcessorException on egress-down is the correct failure mode; do not
    // catch it.
    [<Test>]
    [<Category("LiveNetwork")>]
    [<Explicit("requires outbound egress to schema.org")>]
    member this.``AT-R-live game JSON-LD @context expands against live schema.org``() =
        task {
            use! ctx = this.NewContext()

            let! resp =
                ctx.GetAsync(
                    "/games/at-r-live",
                    APIRequestContextOptions(Headers = dict [ "Accept", "application/ld+json" ])
                )

            Assert.That(resp.Status, Is.EqualTo 200, "GET game ld+json not 200")
            let! body = resp.TextAsync()

            let opts = JsonLdProcessorOptions()

            opts.DocumentLoader <-
                Func<Uri, JsonLdLoaderOptions, RemoteDocument>(fun uri o -> DefaultDocumentLoader.LoadJson(uri, o))

            let expanded =
                JsonLdProcessor.Expand(JToken.Parse body, opts, System.Collections.Generic.List())

            let expandedJson = expanded.ToString()

            Assert.That(
                expandedJson.Contains "\"https://schema.org/actionStatus\"",
                Is.True,
                "Live-expanded JSON-LD must contain full IRI https://schema.org/actionStatus as a key"
            )

            Assert.That(
                expandedJson.Contains "\"schema:actionStatus\"",
                Is.False,
                "Live-expanded JSON-LD must not contain CURIE 'schema:actionStatus' as a key"
            )
        }

    // ── AT-R6-live: ttt vocabulary expansion against all four live external contexts ─
    //
    // Opt-in tier (#394 review). Twin of AT-R-live for /tictactoe: proves rdf, rdfs, owl
    // AND schema all genuinely resolve via live fetch — not merely offline via the strict
    // loader AT-R6 uses. A thrown JsonLdProcessorException on egress-down is the correct
    // failure mode; do not catch it.
    [<Test>]
    [<Category("LiveNetwork")>]
    [<Explicit("requires outbound egress to w3.org and schema.org")>]
    member this.``AT-R6-live ttt vocabulary JSON-LD expands against live external contexts``() =
        task {
            use! ctx = this.NewContext()

            let! resp =
                ctx.GetAsync("/tictactoe", APIRequestContextOptions(Headers = dict [ "Accept", "application/ld+json" ]))

            Assert.That(resp.Status, Is.EqualTo 200, "GET /tictactoe ld+json not 200")
            let! body = resp.TextAsync()

            // Real RDF triples (not raw Expand() JSON text): rdf:type triples always carry
            // the full rdf:type predicate regardless of @type-keyword compaction, so this
            // avoids asserting on a JSON shape that JsonLdProcessor.Expand never produces.
            use g = RdfVerificationTests.ParseJsonLdLive body

            let assertPredicateResolves (predIri: string) (label: string) =
                Assert.That(
                    RdfVerificationTests.TriplesWithPred(g, predIri) |> Seq.isEmpty |> not,
                    Is.True,
                    sprintf "Live-parsed IGraph has no triple with predicate %s (%s)" predIri label
                )

            assertPredicateResolves "http://www.w3.org/1999/02/22-rdf-syntax-ns#type" "rdf"
            assertPredicateResolves "http://www.w3.org/2000/01/rdf-schema#label" "rdfs"

            let domainTriples =
                RdfVerificationTests.TriplesWithPred(g, "http://www.w3.org/2000/01/rdf-schema#domain")
                |> Seq.toList

            Assert.That(domainTriples, Is.Not.Empty, "Live-parsed IGraph has no rdfs:domain triple")

            match domainTriples.[0].Object with
            | :? IUriNode as u ->
                Assert.That(
                    u.Uri.AbsoluteUri,
                    Is.EqualTo "https://schema.org/MoveAction",
                    "rdfs:domain object must resolve to full schema.org IRI via live fetch"
                )
            | other -> Assert.Fail(sprintf "rdfs:domain object is not a URI node: %A" other)

            let classTriples =
                RdfVerificationTests.TriplesWithPred(g, "http://www.w3.org/1999/02/22-rdf-syntax-ns#type")
                |> Seq.filter (fun t ->
                    match t.Object with
                    | :? IUriNode as u -> u.Uri.AbsoluteUri = "http://www.w3.org/2002/07/owl#Class"
                    | _ -> false)
                |> Seq.toList

            Assert.That(
                classTriples,
                Is.Not.Empty,
                "No rdf:type triple resolving to full IRI http://www.w3.org/2002/07/owl#Class via live fetch"
            )
        }
