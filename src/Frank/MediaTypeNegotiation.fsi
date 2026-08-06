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

/// RFC 9110 §12.5.1 media-type matching and quality-value selection, shared
/// between `NegotiateBuilder` (today's dispatch, pending removal) and
/// `FrankProducesMatcherPolicy` (routing-layer dispatch). Pure functions --
/// no `HttpContext` dependency -- so both a request-time policy and a
/// unit test can call them directly.
module MediaTypeNegotiation =

    val inline isWildcard: mediaType: string -> bool

    val inline matches: candidate: MediaTypeHeaderValue -> registered: string -> bool

    val inline specificity: entry: MediaTypeHeaderValue -> int

    val inline effectiveQuality: parsed: MediaTypeHeaderValue list -> mt: string -> float option

    /// Selects the index of the representation that should serve this request,
    /// given the raw Accept header values and the registered media types, in
    /// registration order. See `NegotiateBuilder.fs`'s original doc comment
    /// (git history) for the full RFC 9110 rationale -- behavior is unchanged
    /// from the pre-extraction implementation.
    val inline selectRepresentation: acceptValues: string seq -> mediaTypes: string list -> int option
