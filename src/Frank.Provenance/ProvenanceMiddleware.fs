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
        ctx.Request.Headers.Accept
        |> Seq.exists (fun v ->
            v.Contains("profile=", StringComparison.OrdinalIgnoreCase)
            && v.Contains("http://www.w3.org/ns/prov", StringComparison.OrdinalIgnoreCase))

[<RequireQualifiedAccess>]
module private Capture =

    let private resolveDomainType
        (endpoint: Endpoint)
        (config: ProvenanceConfig)
        (statusCode: int)
        : (Frank.Semantic.ProvOClass * Uri) option =
        if isNull endpoint then
            None
        else
            endpoint.Metadata.GetOrderedMetadata<IProducesResponseTypeMetadata>()
            |> Seq.tryFind (fun m -> m.StatusCode = statusCode)
            |> Option.bind (fun m ->
                if isNull m.Type || m.Type = typeof<Void> then
                    None
                else
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
        let buffer = new MemoryStream()
        ctx.Response.Body <- buffer

        task {
            try
                do! next.Invoke ctx
                let ended = DateTimeOffset.UtcNow
                let record = Capture.build config ctx started ended
                store.Append record
                ctx.Response.Body <- originalBody
                ctx.Response.ContentLength <- System.Nullable()
                ctx.Response.ContentType <- "application/ld+json; profile=\"http://www.w3.org/ns/prov\""
                do! ctx.Response.WriteAsync(ProvenanceGraph.toJsonLd record)
            finally
                ctx.Response.Body <- originalBody
                buffer.Dispose()
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
