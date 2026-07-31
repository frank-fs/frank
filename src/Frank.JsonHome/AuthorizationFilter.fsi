namespace Frank.JsonHome

open System.Threading.Tasks
open Microsoft.AspNetCore.Http

module AuthorizationFilter =

    /// True when any resource declares authorization requirements, meaning the
    /// document varies by principal and must not be cached by a shared cache.
    val varies: resources: ResourceDescription list -> bool

    /// Filters each resource's Methods -- and the Accepts/Formats hints
    /// derived from them -- down to what the current principal can call,
    /// evaluating authorization per HTTP method rather than per resource. A
    /// method carrying IAllowAnonymous metadata is always kept. A resource
    /// left with no visible methods is dropped entirely. Evaluation failures
    /// deny that method rather than throw or fail open.
    val apply: ctx: HttpContext -> resources: ResourceDescription list -> Task<ResourceDescription list>
