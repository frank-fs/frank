namespace Frank.Affordances

open System.Collections.Generic
open System.Threading.Tasks
open Microsoft.AspNetCore.Http
open Microsoft.AspNetCore.Routing
open Frank.Resources.Model
open Frank.Statecharts

/// ASP.NET Core convention-based middleware that injects Link and Allow headers
/// based on pre-computed affordance data. Reads the current statechart state key
/// from HttpContext.Features and looks up pre-computed header values by composite key.
///
/// The middleware is additive: it always calls next regardless of whether headers
/// were injected. When no matching affordance entry exists, the request passes
/// through unmodified.
type AffordanceMiddleware(next: RequestDelegate, lookup: Dictionary<string, PreComputedAffordance>) =

    member _.InvokeAsync(ctx: HttpContext) : Task =
        let endpoint = ctx.GetEndpoint()

        if not (isNull endpoint) then
            let routeTemplate =
                match endpoint with
                | :? RouteEndpoint as re -> re.RoutePattern.RawText
                | _ -> null

            if not (isNull routeTemplate) then
                // Try stateful lookup first (state key from statechart middleware)
                let stateKey =
                    let f = ctx.Features.Get<IStatechartFeature>()
                    if obj.ReferenceEquals(f, null) then null
                    else
                        match f.StateKey with
                        | Some key -> key
                        | None -> null

                let compositeKey =
                    if not (isNull stateKey) then
                        AffordanceMap.lookupKey routeTemplate stateKey
                    else
                        // Fallback to wildcard for plain resources
                        AffordanceMap.lookupKey routeTemplate AffordanceMap.WildcardStateKey

                match lookup.TryGetValue(compositeKey) with
                | true, preComputed ->
                    ctx.Response.Headers["Allow"] <- preComputed.AllowHeaderValue
                    ctx.Response.Headers["Link"] <- preComputed.LinkHeaderValues
                | false, _ -> ()

        next.Invoke(ctx)
