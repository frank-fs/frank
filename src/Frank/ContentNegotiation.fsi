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
    /// a hand-written producer. Unlike `negotiate`, this does not parse Accept itself --
    /// it asks for a formatter constrained to this one already-decided media type.
    /// Throws if no formatter supports it (a server misconfiguration, not a client
    /// error, by the time this is called).
    val viaOutputFormatter: mediaType: string -> body: 'a -> ctx: HttpContext -> Task

    type HttpContext with
        member Negotiate: statusCode: int * body: 'a -> Task
