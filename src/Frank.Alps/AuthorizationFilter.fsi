namespace Frank.Alps

open System.Threading.Tasks
open Microsoft.AspNetCore.Http

/// Principal-based filtering, ported from `Frank.JsonHome/AuthorizationFilter.fs`'s evaluation logic
/// and retargeted to read a real `Endpoint`'s own `Metadata` directly (Task 12's `EndpointSurface`)
/// instead of `Frank.JsonHome`'s `ResourceDescription`.
module AuthorizationFilter =
    /// Whether `ctx`'s principal is allowed to see `endpoint`, per its `IAuthorizeData`/
    /// `AuthorizationPolicy` metadata (or `IAllowAnonymous`). Fails closed: any evaluation error
    /// returns `false`, never `true`.
    val isAllowed: ctx: HttpContext -> endpoint: Endpoint -> Task<bool>

    /// Keeps only the Descriptors whose bound endpoint `isAllowed` returns true for, order preserved.
    val filter: ctx: HttpContext -> pairs: (Endpoint * Descriptor) list -> Task<Descriptor list>

    /// True if any pair's endpoint carries authorization metadata -- callers use this to decide
    /// whether to set `Cache-Control: private, no-cache` / `Vary: Authorization`.
    val varies: pairs: (Endpoint * Descriptor) list -> bool
