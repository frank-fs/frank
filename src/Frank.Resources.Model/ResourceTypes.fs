namespace Frank.Resources.Model

open System

/// Metadata about a single state (HTTP configuration).
type StateInfo =
    { AllowedMethods: string list
      IsFinal: bool
      Description: string option }

/// Pre-generated profile strings for all formats, keyed by resource slug.
type ProjectedProfiles =
    { /// ALPS JSON per resource slug
      AlpsProfiles: Map<string, string>
      /// OWL Turtle per resource slug
      OwlOntologies: Map<string, string>
      /// SHACL Turtle per resource slug
      ShaclShapes: Map<string, string>
      /// JSON Schema per resource slug
      JsonSchemas: Map<string, string> }

module ProjectedProfiles =

    let empty: ProjectedProfiles =
        { AlpsProfiles = Map.empty
          OwlOntologies = Map.empty
          ShaclShapes = Map.empty
          JsonSchemas = Map.empty }

    let isEmpty (profiles: ProjectedProfiles) : bool =
        Map.isEmpty profiles.AlpsProfiles
        && Map.isEmpty profiles.OwlOntologies
        && Map.isEmpty profiles.ShaclShapes
        && Map.isEmpty profiles.JsonSchemas

/// Structured representation of a single stateful resource extracted from source.
/// String keys (StateNames, InitialStateKey, GuardNames) are F# DU case names
/// derived from the state type at compile time (e.g., "XTurn", "GameOver").
type ExtractedStatechart =
    { RouteTemplate: string
      StateNames: string list
      InitialStateKey: string
      GuardNames: string list
      StateMetadata: Map<string, StateInfo> }

/// HTTP capability for a resource, optionally scoped to a state.
type HttpCapability =
    { /// HTTP method (GET, POST, PUT, DELETE, PATCH)
      Method: string
      /// Which state this applies to (None = always available, for plain resources)
      StateKey: string option
      /// IANA or ALPS-derived link relation type URI
      LinkRelation: string
      /// true for GET/HEAD/OPTIONS (safe methods)
      IsSafe: bool }

/// Computed invariant checks for structure-behavior consistency.
type DerivedResourceFields =
    { /// State DU cases not covered by any inState call
      OrphanStates: string list
      /// DU cases in the state type but not in the statechart
      UnhandledCases: string list
      /// Per-state: which type fields are relevant
      StateStructure: Map<string, AnalyzedField list>
      /// Ratio of mapped types to total types (0.0-1.0)
      TypeCoverage: float }

/// A combined description of a single HTTP resource.
type UnifiedResource =
    { /// HTTP route pattern (e.g., /games/{gameId})
      RouteTemplate: string
      /// Filename-safe slug derived from route (e.g., games)
      ResourceSlug: string
      /// F# types associated with this resource (records, DUs)
      TypeInfo: AnalyzedType list
      /// Behavioral data (None for plain resource CEs)
      Statechart: ExtractedStatechart option
      /// Methods available (globally or per-state)
      HttpCapabilities: HttpCapability list
      /// Computed invariant checks
      DerivedFields: DerivedResourceFields }

/// The cached state persisted to binary.
type UnifiedExtractionState =
    { /// All extracted resources
      Resources: UnifiedResource list
      /// Hash of source files for staleness detection
      SourceHash: string
      /// Base URI for ALPS profile namespace
      BaseUri: string
      /// Schema.org vocabularies used for alignment
      Vocabularies: string list
      /// Timestamp of extraction
      ExtractedAt: DateTimeOffset
      /// CLI version for cache compatibility
      ToolVersion: string
      /// Pre-computed profile strings for runtime serving (ALPS, OWL, SHACL, JSON Schema)
      Profiles: ProjectedProfiles }

module ResourceModel =

    /// Derive a filename-safe slug from a route template.
    /// "/games/{gameId}" -> "games", "/health" -> "health"
    let resourceSlug (routeTemplate: string) : string =
        routeTemplate.TrimStart('/')
        |> fun s ->
            match s.IndexOf('/') with
            | -1 -> s
            | i -> s.Substring(0, i)
        |> fun s ->
            match s.IndexOf('{') with
            | -1 -> s
            | i -> s.Substring(0, i).TrimEnd('/')
        |> fun s -> if String.IsNullOrEmpty(s) then "root" else s

    /// Empty derived fields for resources without statecharts.
    let emptyDerivedFields: DerivedResourceFields =
        { OrphanStates = []
          UnhandledCases = []
          StateStructure = Map.empty
          TypeCoverage = 1.0 }
