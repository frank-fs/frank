namespace Frank.Provenance

open System
open System.IO
open System.Threading.Tasks
open Microsoft.AspNetCore.Http
open Microsoft.AspNetCore.Http.Metadata
open Microsoft.Extensions.Logging

[<RequireQualifiedAccess>]
module private ProvNegotiation =

    let requested (ctx: HttpContext) : bool =
        Frank.AcceptNegotiation.wantsProfile ctx "application/ld+json" "http://www.w3.org/ns/prov"

[<RequireQualifiedAccess>]
module private Capture =

    // Prefer an entry whose Type is non-null / non-sentinel; first-match-by-metadata-order is the contract
    // when multiple entries share a status code and all have usable types.
    let private sentinelTypes = [| typeof<Void>; typeof<unit>; typeof<obj> |]

    let private isSentinel (t: Type) =
        sentinelTypes |> Array.exists (fun s -> s = t)

    let private resolveDomainType
        (endpoint: Endpoint)
        (config: ProvenanceConfig)
        (statusCode: int)
        : (Frank.Semantic.ProvOClass * Uri) option =
        if isNull endpoint then
            None
        else
            endpoint.Metadata.GetOrderedMetadata<IProducesResponseTypeMetadata>()
            |> Seq.filter (fun m -> m.StatusCode = statusCode)
            |> Seq.tryFind (fun m -> not (isNull m.Type) && not (isSentinel m.Type))
            |> Option.bind (fun m ->
                let key = m.Type.FullName.Replace('+', '.')
                Map.tryFind key config.ProvClasses)
            |> Option.bind (fun (cls, iriOpt) -> iriOpt |> Option.map (fun iri -> cls, iri))

    let private absoluteUri (ctx: HttpContext) =
        ctx.Request.Scheme + "://" + ctx.Request.Host.Value + ctx.Request.Path.Value

    let private resolveAgent (ctx: HttpContext) : ProvAgent =
        let name =
            if not (isNull ctx.User) && not (isNull ctx.User.Identity) then
                let n = ctx.User.Identity.Name
                if String.IsNullOrEmpty n then "anonymous" else n
            else
                "anonymous"

        let id =
            ctx.Request.Scheme
            + "://"
            + ctx.Request.Host.Value
            + "/agents/"
            + Uri.EscapeDataString name

        { Id = id; Label = Some name }

    let build
        (config: ProvenanceConfig)
        (ctx: HttpContext)
        (started: DateTimeOffset)
        (ended: DateTimeOffset)
        : ProvenanceRecord =
        let endpoint = ctx.GetEndpoint()
        let domainType = resolveDomainType endpoint config ctx.Response.StatusCode

        { Id = "urn:uuid:" + Guid.NewGuid().ToString()
          ResourceUri = absoluteUri ctx
          HttpMethod = ctx.Request.Method
          StatusCode = ctx.Response.StatusCode
          DomainType = domainType
          Agent = resolveAgent ctx
          StartedAt = started
          EndedAt = ended }

type ProvenanceMiddleware
    (next: RequestDelegate, config: ProvenanceConfig, store: IProvenanceStore, logger: ILogger<ProvenanceMiddleware>) =

    do
        if isNull (box config) then
            invalidArg (nameof config) "ProvenanceConfig must not be null"

        if isNull (box store) then
            invalidArg (nameof store) "IProvenanceStore must not be null"

    member private _.InvokeWithProv(ctx: HttpContext, started: DateTimeOffset) : Task =
        let originalBody = ctx.Response.Body
        ctx.Response.Body <- Stream.Null

        task {
            try
                do! next.Invoke ctx
                let ended = DateTimeOffset.UtcNow
                let record = Capture.build config ctx started ended
                store.Append record
                ctx.Response.Body <- originalBody

                if ctx.Response.HasStarted then
                    logger.LogWarning(
                        "ProvenanceMiddleware: response already started for {Method} {Path}; skipping prov rewrite",
                        ctx.Request.Method,
                        ctx.Request.Path
                    )
                else
                    ctx.Response.ContentLength <- System.Nullable()
                    ctx.Response.ContentType <- "application/ld+json; profile=\"http://www.w3.org/ns/prov\""
                    do! ctx.Response.WriteAsync(ProvenanceGraph.toJsonLd record)
            finally
                ctx.Response.Body <- originalBody
        }

    member this.InvokeAsync(ctx: HttpContext) : Task =
        let started = DateTimeOffset.UtcNow

        if ProvNegotiation.requested ctx then
            this.InvokeWithProv(ctx, started)
        else
            task {
                do! next.Invoke ctx
                let ended = DateTimeOffset.UtcNow
                store.Append(Capture.build config ctx started ended)
            }
