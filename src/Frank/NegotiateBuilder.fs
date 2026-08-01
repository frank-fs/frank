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

    /// RFC 9110 §12.5.1: a quality value of exactly 0 means the client explicitly
    /// does NOT want this media type -- it must never be selected, unlike a merely
    /// low (but nonzero) quality value which is just deprioritized.
    let isExplicitlyRejected (entry: MediaTypeHeaderValue) : bool =
        entry.Quality.HasValue && entry.Quality.Value = 0.0

    /// Selects the index of the representation that should serve this request, given
    /// the raw Accept header values and the registered media types, in registration
    /// order. An absent, empty, or entirely unparseable Accept is treated as an
    /// implicit "*/*" -- there is no separate "default representation" concept, it
    /// falls out of ordinary wildcard matching. Returns None when nothing registered
    /// matches any (non-rejected) entry, or when the Accept header named entries but
    /// every one of them was excluded by an explicit q=0 -- that case must NOT fall
    /// back to the "*/*" default, since the client did express a preference, it just
    /// rejected everything on offer.
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
                let indexedMediaTypes = mediaTypes |> List.indexed

                // A representation matched by an explicit q=0 entry is excluded outright,
                // even if a broader entry (e.g. "*/*;q=0.5") also matches it with positive
                // quality -- an explicit rejection takes precedence over a less specific
                // positive match, it is not merely outranked by it.
                let rejectedIndices =
                    parsed
                    |> List.filter isExplicitlyRejected
                    |> List.collect (fun entry ->
                        indexedMediaTypes
                        |> List.filter (fun (_, mt) -> matches entry mt)
                        |> List.map fst)
                    |> Set.ofList

                let acceptedEntries =
                    parsed
                    |> List.filter (isExplicitlyRejected >> not)
                    |> List.sortWith (fun a b -> MediaTypeHeaderValueComparer.QualityComparer.Compare(b, a))

                acceptedEntries
                |> List.tryPick (fun entry ->
                    indexedMediaTypes
                    |> List.tryFind (fun (idx, mt) -> not (Set.contains idx rejectedIndices) && matches entry mt)
                    |> Option.map fst)

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
