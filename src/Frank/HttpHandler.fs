module Frank.HttpHandler

open System.Threading.Tasks
open Microsoft.AspNetCore.Http

let private methodNotAllowed (ctx:HttpContext) =
        ctx.Response.StatusCode <- 405
        Task.FromResult(Some ctx)

/// Applies a method not allowed (405) fallback handler to a
/// Giraffe-style HttpHandler to create an HttpFunc. 
/// 405 is chosen over 404 as routing should have already taken place.
/// Please note that other status codes may be more suitable.
let toHttpFunc (handler:(HttpContext -> Task<HttpContext option>) -> HttpContext -> Task<HttpContext option>) =
    handler methodNotAllowed

/// Applies a method not allowed (405) fallback handler to a
/// Giraffe-style HttpHandler to create an HttpFunc. 
/// 405 is chosen over 404 as routing should have already taken place.
/// Please note that other status codes may be more suitable.
let toRequestDelegate (handler:(HttpContext -> Task<HttpContext option>) -> HttpContext -> Task<HttpContext option>) =
    RequestDelegate(fun ctx -> handler methodNotAllowed ctx :> Task)
