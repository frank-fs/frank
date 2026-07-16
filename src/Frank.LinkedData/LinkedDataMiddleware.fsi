namespace Frank.LinkedData

open System.Threading.Tasks
open Microsoft.AspNetCore.Http
open Microsoft.Extensions.Logging

/// Content-negotiation middleware serving per-endpoint RDF graphs in multiple
/// representations: application/ld+json, text/turtle, application/rdf+xml.
/// Only fires for GET/HEAD (safe-method guard) on endpoints that carry a
/// LinkedDataConfig in their metadata. All other requests pass through.
type LinkedDataMiddleware =
    new: next: RequestDelegate * logger: ILogger<LinkedDataMiddleware> -> LinkedDataMiddleware

    /// Test-only visibility (internal + InternalsVisibleTo, #392 pattern): number of times a
    /// static-graph body was actually (re)built — proves build-once-per-(origin,mediaType) for
    /// the GraphFactory=None branch (issue #382).
    member internal StaticBodyBuildCount: int

    member InvokeAsync: ctx: HttpContext -> Task
