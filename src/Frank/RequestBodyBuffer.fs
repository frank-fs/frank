namespace Frank

open System.IO
open System.Threading.Tasks
open Microsoft.AspNetCore.Http

[<RequireQualifiedAccess>]
module RequestBodyBuffer =

    let defaultMaxBodyBytes: int64 = 1L * 1024L * 1024L

    let enable (maxBytes: int64) (request: HttpRequest) : unit = request.EnableBuffering(maxBytes)

    let respond413 (ctx: HttpContext) : Task =
        ProblemJson.write ctx 413 "about:blank" "Payload Too Large" "Request body exceeds the configured maximum size"
