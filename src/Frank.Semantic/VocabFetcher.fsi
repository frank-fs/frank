namespace Frank.Semantic

open System
open VDS.RDF

module VocabFetcher =

    // ── Public types ──────────────────────────────────────────────────────────

    /// Supported RDF serialisation formats for vocabulary schemas.
    type VocabFormat =
        | JsonLd
        | RdfXml
        | Turtle

    /// Drift comparison result from detectDrift.
    type DriftResult =
        | NoDrift
        | Drift of recordedHash: string * currentHash: string

    /// Returned from a successful fetchAndCache call.
    type CachedVocab =
        { Hash: string
          Format: VocabFormat
          CacheFilePath: string
          Graph: IGraph }

    /// The fetch boundary: Uri → Async<Result<anonymous, reason>>.
    /// Inject a real HttpClient-backed implementation in production;
    /// inject a stub in tests to avoid network calls.
    type Fetch =
        Uri
            -> Async<
                Result<
                    {| ContentType: string option
                       Body: byte[] |},
                    string
                 >
             >

    val stripParams: ct: string -> string

    /// Detect format from Content-Type header, falling back to URI file extension.
    /// Defaults to JsonLd when neither source resolves.
    val detectFormat: contentType: string option -> uri: Uri -> VocabFormat

    /// SHA-256 hex string (lowercase, 64 chars) of the given bytes.
    val sha256Hex: bytes: byte[] -> string

    /// Canonical cache file name: <name>.<hash>.<ext>
    val cacheFileName: name: string -> hash: string -> format: VocabFormat -> string

    /// Parse raw bytes into an IGraph. Returns Error with reason on failure.
    val parseGraph: format: VocabFormat -> bytes: byte[] -> Result<IGraph, string>

    /// Load a vocabulary graph from the cache directory without network access.
    /// Returns None if no cache file for 'name' exists (vocab not yet fetched).
    /// Returns Some (Ok graph) on success; Some (Error msg) if the file is corrupt.
    val loadCachedGraph: cacheDir: string -> name: string -> Result<IGraph, string> option

    /// Cache-only lookup: same semantics as fetchAndCache's cache-hit branch, without
    /// invoking any fetch. Returns None if no cache file for 'name' exists.
    /// Shared by fetchAndCache and any alternate (e.g. content-negotiated) fetch boundary
    /// that wants the same on-disk cache without re-deriving the lookup.
    val loadCachedVocab: cacheDir: string -> name: string -> Result<CachedVocab, string> option

    /// Parse `bytes` as `format`, write to cacheDir under `name`, and wrap as CachedVocab.
    /// Shared write-and-wrap step for any fetch boundary (plain HTTP or content-negotiated)
    /// that has already obtained a successful response body — the counterpart to
    /// loadCachedVocab's shared read step.
    val parseAndCacheBytes:
        cacheDir: string -> name: string -> format: VocabFormat -> bytes: byte[] -> Result<CachedVocab, string>

    /// Fetch a vocabulary URI, parse it, and write it to cacheDir.
    /// Returns CachedVocab on success.
    /// Cache hit (file matching <name>.*) returns cached result without invoking fetch.
    /// fetch failure returns Error with reason; cache dir is left untouched.
    val fetchAndCache:
        fetch: Fetch -> cacheDir: string -> name: string -> uri: Uri -> Async<Result<CachedVocab, string>>

    /// Compare recorded hash to current hash.
    /// Returns NoDrift or Drift(recorded, current).
    /// B3 only compares — it does not mutate any mappings.
    val detectDrift: recordedHash: string -> currentHash: string -> DriftResult

    /// Plain-GET vocabulary fetcher.
    /// Retired in V3 — use Frank.Semantic.RdfConneg.rdfFetch for content-negotiation,
    /// conditional requests (ETag/If-None-Match), and structured exit codes.
    [<Obsolete("Use RdfConneg.rdfFetch (via RdfConneg.makeNoRedirectClient) instead. httpFetch does not negotiate RDF content, send conditional headers, or distinguish link-rot from transient failures.")>]
    val httpFetch: client: Net.Http.HttpClient -> Fetch
