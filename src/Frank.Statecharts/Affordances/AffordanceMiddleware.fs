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

    let tryLookup (key: string) =
        match lookup.TryGetValue(key) with
        | true, entry -> Some entry
        | false, _ -> None

    let applyHeaders (ctx: HttpContext) (preComputed: PreComputedAffordance) =
        ctx.Response.Headers["Allow"] <- preComputed.AllowHeaderValue
        ctx.Response.Headers["Link"] <- preComputed.LinkHeaderValues

    member _.InvokeAsync(ctx: HttpContext) : Task =
        let endpoint = ctx.GetEndpoint()

        if not (isNull endpoint) then
            let routeTemplate =
                match endpoint with
                | :? RouteEndpoint as re -> re.RoutePattern.RawText
                | _ -> null

            if not (isNull routeTemplate) then
                // Resolve state key from statechart middleware
                let stateKey =
                    let f = ctx.Features.Get<IStatechartFeature>()
                    if obj.ReferenceEquals(f, null) then null
                    else
                        match f.StateKey with
                        | Some key -> key
                        | None -> null

                let effectiveStateKey =
                    if not (isNull stateKey) then stateKey
                    else AffordanceMap.WildcardStateKey

                let baseKey = AffordanceMap.lookupKey routeTemplate effectiveStateKey

                // Resolve entry: role-specific > authenticated fallback > base
                let roles = ctx.GetRoles()
                let hasRoles = not (Set.isEmpty roles)

                let resolved =
                    if hasRoles then
                        // Try role-specific entry (first matching role wins)
                        let roleMatch =
                            roles
                            |> Seq.tryPick (fun role ->
                                tryLookup (AffordanceMap.lookupKeyWithRole routeTemplate effectiveStateKey role))

                        match roleMatch with
                        | Some _ -> roleMatch
                        | None ->
                            // Authenticated fallback (role-agnostic links only)
                            let authMatch = tryLookup (AffordanceMap.lookupKeyAuthenticated routeTemplate effectiveStateKey)

                            match authMatch with
                            | Some _ -> authMatch
                            | None ->
                                // No role-restricted links exist: use base entry
                                tryLookup baseKey
                    else
                        // Unauthenticated: base entry with all links
                        tryLookup baseKey

                resolved |> Option.iter (applyHeaders ctx)

        next.Invoke(ctx)
