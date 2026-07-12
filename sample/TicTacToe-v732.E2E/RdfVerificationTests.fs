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
/// Offline strict loader (Option A): returns empty @context for exactly
/// https://schema.org / https://schema.org/; throws for any other remote URI.
[<TestFixture>]
type RdfVerificationTests() =
    inherit PlaywrightTest()

    // ── Strict offline document loader ───────────────────────────────────────
    static let strictLoader: Func<Uri, JsonLdLoaderOptions, RemoteDocument> =
        Func<Uri, JsonLdLoaderOptions, RemoteDocument>(fun uri _ ->
            let s = uri.ToString()

            if s = "https://schema.org" || s = "https://schema.org/" then
                let doc = RemoteDocument()
                doc.Document <- JObject.Parse """{"@context":{}}"""
                doc.DocumentUrl <- uri
                doc
            else
                invalidOp (sprintf "strictOfflineLoader: blocked remote URI '%s'" s))

    // ── Core parsing helpers ─────────────────────────────────────────────────

    /// Parse a JSON-LD body into an IGraph using the strict offline loader.
    /// Merges all named graphs from the JSON-LD document into one flat IGraph.
    static member private ParseJsonLd(body: string) : IGraph =
        let opts = JsonLdProcessorOptions()
        opts.DocumentLoader <- strictLoader
        let parser = JsonLdParser(opts)
        use store = new TripleStore()
        use reader = new StringReader(body)
        parser.Load(store :> ITripleStore, reader)
        let merged = new Graph()

        for g in store.Graphs do
            merged.Merge(g) |> ignore

        merged :> IGraph

    /// Parse a Turtle body into an IGraph.
    static member private ParseTurtle(body: string) : IGraph =
        let g = new Graph()
        let parser = TurtleParser()
        use reader = new StringReader(body)
        parser.Load(g, reader)
        g :> IGraph

    // ── Graph query helpers ──────────────────────────────────────────────────

    /// All triples in graph whose predicate matches predIri.
    static member private TriplesWithPred(g: IGraph, predIri: string) : Triple seq =
        g.GetTriplesWithPredicate(Uri predIri)

    /// Objects of triples matching (subject, predIri) in graph.
    static member private ObjectsFor(g: IGraph, subj: INode, predIri: string) : INode seq =
        g.GetTriplesWithSubjectPredicate(subj, g.CreateUriNode(Uri predIri))
        |> Seq.map (fun t -> t.Object)

    /// Subjects typed by the given class IRI (via rdf:type).
    static member private SubjectsByType(g: IGraph, classIri: string) : INode list =
        let rdfType = "http://www.w3.org/1999/02/22-rdf-syntax-ns#type"

        RdfVerificationTests.TriplesWithPred(g, rdfType)
        |> Seq.filter (fun t ->
            match t.Object with
            | :? IUriNode as u -> u.Uri.AbsoluteUri = classIri
            | _ -> false)
        |> Seq.map (fun t -> t.Subject)
        |> Seq.distinctBy (fun n -> n.ToString())
        |> Seq.toList

    /// Extract a literal string value from an INode (literal or URI).
    static member private NodeString(node: INode) : string =
        match node with
        | :? ILiteralNode as l -> l.Value
        | :? IUriNode as u -> u.Uri.AbsoluteUri
        | n -> n.ToString()

    // ── Link header parsing ──────────────────────────────────────────────────

    static member private ParseLinkRels(resp: IAPIResponse) : IDictionary<string, string> =
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

        rels :> IDictionary<string, string>

    // ── Context inspection helpers ───────────────────────────────────────────

    /// True if the JSON-LD body's @context array contains exactly the literal
    /// string "https://schema.org" (not a relative ref, not http://, not example.org).
    static member private HasCanonicalSchemaOrgRef(body: string) : bool =
        use doc = JsonDocument.Parse body
        let mutable ctxEl = Unchecked.defaultof<JsonElement>

        if not (doc.RootElement.TryGetProperty("@context", &ctxEl)) then
            false
        else
            match ctxEl.ValueKind with
            | JsonValueKind.Array ->
                ctxEl.EnumerateArray()
                |> Seq.exists (fun el -> el.ValueKind = JsonValueKind.String && el.GetString() = "https://schema.org")
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

            // @context must contain the canonical "https://schema.org" literal
            Assert.That(
                RdfVerificationTests.HasCanonicalSchemaOrgRef body,
                Is.True,
                "body @context must contain the literal string \"https://schema.org\" (https, canonical)"
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
