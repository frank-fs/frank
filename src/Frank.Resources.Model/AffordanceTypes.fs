namespace Frank.Resources.Model

open System

/// A single link relation in an affordance map entry.
[<RequireQualifiedAccess>]
type AffordanceLinkRelation =
    {
        /// Link relation type (IANA registered or ALPS profile fragment URI)
        Rel: string
        /// Target URL template
        Href: string
        /// HTTP method for this transition
        Method: string
        /// Human-readable label (optional)
        Title: string option
        /// Roles that may use this transition. Empty list = available to all roles.
        Roles: string list
    }

/// One entry per (route, state) pair in the affordance map.
[<RequireQualifiedAccess>]
type AffordanceMapEntry =
    {
        /// HTTP route pattern
        RouteTemplate: string
        /// State name, or "*" for stateless resources
        StateKey: string
        /// HTTP methods available in this state
        AllowedMethods: string list
        /// Available transitions with relation types
        LinkRelations: AffordanceLinkRelation list
        /// URL to the ALPS profile for this resource
        ProfileUrl: string
    }

/// The complete affordance map with version metadata.
type AffordanceMap =
    {
        /// Schema version for forward compatibility
        Version: string
        /// All affordance entries
        Entries: AffordanceMapEntry list
    }

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

    /// IANA-registered "self" link relation. Filtered from affordance Link headers
    /// (not from body representations) because in Link header context the client
    /// already knows the URI it requested. GET is preserved in AllowedMethods.
    [<Literal>]
    let SelfRelation = "self"

    /// Build a composite lookup key from route template and state key.
    let lookupKey (routeTemplate: string) (stateKey: string) : string =
        String.Concat(routeTemplate, KeySeparator, stateKey)

    /// Build a composite lookup key from route template, state key, and role.
    let lookupKeyWithRole (routeTemplate: string) (stateKey: string) (role: string) : string =
        String.Concat(routeTemplate, KeySeparator, stateKey, KeySeparator, role)

    // "~authenticated" is used as a sentinel role key for the authenticated fallback entry.
    // Despite the name, this entry is also used for unauthenticated users (who get only
    // role-agnostic links). The name refers to the DI/lookup convention, not the auth state.
    /// Sentinel role name for "authenticated but no matching role" fallback entry.
    /// Contains links available to all roles (Roles = []) only.
    [<Literal>]
    let AuthenticatedFallbackRole = "~authenticated"

    /// Build a composite lookup key for authenticated users without a matching role.
    let lookupKeyAuthenticated (routeTemplate: string) (stateKey: string) : string =
        lookupKeyWithRole routeTemplate stateKey AuthenticatedFallbackRole

    /// Try to find an entry in the affordance map by route and state.
    /// O(n) scan — suitable for tests/startup. Runtime uses pre-computed Dictionary.
    let tryFind (routeTemplate: string) (stateKey: string) (map: AffordanceMap) : AffordanceMapEntry option =
        let key = lookupKey routeTemplate stateKey

        map.Entries
        |> List.tryFind (fun e -> lookupKey e.RouteTemplate e.StateKey = key)

    /// Derive the ALPS profile URL from a base URI and resource slug.
    let profileUrl (baseUri: string) (slug: string) : string =
        let trimmed = baseUri.TrimEnd('/')
        sprintf "%s/%s" trimmed slug

    /// Build link relations from (method, linkRelation) pairs.
    /// Filters out rel="self" which is informationally vacuous in Link header context —
    /// the client already knows the URI it requested. GET is preserved in AllowedMethods.
    /// Shared by runtime (RuntimeHttpCapability) and CLI (HttpCapability) paths.
    let buildLinkRelations
        (routeTemplate: string)
        (capabilities: (string * string) list)
        : AffordanceLinkRelation list =
        capabilities
        |> List.choose (fun (method, linkRelation) ->
            if linkRelation = SelfRelation then
                None
            else
                Some
                    { Rel = linkRelation
                      Href = routeTemplate
                      Method = method
                      Title = None
                      Roles = [] })

    /// Build affordance map entries for a single runtime resource.
    let private buildEntries (resource: RuntimeResource) (baseUri: string) : AffordanceMapEntry list =
        let profile = profileUrl baseUri resource.ResourceSlug

        match resource.Statechart.StateNames with
        | [] ->
            let methods =
                resource.HttpCapabilities
                |> List.map _.Method
                |> fun ms -> "HEAD" :: "OPTIONS" :: ms
                |> List.distinct
                |> List.sort

            let linkRels =
                buildLinkRelations
                    resource.RouteTemplate
                    (resource.HttpCapabilities |> List.map (fun c -> c.Method, c.LinkRelation))

            [ { RouteTemplate = resource.RouteTemplate
                StateKey = WildcardStateKey
                AllowedMethods = methods
                LinkRelations = linkRels
                ProfileUrl = profile } ]
        | stateNames ->
            stateNames
            |> List.map (fun stateName ->
                let capsForState =
                    resource.HttpCapabilities
                    |> List.filter (fun cap -> cap.StateKey = WildcardStateKey || cap.StateKey = stateName)

                let methods =
                    capsForState
                    |> List.map _.Method
                    |> fun ms -> "HEAD" :: "OPTIONS" :: ms
                    |> List.distinct
                    |> List.sort

                let linkRels =
                    buildLinkRelations
                        resource.RouteTemplate
                        (capsForState |> List.map (fun c -> c.Method, c.LinkRelation))

                { RouteTemplate = resource.RouteTemplate
                  StateKey = stateName
                  AllowedMethods = methods
                  LinkRelations = linkRels
                  ProfileUrl = profile })

    /// Generate an AffordanceMap from runtime resource data at startup.
    let generateFromResources (resources: RuntimeResource list) (baseUri: string) : AffordanceMap =
        let entries = resources |> List.collect (fun r -> buildEntries r baseUri)

        { Version = currentVersion
          Entries = entries }

    /// Load an AffordanceMap from a RuntimeState.
    let fromRuntimeState (state: RuntimeState) : AffordanceMap =
        generateFromResources state.Resources state.BaseUri

    /// Convert a PascalCase or camelCase identifier to kebab-case.
    /// "AuthorizePayment" → "authorize-payment", "getHTMLParser" → "get-html-parser"
    let toKebabCase (name: string) : string =
        if System.String.IsNullOrEmpty(name) then
            name
        else
            let sb = System.Text.StringBuilder(name.Length + 2)

            for i in 0 .. name.Length - 1 do
                let c = name.[i]

                if i > 0 && System.Char.IsUpper(c) then
                    let prevIsLower = System.Char.IsLower(name.[i - 1])

                    let isEndOfAcronym =
                        i >= 2
                        && System.Char.IsUpper(name.[i - 1])
                        && System.Char.IsUpper(name.[i - 2])
                        && i + 1 < name.Length
                        && System.Char.IsLower(name.[i + 1])

                    if prevIsLower || isEndOfAcronym then
                        sb.Append('-') |> ignore

                sb.Append(System.Char.ToLowerInvariant(c)) |> ignore

            sb.ToString()

    // Pre-computed AllowedMethods lists including HEAD per RFC 9110 §9.3.2.
    let private safeOnlyMethods = [ "GET"; "HEAD"; "OPTIONS" ]
    let private safeUnsafeMethods = [ "GET"; "HEAD"; "OPTIONS"; "POST" ]
    let private safeIdempotentMethods = [ "GET"; "HEAD"; "OPTIONS"; "PUT" ]
    let private safeUnsafeIdempotentMethods = [ "GET"; "HEAD"; "OPTIONS"; "POST"; "PUT" ]

    /// Generate an AffordanceMap from an ExtractedStatechart for CE-based apps.
    /// Each transition becomes an AffordanceLinkRelation with Roles derived from RoleConstraint.
    /// Method derived from TransitionSafety: Safe→GET, Unsafe→POST, Idempotent→PUT.
    /// Rel is an ALPS profile fragment URI: "{profileUrl}#{EventName}".
    /// Roles with no transitions from a state get a "monitor" GET rel (IANA RFC 5765).
    let fromStatechart (baseUri: string) (sc: ExtractedStatechart) : AffordanceMap =
        let slug = ResourceModel.resourceSlug sc.RouteTemplate
        let profile = profileUrl baseUri slug

        let entries =
            sc.StateNames
            |> List.map (fun stateKey ->
                let transitions =
                    sc.Transitions
                    |> List.filter (fun t -> t.Source = stateKey)
                    |> List.filter (fun t ->
                        match t.Constraint with
                        | RestrictedTo [] -> false // dead transition: no role can trigger it
                        | _ -> true)

                let transitionRels =
                    transitions
                    |> List.map (fun t ->
                        let roles =
                            match t.Constraint with
                            | Unrestricted -> []
                            | RestrictedTo r -> r

                        let method =
                            match t.Safety with
                            | Safe -> "GET"
                            | Unsafe -> "POST"
                            | Idempotent -> "PUT"

                        { AffordanceLinkRelation.Rel = sprintf "%s#%s" profile t.Event
                          AffordanceLinkRelation.Href = sc.RouteTemplate
                          AffordanceLinkRelation.Method = method
                          AffordanceLinkRelation.Title = None
                          AffordanceLinkRelation.Roles = roles })

                // MayPoll: roles with no transitions from this state get a monitor GET rel.
                // A role "has transitions" if any transition is Unrestricted or RestrictedTo includes the role.
                let hasUnrestricted = transitions |> List.exists (fun t -> t.Constraint = Unrestricted)

                let monitorRels =
                    if hasUnrestricted then
                        [] // All roles participate in unrestricted transitions
                    else
                        sc.Roles
                        |> List.filter (fun role ->
                            transitions
                            |> List.exists (fun t ->
                                match t.Constraint with
                                | RestrictedTo roles -> roles |> List.contains role.Name
                                | Unrestricted -> true)
                            |> not)
                        |> List.map (fun role ->
                            { AffordanceLinkRelation.Rel = "monitor"
                              AffordanceLinkRelation.Href = sc.RouteTemplate
                              AffordanceLinkRelation.Method = "GET"
                              AffordanceLinkRelation.Title = None
                              AffordanceLinkRelation.Roles = [ role.Name ] })

                let linkRelations = transitionRels @ monitorRels

                let allowedMethods =
                    let hasUnsafe = transitions |> List.exists (fun t -> t.Safety = Unsafe)
                    let hasIdempotent = transitions |> List.exists (fun t -> t.Safety = Idempotent)

                    match hasUnsafe, hasIdempotent with
                    | true, true -> safeUnsafeIdempotentMethods
                    | true, false -> safeUnsafeMethods
                    | false, true -> safeIdempotentMethods
                    | false, false -> safeOnlyMethods

                { AffordanceMapEntry.RouteTemplate = sc.RouteTemplate
                  AffordanceMapEntry.StateKey = stateKey
                  AffordanceMapEntry.AllowedMethods = allowedMethods
                  AffordanceMapEntry.LinkRelations = linkRelations
                  AffordanceMapEntry.ProfileUrl = profile })

        { Version = currentVersion
          Entries = entries }
