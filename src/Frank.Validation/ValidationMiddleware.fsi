namespace Frank.Validation

open System.Threading.Tasks
open Microsoft.AspNetCore.Http
open Microsoft.Extensions.Logging

type ValidationMiddleware =
    new:
        next: RequestDelegate * config: ValidationConfig * logger: ILogger<ValidationMiddleware> -> ValidationMiddleware

    /// Test-only visibility (internal + InternalsVisibleTo, #392 pattern): number of times the
    /// host-relative ShapesGraph was actually rebuilt — proves build-once-per-origin under
    /// repeated requests to the same host (issue #382).
    member internal HostRelativeShapesBuildCount: int

    member InvokeAsync: ctx: HttpContext -> Task
