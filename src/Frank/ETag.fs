namespace Frank

open System
open System.Security.Cryptography
open System.Threading.Tasks
open Microsoft.AspNetCore.Http
open Microsoft.AspNetCore.Routing

/// Marker metadata indicating an endpoint participates in conditional requests.
/// Carries the provider key and a function to resolve the instance ID from the request context.
[<Sealed>]
type ETagMetadata(providerKey: string, instanceIdResolver: HttpContext -> string) =
    /// The key used to look up the IETagProvider for this endpoint.
    member _.ProviderKey = providerKey

    /// Resolves the instance identifier from the current HTTP context.
    member _.ResolveInstanceId(ctx) = instanceIdResolver ctx

/// Non-generic provider that computes an ETag for a given resource instance.
type IETagProvider =
    /// Computes a strong ETag value for the resource instance identified by instanceId.
    /// Returns None if no ETag can be computed (e.g., resource not found).
    /// The returned string is the raw ETag value (without quotes); use ETagFormat to produce the wire format.
    abstract ComputeETag: instanceId: string -> Task<string option>

/// Factory that resolves an IETagProvider for a given endpoint.
type IETagProviderFactory =
    /// Returns an IETagProvider for the specified endpoint, or None if no provider is registered.
    abstract CreateProvider: endpoint: Endpoint -> IETagProvider option

/// RFC 9110 strong ETag formatting utilities.
/// Strong ETags are quoted strings: e.g., "abc123" on the wire is represented as \"abc123\".
module ETagFormat =

    /// Wraps a raw ETag value in double quotes per RFC 9110 strong ETag format.
    /// Example: quote "abc123" returns "\"abc123\""
    let quote (rawValue: string) : string =
        if isNull rawValue then
            invalidArg (nameof rawValue) "ETag raw value cannot be null."

        "\"" + rawValue + "\""

    /// Extracts the inner value from a quoted strong ETag wire format.
    /// Returns None if the format is invalid or the ETag is weak.
    let unquote (wireValue: string) : string option =
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

    /// Returns true if the wire-format value represents a weak ETag (W/"..." prefix).
    let isWeak (wireValue: string) : bool =
        if isNull wireValue then
            false
        else
            wireValue.StartsWith("W/\"", StringComparison.Ordinal)
            && wireValue.Length >= 4
            && wireValue.[wireValue.Length - 1] = '"'

    /// Computes a strong ETag value from raw bytes using SHA-256, truncated to 128 bits (32 hex chars).
    let computeFromBytes (data: byte[]) : string =
        let hash = SHA256.HashData(data)
        let truncated = hash.AsSpan(0, 16)
        Convert.ToHexString(truncated).ToLowerInvariant()

/// ETag comparison utilities per RFC 9110 Section 8.8.3.2 (strong comparison).
module ETagComparison =

    /// Strong comparison per RFC 9110 Section 8.8.3.2:
    /// Both must be strong ETags (no W/ prefix) and their opaque-tags must be identical.
    let strongMatch (etag1: string) (etag2: string) : bool =
        ETagFormat.isStrong etag1
        && ETagFormat.isStrong etag2
        && String.Equals(etag1, etag2, StringComparison.Ordinal)

    /// Parses a comma-separated list of ETags from an If-None-Match header value.
    /// Handles whitespace around commas and individual ETag values.
    /// Returns a list of trimmed, individual ETag wire-format strings.
    let parseIfNoneMatch (headerValue: string) : string list =
        if isNull headerValue || String.IsNullOrWhiteSpace headerValue then
            []
        else
            headerValue.Split(',')
            |> Array.map (fun s -> s.Trim())
            |> Array.filter (fun s -> s.Length > 0)
            |> Array.toList

    /// Parses a comma-separated list of ETags from an If-Match header value.
    /// Same behavior as parseIfNoneMatch.
    let parseIfMatch (headerValue: string) : string list = parseIfNoneMatch headerValue

    /// Checks whether a given current ETag matches any ETag in a header value.
    /// When currentETag is None, always returns false (even for wildcard).
    /// When headerValue is "*", returns true if currentETag is Some.
    /// Uses strong comparison per RFC 9110.
    let anyMatch (currentETag: string option) (headerValue: string) : bool =
        match currentETag with
        | None -> false
        | Some current ->
            if isNull headerValue then
                false
            elif headerValue.Trim() = "*" then
                true
            else
                let candidates = parseIfNoneMatch headerValue
                candidates |> List.exists (fun candidate -> strongMatch candidate current)
