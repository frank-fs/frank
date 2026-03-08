namespace Frank

open System
open System.Threading.Tasks

/// Marker metadata indicating an endpoint participates in conditional requests.
[<Sealed>]
type ETagMetadata() = class end

/// Non-generic provider that computes an ETag for a given resource instance.
type IETagProvider =
    /// Computes a strong ETag value for the resource instance identified by instanceId.
    /// Returns None if no ETag can be computed (e.g., resource not found).
    /// The returned string is the raw ETag value (without quotes); use ETagFormat to produce the wire format.
    abstract ComputeETag: instanceId: string -> Task<string option>

/// Factory that resolves an IETagProvider for a given resource type.
type IETagProviderFactory =
    /// Returns an IETagProvider for the specified resource type, or None if no provider is registered.
    abstract GetProvider: resourceType: Type -> IETagProvider option

/// RFC 9110 strong ETag formatting utilities.
/// Strong ETags are quoted strings: e.g., "abc123" on the wire is represented as \"abc123\".
module ETagFormat =

    /// Wraps a raw ETag value in double quotes per RFC 9110 strong ETag format.
    /// Example: formatStrong "abc123" returns "\"abc123\""
    let formatStrong (rawValue: string) : string =
        if isNull rawValue then
            invalidArg (nameof rawValue) "ETag raw value cannot be null."

        "\"" + rawValue + "\""

    /// Attempts to parse a strong ETag from its wire format (quoted string).
    /// Returns the inner value without quotes, or None if the format is invalid.
    /// Weak ETags (W/"...") are rejected.
    let tryParseStrong (wireValue: string) : string option =
        if isNull wireValue then
            None
        elif
            wireValue.Length >= 2
            && wireValue.[0] = '"'
            && wireValue.[wireValue.Length - 1] = '"'
        then
            Some(wireValue.Substring(1, wireValue.Length - 2))
        else
            None

    /// Returns true if the wire-format value represents a strong ETag (quoted, no W/ prefix).
    let isStrong (wireValue: string) : bool =
        if isNull wireValue then
            false
        elif wireValue.StartsWith("W/", StringComparison.Ordinal) then
            false
        else
            wireValue.Length >= 2
            && wireValue.[0] = '"'
            && wireValue.[wireValue.Length - 1] = '"'

/// ETag comparison utilities per RFC 9110 Section 8.8.3.2 (strong comparison).
module ETagComparison =

    /// Parses a comma-separated list of ETags from an If-None-Match or If-Match header value.
    /// Handles whitespace around commas and individual ETag values.
    /// Returns an array of trimmed, individual ETag wire-format strings.
    let parseMultiple (headerValue: string) : string[] =
        if isNull headerValue || String.IsNullOrWhiteSpace headerValue then
            [||]
        else
            headerValue.Split(',')
            |> Array.map (fun s -> s.Trim())
            |> Array.filter (fun s -> s.Length > 0)

    /// Returns true if the header value is the wildcard "*", indicating match-any.
    let isWildcard (headerValue: string) : bool =
        not (isNull headerValue) && headerValue.Trim() = "*"

    /// Strong comparison per RFC 9110 Section 8.8.3.2:
    /// Both must be strong ETags (no W/ prefix) and their opaque-tags must be identical.
    let strongMatch (etag1: string) (etag2: string) : bool =
        ETagFormat.isStrong etag1
        && ETagFormat.isStrong etag2
        && String.Equals(etag1, etag2, StringComparison.Ordinal)

    /// Checks whether a given ETag (in wire format) matches any ETag in a header value.
    /// Supports wildcard "*" (matches everything), single ETags, and comma-separated lists.
    /// Uses strong comparison per RFC 9110.
    let matchesAny (headerValue: string) (currentETag: string) : bool =
        if isNull headerValue || isNull currentETag then
            false
        elif isWildcard headerValue then
            true
        else
            let candidates = parseMultiple headerValue
            candidates |> Array.exists (fun candidate -> strongMatch candidate currentETag)
