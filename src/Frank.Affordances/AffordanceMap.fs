namespace Frank.Affordances

open System
open System.Collections.Generic
open Microsoft.Extensions.Primitives

/// A single link relation in an affordance map entry.
type AffordanceLinkRelation =
    { /// Link relation type (IANA registered or ALPS profile fragment URI)
      Rel: string
      /// Target URL template
      Href: string
      /// HTTP method for this transition
      Method: string
      /// Human-readable label (optional)
      Title: string option }

/// One entry per (route, state) pair in the affordance map.
type AffordanceMapEntry =
    { /// HTTP route pattern
      RouteTemplate: string
      /// State name, or "*" for stateless resources
      StateKey: string
      /// HTTP methods available in this state
      AllowedMethods: string list
      /// Available transitions with relation types
      LinkRelations: AffordanceLinkRelation list
      /// URL to the ALPS profile for this resource
      ProfileUrl: string }

/// The complete affordance map with version metadata.
type AffordanceMap =
    { /// Schema version for forward compatibility
      Version: string
      /// All affordance entries
      Entries: AffordanceMapEntry list }

/// Pre-computed header values for a single (route, state) pair.
/// Built at startup for zero per-request allocation beyond header assignment.
type PreComputedAffordance =
    { /// Pre-formatted Allow header value, e.g. "GET, POST"
      AllowHeaderValue: StringValues
      /// Pre-formatted Link header values as a single StringValues (from string array).
      /// Each entry follows RFC 8288 syntax: `<URI>; rel="relation-type"`
      LinkHeaderValues: StringValues }

module AffordanceMap =

    /// Current affordance map schema version.
    let currentVersion = "1.0"

    /// Wildcard state key for resources without statecharts.
    [<Literal>]
    let WildcardStateKey = "*"

    /// Separator for composite lookup keys.
    [<Literal>]
    let KeySeparator = "|"

    /// HttpContext.Items key convention for the current statechart state key.
    /// The statechart middleware stores the resolved state key at this key.
    [<Literal>]
    let StateKeyItemsKey = "statechart.stateKey"

    /// Build a composite lookup key from route template and state key.
    let lookupKey (routeTemplate: string) (stateKey: string) : string =
        routeTemplate + KeySeparator + stateKey

    /// Try to find an entry in the affordance map by route and state.
    let tryFind (routeTemplate: string) (stateKey: string) (map: AffordanceMap) : AffordanceMapEntry option =
        let key = lookupKey routeTemplate stateKey

        map.Entries
        |> List.tryFind (fun e -> lookupKey e.RouteTemplate e.StateKey = key)

    /// Format a single link relation as an RFC 8288 Link header value.
    let private formatLinkValue (href: string) (rel: string) : string =
        sprintf "<%s>; rel=\"%s\"" href rel

    /// Pre-compute header strings for all entries in the affordance map.
    /// Returns a dictionary indexed by composite key for O(1) request-time lookup.
    let preCompute (map: AffordanceMap) : Dictionary<string, PreComputedAffordance> =
        let dict = Dictionary<string, PreComputedAffordance>(StringComparer.Ordinal)

        for entry in map.Entries do
            let key = lookupKey entry.RouteTemplate entry.StateKey
            let allowHeader = StringValues(String.Join(", ", entry.AllowedMethods))

            let linkValues =
                [| // Profile link
                   if not (String.IsNullOrEmpty entry.ProfileUrl) then
                       formatLinkValue entry.ProfileUrl "profile"
                   // Transition links
                   for lr in entry.LinkRelations do
                       formatLinkValue lr.Href lr.Rel |]

            let linkHeader = StringValues(linkValues)

            dict.[key] <-
                { AllowHeaderValue = allowHeader
                  LinkHeaderValues = linkHeader }

        dict
