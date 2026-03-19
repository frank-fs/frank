namespace Frank.Affordances

/// Pre-generated profile strings for all formats, keyed by resource slug.
/// All formats are pre-generated at CLI time (by frank-cli extract).
/// The runtime deserializes this record and serves the strings directly --
/// no dotNetRdf, FCS, or other CLI-only dependencies at runtime.
type ProjectedProfiles =
    { /// ALPS JSON per resource slug, served at GET /alps/{slug}
      AlpsProfiles: Map<string, string>
      /// OWL Turtle per resource slug, served at GET /ontology/{slug}
      OwlOntologies: Map<string, string>
      /// SHACL Turtle per resource slug, served at GET /shapes/{slug}
      ShaclShapes: Map<string, string>
      /// JSON Schema per resource slug, served at GET /schemas/{slug}
      JsonSchemas: Map<string, string> }

module ProjectedProfiles =

    /// Empty profiles for when no embedded resource is available.
    let empty: ProjectedProfiles =
        { AlpsProfiles = Map.empty
          OwlOntologies = Map.empty
          ShaclShapes = Map.empty
          JsonSchemas = Map.empty }

    /// Check whether the projected profiles have any content.
    let isEmpty (profiles: ProjectedProfiles) : bool =
        Map.isEmpty profiles.AlpsProfiles
        && Map.isEmpty profiles.OwlOntologies
        && Map.isEmpty profiles.ShaclShapes
        && Map.isEmpty profiles.JsonSchemas

/// HTTP capability data for runtime affordance map generation.
/// Projected from CLI-only HttpCapability at compile time.
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
/// Mirrors Frank.Statecharts.StateInfo but avoids option types for JSON serialization.
type RuntimeStateInfo =
    { /// HTTP methods allowed in this state
      AllowedMethods: string list
      /// Whether this is a final/terminal state
      IsFinal: bool
      /// Human-readable description (empty string if none)
      Description: string }

/// Statechart data for runtime format generation (smcat, SCXML, XState, WSD).
/// Contains enough data for FormatPipeline.buildDocumentFromExtracted equivalent.
type RuntimeStatechart =
    { /// All state names
      StateNames: string list
      /// The initial state key
      InitialStateKey: string
      /// Guard identifiers
      GuardNames: string list
      /// Per-state metadata (AllowedMethods, IsFinal, Description)
      StateMetadata: Map<string, RuntimeStateInfo> }

/// Resource data needed to generate affordance map and statechart formats at runtime.
/// Projected from UnifiedResource at CLI compile time — drops AnalyzedType,
/// DerivedFields, and other CLI-only data.
type RuntimeResource =
    { /// HTTP route pattern (e.g. "/games/{gameId}")
      RouteTemplate: string
      /// URL path segment for profiles (e.g. "games")
      ResourceSlug: string
      /// Statechart data for format generation. Empty StateNames = stateless resource.
      Statechart: RuntimeStatechart
      /// HTTP capabilities with state scoping and link relations
      HttpCapabilities: RuntimeHttpCapability list }

module RuntimeStatechart =

    /// Empty statechart for stateless resources.
    let empty: RuntimeStatechart =
        { StateNames = []
          InitialStateKey = ""
          GuardNames = []
          StateMetadata = Map.empty }

    /// Check whether this represents a stateless resource.
    let isEmpty (sc: RuntimeStatechart) : bool = sc.StateNames.IsEmpty

/// Complete runtime state loaded from embedded resource at startup.
/// Contains resource data for AffordanceMap and statechart format generation
/// at runtime, plus pre-computed profile strings for formats needing heavy
/// dependencies (dotNetRdf, FCS) that cannot run at runtime.
type RuntimeState =
    { /// Resources for AffordanceMap and statechart format generation
      Resources: RuntimeResource list
      /// Base URI for ALPS profile namespace
      BaseUri: string
      /// Pre-computed profile strings (ALPS, OWL, SHACL, JSON Schema)
      Profiles: ProjectedProfiles }
