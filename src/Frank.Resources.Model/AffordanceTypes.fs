namespace Frank.Resources.Model

open System

/// A single link relation in an affordance map entry.
[<RequireQualifiedAccess>]
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
[<RequireQualifiedAccess>]
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

    /// An empty affordance map.
    let empty: AffordanceMap =
        { Version = currentVersion
          Entries = [] }

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
        map.Entries |> List.tryFind (fun e -> lookupKey e.RouteTemplate e.StateKey = key)

    /// Derive the ALPS profile URL from a base URI and resource slug.
    let private profileUrl (baseUri: string) (slug: string) : string =
        let trimmed = baseUri.TrimEnd('/')
        sprintf "%s/%s" trimmed slug

    /// Build link relations from runtime HTTP capabilities.
    let private buildLinkRelations (routeTemplate: string) (capabilities: RuntimeHttpCapability list) : AffordanceLinkRelation list =
        capabilities |> List.map (fun cap ->
            { Rel = cap.LinkRelation; Href = routeTemplate; Method = cap.Method; Title = None })

    /// Build affordance map entries for a single runtime resource.
    let private buildEntries (resource: RuntimeResource) (baseUri: string) : AffordanceMapEntry list =
        let profile = profileUrl baseUri resource.ResourceSlug
        match resource.Statechart.StateNames with
        | [] ->
            let methods = resource.HttpCapabilities |> List.map _.Method |> List.distinct |> List.sort
            let linkRels = buildLinkRelations resource.RouteTemplate resource.HttpCapabilities
            [ { RouteTemplate = resource.RouteTemplate; StateKey = WildcardStateKey; AllowedMethods = methods; LinkRelations = linkRels; ProfileUrl = profile } ]
        | stateNames ->
            stateNames |> List.map (fun stateName ->
                let capsForState = resource.HttpCapabilities |> List.filter (fun cap -> cap.StateKey = WildcardStateKey || cap.StateKey = stateName)
                let methods = capsForState |> List.map _.Method |> List.distinct |> List.sort
                let linkRels = buildLinkRelations resource.RouteTemplate capsForState
                { RouteTemplate = resource.RouteTemplate; StateKey = stateName; AllowedMethods = methods; LinkRelations = linkRels; ProfileUrl = profile })

    /// Generate an AffordanceMap from runtime resource data at startup.
    let generateFromResources (resources: RuntimeResource list) (baseUri: string) : AffordanceMap =
        let entries = resources |> List.collect (fun r -> buildEntries r baseUri)
        { Version = currentVersion; Entries = entries }

    /// Load an AffordanceMap from a RuntimeState.
    let fromRuntimeState (state: RuntimeState) : AffordanceMap =
        generateFromResources state.Resources state.BaseUri
