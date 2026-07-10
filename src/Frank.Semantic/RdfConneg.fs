namespace Frank.Semantic

open System
open System.Net.Http

// ── Public types ──────────────────────────────────────────────────────────────

/// Result of a content-negotiated RDF fetch.
type ConnegFetchResult =
    | RdfContent of
        {| MediaType: string
           Body: byte[]
           HttpStatus: int
           ETag: string option
           LastModified: string option
           CacheControlMaxAge: int option |}
    | NotModified
    | NonRdfContent of {| MediaType: string; HttpStatus: int |}
    | RedirectCapHit
    | FetchFailed of reason: string
    /// Durable HTTP error with the response status code.
    /// 404/410 = link rot (drift); 5xx/429 = probe-failed (transient).
    | HttpErrorStatus of status: int * uri: Uri

/// Injectable boundary: URI × prior-ETag × prior-LastModified → ConnegFetchResult.
/// Inject the real rdfFetch in production; inject a stub in tests.
type ConnegFetch = Uri -> string option -> string option -> Async<ConnegFetchResult>

/// Schema-v2 evidence produced by RdfConneg.buildEvidence.
type FetchEvidence =
    { MediaType: string option
      Validated: LockFile.ValidationStatus
      Terms: Set<string> option
      HttpStatus: int option
      ETag: string option
      LastModified: string option
      Hash: string
      CacheControlMaxAge: int option }

/// Outcome from RdfConneg.buildEvidence.
type EvidenceResult =
    | Updated of FetchEvidence
    | Unchanged
    /// Durable: 404/410/406/415/401/403/redirect-cap/RDF-parse-failed — Validated=false, exit 2.
    | Undereferenceable of reason: string
    /// Transient: 5xx/429/network/timeout — Validated unchanged, exit 1.
    | TransientFailure of reason: string
    /// External vocab served text/html (possibly RDFa) — not verifiable offline, not durable drift.
    /// Validated=false but NOT exit-2; maps to a non-durable probe outcome for unowned vocabs.
    | UnverifiableNonRdf of reason: string

module RdfConneg =

    // ── Constants ─────────────────────────────────────────────────────────────

    // L6.3: Accept header aligned with isRdfMediaType (advertise all accepted types)
    let private rdfAcceptValue =
        "text/turtle;q=1.0, application/ld+json;q=0.9, application/rdf+xml;q=0.8, application/n-triples;q=0.7, text/n3;q=0.6"

    /// Maximum 3xx redirects to follow before giving up (httpRange-14 / cap per Holzmann #10).
    let maxRedirectHops = 5

    // M6: explicit per-request timeout to bound wall-clock (Holzmann #10)
    let private requestTimeoutSeconds = 30

    let private rdfMediaTypes =
        Set.ofList
            [ "text/turtle"
              "application/ld+json"
              "application/rdf+xml"
              "application/n-triples"
              "text/n3" ]

    // ── Pure helpers ──────────────────────────────────────────────────────────

    let private stripParams (ct: string) : string =
        match ct.IndexOf(';') with
        | -1 -> ct.Trim().ToLowerInvariant()
        | idx -> ct.[.. idx - 1].Trim().ToLowerInvariant()

    /// True when contentType is a recognised RDF serialisation media type.
    let isRdfMediaType (contentType: string) : bool =
        Set.contains (stripParams contentType) rdfMediaTypes

    let private extractLocalName (iri: string) : string option =
        let idx = max (iri.LastIndexOf '#') (iri.LastIndexOf '/')

        if idx >= 0 && idx < iri.Length - 1 then
            Some iri.[idx + 1 ..]
        else
            None

    /// Local names of terms whose absolute IRI starts with namespaceBase.
    let termsInNamespace (namespaceBase: Uri) (iris: VocabTermIris) : Set<string> =
        let baseStr = namespaceBase.AbsoluteUri

        Set.unionMany [ iris.ClassIris; iris.PropertyIris; iris.IndividualIris ]
        |> Set.toSeq
        |> Seq.filter (fun iri -> iri.StartsWith(baseStr, StringComparison.Ordinal))
        |> Seq.choose extractLocalName
        |> Set.ofSeq

    /// Extract the HTTP status code from a ConnegFetchResult.
    /// L3: extracted from duplicated inline match in Refresh.fs and Validate.fs.
    let statusOf (result: ConnegFetchResult) : int =
        match result with
        | HttpErrorStatus(s, _) -> s
        | NonRdfContent r -> r.HttpStatus
        | RdfContent r -> r.HttpStatus
        | _ -> 0

    // ── Pure evidence builder ─────────────────────────────────────────────────

    let private validatedStatus (now: DateTimeOffset) : LockFile.ValidationStatus =
        { IsValidated = true
          Reason = None
          LastChecked = Some now }

    let private fromRdfContent
        (namespaceBase: Uri)
        (now: DateTimeOffset)
        (r:
            {| MediaType: string
               Body: byte[]
               HttpStatus: int
               ETag: string option
               LastModified: string option
               CacheControlMaxAge: int option |})
        : EvidenceResult =
        let format = VocabFetcher.detectFormat (Some r.MediaType) namespaceBase

        match VocabFetcher.parseGraph format r.Body with
        | Error msg -> Undereferenceable $"RDF parse failed: {msg}"
        | Ok graph ->
            let terms = termsInNamespace namespaceBase (ConventionEngine.extractTermIris graph)

            Updated
                { MediaType = Some r.MediaType
                  Validated = validatedStatus now
                  Terms = Some terms
                  HttpStatus = Some r.HttpStatus
                  ETag = r.ETag
                  LastModified = r.LastModified
                  Hash = VocabFetcher.sha256Hex r.Body
                  CacheControlMaxAge = r.CacheControlMaxAge }

    /// Build schema-v2 evidence from a ConnegFetchResult.
    /// Pure: no network I/O; RDF parsing and hashing are in-memory.
    /// 404/410/406/415 → Undereferenceable (durable); 401/403 → Undereferenceable auth-walled (durable,
    ///   deliberate decision: anonymous follow-your-nose agent cannot resolve auth-walled IRIs).
    /// 5xx/429/network → TransientFailure (operational).
    /// text/html (unowned) → UnverifiableNonRdf (non-durable; possibly RDFa, not verifiable offline).
    let buildEvidence (namespaceBase: Uri) (now: DateTimeOffset) (result: ConnegFetchResult) : EvidenceResult =
        match result with
        | NotModified -> Unchanged
        | RedirectCapHit -> Undereferenceable $"redirect cap ({maxRedirectHops} hops) exceeded"
        | FetchFailed reason -> TransientFailure $"network error: {reason}"
        | NonRdfContent r ->
            if stripParams r.MediaType = "text/html" then
                // M2: external text/html (possibly RDFa) — not verifiable offline, not durable drift.
                // An owned endpoint serving text/html is LyingIri — callers (Validate.validateOne)
                // must map UnverifiableNonRdf to LyingIri for owned entries.
                UnverifiableNonRdf $"non-RDF media type 'text/html' (possibly RDFa) — not verifiable offline"
            else
                Undereferenceable $"non-RDF content-type '{r.MediaType}' (HTTP {r.HttpStatus})"
        | RdfContent r -> fromRdfContent namespaceBase now r
        | HttpErrorStatus(404, uri) -> Undereferenceable $"HTTP 404 — gone: {uri}"
        | HttpErrorStatus(410, uri) -> Undereferenceable $"HTTP 410 — permanently gone: {uri}"
        // M1: 406/415 = server will never give RDF for this IRI — durable Undereferenceable.
        | HttpErrorStatus(406, uri) -> Undereferenceable $"HTTP 406 — no RDF representation (not acceptable): {uri}"
        | HttpErrorStatus(415, uri) ->
            Undereferenceable $"HTTP 415 — no RDF representation (unsupported media type): {uri}"
        // M1: 401/403 = auth-walled; deliberate decision — anonymous follow-your-nose agent cannot
        // resolve this IRI. Durable: requires credentials we do not have.
        | HttpErrorStatus(401, uri) -> Undereferenceable $"HTTP 401 — auth-walled (anonymous dereference fails): {uri}"
        | HttpErrorStatus(403, uri) -> Undereferenceable $"HTTP 403 — auth-walled (anonymous dereference fails): {uri}"
        // 5xx/429 = transient server-side failures; keep as TransientFailure (exit 1).
        | HttpErrorStatus(status, uri) -> TransientFailure $"HTTP {status} probe-failed: {uri}"

    // ── Effectful helpers ─────────────────────────────────────────────────────

    let private extractETag (response: HttpResponseMessage) : string option =
        response.Headers.ETag |> Option.ofObj |> Option.map (fun e -> e.ToString())

    let private extractLastModified (response: HttpResponseMessage) : string option =
        let lm = response.Content.Headers.LastModified
        if lm.HasValue then Some(lm.Value.ToString("R")) else None

    let private extractCacheMaxAge (response: HttpResponseMessage) : int option =
        let cc = response.Headers.CacheControl

        if cc <> null && cc.MaxAge.HasValue then
            Some(int cc.MaxAge.Value.TotalSeconds)
        else
            None

    let private resolveLocation (current: Uri) (response: HttpResponseMessage) : Uri option =
        response.Headers.Location
        |> Option.ofObj
        |> Option.map (fun loc -> Uri(current, loc))

    let private sendRequest
        (client: HttpClient)
        (uri: Uri)
        (priorETag: string option)
        (priorLastModified: string option)
        : Async<Result<HttpResponseMessage, string>> =
        async {
            // L5: only wrap network/IO exceptions; genuine defects must surface, not be mislabeled.
            use msg = new HttpRequestMessage(HttpMethod.Get, uri)
            msg.Headers.TryAddWithoutValidation("Accept", rdfAcceptValue) |> ignore

            priorETag
            |> Option.iter (fun e -> msg.Headers.TryAddWithoutValidation("If-None-Match", e) |> ignore)

            priorLastModified
            |> Option.iter (fun lm -> msg.Headers.TryAddWithoutValidation("If-Modified-Since", lm) |> ignore)

            try
                let! resp = client.SendAsync(msg) |> Async.AwaitTask
                return Ok resp
            with
            | :? HttpRequestException as ex -> return Error ex.Message
            | :? Threading.Tasks.TaskCanceledException as ex -> return Error ex.Message
        }

    let private handleSuccessResponse (response: HttpResponseMessage) : Async<ConnegFetchResult> =
        async {
            let ctHeader = response.Content.Headers.ContentType

            let mediaType =
                if ctHeader <> null then
                    stripParams ctHeader.MediaType
                else
                    ""

            if not (isRdfMediaType mediaType) then
                return
                    NonRdfContent
                        {| MediaType = mediaType
                           HttpStatus = int response.StatusCode |}
            else
                let! bytes = response.Content.ReadAsByteArrayAsync() |> Async.AwaitTask

                return
                    RdfContent
                        {| MediaType = mediaType
                           Body = bytes
                           HttpStatus = int response.StatusCode
                           ETag = extractETag response
                           LastModified = extractLastModified response
                           CacheControlMaxAge = extractCacheMaxAge response |}
        }

    let rec private fetchLoop
        (client: HttpClient)
        (uri: Uri)
        (priorETag: string option)
        (priorLastModified: string option)
        (hops: int)
        : Async<ConnegFetchResult> =
        async {
            let! reqResult = sendRequest client uri priorETag priorLastModified

            match reqResult with
            | Error msg -> return FetchFailed msg
            | Ok response ->
                // H3: Constitution rule 6 — dispose HttpResponseMessage on all exit paths.
                use _ = response
                let status = int response.StatusCode

                // L6.1: 304 is only meaningful when we sent a conditional (If-None-Match or
                // If-Modified-Since). A spurious 304 without a validator would incorrectly reset
                // the SLA clock on a never-confirmed entry; treat as a fresh response instead.
                let sentConditional = priorETag.IsSome || priorLastModified.IsSome

                if status = 304 && sentConditional then
                    return NotModified
                // M5: accept any 2xx, not just 200.
                elif status >= 200 && status < 300 then
                    return! handleSuccessResponse response
                // L6.2: 300 (Multiple Choices) excluded from followable-redirect range.
                elif status >= 301 && status < 400 then
                    return! handleRedirect client uri priorETag priorLastModified hops response
                else
                    return HttpErrorStatus(status, uri)
        }

    and private handleRedirect
        (client: HttpClient)
        (uri: Uri)
        (priorETag: string option)
        (priorLastModified: string option)
        (hops: int)
        (response: HttpResponseMessage)
        : Async<ConnegFetchResult> =
        if hops >= maxRedirectHops then
            async.Return RedirectCapHit
        else
            match resolveLocation uri response with
            | None -> async.Return(FetchFailed $"redirect from {uri} has no Location header")
            // L1: RFC 9110 §13.1 — validators (If-None-Match/If-Modified-Since) are resource-specific.
            // Do not forward them across redirect hops to the new Location target.
            | Some next -> fetchLoop client next None None (hops + 1)

    /// Create an HttpClient with AllowAutoRedirect=false, as required by rdfFetch.
    /// Asserts the property at construction time; fails loudly if the setting does not take effect.
    /// M6: explicit 30s per-request timeout to bound wall-clock (Holzmann #10).
    let makeNoRedirectClient () : HttpClient =
        let handler = new HttpClientHandler()
        handler.AllowAutoRedirect <- false

        if handler.AllowAutoRedirect then
            invalidArg "handler" "AllowAutoRedirect must be false for rdfFetch; hop counting is done in fetchLoop"

        let client = new HttpClient(handler)
        client.Timeout <- TimeSpan.FromSeconds(float requestTimeoutSeconds)
        client

    /// Production ConnegFetch backed by a shared HttpClient.
    /// The client MUST have AllowAutoRedirect = false. Use makeNoRedirectClient.
    let rdfFetch (client: HttpClient) : ConnegFetch =
        fun uri priorETag priorLastModified -> fetchLoop client uri priorETag priorLastModified 0
