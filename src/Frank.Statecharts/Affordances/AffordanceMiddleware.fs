namespace Frank.Affordances

open System.Collections.Generic
open System.Threading.Tasks
open Microsoft.AspNetCore.Http
open Microsoft.AspNetCore.Routing
open Microsoft.Extensions.Primitives
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

    /// Resolve URI template parameters in Link header values using actual route values.
    /// RFC 8288 requires Link targets to be URIs, not URI Templates — curly braces
    /// are invalid URI characters per RFC 3986.
    let resolveTemplateLinks (ctx: HttpContext) (values: StringValues) =
        let routeValues = ctx.Request.RouteValues

        let resolved =
            values.ToArray()
            |> Array.map (fun link ->
                let mutable result = link

                for kv in routeValues do
                    result <- result.Replace(sprintf "{%s}" kv.Key, string kv.Value)

                result)

        StringValues(resolved)

    let applyHeaders (ctx: HttpContext) (preComputed: PreComputedAffordance) =
        ctx.Response.Headers["Allow"] <- preComputed.AllowHeaderValue

        if preComputed.HasTemplateLinks then
            ctx.Response.Headers["Link"] <- resolveTemplateLinks ctx preComputed.LinkHeaderValues
        else
            ctx.Response.Headers["Link"] <- preComputed.LinkHeaderValues

    member _.Invoke(ctx: HttpContext) : Task =
        let endpoint = ctx.GetEndpoint()

        if not (isNull endpoint) then
            let routeTemplate =
                match endpoint with
                | :? RouteEndpoint as re -> re.RoutePattern.RawText
                | _ -> null

            if not (isNull routeTemplate) then
                let stateKey =
                    let f = ctx.Features.Get<IStatechartFeature>()

                    if obj.ReferenceEquals(f, null) then
                        null
                    else
                        match f.StateKey with
                        | Some key -> key
                        | None -> null

                let effectiveStateKey =
                    if not (isNull stateKey) then
                        stateKey
                    else
                        AffordanceMap.WildcardStateKey

                let baseKey = AffordanceMap.lookupKey routeTemplate effectiveStateKey

                // Resolve entry: role-specific > authenticated fallback > base.
                // Role matching uses Set iteration order (alphabetical for strings).
                // When multiple roles match, the first alphabetically wins.
                // Role priority ordering is a potential Phase 2 enhancement.
                let roles = ctx.GetRoles()
                let hasRoles = not (Set.isEmpty roles)

                let resolved =
                    if hasRoles then
                        roles
                        |> Seq.tryPick (fun role ->
                            tryLookup (AffordanceMap.lookupKeyWithRole routeTemplate effectiveStateKey role))
                        |> Option.orElseWith (fun () ->
                            tryLookup (AffordanceMap.lookupKeyAuthenticated routeTemplate effectiveStateKey))
                        |> Option.orElseWith (fun () -> tryLookup baseKey)
                    else
                        // Unauthenticated: use authenticated fallback (role-agnostic links only).
                        // Showing role-restricted transitions to unauthenticated users violates
                        // HATEOAS — clients should only see transitions they can follow.
                        tryLookup (AffordanceMap.lookupKeyAuthenticated routeTemplate effectiveStateKey)
                        |> Option.orElseWith (fun () -> tryLookup baseKey)

                resolved |> Option.iter (applyHeaders ctx)

        next.Invoke(ctx)
