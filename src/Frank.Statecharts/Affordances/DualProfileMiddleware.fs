namespace Frank.Affordances

open System.Threading.Tasks
open Microsoft.AspNetCore.Http
open Microsoft.AspNetCore.Routing
open Microsoft.Extensions.Primitives
open Frank.Statecharts

/// ASP.NET Core convention-based middleware that replaces the projected ALPS profile
/// Link header with a dual-annotated variant when the client sends `Prefer: return=dual`.
///
/// Runs after ProjectedProfileMiddleware (which sets the role-specific profile Link).
/// Additive: always calls next regardless of whether headers were modified.
///
/// Per RFC 7240:
/// - Sets `Preference-Applied: return=dual` when the dual is served
/// - Adds `Vary: Prefer` so caches distinguish dual from non-dual responses
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
                | true, stateDict ->
                    // This route has dual profiles — Vary: Prefer applies to all responses
                    // (RFC 7234 section 4.1: Vary describes the selection algorithm)
                    VaryHeader.append ctx "Prefer"

                    let preferHeader = ctx.Request.Headers["Prefer"].ToString()

                    if PreferHeader.hasReturnDual preferHeader then
                        let roles = ctx.GetRoles()

                        if not (Set.isEmpty roles) then
                            // Get current statechart state
                            let stateKey =
                                let f = ctx.Features.Get<IStatechartFeature>()

                                if obj.ReferenceEquals(f, null) then
                                    null
                                else
                                    match f.StateKey with
                                    | Some key -> key
                                    | None -> null

                            if not (isNull stateKey) then
                                match stateDict.TryGetValue(stateKey) with
                                | true, roleDict ->
                                    // First matching role wins (alphabetical via Set iteration)
                                    let dualMatch =
                                        roles
                                        |> Seq.tryPick (fun role ->
                                            match roleDict.TryGetValue(role) with
                                            | true, _alpsJson -> Some role
                                            | false, _ -> None)

                                    match dualMatch with
                                    | Some matchedRole ->
                                        // Swap profile link to dual variant
                                        let existingLinks = ctx.Response.Headers["Link"]

                                        if existingLinks.Count > 0 then
                                            let dualLinkValue =
                                                sprintf
                                                    "<%s-%s-dual>; rel=\"profile\""
                                                    (matchedRole.ToLowerInvariant())
                                                    stateKey

                                            ctx.Response.Headers["Link"] <-
                                                LinkHeaderRewriter.replaceProfileLink existingLinks dualLinkValue

                                        // RFC 7240: indicate the preference was applied
                                        ctx.Response.Headers["Preference-Applied"] <- StringValues("return=dual")
                                    | None -> ()
                                | false, _ -> ()
                | false, _ -> ()

        next.Invoke(ctx)
