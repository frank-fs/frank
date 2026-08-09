namespace Frank.Builder

open Microsoft.Net.Http.Headers

/// One representation's declared media type, paired with its position among
/// its siblings for tie-breaking. Read by `FrankProducesMatcherPolicy`, written by
/// `NegotiateBuilder.Run` -- one instance per representation's own `RouteEndpoint`.
[<Sealed>]
type ProducesMediaTypeMetadata =
    new: mediaType: string * ordinal: int -> ProducesMediaTypeMetadata
    member MediaType: string
    member Ordinal: int

/// RFC 9110 §12.5.1 media-type matching and quality-value selection. Pure
/// functions -- no `HttpContext` dependency -- so a request-time policy and a
/// unit test can call them alike. `FrankProducesMatcherPolicy` uses
/// `effectiveQuality` (and `isWildcard`) to pick a representation's endpoint
/// per request at the routing layer; `NegotiateBuilder` uses only `isWildcard`,
/// at startup, to reject a wildcard paired with a value-returning handler.
module MediaTypeNegotiation =

    val inline isWildcard: mediaType: string -> bool

    val inline matches: candidate: MediaTypeHeaderValue -> registered: string -> bool

    val inline specificity: entry: MediaTypeHeaderValue -> int

    val effectiveQuality: parsed: MediaTypeHeaderValue list -> mt: string -> float option

    /// Selects the index of the representation that should serve this request,
    /// given the raw Accept header values and the registered media types, in
    /// registration order. See `NegotiateBuilder.fs`'s original doc comment
    /// (git history) for the full RFC 9110 rationale -- behavior is unchanged
    /// from the pre-extraction implementation.
    val selectRepresentation: acceptValues: string seq -> mediaTypes: string list -> int option
