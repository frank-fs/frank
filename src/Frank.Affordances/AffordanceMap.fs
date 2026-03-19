namespace Frank.Affordances

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

module AffordanceMap =

    /// Current affordance map schema version.
    let currentVersion = "1.0"

    /// Wildcard state key for resources without statecharts.
    [<Literal>]
    let WildcardStateKey = "*"

    /// Separator for composite lookup keys.
    [<Literal>]
    let KeySeparator = "|"

    /// Build a composite lookup key from route template and state key.
    let lookupKey (routeTemplate: string) (stateKey: string) : string =
        routeTemplate + KeySeparator + stateKey

    /// Try to find an entry in the affordance map by route and state.
    let tryFind (routeTemplate: string) (stateKey: string) (map: AffordanceMap) : AffordanceMapEntry option =
        let key = lookupKey routeTemplate stateKey

        map.Entries
        |> List.tryFind (fun e -> lookupKey e.RouteTemplate e.StateKey = key)
