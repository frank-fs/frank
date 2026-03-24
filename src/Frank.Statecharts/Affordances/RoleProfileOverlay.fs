namespace Frank.Affordances

open System
open System.Collections.Generic
open Frank.Resources.Model

/// Pre-computed lookup for role-specific ALPS profile Link header values.
/// Outer key: route template (e.g., "/games/{gameId}")
/// Inner key: role name (case-insensitive via OrdinalIgnoreCase comparer)
/// Value: formatted Link header value (e.g., "<https://example.com/alps/games-playerx>; rel=\"profile\"")
type RoleProfileLookup = Dictionary<string, Dictionary<string, string>>

module RoleProfileOverlay =

    /// Build a profile URL from base URI and slug, reusing the same format as AffordanceMap.profileUrl.
    let private profileUrl (baseUri: string) (slug: string) : string =
        let trimmed = baseUri.TrimEnd('/')
        sprintf "%s/%s" trimmed slug

    /// Build a role profile overlay from runtime state.
    /// Returns a lookup mapping route templates to per-role profile Link values.
    let build (state: RuntimeState) : RoleProfileLookup =
        let lookup = RoleProfileLookup(StringComparer.Ordinal)

        if Map.isEmpty state.Profiles.RoleAlpsProfiles then
            lookup
        else
            // Build resourceSlug → routeTemplate(s) index
            let slugToRoutes = Dictionary<string, ResizeArray<string>>(StringComparer.Ordinal)

            for resource in state.Resources do
                match slugToRoutes.TryGetValue(resource.ResourceSlug) with
                | true, routes -> routes.Add(resource.RouteTemplate)
                | false, _ ->
                    let routes = ResizeArray<string>()
                    routes.Add(resource.RouteTemplate)
                    slugToRoutes.[resource.ResourceSlug] <- routes

            // Match role slug keys against known resource slugs using prefix matching.
            // Handles hyphenated resource slugs correctly (e.g., "tic-tac-toe-playerx"
            // matched against known slug "tic-tac-toe" → role "playerx").
            for roleSlugKey in state.Profiles.RoleAlpsProfiles |> Map.toSeq |> Seq.map fst do
                let mutable matched = false

                for slug, routes in slugToRoutes |> Seq.map (fun kv -> kv.Key, kv.Value) do
                    if
                        not matched
                        && roleSlugKey.Length > slug.Length + 1
                        && roleSlugKey.StartsWith(slug, StringComparison.Ordinal)
                        && roleSlugKey.[slug.Length] = '-'
                    then
                        let roleName = roleSlugKey.Substring(slug.Length + 1)
                        let url = profileUrl state.BaseUri roleSlugKey
                        let linkValue = AffordancePreCompute.formatLinkValue url "profile"

                        for routeTemplate in routes do
                            match lookup.TryGetValue(routeTemplate) with
                            | true, roleMap -> roleMap.[roleName] <- linkValue
                            | false, _ ->
                                let roleMap = Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                                roleMap.[roleName] <- linkValue
                                lookup.[routeTemplate] <- roleMap

                        matched <- true

            lookup
