namespace Frank

open Microsoft.AspNetCore.Http
open Microsoft.Net.Http.Headers

/// RFC 7231 / RFC 6906 Accept header negotiation helpers.
module AcceptNegotiation =

    /// Strip surrounding double-quotes from a parameter value, if present.
    let private unquote (s: string) =
        if s.Length >= 2 && s.[0] = '"' && s.[s.Length - 1] = '"' then
            s.[1 .. s.Length - 2]
        else
            s

    /// Returns true iff the Accept header on ctx contains an entry for mediaType
    /// with a non-zero q-value whose profile parameter exactly equals profile.
    /// Comparison is ordinal (RFC 6906 IRIs are case-sensitive).
    let wantsProfile (ctx: HttpContext) (mediaType: string) (profile: string) : bool =
        let raw = ctx.Request.Headers.Accept.ToString()

        if System.String.IsNullOrEmpty raw then
            false
        else
            let entries =
                MediaTypeHeaderValue.ParseList(System.Collections.Generic.List<string>([ raw ]))

            let matchesMediaType (e: MediaTypeHeaderValue) =
                System.String.Equals(e.Type.Value, mediaType.Split('/')[0], System.StringComparison.OrdinalIgnoreCase)
                && System.String.Equals(
                    e.SubType.Value,
                    mediaType.Split('/')[1],
                    System.StringComparison.OrdinalIgnoreCase
                )

            let hasExactProfile (e: MediaTypeHeaderValue) =
                e.Parameters
                |> Seq.exists (fun p ->
                    System.String.Equals(p.Name.Value, "profile", System.StringComparison.OrdinalIgnoreCase)
                    && unquote p.Value.Value = profile)

            let isNonZeroQ (e: MediaTypeHeaderValue) =
                not e.Quality.HasValue || e.Quality.Value > 0.0

            entries
            |> Seq.exists (fun e -> matchesMediaType e && hasExactProfile e && isNonZeroQ e)
