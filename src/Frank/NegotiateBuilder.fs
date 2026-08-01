namespace Frank.Builder

open System.Threading.Tasks
open Microsoft.AspNetCore.Http
open Microsoft.AspNetCore.Http.Metadata
open Microsoft.Net.Http.Headers

type NegotiateSpec =
    { Representations: (string * RequestDelegate) list
      Metadata: obj list }

    static member Empty =
        { Representations = []
          Metadata = [] }

module internal Negotiation =

    let isWildcard (mediaType: string) = mediaType.Contains "*"

    /// Guards the value-returning `accepts` overloads (`HttpContext -> Task<'a>` and
    /// `HttpContext -> Async<'a>`), which auto-format their returned value through
    /// `viaOutputFormatter mediaType`. Those overloads bypass `dispatch`'s own
    /// `isWildcard` guard on `ctx.Response.ContentType`, because `viaOutputFormatter`
    /// sets Content-Type itself, unconditionally, to whatever media type it is given --
    /// so a wildcard entry would emit an invalid `Content-Type: */*`. There is also no
    /// concrete type to hand MVC's formatter selector. Fails at registration time (when
    /// the `negotiate { }` block is built) rather than waiting for a request that
    /// happens to select the wildcard representation.
    let rejectWildcardAutoFormat (mediaType: string) =
        if isWildcard mediaType then
            failwithf
                "accepts \"%s\" cannot auto-format a value-returning handler via viaOutputFormatter -- wildcard media types have no concrete type for the formatter selector, and would emit an invalid Content-Type. Use a RequestDelegate or HttpContext -> unit handler instead."
                mediaType

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
    let matches (candidate: MediaTypeHeaderValue) (registered: string) : bool =
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
    let specificity (entry: MediaTypeHeaderValue) : int =
        (if entry.MatchesAllTypes then 0 else 1) + (if entry.MatchesAllSubTypes then 0 else 1)

    /// The effective quality of `mt` under this Accept header: the Quality (defaulting
    /// to 1.0 when unspecified) of the MOST SPECIFIC parsed entry that matches `mt`,
    /// per RFC 9110 §12.5.1 -- not simply the best quality among all matching entries.
    /// This is what lets a narrow "text/html;q=0.8" override a broader "*/*;q=0" (the
    /// narrow entry wins and the representation is served), and equally lets a narrow
    /// "text/html;q=0" override a broader "*/*;q=0.5" (the narrow entry wins and the
    /// representation is rejected) -- both directions of precedence fall out of the
    /// same rule. None means no parsed entry matches `mt` at all.
    let effectiveQuality (parsed: MediaTypeHeaderValue list) (mt: string) : float option =
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
    let selectRepresentation (acceptValues: string seq) (mediaTypes: string list) : int option =
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

    let dispatch (representations: (string * RequestDelegate) list) : RequestDelegate =
        RequestDelegate(fun ctx ->
            // RFC 9110 12.5.5: every response from a `negotiate { }` block varies by
            // Accept -- including the 406 -- so a shared cache must not reuse one
            // client's representation for another. Set before the match so it lands on
            // both the selected-representation and the 406 branch.
            ctx.Response.Headers.Append("Vary", "Accept")

            let mediaTypes = representations |> List.map fst

            match selectRepresentation ctx.Request.Headers.Accept mediaTypes with
            | Some idx ->
                let mediaType, handler = representations.[idx]

                if not (isWildcard mediaType) then
                    ctx.Response.ContentType <- mediaType

                handler.Invoke(ctx)
            | None ->
                ctx.Response.StatusCode <- StatusCodes.Status406NotAcceptable
                Task.CompletedTask)

    /// Merges `IProducesResponseTypeMetadata` entries that share both the same status code
    /// and the same response `Type` into one, unioning their content types -- the exact
    /// shape (one metadata object, several content types) that already reaches the
    /// generated OpenAPI document correctly (see `OpenApiDocumentTests.fs`'s "HandlerDefinition
    /// with custom content types for content negotiation" test). Without this, two
    /// `handler { produces ... }` representations sharing a status code (the common
    /// `negotiate { }` case: the same response type serialized as e.g. both
    /// `application/json` and `application/xml`) would emit two SEPARATE metadata objects
    /// for that status code -- and Microsoft.AspNetCore.OpenApi's own document generator
    /// keeps only the last-registered one, silently dropping the other from the generated
    /// document (verified: this reproduces with a bare ASP.NET Core minimal API, zero Frank
    /// code involved -- it's inherent framework behavior, not something Frank broke).
    ///
    /// A status/type group of exactly one is left untouched -- the original object is
    /// passed through by reference, not rebuilt as a bare `ProducesResponseTypeMetadata`.
    /// `HandlerDefinition.Metadata` is documented as an open extension point: some other
    /// `IProducesResponseTypeMetadata` implementation (from an extension library, or
    /// attached directly via `HandlerDefinition.addMetadata`) may carry data or interfaces
    /// beyond `StatusCode`/`Type`/`ContentTypes`, and nothing needs merging when it's the
    /// only entry for its status/type pair -- rebuilding it anyway would silently downgrade
    /// it to the bare three-field shape for no reason.
    ///
    /// Representations sharing a status code but declaring DIFFERENT response types are
    /// left as separate metadata objects -- Microsoft.AspNetCore.OpenApi's last-wins
    /// behavior still applies to that narrower case. A documented, accepted limitation, not
    /// fixed here.
    let mergeProducesMetadata (metadata: obj list) : obj list =
        let produces, other =
            metadata |> List.partition (fun m -> m :? IProducesResponseTypeMetadata)

        let merged =
            produces
            |> List.map (fun m -> m :?> IProducesResponseTypeMetadata)
            |> List.groupBy (fun m -> m.StatusCode, m.Type)
            |> List.map (fun ((statusCode, responseType), group) ->
                match group with
                | [ single ] -> box single
                | _ ->
                    let contentTypes =
                        group
                        |> List.collect (fun m -> m.ContentTypes |> List.ofSeq)
                        |> List.distinct
                        |> Array.ofList

                    ProducesResponseTypeMetadata(statusCode, responseType, contentTypes) :> obj)

        other @ merged

[<Sealed>]
type NegotiateBuilder() =

    member _.Yield(_) = NegotiateSpec.Empty

    member _.Run(spec: NegotiateSpec) : HandlerDefinition =
        if List.isEmpty spec.Representations then
            failwith "At least one representation must be registered using the 'accepts' operation"

        { Handler = Negotiation.dispatch spec.Representations
          Metadata = Negotiation.mergeProducesMetadata spec.Metadata }

    [<CustomOperation("accepts")>]
    member _.Accepts(spec: NegotiateSpec, mediaType: string, handler: RequestDelegate) =
        { spec with Representations = spec.Representations @ [ mediaType, handler ] }

    [<CustomOperation("accepts")>]
    member _.Accepts(spec: NegotiateSpec, mediaType: string, handler: HttpContext -> unit) =
        let producer =
            RequestDelegate(fun ctx ->
                handler ctx
                Task.CompletedTask)

        { spec with Representations = spec.Representations @ [ mediaType, producer ] }

    [<CustomOperation("accepts")>]
    member _.Accepts(spec: NegotiateSpec, mediaType: string, handlerDef: HandlerDefinition) =
        { spec with
            Representations = spec.Representations @ [ mediaType, handlerDef.Handler ]
            Metadata = spec.Metadata @ handlerDef.Metadata }

    [<CustomOperation("accepts")>]
    member _.Accepts(spec: NegotiateSpec, mediaType: string, handler: HttpContext -> Task<'a>) =
        Negotiation.rejectWildcardAutoFormat mediaType

        let producer =
            RequestDelegate(fun ctx ->
                task {
                    let! value = handler ctx
                    return! Frank.ContentNegotiation.viaOutputFormatter mediaType value ctx
                })

        { spec with Representations = spec.Representations @ [ mediaType, producer ] }

    [<CustomOperation("accepts")>]
    member _.Accepts(spec: NegotiateSpec, mediaType: string, handler: HttpContext -> Async<'a>) =
        Negotiation.rejectWildcardAutoFormat mediaType

        let producer =
            RequestDelegate(fun ctx ->
                task {
                    let! value = Async.StartAsTask(handler ctx)
                    return! Frank.ContentNegotiation.viaOutputFormatter mediaType value ctx
                })

        { spec with Representations = spec.Representations @ [ mediaType, producer ] }

    [<CustomOperation("accepts")>]
    member this.Accepts(spec: NegotiateSpec, mediaTypes: string list, handler: HttpContext -> Task<'a>) =
        mediaTypes |> List.fold (fun s mt -> this.Accepts(s, mt, handler)) spec

    [<CustomOperation("accepts")>]
    member this.Accepts(spec: NegotiateSpec, mediaTypes: string list, handler: HttpContext -> Async<'a>) =
        mediaTypes |> List.fold (fun s mt -> this.Accepts(s, mt, handler)) spec

[<AutoOpen>]
module NegotiateFunctions =
    let negotiate = NegotiateBuilder()
