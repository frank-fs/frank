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

    /// True if `candidate` (one entry from the client's Accept header) and
    /// `registered` (one representation's declared media type) match. The first
    /// clause handles a wildcard (or structured-suffix-lenient) *client* entry
    /// matching a concrete representation -- the common case. The second clause
    /// exists only so a wildcard-*registered* representation (e.g. a catch-all
    /// `accepts "*/*"`) can match a concrete client entry; it is gated on
    /// `registered` actually being a wildcard pattern. Without that gate, a
    /// concrete registered type would be treated as if it were itself a pattern
    /// via MatchesMediaType's own leniency (e.g. it would let a concrete
    /// "application/json" registration match an Accept of "application/ld+json",
    /// even though "application/json" was never meant to act as a catch-all) --
    /// that was a real defect this gate fixes; MatchesMediaType's leniency in the
    /// *other* direction (a client Accept of "application/json" matching a
    /// registered "application/ld+json") is intentional BCL behavior for RFC 6839
    /// structured-syntax suffixes and is left alone.
    let matches (candidate: MediaTypeHeaderValue) (registered: string) : bool =
        let registeredValue = MediaTypeHeaderValue.Parse(registered)

        candidate.MatchesMediaType(registeredValue.MediaType)
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
        let producer =
            RequestDelegate(fun ctx ->
                task {
                    let! value = handler ctx
                    return! Frank.ContentNegotiation.viaOutputFormatter mediaType value ctx
                })

        { spec with Representations = spec.Representations @ [ mediaType, producer ] }

    [<CustomOperation("accepts")>]
    member _.Accepts(spec: NegotiateSpec, mediaType: string, handler: HttpContext -> Async<'a>) =
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
