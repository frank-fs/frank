namespace Frank

/// Lightweight content negotiation from AspNetCore.Mvc.Core.
/// Based on https://www.strathweb.com/2018/09/running-asp-net-core-content-negotiation-by-hand/
module ContentNegotiation =

    open System.Threading.Tasks
    open Microsoft.AspNetCore.Http

    val notAcceptable: ctx: HttpContext -> Task

    val negotiate: statusCode: int -> body: 'a -> ctx: HttpContext -> Task

    type HttpContext with
        member Negotiate: statusCode: int * body: 'a -> Task
