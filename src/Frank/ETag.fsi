namespace Frank

open System.Threading.Tasks
open Microsoft.AspNetCore.Http

/// Context passed to an ETagMetadata's `compute` closure -- carries the resolved instance id
/// AND the full HttpContext, so a handler-adjacent compute function can read route values,
/// query strings, or anything else the endpoint itself would use to build its response,
/// without re-deriving the endpoint's own dispatch logic in a separate implementation (#426).
type ETagContext =
    { InstanceId: string
      HttpContext: HttpContext }

/// Marker metadata indicating an endpoint participates in conditional requests. Carries a
/// function to resolve the instance ID from the request context, and the `compute` closure
/// that produces the resource's current ETag (raw, unquoted) given an ETagContext. Metadata
/// presence on the endpoint IS the capability check -- there is no separate provider lookup.
/// `compute` typically closes over whatever the endpoint needs (a store, a graph-builder,
/// etc.) and resolves any Scoped services from `ctx.HttpContext.RequestServices` inside the
/// closure body, at call time -- nothing is captured at DI-construction time (#426).
[<Sealed>]
type ETagMetadata =
    new: instanceIdResolver: (HttpContext -> string) * compute: (ETagContext -> Task<string option>) -> ETagMetadata

    /// Resolves the instance identifier from the current HTTP context.
    member ResolveInstanceId: ctx: HttpContext -> string

    /// Computes the resource's current ETag (raw, unquoted) for the given context.
    /// Returns None if no ETag can be computed (e.g., resource not found).
    member Compute: (ETagContext -> Task<string option>)

/// RFC 9110 strong ETag formatting utilities.
/// Strong ETags are quoted strings: e.g., "abc123" on the wire is represented as \"abc123\".
module ETagFormat =

    /// Wraps a raw ETag value in double quotes per RFC 9110 strong ETag format.
    /// Example: quote "abc123" returns "\"abc123\""
    val quote: rawValue: string -> string

    /// Extracts the inner value from a quoted strong ETag wire format.
    /// Returns None if the format is invalid or the ETag is weak.
    val unquote: wireValue: string -> string option

    /// Returns true if the wire-format value represents a strong ETag (quoted, no W/ prefix).
    val isStrong: wireValue: string -> bool

    /// Returns true if the wire-format value represents a weak ETag (W/"..." prefix).
    val isWeak: wireValue: string -> bool

    /// Computes a strong ETag value from raw bytes using SHA-256, truncated to 128 bits (32 hex chars).
    val computeFromBytes: data: byte[] -> string

/// ETag comparison utilities per RFC 9110 Section 8.8.3.2 (strong comparison).
module ETagComparison =

    /// Strong comparison per RFC 9110 Section 8.8.3.2:
    /// Both must be strong ETags (no W/ prefix) and their opaque-tags must be identical.
    val strongMatch: etag1: string -> etag2: string -> bool

    /// Parses a comma-separated list of ETags from an If-None-Match header value.
    /// Handles whitespace around commas and individual ETag values.
    /// Returns a list of trimmed, individual ETag wire-format strings.
    val parseIfNoneMatch: headerValue: string -> string list

    /// Parses a comma-separated list of ETags from an If-Match header value.
    /// Same behavior as parseIfNoneMatch.
    val parseIfMatch: headerValue: string -> string list

    /// Checks whether a given current ETag matches any ETag in a header value.
    /// When currentETag is None, always returns false (even for wildcard).
    /// When headerValue is "*", returns true if currentETag is Some.
    /// Uses strong comparison per RFC 9110.
    val anyMatch: currentETag: string option -> headerValue: string -> bool
