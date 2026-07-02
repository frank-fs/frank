module Frank.Provenance.ProvenanceEndpoint

open System.Threading.Tasks
open Microsoft.AspNetCore.Http
open Microsoft.Extensions.Primitives

let handle (store: IProvenanceStore) (config: ProvenanceConfig) (ctx: HttpContext) : Task =
    if isNull (box store) then
        invalidArg (nameof store) "store must not be null"

    if isNull ctx then
        invalidArg (nameof ctx) "HttpContext must not be null"

    let resource = ctx.Request.Query.["resource"]

    if StringValues.IsNullOrEmpty resource then
        Frank.ProblemJson.write
            ctx
            400
            "https://frankfs.dev/problems/missing-parameter"
            "Missing required query parameter"
            "provenance query requires a 'resource' parameter"
    else
        match Frank.OriginValidation.tryValidateOrigin ctx.Request with
        | None ->
            ctx.Response.StatusCode <- 400
            Task.CompletedTask
        | Some origin ->
            task {
                let rawResource = resource.ToString()

                let resolvedResource =
                    if rawResource.StartsWith("/") then
                        origin + rawResource
                    else
                        rawResource

                let! records = store.QueryByResource(resolvedResource)
                let g = ProvenanceGraph.buildMergedGraph records
                let extraCtx = ProvenanceGraph.usedPrefixContext config.DeclaredPrefixes g
                ctx.Response.StatusCode <- 200
                ctx.Response.ContentType <- "application/ld+json"
                do! ctx.Response.WriteAsync(ProvenanceGraph.compactGraph extraCtx g)
            }
