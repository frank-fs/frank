namespace Frank

open System.Threading.Tasks
open Microsoft.AspNetCore.Http

[<RequireQualifiedAccess>]
module RequestBodyBuffer =

    val defaultMaxBodyBytes: int64

    val enable: maxBytes: int64 -> request: HttpRequest -> unit

    val respond413: ctx: HttpContext -> Task
