namespace Frank.Affordances

open System
open System.Collections.Generic
open System.Threading.Tasks
open Microsoft.AspNetCore.Http
open Microsoft.AspNetCore.Routing
open Microsoft.Extensions.DependencyInjection
open Microsoft.Extensions.Logging
open Microsoft.Extensions.Primitives
open Frank.Resources.Model
open Frank.Statecharts

/// ASP.NET Core convention-based middleware that injects Link and Allow headers
/// based on pre-computed affordance data. Uses Response.OnStarting to defer header
/// injection until just before the response is sent — by that point,
/// StateMachineMiddleware has resolved state + roles via IStatechartFeature/IRoleFeature.
///
/// Also applies role-projected and dual profile Link header overlays when
/// RoleProfileLookup and/or DualProfileLookup are registered in DI.
/// This consolidation avoids LIFO ordering issues with multiple OnStarting callbacks.
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
                    result <- result.Replace(sprintf "{%s}" kv.Key, Uri.EscapeDataString(string kv.Value))

                result)

        StringValues(resolved)

    let applyHeaders (ctx: HttpContext) (preComputed: PreComputedAffordance) =
        ctx.Response.Headers["Allow"] <- preComputed.AllowHeaderValue

        if preComputed.HasTemplateLinks then
            ctx.Response.Headers["Link"] <- resolveTemplateLinks ctx preComputed.LinkHeaderValues
        else
            ctx.Response.Headers["Link"] <- preComputed.LinkHeaderValues

    /// Apply role-projected profile Link header overlay.
    /// Swaps the global ALPS profile link for a role-specific one when roles are resolved.
    let applyRoleProfileOverlay (ctx: HttpContext) (routeTemplate: string) (roles: Set<string>) =
        let roleLookup = ctx.RequestServices.GetService<RoleProfileLookup>()

        if not (isNull roleLookup) then
            match roleLookup.TryGetValue(routeTemplate) with
            | true, roleMap ->
                if not (Set.isEmpty roles) then
                    let profileLinkValue =
                        roles
                        |> Seq.tryPick (fun role ->
                            match roleMap.TryGetValue(role) with
                            | true, v -> Some v
                            | _ -> None)

                    match profileLinkValue with
                    | Some linkValue ->
                        let existingLinks = ctx.Response.Headers["Link"]

                        if existingLinks.Count > 0 then
                            ctx.Response.Headers["Link"] <-
                                LinkHeaderRewriter.replaceProfileLink existingLinks linkValue
                    | None -> ()
            | _ -> ()

    /// Apply dual profile Link header overlay.
    /// Swaps the profile link for a dual-annotated variant when Prefer: return=dual is present.
    let applyDualProfileOverlay (ctx: HttpContext) (routeTemplate: string) (roles: Set<string>) (stateKey: string) =
        let dualLookup = ctx.RequestServices.GetService<DualProfileLookup>()

        if not (isNull dualLookup) then
            match dualLookup.TryGetValue(routeTemplate) with
            | true, stateDict ->
                let preferHeader = ctx.Request.Headers["Prefer"].ToString()

                if PreferHeader.hasReturnDual preferHeader then
                    if not (Set.isEmpty roles) then
                        match stateDict.TryGetValue(stateKey) with
                        | true, roleDict ->
                            let dualEntry =
                                roles
                                |> Seq.tryPick (fun role ->
                                    match roleDict.TryGetValue(role) with
                                    | true, entry -> Some entry
                                    | false, _ -> None)

                            match dualEntry with
                            | Some entry ->
                                let existingLinks = ctx.Response.Headers["Link"]

                                if existingLinks.Count > 0 then
                                    ctx.Response.Headers["Link"] <-
                                        LinkHeaderRewriter.replaceProfileLink existingLinks entry.LinkHeaderValue

                                ctx.Response.Headers["Preference-Applied"] <- StringValues("return=dual")
                            | None -> ()
                        | false, _ -> ()
            | false, _ -> ()

    member _.Invoke(ctx: HttpContext) : Task =
        // Only register OnStarting for RouteEndpoint requests with a non-null route pattern.
        // This avoids allocating a closure + Func<Task> on every non-statechart request.
        // By the time OnStarting fires, StateMachineMiddleware has resolved state + roles
        // via IStatechartFeature/IRoleFeature.
        let endpoint = ctx.GetEndpoint()

        if not (isNull endpoint) then
            match endpoint with
            | :? RouteEndpoint as re when not (isNull re.RoutePattern.RawText) ->
                let routeTemplate = re.RoutePattern.RawText

                ctx.Response.OnStarting(
                    Func<Task>(fun () ->
                        try
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

                            let resolved, usedRoleKey =
                                if hasRoles then
                                    let roleSpecific =
                                        roles
                                        |> Seq.tryPick (fun role ->
                                            tryLookup (
                                                AffordanceMap.lookupKeyWithRole routeTemplate effectiveStateKey role
                                            ))

                                    match roleSpecific with
                                    | Some entry -> Some entry, true
                                    | None ->
                                        let fallback =
                                            tryLookup (
                                                AffordanceMap.lookupKeyAuthenticated routeTemplate effectiveStateKey
                                            )
                                            |> Option.orElseWith (fun () -> tryLookup baseKey)

                                        fallback, false
                                else
                                    // Unauthenticated: use authenticated fallback (role-agnostic links only).
                                    // Showing role-restricted transitions to unauthenticated users violates
                                    // HATEOAS — clients should only see transitions they can follow.
                                    let fallback =
                                        tryLookup (
                                            AffordanceMap.lookupKeyAuthenticated routeTemplate effectiveStateKey
                                        )
                                        |> Option.orElseWith (fun () -> tryLookup baseKey)

                                    fallback, false

                            resolved |> Option.iter (applyHeaders ctx)

                            // When the resolved entry came from a role-specific key, add Vary: Authorization
                            // so caches do not serve one role's headers to a different role's client.
                            if usedRoleKey then
                                let current = ctx.Response.Headers["Vary"].ToString()

                                if not (current.Contains("Authorization", StringComparison.OrdinalIgnoreCase)) then
                                    ctx.Response.Headers.Append("Vary", StringValues("Authorization"))

                            // Apply profile overlays in order: role-projected, then dual.
                            // These read Link headers set by applyHeaders above and swap
                            // the profile entry for a role-specific or dual-annotated variant.
                            applyRoleProfileOverlay ctx routeTemplate roles
                            applyDualProfileOverlay ctx routeTemplate roles effectiveStateKey

                            Task.CompletedTask
                        with ex ->
                            // Log and degrade gracefully: no headers is better than a broken response.
                            let loggerFactory = ctx.RequestServices.GetService<ILoggerFactory>()

                            if not (isNull loggerFactory) then
                                let logger = loggerFactory.CreateLogger("Frank.Affordances.AffordanceMiddleware")
                                logger.LogError(ex, "AffordanceMiddleware OnStarting callback failed for {RouteTemplate}", routeTemplate)

                            Task.CompletedTask))
            | _ -> ()

        next.Invoke(ctx)
