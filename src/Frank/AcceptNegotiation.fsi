namespace Frank

open Microsoft.AspNetCore.Http

/// RFC 7231 / RFC 6906 Accept header negotiation helpers.
module AcceptNegotiation =

    /// Returns true iff the Accept header on ctx contains an entry for mediaType
    /// with a non-zero q-value whose profile parameter exactly equals profile.
    /// Comparison is ordinal (RFC 6906 IRIs are case-sensitive).
    val wantsProfile: ctx: HttpContext -> mediaType: string -> profile: string -> bool

    /// Appends "Accept" to the response Vary header exactly once.
    /// If "Accept" is already present (case-insensitive token match), this is a no-op.
    /// Preserves other existing Vary tokens (e.g. "Accept-Encoding").
    val appendVaryAccept: response: HttpResponse -> unit
