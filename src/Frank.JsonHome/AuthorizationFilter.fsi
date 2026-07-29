namespace Frank.JsonHome

open System.Threading.Tasks
open Microsoft.AspNetCore.Http

module AuthorizationFilter =

    /// True when any resource declares authorization requirements, meaning the
    /// document varies by principal and must not be cached by a shared cache.
    val varies: resources: ResourceDescription list -> bool

    /// Drops resources the current principal cannot reach. Resources with no
    /// authorization metadata are always kept; evaluation failures deny.
    val apply: ctx: HttpContext -> resources: ResourceDescription list -> Task<ResourceDescription list>
