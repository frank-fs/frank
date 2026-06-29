namespace Frank.Provenance

open System
open System.IO
open System.Text.Json
open System.Threading.Tasks
open Microsoft.AspNetCore.Http
open Microsoft.AspNetCore.Http.Metadata
open Microsoft.Extensions.Logging
open Microsoft.Extensions.Primitives

[<RequireQualifiedAccess>]
module private ProvNegotiation =

    let requested (ctx: HttpContext) : bool =
        Frank.AcceptNegotiation.wantsProfile ctx "application/ld+json" "http://www.w3.org/ns/prov"

[<RequireQualifiedAccess>]
module private BodyCapture =

    let private isAbsoluteIri (s: string) =
        s.StartsWith("http://", StringComparison.Ordinal)
        || s.StartsWith("https://", StringComparison.Ordinal)

    let private extractFromJson (json: string) : (string * string) list =
        try
            use doc = JsonDocument.Parse json

            if doc.RootElement.ValueKind <> JsonValueKind.Object then
                []
            else
                doc.RootElement.EnumerateObject()
                |> Seq.choose (fun p ->
                    if isAbsoluteIri p.Name && p.Value.ValueKind = JsonValueKind.String then
                        Some(p.Name, p.Value.GetString())
                    else
                        None)
                |> Seq.toList
        with :? JsonException ->
            []

    // Read request body for provenance capture, then reset Position to 0 so the downstream
    // handler can read it. Must be called BEFORE next.Invoke. leaveOpen=true prevents the
    // StreamReader from disposing ctx.Request.Body.
    let readAndResetAsync (ctx: HttpContext) : Task<(string * string) list> =
        if ctx.Request.Method <> "POST" || not ctx.Request.Body.CanSeek then
            Task.FromResult []
        else
            task {
                ctx.Request.Body.Position <- 0L

                use reader =
                    new StreamReader(ctx.Request.Body, Text.Encoding.UTF8, false, 4096, true)

                let! json = reader.ReadToEndAsync()
                ctx.Request.Body.Position <- 0L
                return if String.IsNullOrEmpty json then [] else extractFromJson json
            }

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

    let absoluteUri (ctx: HttpContext) =
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
        (bodyAttrs: (string * string) list)
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
          EndedAt = ended
          BodyAttributes = bodyAttrs }

type ProvenanceMiddleware
    (next: RequestDelegate, config: ProvenanceConfig, store: IProvenanceStore, logger: ILogger<ProvenanceMiddleware>) =

    do
        if isNull (box next) then
            invalidArg (nameof next) "RequestDelegate must not be null"

        if isNull (box config) then
            invalidArg (nameof config) "ProvenanceConfig must not be null"

        if isNull (box store) then
            invalidArg (nameof store) "IProvenanceStore must not be null"

    static member private withDiscardedBody (ctx: HttpContext) (inner: unit -> Task) : Task =
        let originalBody = ctx.Response.Body
        ctx.Response.Body <- Stream.Null

        task {
            try
                do! inner ()
            finally
                ctx.Response.Body <- originalBody
        }

    member private this.InvokeWithProv(ctx: HttpContext, started: DateTimeOffset) : Task =
        task {
            let! bodyAttrs = BodyCapture.readAndResetAsync ctx
            do! ProvenanceMiddleware.withDiscardedBody ctx (fun () -> next.Invoke ctx)

            let ended = DateTimeOffset.UtcNow
            let record = Capture.build config ctx started ended bodyAttrs
            store.Append record

            if ctx.Response.HasStarted then
                logger.LogWarning(
                    "ProvenanceMiddleware: response already started for {Method} {Path}; skipping prov rewrite",
                    ctx.Request.Method,
                    ctx.Request.Path
                )
            else
                ctx.Response.ContentLength <- System.Nullable()
                ctx.Response.ContentType <- "application/ld+json; profile=\"http://www.w3.org/ns/prov\""
                let varyValue = StringValues "Accept"
                ctx.Response.Headers.Append("Vary", varyValue)
                do! ctx.Response.WriteAsync(ProvenanceGraph.toJsonLd record)
        }

    member this.InvokeAsync(ctx: HttpContext) : Task =
        let started = DateTimeOffset.UtcNow

        if ctx.Request.Method = "POST" then
            ctx.Request.EnableBuffering()

        if ProvNegotiation.requested ctx then
            this.InvokeWithProv(ctx, started)
        else
            let resourceUri = Capture.absoluteUri ctx

            let linkHeaderValue =
                StringValues(
                    $"<{resourceUri}>; rel=\"http://www.w3.org/ns/prov#has_provenance\"; type=\"application/ld+json\""
                )

            let varyValue = StringValues "Accept"
            ctx.Response.Headers.Append("Vary", varyValue)
            ctx.Response.Headers.Append("Link", linkHeaderValue)

            task {
                let! bodyAttrs = BodyCapture.readAndResetAsync ctx
                do! next.Invoke ctx
                let ended = DateTimeOffset.UtcNow
                store.Append(Capture.build config ctx started ended bodyAttrs)
            }
