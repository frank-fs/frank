namespace Frank.Resources.Model

/// HTTP capability data for runtime affordance map generation.
type RuntimeHttpCapability =
    { /// HTTP method (GET, POST, PUT, DELETE, PATCH)
      Method: string
      /// State key this capability applies to, or "*" for all states
      StateKey: string
      /// IANA or ALPS-derived link relation type URI
      LinkRelation: string
      /// true for GET/HEAD/OPTIONS (safe methods)
      IsSafe: bool }

/// Per-state metadata for statechart format generation.
/// This is a runtime-optimized projection of StateInfo. Key difference:
/// StateInfo.Description is string option (semantically correct — "not provided"
/// differs from "empty"). RuntimeStateInfo.Description is string (flattened via
/// Option.defaultValue "" in StartupProjection for zero-allocation runtime matching).
type RuntimeStateInfo =
    { /// HTTP methods allowed in this state
      AllowedMethods: string list
      /// Whether this is a final/terminal state
      IsFinal: bool
      /// Human-readable description, or empty string if none was provided.
      /// Flattened from StateInfo.Description (string option) for runtime efficiency.
      Description: string }

/// Statechart data for runtime format generation (smcat, SCXML, XState, WSD).
type RuntimeStatechart =
    { /// All state names
      StateNames: string list
      /// The initial state key
      InitialStateKey: string
      /// Guard identifiers
      GuardNames: string list
      /// Per-state metadata
      StateMetadata: Map<string, RuntimeStateInfo> }

/// Resource data for runtime AffordanceMap and statechart format generation.
type RuntimeResource =
    { /// HTTP route pattern (e.g. "/games/{gameId}")
      RouteTemplate: string
      /// URL path segment for profiles (e.g. "games")
      ResourceSlug: string
      /// Statechart data. Empty StateNames = stateless resource.
      Statechart: RuntimeStatechart
      /// HTTP capabilities with state scoping and link relations
      HttpCapabilities: RuntimeHttpCapability list }

module RuntimeStatechart =

    let empty: RuntimeStatechart =
        { StateNames = []
          InitialStateKey = ""
          GuardNames = []
          StateMetadata = Map.empty }

    let isEmpty (sc: RuntimeStatechart) : bool = sc.StateNames.IsEmpty

/// Complete runtime state loaded from embedded resource at startup.
type RuntimeState =
    { /// Resources for AffordanceMap and statechart format generation
      Resources: RuntimeResource list
      /// Base URI for ALPS profile namespace
      BaseUri: string
      /// Pre-computed profile strings (ALPS, OWL, SHACL, JSON Schema)
      Profiles: ProjectedProfiles }
