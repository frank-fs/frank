namespace Frank.Builder

open System.Threading.Tasks
open Microsoft.AspNetCore.Http
open Microsoft.Net.Http.Headers

type NegotiateSpec =
    { Representations: (string * RequestDelegate) list
      Metadata: obj list }

    static member Empty =
        { Representations = []
          Metadata = [] }

module internal Negotiation =

    let isWildcard (mediaType: string) =
        mediaType = "*/*" || mediaType.EndsWith("/*")

    /// True if `candidate` (one entry from the client's Accept header) and
    /// `registered` (one representation's declared media type) match, honoring a
    /// wildcard on either side -- a wildcard client entry matching a concrete
    /// representation is the common case; a wildcard *registered* representation
    /// matching a concrete client entry is what makes a catch-all `accepts "*/*"`
    /// work. MatchesMediaType only interprets wildcards on the receiver, not its
    /// StringSegment argument, so checking both directions is what makes the match
    /// symmetric.
    let matches (candidate: MediaTypeHeaderValue) (registered: string) : bool =
        let registeredValue = MediaTypeHeaderValue.Parse(registered)
        candidate.MatchesMediaType(registeredValue.MediaType) || registeredValue.MatchesMediaType(candidate.MediaType)

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

[<Sealed>]
type NegotiateBuilder() =

    member _.Yield(_) = NegotiateSpec.Empty

    member _.Run(spec: NegotiateSpec) : HandlerDefinition =
        if List.isEmpty spec.Representations then
            failwith "At least one representation must be registered using the 'accepts' operation"

        { Handler = Negotiation.dispatch spec.Representations
          Metadata = spec.Metadata }

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

[<AutoOpen>]
module NegotiateFunctions =
    let negotiate = NegotiateBuilder()
