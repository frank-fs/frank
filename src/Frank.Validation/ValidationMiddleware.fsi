namespace Frank.Validation

open System.Threading.Tasks
open Microsoft.AspNetCore.Http
open Microsoft.Extensions.Logging

type ValidationMiddleware =
    new:
        next: RequestDelegate * config: ValidationConfig * logger: ILogger<ValidationMiddleware> -> ValidationMiddleware

    member InvokeAsync: ctx: HttpContext -> Task
