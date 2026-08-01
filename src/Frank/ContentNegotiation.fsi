namespace Frank

/// Lightweight content negotiation from AspNetCore.Mvc.Core.
/// Based on https://www.strathweb.com/2018/09/running-asp-net-core-content-negotiation-by-hand/
module ContentNegotiation =

    open System.Threading.Tasks
    open Microsoft.AspNetCore.Http

    val notAcceptable: ctx: HttpContext -> Task

    val negotiate: statusCode: int -> body: 'a -> ctx: HttpContext -> Task

    /// Delegates to ASP.NET Core MVC's registered IOutputFormatters to write `body` as
    /// exactly `mediaType`, for representations that want to reuse an app's existing
    /// formatter registry (AddMvcCore(), AddXmlSerializerFormatters(), etc.) instead of
    /// a hand-written producer. Unlike `negotiate`, the caller supplies the target media
    /// type explicitly rather than having it derived from Accept -- but the underlying
    /// OutputFormatterSelector may still consult the request's actual Accept header when
    /// one is present. Callers should pass a media type the client's Accept header
    /// genuinely admits, which is always true for the intended usage: passing the same
    /// media type as the `accepts` entry this call is nested inside. `mediaType` must be
    /// a concrete type (e.g. "application/xml"), never a wildcard pattern like "*/*" or
    /// "text/*" -- this asks MVC for a formatter for exactly this type, and a wildcard
    /// cannot resolve to one. Throws if no formatter supports it (a server
    /// misconfiguration, not a client error, by the time this is called).
    val viaOutputFormatter: mediaType: string -> body: 'a -> ctx: HttpContext -> Task

    type HttpContext with
        member Negotiate: statusCode: int * body: 'a -> Task
