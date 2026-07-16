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

    /// Maximum 3xx redirects to follow before giving up (httpRange-14 / cap per Holzmann #10).
    val maxRedirectHops: int

    /// Local names of terms whose absolute IRI starts with namespaceBase.
    val termsInNamespace: namespaceBase: Uri -> iris: VocabTermIris -> Set<string>

    /// Extract the HTTP status code from a ConnegFetchResult.
    /// L3: extracted from duplicated inline match in Refresh.fs and Validate.fs.
    val statusOf: result: ConnegFetchResult -> int

    /// Build schema-v2 evidence from a ConnegFetchResult.
    /// Pure: no network I/O; RDF parsing and hashing are in-memory.
    /// 404/410/406/415 → Undereferenceable (durable); 401/403 → Undereferenceable auth-walled (durable,
    ///   deliberate decision: anonymous follow-your-nose agent cannot resolve auth-walled IRIs).
    /// 5xx/429/network → TransientFailure (operational).
    /// text/html (unowned) → UnverifiableNonRdf (non-durable; possibly RDFa, not verifiable offline).
    val buildEvidence: namespaceBase: Uri -> now: DateTimeOffset -> result: ConnegFetchResult -> EvidenceResult

    /// Create an HttpClient with AllowAutoRedirect=false, as required by rdfFetch.
    /// Asserts the property at construction time; fails loudly if the setting does not take effect.
    /// M6: explicit 30s per-request timeout to bound wall-clock (Holzmann #10).
    val makeNoRedirectClient: unit -> HttpClient

    /// Production ConnegFetch backed by a shared HttpClient.
    /// The client MUST have AllowAutoRedirect = false. Use makeNoRedirectClient.
    val rdfFetch: client: HttpClient -> ConnegFetch
