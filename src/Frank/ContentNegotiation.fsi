namespace Frank

/// Lightweight content negotiation from AspNetCore.Mvc.Core.
/// Based on https://www.strathweb.com/2018/09/running-asp-net-core-content-negotiation-by-hand/
module ContentNegotiation =

    open System.Threading.Tasks
    open Microsoft.AspNetCore.Http

    val notAcceptable: ctx: HttpContext -> Task

    /// Writes `body` at `statusCode` using whichever registered IOutputFormatter MVC's
    /// own OutputFormatterSelector picks for the request's Accept header, or 406 if none
    /// matches.
    ///
    /// NAME COLLISION: this shares the identifier `negotiate` with the unrelated
    /// `negotiate { }` computation expression `Frank.Builder.negotiate` (a
    /// `NegotiateBuilder` value, auto-opened from `Frank.Builder`). With both
    /// `open Frank.Builder` and `open Frank.ContentNegotiation` in scope, F#'s normal
    /// shadowing rules apply and the LAST `open` wins. If you need both in the same
    /// file, qualify at least one of them -- or use the `ctx.Negotiate(statusCode, body)`
    /// extension member below, which never collides.
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
