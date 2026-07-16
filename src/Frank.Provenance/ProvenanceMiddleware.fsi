namespace Frank.Provenance

open System.Threading.Tasks
open Microsoft.AspNetCore.Http
open Microsoft.Extensions.Logging

type ProvenanceMiddleware =
    new:
        next: RequestDelegate *
        config: ProvenanceConfig *
        store: IProvenanceStore *
        logger: ILogger<ProvenanceMiddleware> ->
            ProvenanceMiddleware

    member InvokeAsync: ctx: HttpContext -> Task
