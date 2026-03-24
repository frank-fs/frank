namespace Frank.Affordances

open System.Threading.Tasks
open Microsoft.AspNetCore.Http
open Microsoft.AspNetCore.Routing
open Microsoft.Extensions.Primitives
open Frank.Statecharts

/// Replaces the profile entry in a Link header StringValues with a role-specific value.
/// Returns the original values unchanged if no profile entry is found.
module LinkHeaderRewriter =

    let private profileMarker = "rel=\"profile\""

    let replaceProfileLink (existing: StringValues) (replacement: string) : StringValues =
        let count = existing.Count

        if count = 0 then
            existing
        elif count = 1 then
            if existing.[0].Contains(profileMarker) then
                StringValues(replacement)
            else
                existing
        else
            // Find the index of the profile entry using StringValues indexer
            // (avoids ToArray which returns the backing array reference, not a copy)
            let mutable profileIdx = -1

            for i in 0 .. count - 1 do
                if profileIdx = -1 && existing.[i].Contains(profileMarker) then
                    profileIdx <- i

            if profileIdx = -1 then
                existing
            else
                let result =
                    Array.init count (fun i -> if i = profileIdx then replacement else existing.[i])

                StringValues(result)

/// Appends a value to the Vary header without overwriting existing entries.
module VaryHeader =

    let append (ctx: HttpContext) (value: string) =
        let existing = ctx.Response.Headers["Vary"]

        if existing.Count = 0 then
            ctx.Response.Headers["Vary"] <- StringValues(value)
        else
            let current = existing.ToString()

            if not (current.Contains(value)) then
                ctx.Response.Headers["Vary"] <- StringValues(current + ", " + value)

/// ASP.NET Core convention-based middleware that replaces the global ALPS profile
/// Link header with a role-specific one when the authenticated user has resolved roles.
///
/// Runs after AffordanceMiddleware (which sets the base Link headers).
/// Additive: always calls next regardless of whether headers were modified.
type ProjectedProfileMiddleware(next: RequestDelegate, roleLookup: RoleProfileLookup) =

    member _.InvokeAsync(ctx: HttpContext) : Task =
        let endpoint = ctx.GetEndpoint()

        if not (isNull endpoint) then
            let routeTemplate =
                match endpoint with
                | :? RouteEndpoint as re -> re.RoutePattern.RawText
                | _ -> null

            if not (isNull routeTemplate) then
                match roleLookup.TryGetValue(routeTemplate) with
                | true, roleMap ->
                    // This route has role projections — Vary: Authorization applies to all
                    // responses (RFC 7234 §4.1: Vary describes the selection algorithm,
                    // not whether this specific response was affected).
                    VaryHeader.append ctx "Authorization"

                    let roles = ctx.GetRoles()

                    if not (Set.isEmpty roles) then
                        // First match wins; Set iteration order is alphabetical.
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

        next.Invoke(ctx)
