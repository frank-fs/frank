namespace Frank.Affordances

open System.Threading.Tasks
open Microsoft.AspNetCore.Http
open Microsoft.AspNetCore.Routing

/// ASP.NET Core convention-based middleware that adds Vary: Prefer when the matched
/// route has dual profiles. The actual dual profile link swapping and
/// Preference-Applied header are handled by AffordanceMiddleware's OnStarting callback
/// (which has access to resolved state + roles from StateMachineMiddleware).
///
/// Runs after ProjectedProfileMiddleware. Additive: always calls next.
///
/// Per RFC 7240:
/// - `Preference-Applied: return=dual` is set by AffordanceMiddleware when dual is served
/// - `Vary: Prefer` is set here so caches distinguish dual from non-dual responses
type DualProfileMiddleware(next: RequestDelegate, dualLookup: DualProfileLookup) =

    member _.Invoke(ctx: HttpContext) : Task =
        let endpoint = ctx.GetEndpoint()

        if not (isNull endpoint) then
            let routeTemplate =
                match endpoint with
                | :? RouteEndpoint as re -> re.RoutePattern.RawText
                | _ -> null

            if not (isNull routeTemplate) then
                match dualLookup.TryGetValue(routeTemplate) with
                | true, _ ->
                    // This route has dual profiles — Vary: Prefer applies to all responses
                    // (RFC 7234 section 4.1: Vary describes the selection algorithm)
                    VaryHeader.append ctx "Prefer"
                | false, _ -> ()

        next.Invoke(ctx)
