namespace Frank.Builder

open Microsoft.Net.Http.Headers

[<Sealed>]
type ProducesMediaTypeMetadata(mediaType: string, ordinal: int) =
    member _.MediaType = mediaType
    member _.Ordinal = ordinal

module MediaTypeNegotiation =

    let inline isWildcard (mediaType: string) = mediaType.Contains "*"

    /// True if `candidate` (one entry from the client's Accept header) and
    /// `registered` (one representation's declared media type) match.
    ///
    /// Both directions of `MatchesMediaType` leniency are gated on the *pattern*
    /// side actually being a wildcard, because `MediaTypeHeaderValue.MatchesMediaType`
    /// is lenient about RFC 6839 structured-syntax suffixes in BOTH directions and
    /// that leniency is wrong for concrete-vs-concrete comparisons here:
    ///
    /// - First clause (wildcard *client* entry, e.g. `application/*` or `*/*`,
    ///   matching a concrete registered type) -- the common case for an absent or
    ///   catch-all Accept.
    /// - Second clause -- a concrete client entry matches a concrete registered type
    ///   only on exact (case-insensitive) equality. Without this restriction, an
    ///   Accept of `application/json` would match a registered `application/ld+json`
    ///   via suffix leniency, which silently INVERTS an explicit client preference:
    ///   `Accept: application/json;q=1, application/ld+json;q=0.5` against a block
    ///   registering only `application/ld+json` would serve JSON-LD at effective
    ///   quality 1.0 instead of 0.5, even though the client ranked JSON-LD lower.
    /// - Third clause -- gated on `registered` being a wildcard pattern, so a
    ///   catch-all `accepts "*/*"` still matches any concrete client entry. Without
    ///   that gate a concrete registered `application/json` would act as if it were
    ///   itself a pattern and match an Accept of `application/ld+json`.
    let inline matches (candidate: MediaTypeHeaderValue) (registered: string) : bool =
        let registeredValue = MediaTypeHeaderValue.Parse(registered)
        // MediaTypeHeaderValue.MediaType is a StringSegment, not a string -- render both
        // sides to plain strings so `isWildcard` and the equality check are unambiguous.
        let candidateMediaType = candidate.MediaType.ToString()
        let registeredMediaType = registeredValue.MediaType.ToString()

        (isWildcard candidateMediaType && candidate.MatchesMediaType(registeredValue.MediaType))
        || System.String.Equals(candidateMediaType, registeredMediaType, System.StringComparison.OrdinalIgnoreCase)
        || (isWildcard registered && registeredValue.MatchesMediaType(candidate.MediaType))

    /// Specificity rank of an Accept entry, most specific first: an entry with
    /// neither type nor subtype wildcarded (e.g. "text/html") outranks one with only
    /// the subtype wildcarded ("text/*"), which outranks "*/*". This -- not quality
    /// -- is what RFC 9110 §12.5.1 says determines which entry governs a given
    /// representation when more than one entry matches it.
    let inline specificity (entry: MediaTypeHeaderValue) : int =
        (if entry.MatchesAllTypes then 0 else 1) + (if entry.MatchesAllSubTypes then 0 else 1)

    /// The effective quality of `mt` under this Accept header: the Quality (defaulting
    /// to 1.0 when unspecified) of the MOST SPECIFIC parsed entry that matches `mt`,
    /// per RFC 9110 §12.5.1 -- not simply the best quality among all matching entries.
    /// This is what lets a narrow "text/html;q=0.8" override a broader "*/*;q=0" (the
    /// narrow entry wins and the representation is served), and equally lets a narrow
    /// "text/html;q=0" override a broader "*/*;q=0.5" (the narrow entry wins and the
    /// representation is rejected) -- both directions of precedence fall out of the
    /// same rule. None means no parsed entry matches `mt` at all.
    let inline effectiveQuality (parsed: MediaTypeHeaderValue list) (mt: string) : float option =
        parsed
        |> List.filter (fun entry -> matches entry mt)
        |> List.fold
            (fun best entry ->
                match best with
                | Some(bestEntry: MediaTypeHeaderValue) when specificity bestEntry >= specificity entry -> best
                | _ -> Some entry)
            None
        |> Option.map (fun entry -> if entry.Quality.HasValue then entry.Quality.Value else 1.0)

    /// Selects the index of the representation that should serve this request, given
    /// the raw Accept header values and the registered media types, in registration
    /// order. An absent, empty, or entirely unparseable Accept is treated as an
    /// implicit "*/*" -- there is no separate "default representation" concept, it
    /// falls out of ordinary wildcard matching. Once the Accept header does parse,
    /// each representation's effective quality (see `effectiveQuality`) is compared;
    /// the highest wins, ties broken by registration order; a representation whose
    /// effective quality is 0, or that no entry matches at all, is never a candidate.
    /// Returns None when no representation has a positive effective quality.
    let inline selectRepresentation (acceptValues: string seq) (mediaTypes: string list) : int option =
        if List.isEmpty mediaTypes then
            None
        else
            // A single Accept header value can itself be a comma-separated list of media
            // ranges (e.g. "text/html;q=0.3, application/json;q=0.8"), so this must use
            // ParseList rather than parsing each raw header value as one media type --
            // TryParse on a comma-joined string simply fails to parse.
            let raw: System.Collections.Generic.IList<string> = acceptValues |> Array.ofSeq :> _

            let parsed =
                match MediaTypeHeaderValue.TryParseList(raw) with
                | true, values -> values |> List.ofSeq
                | false, _ -> []

            if List.isEmpty parsed then
                let defaultEntry = MediaTypeHeaderValue.Parse("*/*")
                mediaTypes |> List.tryFindIndex (matches defaultEntry)
            else
                let candidates =
                    mediaTypes
                    |> List.indexed
                    |> List.choose (fun (idx, mt) ->
                        effectiveQuality parsed mt
                        |> Option.filter (fun q -> q > 0.0)
                        |> Option.map (fun q -> idx, q))

                match candidates with
                | [] -> None
                | first :: rest ->
                    // Highest effective quality wins; a strict ">" comparison keeps the
                    // earliest (lowest-index, i.e. first-registered) candidate on a tie.
                    rest |> List.fold (fun (bestIdx, bestQ) (idx, q) -> if q > bestQ then idx, q else bestIdx, bestQ) first
                    |> fst
                    |> Some
