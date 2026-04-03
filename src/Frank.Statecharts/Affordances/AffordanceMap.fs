namespace Frank.Affordances

open System
open System.Collections.Generic
open Microsoft.Extensions.Primitives
open Frank.Resources.Model

/// Pre-computed header values for a single (route, state) pair.
/// Built at startup for zero per-request allocation beyond header assignment.
type PreComputedAffordance =
    {
        /// Pre-formatted Allow header value, e.g. "GET, POST"
        AllowHeaderValue: StringValues
        /// Pre-formatted Link header values as a single StringValues (from string array).
        /// Each entry follows RFC 8288 syntax: `<URI>; rel="relation-type"`
        LinkHeaderValues: StringValues
        /// True when any Link href contains route template parameters ({param}).
        /// Middleware must resolve templates against ctx.Request.RouteValues at request time
        /// because RFC 8288 Link targets must be URIs, not URI Templates.
        HasTemplateLinks: bool
        /// The HTTP methods permitted for this affordance, e.g. ["GET"; "OPTIONS"; "POST"].
        /// Stored as a sorted, distinct list to allow merge without re-parsing AllowHeaderValue.
        Methods: string list
    }

module AffordancePreCompute =

    /// Format a single link relation as an RFC 8288 Link header value.
    let internal formatLinkValue (href: string) (rel: string) : string = sprintf "<%s>; rel=\"%s\"" href rel

    /// Merge multiple pre-computed affordance entries into one.
    /// Used when a user holds multiple roles: the merged entry contains the union
    /// of methods (deduplicated, sorted) and the union of link relations (deduplicated).
    let internal merge (entries: PreComputedAffordance list) : PreComputedAffordance =
        match entries with
        | [] -> failwith "merge requires at least one entry"
        | [ single ] -> single
        | _ ->
            let allMethods =
                entries
                |> List.collect _.Methods
                |> List.distinct
                |> List.sort

            // Deduplication relies on consistent formatting from formatLinkValue:
            // the same (href, rel) pair always produces a byte-identical string,
            // so Array.distinct is sufficient to collapse duplicates across role entries.
            let allLinks =
                entries
                |> Array.ofList
                |> Array.collect (fun e -> e.LinkHeaderValues.ToArray())
                |> Array.distinct

            let hasTemplates = entries |> List.exists _.HasTemplateLinks

            { AllowHeaderValue = StringValues(String.Join(", ", allMethods))
              LinkHeaderValues = StringValues(allLinks)
              HasTemplateLinks = hasTemplates
              Methods = allMethods }

    // Note: Base entry AllowedMethods comes from AffordanceMapEntry.AllowedMethods (caller-provided).
    // Role entries derive AllowedMethods from their filtered link relations + GET + OPTIONS.
    // This asymmetry is intentional: base is the unauthenticated fallback; role entries are derived.
    /// Pre-compute header strings for all entries in the affordance map.
    /// Returns a dictionary indexed by composite key for O(1) request-time lookup.
    /// Also generates role-scoped entries keyed by route|state|role for role-filtered
    /// transition link rels.
    let preCompute (map: AffordanceMap) : Dictionary<string, PreComputedAffordance> =
        let dict = Dictionary<string, PreComputedAffordance>(StringComparer.Ordinal)

        for entry in map.Entries do
            let key = AffordanceMap.lookupKey entry.RouteTemplate entry.StateKey
            let allowHeader = StringValues(String.Join(", ", entry.AllowedMethods))

            // Base entry: ALL links (unauthenticated fallback)
            let linkValues =
                [| if not (String.IsNullOrEmpty entry.ProfileUrl) then
                       formatLinkValue entry.ProfileUrl "profile"

                   for lr in entry.LinkRelations do
                       formatLinkValue lr.Href lr.Rel |]

            let linkHeader = StringValues(linkValues)

            let hasTemplates = linkValues |> Array.exists (fun v -> v.Contains("{"))

            dict.[key] <-
                { AllowHeaderValue = allowHeader
                  LinkHeaderValues = linkHeader
                  HasTemplateLinks = hasTemplates
                  Methods = entry.AllowedMethods }

            // Collect distinct roles from link relations
            let distinctRoles =
                entry.LinkRelations |> List.collect (fun lr -> lr.Roles) |> List.distinct

            let hasRoleRestrictedLinks = not (List.isEmpty distinctRoles)

            // Generate role-scoped entries
            for role in distinctRoles do
                let roleKey =
                    AffordanceMap.lookupKeyWithRole entry.RouteTemplate entry.StateKey role

                let roleFilteredLinks =
                    entry.LinkRelations
                    |> List.filter (fun lr -> lr.Roles = [] || List.contains role lr.Roles)

                let roleMethods =
                    roleFilteredLinks
                    |> List.map (fun lr -> lr.Method)
                    |> fun ms -> "GET" :: "HEAD" :: "OPTIONS" :: ms
                    |> List.distinct
                    |> List.sort

                let roleAllowHeader = StringValues(String.Join(", ", roleMethods))

                let roleLinkValues =
                    [| if not (String.IsNullOrEmpty entry.ProfileUrl) then
                           formatLinkValue entry.ProfileUrl "profile"

                       for lr in roleFilteredLinks do
                           formatLinkValue lr.Href lr.Rel |]

                dict.[roleKey] <-
                    { AllowHeaderValue = roleAllowHeader
                      LinkHeaderValues = StringValues(roleLinkValues)
                      HasTemplateLinks = roleLinkValues |> Array.exists (fun v -> v.Contains("{"))
                      Methods = roleMethods }

            // Generate authenticated fallback entry: only role-agnostic links
            // Used when user has roles but none match any role-specific entry
            if hasRoleRestrictedLinks then
                let authKey =
                    AffordanceMap.lookupKeyAuthenticated entry.RouteTemplate entry.StateKey

                let authFilteredLinks =
                    entry.LinkRelations |> List.filter (fun lr -> lr.Roles = [])

                let authMethods =
                    authFilteredLinks
                    |> List.map (fun lr -> lr.Method)
                    |> fun ms -> "GET" :: "HEAD" :: "OPTIONS" :: ms
                    |> List.distinct
                    |> List.sort

                let authAllowHeader = StringValues(String.Join(", ", authMethods))

                let authLinkValues =
                    [| if not (String.IsNullOrEmpty entry.ProfileUrl) then
                           formatLinkValue entry.ProfileUrl "profile"

                       for lr in authFilteredLinks do
                           formatLinkValue lr.Href lr.Rel |]

                dict.[authKey] <-
                    { AllowHeaderValue = authAllowHeader
                      LinkHeaderValues = StringValues(authLinkValues)
                      HasTemplateLinks = authLinkValues |> Array.exists (fun v -> v.Contains("{"))
                      Methods = authMethods }

        dict
