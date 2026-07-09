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
    /// Durable: 404/410/non-RDF/redirect-cap/RDF-parse-failed — Validated=false, exit 2.
    | Undereferenceable of reason: string
    /// Transient: 5xx/429/network/timeout — Validated unchanged, exit 1.
    | TransientFailure of reason: string

module RdfConneg =

    // ── Constants ─────────────────────────────────────────────────────────────

    let private rdfAcceptValue =
        "text/turtle;q=1.0, application/ld+json;q=0.9, application/rdf+xml;q=0.8"

    /// Maximum 3xx redirects to follow before giving up (httpRange-14 / cap per Holzmann #10).
    let maxRedirectHops = 5

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
    /// 404/410 → Undereferenceable (durable); 5xx/429/network → TransientFailure (operational).
    let buildEvidence (namespaceBase: Uri) (now: DateTimeOffset) (result: ConnegFetchResult) : EvidenceResult =
        match result with
        | NotModified -> Unchanged
        | RedirectCapHit -> Undereferenceable $"redirect cap ({maxRedirectHops} hops) exceeded"
        | FetchFailed reason -> TransientFailure $"network error: {reason}"
        | NonRdfContent r -> Undereferenceable $"non-RDF content-type '{r.MediaType}' (HTTP {r.HttpStatus})"
        | RdfContent r -> fromRdfContent namespaceBase now r
        | HttpErrorStatus(404, uri) -> Undereferenceable $"HTTP 404 — gone: {uri}"
        | HttpErrorStatus(410, uri) -> Undereferenceable $"HTTP 410 — permanently gone: {uri}"
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
            try
                use msg = new HttpRequestMessage(HttpMethod.Get, uri)
                msg.Headers.TryAddWithoutValidation("Accept", rdfAcceptValue) |> ignore

                priorETag
                |> Option.iter (fun e -> msg.Headers.TryAddWithoutValidation("If-None-Match", e) |> ignore)

                priorLastModified
                |> Option.iter (fun lm -> msg.Headers.TryAddWithoutValidation("If-Modified-Since", lm) |> ignore)

                let! resp = client.SendAsync(msg) |> Async.AwaitTask
                return Ok resp
            with ex ->
                return Error ex.Message
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
                let status = int response.StatusCode

                if status = 304 then
                    return NotModified
                elif status = 200 then
                    return! handleSuccessResponse response
                elif status >= 300 && status < 400 then
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
            | Some next -> fetchLoop client next priorETag priorLastModified (hops + 1)

    /// Create an HttpClient with AllowAutoRedirect=false, as required by rdfFetch.
    /// Asserts the property at construction time; fails loudly if the setting does not take effect.
    let makeNoRedirectClient () : HttpClient =
        let handler = new HttpClientHandler()
        handler.AllowAutoRedirect <- false

        if handler.AllowAutoRedirect then
            invalidArg "handler" "AllowAutoRedirect must be false for rdfFetch; hop counting is done in fetchLoop"

        new HttpClient(handler)

    /// Production ConnegFetch backed by a shared HttpClient.
    /// The client MUST have AllowAutoRedirect = false. Use makeNoRedirectClient.
    let rdfFetch (client: HttpClient) : ConnegFetch =
        fun uri priorETag priorLastModified -> fetchLoop client uri priorETag priorLastModified 0
