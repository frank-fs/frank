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

    // AC3/security: prefix check alone accepts malformed IRIs (e.g. "http://[invalid") that
    // pass into UriFactory.Create and throw UriFormatException → 500. Use Uri.TryCreate instead.
    let private isAbsoluteIri (logger: ILogger) (s: string) =
        let mutable uri = Unchecked.defaultof<Uri>

        if Uri.TryCreate(s, UriKind.Absolute, &uri) then
            true
        else
            logger.LogWarning("ProvenanceMiddleware: dropping body key '{Key}' — not a valid absolute IRI", s)
            false

    let private extractFromJson (logger: ILogger) (json: string) : (string * string) list =
        try
            use doc = JsonDocument.Parse json

            if doc.RootElement.ValueKind <> JsonValueKind.Object then
                []
            else
                doc.RootElement.EnumerateObject()
                |> Seq.choose (fun p ->
                    if isAbsoluteIri logger p.Name && p.Value.ValueKind = JsonValueKind.String then
                        Some(p.Name, p.Value.GetString())
                    else
                        None)
                |> Seq.toList
        with :? JsonException ->
            []

    let isBodyBearing (method: string) =
        method = "POST" || method = "PUT" || method = "PATCH"

    // Read request body for provenance capture, then reset Position to 0 so the downstream
    // handler can read it. Must be called BEFORE next.Invoke. leaveOpen=true prevents the
    // StreamReader from disposing ctx.Request.Body.
    let readAndResetAsync (ctx: HttpContext) (logger: ILogger) : Task<(string * string) list> =
        if not (isBodyBearing ctx.Request.Method) || not ctx.Request.Body.CanSeek then
            Task.FromResult []
        else
            task {
                ctx.Request.Body.Position <- 0L

                use reader =
                    new StreamReader(ctx.Request.Body, Text.Encoding.UTF8, false, 4096, true)

                let! json = reader.ReadToEndAsync()
                ctx.Request.Body.Position <- 0L

                return
                    if String.IsNullOrEmpty json then
                        []
                    else
                        extractFromJson logger json
            }

[<RequireQualifiedAccess>]
module private Capture =

    // Prefer an entry whose Type is non-null / non-sentinel; first-match-by-metadata-order is the contract
    // when multiple entries share a status code and all have usable types.
    let private sentinelTypes = [| typeof<Void>; typeof<unit>; typeof<obj> |]

    let private isSentinel (t: Type) =
        sentinelTypes |> Array.exists (fun s -> s = t)

    let private lookupProvClass (config: ProvenanceConfig) (t: Type) : (Frank.Semantic.ProvOClass * Uri) option =
        let key = t.FullName.Replace('+', '.')

        Map.tryFind key config.ProvClasses
        |> Option.bind (fun (cls, iriOpt) -> iriOpt |> Option.map (fun iri -> cls, iri))

    // A prov:Activity represents the action performed (the request), so type it from the
    // request type (IAcceptsMetadata) when present — not from the response type.
    let private resolveFromAccepts
        (endpoint: Endpoint)
        (config: ProvenanceConfig)
        : (Frank.Semantic.ProvOClass * Uri) option =
        endpoint.Metadata.GetOrderedMetadata<IAcceptsMetadata>()
        |> Seq.tryFind (fun m -> not (isNull m.RequestType) && not (isSentinel m.RequestType))
        |> Option.bind (fun m -> lookupProvClass config m.RequestType)

    let private resolveFromProduces
        (endpoint: Endpoint)
        (config: ProvenanceConfig)
        (statusCode: int)
        : (Frank.Semantic.ProvOClass * Uri) option =
        endpoint.Metadata.GetOrderedMetadata<IProducesResponseTypeMetadata>()
        |> Seq.filter (fun m -> m.StatusCode = statusCode)
        |> Seq.tryFind (fun m -> not (isNull m.Type) && not (isSentinel m.Type))
        |> Option.bind (fun m -> lookupProvClass config m.Type)

    let private resolveDomainType
        (endpoint: Endpoint)
        (config: ProvenanceConfig)
        (statusCode: int)
        : (Frank.Semantic.ProvOClass * Uri) option =
        if isNull endpoint then
            None
        else
            resolveFromAccepts endpoint config
            |> Option.orElseWith (fun () -> resolveFromProduces endpoint config statusCode)

    let absoluteUri (ctx: HttpContext) =
        ctx.Request.Scheme + "://" + ctx.Request.Host.Value + ctx.Request.Path.Value

    let private origin (ctx: HttpContext) =
        ctx.Request.Scheme + "://" + ctx.Request.Host.Value

    // AC4: if a body attribute's property IRI has a class range (app-owned vocab term),
    // convert the raw string value to a URI node by resolving it against the class namespace.
    // E.g. "/tictactoe#square" → class ns "/tictactoe#" → "TopLeft" → IRI "origin/tictactoe#TopLeft".
    // Origin is already validated at the middleware edge — no per-value re-check needed.
    let private toBodyAttrValue
        (originStr: string)
        (classRanges: Map<string, string>)
        (iri: string)
        (rawValue: string)
        : BodyAttributeValue =
        let mutable uri = Unchecked.defaultof<Uri>

        if not (Uri.TryCreate(iri, UriKind.Absolute, &uri)) then
            Literal rawValue
        else
            let relPath = uri.AbsolutePath + uri.Fragment

            match Map.tryFind relPath classRanges with
            | None -> Literal rawValue
            | Some classNs -> IriNode(originStr + classNs + rawValue)

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
        let originStr = origin ctx

        { Id = "urn:uuid:" + Guid.NewGuid().ToString()
          ResourceUri = absoluteUri ctx
          HttpMethod = ctx.Request.Method
          StatusCode = ctx.Response.StatusCode
          DomainType = domainType
          Agent = resolveAgent ctx
          StartedAt = started
          EndedAt = ended
          BodyAttributes =
            bodyAttrs
            |> List.map (fun (iri, rawValue) -> iri, toBodyAttrValue originStr config.PropertyClassRanges iri rawValue) }


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

    member private this.InvokeWithProv
        (ctx: HttpContext, started: DateTimeOffset, bodyAttrs: (string * string) list)
        : Task =
        task {
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

    member private this.InvokeNonProv
        (ctx: HttpContext, started: DateTimeOffset, bodyAttrs: (string * string) list)
        : Task =
        let resourceUri = Capture.absoluteUri ctx

        // PROV-AQ §4.1: target = provenance document, anchor = described resource.
        // The type= param is not defined by PROV-AQ and was removed (it was misleading).
        let provenanceUri =
            ctx.Request.Scheme
            + "://"
            + ctx.Request.Host.Value
            + "/provenance?resource="
            + Uri.EscapeDataString resourceUri

        let linkHeaderValue =
            StringValues(
                $"<{provenanceUri}>; rel=\"http://www.w3.org/ns/prov#has_provenance\"; anchor=\"{resourceUri}\""
            )

        ctx.Response.Headers.Append("Vary", StringValues "Accept")
        ctx.Response.Headers.Append("Link", linkHeaderValue)

        task {
            do! next.Invoke ctx
            let ended = DateTimeOffset.UtcNow
            store.Append(Capture.build config ctx started ended bodyAttrs)
        }

    member private this.InvokeCore(ctx: HttpContext) : Task =
        let started = DateTimeOffset.UtcNow

        if BodyCapture.isBodyBearing ctx.Request.Method then
            Frank.RequestBodyBuffer.enable config.MaxBodyBytes ctx.Request

        task {
            let! bodyAttrsOpt =
                task {
                    try
                        let! attrs = BodyCapture.readAndResetAsync ctx (logger :> ILogger)
                        return Some attrs
                    with :? System.IO.IOException ->
                        return None
                }

            match bodyAttrsOpt with
            | None -> do! Frank.RequestBodyBuffer.respond413 ctx
            | Some bodyAttrs ->
                if ProvNegotiation.requested ctx then
                    do! this.InvokeWithProv(ctx, started, bodyAttrs)
                else
                    do! this.InvokeNonProv(ctx, started, bodyAttrs)
        }

    member this.InvokeAsync(ctx: HttpContext) : Task =
        match Frank.OriginValidation.tryValidateOrigin ctx.Request with
        | None ->
            logger.LogWarning(
                "ProvenanceMiddleware: malformed Host header '{Host}' — cannot mint resource IRIs, rejecting with 400",
                ctx.Request.Host.Value
            )

            ctx.Response.StatusCode <- 400
            Task.CompletedTask
        | Some _ -> this.InvokeCore(ctx)
