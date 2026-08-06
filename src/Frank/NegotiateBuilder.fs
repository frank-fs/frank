namespace Frank.Builder

open System.Threading.Tasks
open Microsoft.AspNetCore.Http
open Microsoft.AspNetCore.Http.Metadata

type NegotiateSpec =
    { Representations: (string * RequestDelegate * obj list) list }

    static member Empty = { Representations = [] }

/// CE-specific helpers for `NegotiateBuilder` -- not raw RFC 9110 matching logic
/// (that lives in `MediaTypeNegotiation`, shared with `FrankProducesMatcherPolicy`).
/// Both members here run once, when a `negotiate { }` block is constructed at
/// startup, not once per request.
module internal Negotiation =

    /// Guards the value-returning `accepts` overloads (`HttpContext -> Task<'a>` and
    /// `HttpContext -> Async<'a>`), which auto-format their returned value through
    /// `viaOutputFormatter mediaType`. Those overloads bypass the routing-layer
    /// dispatch's own wildcard guard on `ctx.Response.ContentType`, because
    /// `viaOutputFormatter` sets Content-Type itself, unconditionally, to whatever
    /// media type it is given -- so a wildcard entry would emit an invalid
    /// `Content-Type: */*`. There is also no concrete type to hand MVC's formatter
    /// selector. Fails at registration time (when the `negotiate { }` block is
    /// built) rather than waiting for a request that happens to select the
    /// wildcard representation.
    let rejectWildcardAutoFormat (mediaType: string) =
        if MediaTypeNegotiation.isWildcard mediaType then
            failwithf
                "accepts \"%s\" cannot auto-format a value-returning handler via viaOutputFormatter -- wildcard media types have no concrete type for the formatter selector, and would emit an invalid Content-Type. Use a RequestDelegate or HttpContext -> unit handler instead."
                mediaType

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

    member _.Run(spec: NegotiateSpec) : HandlerDefinition list =
        if List.isEmpty spec.Representations then
            failwith "At least one representation must be registered using the 'accepts' operation"

        let allOwnMetadata =
            spec.Representations |> List.collect (fun (_, _, m) -> m)

        let mergedMetadata = Negotiation.mergeProducesMetadata allOwnMetadata

        spec.Representations
        |> List.mapi (fun ordinal (mediaType, handler, _) ->
            { Handler = handler
              Metadata = (ProducesMediaTypeMetadata(mediaType, ordinal) :> obj) :: mergedMetadata })

    [<CustomOperation("accepts")>]
    member _.Accepts(spec: NegotiateSpec, mediaType: string, handler: RequestDelegate) =
        { spec with Representations = spec.Representations @ [ mediaType, handler, [] ] }

    [<CustomOperation("accepts")>]
    member _.Accepts(spec: NegotiateSpec, mediaType: string, handler: HttpContext -> unit) =
        let producer =
            RequestDelegate(fun ctx ->
                handler ctx
                Task.CompletedTask)

        { spec with Representations = spec.Representations @ [ mediaType, producer, [] ] }

    [<CustomOperation("accepts")>]
    member _.Accepts(spec: NegotiateSpec, mediaType: string, handlerDef: HandlerDefinition) =
        { spec with
            Representations = spec.Representations @ [ mediaType, handlerDef.Handler, handlerDef.Metadata ] }

    /// A `Task<unit>`-returning handler -- what an ordinary `task { ... }` computation
    /// expression with no `return` infers as -- is self-writing, the async counterpart
    /// of the `HttpContext -> unit` overload above, NOT a value-returning handler whose
    /// `unit` "value" should be handed to `viaOutputFormatter`. This overload exists
    /// specifically so F#'s overload resolution has a non-generic, exact match to prefer
    /// over `HttpContext -> Task<'a>` below for this shape -- without it, F# prefers the
    /// generic `Task<'a>` overload (a direct match requiring no delegate conversion) over
    /// the `RequestDelegate` overload, silently routing a self-writing handler through
    /// `viaOutputFormatter`, which then throws when it tries to set `ContentType` after
    /// the handler has already started the response (frank-fs/frank#492).
    [<CustomOperation("accepts")>]
    member _.Accepts(spec: NegotiateSpec, mediaType: string, handler: HttpContext -> Task<unit>) =
        let producer = RequestDelegate(fun ctx -> handler ctx :> Task)
        { spec with Representations = spec.Representations @ [ mediaType, producer, [] ] }

    [<CustomOperation("accepts")>]
    member _.Accepts(spec: NegotiateSpec, mediaType: string, handler: HttpContext -> Task<'a>) =
        Negotiation.rejectWildcardAutoFormat mediaType

        let producer =
            RequestDelegate(fun ctx ->
                task {
                    let! value = handler ctx
                    return! Frank.ContentNegotiation.viaOutputFormatter mediaType value ctx
                })

        { spec with Representations = spec.Representations @ [ mediaType, producer, [] ] }

    [<CustomOperation("accepts")>]
    member _.Accepts(spec: NegotiateSpec, mediaType: string, handler: HttpContext -> Async<'a>) =
        Negotiation.rejectWildcardAutoFormat mediaType

        let producer =
            RequestDelegate(fun ctx ->
                task {
                    let! value = Async.StartAsTask(handler ctx)
                    return! Frank.ContentNegotiation.viaOutputFormatter mediaType value ctx
                })

        { spec with Representations = spec.Representations @ [ mediaType, producer, [] ] }

    [<CustomOperation("accepts")>]
    member this.Accepts(spec: NegotiateSpec, mediaTypes: string list, handler: HttpContext -> Task<'a>) =
        mediaTypes |> List.fold (fun s mt -> this.Accepts(s, mt, handler)) spec

    [<CustomOperation("accepts")>]
    member this.Accepts(spec: NegotiateSpec, mediaTypes: string list, handler: HttpContext -> Async<'a>) =
        mediaTypes |> List.fold (fun s mt -> this.Accepts(s, mt, handler)) spec

[<AutoOpen>]
module NegotiateFunctions =
    let negotiate = NegotiateBuilder()
