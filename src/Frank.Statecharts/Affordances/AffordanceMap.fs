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
    }

module AffordancePreCompute =

    /// Format a single link relation as an RFC 8288 Link header value.
    let internal formatLinkValue (href: string) (rel: string) : string = sprintf "<%s>; rel=\"%s\"" href rel

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

            dict.[key] <-
                { AllowHeaderValue = allowHeader
                  LinkHeaderValues = linkHeader }

            // Collect distinct roles from link relations
            let distinctRoles =
                entry.LinkRelations
                |> List.collect (fun lr -> lr.Roles)
                |> List.distinct

            let hasRoleRestrictedLinks = not (List.isEmpty distinctRoles)

            // Generate role-scoped entries
            for role in distinctRoles do
                let roleKey = AffordanceMap.lookupKeyWithRole entry.RouteTemplate entry.StateKey role

                let roleLinkValues =
                    [| if not (String.IsNullOrEmpty entry.ProfileUrl) then
                           formatLinkValue entry.ProfileUrl "profile"

                       for lr in entry.LinkRelations do
                           if lr.Roles = [] || List.contains role lr.Roles then
                               formatLinkValue lr.Href lr.Rel |]

                dict.[roleKey] <-
                    { AllowHeaderValue = allowHeader
                      LinkHeaderValues = StringValues(roleLinkValues) }

            // Generate authenticated fallback entry: only role-agnostic links
            // Used when user has roles but none match any role-specific entry
            if hasRoleRestrictedLinks then
                let authKey = AffordanceMap.lookupKeyAuthenticated entry.RouteTemplate entry.StateKey

                let authLinkValues =
                    [| if not (String.IsNullOrEmpty entry.ProfileUrl) then
                           formatLinkValue entry.ProfileUrl "profile"

                       for lr in entry.LinkRelations do
                           if lr.Roles = [] then
                               formatLinkValue lr.Href lr.Rel |]

                dict.[authKey] <-
                    { AllowHeaderValue = allowHeader
                      LinkHeaderValues = StringValues(authLinkValues) }

        dict
