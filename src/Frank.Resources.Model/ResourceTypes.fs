namespace Frank.Resources.Model

open System

/// Metadata about a single state (HTTP configuration).
type StateInfo =
    { AllowedMethods: string list
      IsFinal: bool
      Description: string option }

/// Lightweight parent-child containment information for hierarchy-aware analysis.
/// Zero-dependency: just string state names. Built from the richer StateHierarchy
/// in Frank.Statecharts when hierarchy info is available.
///
/// Lives in Frank.Resources.Model (zero-dependency layer) because both Projection.fs
/// (same assembly) and Frank.Statecharts/Analysis need it. Moving to Frank.Statecharts
/// would create a circular dependency; moving to Frank.Statecharts.Core would force
/// Frank.Resources.Model to take a dependency on it, breaking its zero-dep guarantee.
///
/// Design trade-off: does not carry XOR/AND composite kind information.
/// This is sufficient for current analyses (livelock detection only needs
/// "do descendants progress?" which is answerable from containment alone)
/// but insufficient for richer analyses that require distinguishing exclusive
/// vs parallel composition (e.g., AND-state synchronization checks).
/// The full CompositeKind is available in Frank.Statecharts.StateHierarchy
/// for analyses that need it.
type StateContainment =
    {
        /// Parent state -> ordered list of child states.
        ParentOf: Map<string, string list>
        /// Child state -> parent state.
        ChildOf: Map<string, string>
    }

module StateContainment =

    let empty: StateContainment =
        { ParentOf = Map.empty
          ChildOf = Map.empty }

    let isEmpty (c: StateContainment) : bool = Map.isEmpty c.ParentOf

    /// Build a StateContainment from a list of (parent, children) pairs.
    let ofPairs (pairs: (string * string list) list) : StateContainment =
        let parentOf = pairs |> Map.ofList

        let childOf =
            pairs
            |> List.collect (fun (parent, children) -> children |> List.map (fun child -> (child, parent)))
            |> Map.ofList

        { ParentOf = parentOf
          ChildOf = childOf }

    /// Get all children of a state (empty if atomic).
    let children (state: string) (c: StateContainment) : string list =
        c.ParentOf |> Map.tryFind state |> Option.defaultValue []

    /// Get the parent of a state (None if root-level).
    let parent (state: string) (c: StateContainment) : string option = c.ChildOf |> Map.tryFind state

    /// Check if a state is a composite (has children).
    let isComposite (state: string) (c: StateContainment) : bool = c.ParentOf |> Map.containsKey state

    /// Get all descendants of a state (recursive).
    let rec allDescendants (state: string) (c: StateContainment) : string list =
        children state c |> List.collect (fun child -> child :: allDescendants child c)

/// Pre-generated profile strings for all formats, keyed by resource slug.
type ProjectedProfiles =
    {
        /// ALPS JSON per resource slug
        AlpsProfiles: Map<string, string>
        /// Per-role ALPS profiles keyed by slug (e.g., "games-playerx" → JSON).
        RoleAlpsProfiles: Map<string, string>
        /// OWL Turtle per resource slug
        OwlOntologies: Map<string, string>
        /// SHACL Turtle per resource slug
        ShaclShapes: Map<string, string>
        /// JSON Schema per resource slug
        JsonSchemas: Map<string, string>
    }

module ProjectedProfiles =

    let empty: ProjectedProfiles =
        { AlpsProfiles = Map.empty
          RoleAlpsProfiles = Map.empty
          OwlOntologies = Map.empty
          ShaclShapes = Map.empty
          JsonSchemas = Map.empty }

    let isEmpty (profiles: ProjectedProfiles) : bool =
        Map.isEmpty profiles.AlpsProfiles
        && Map.isEmpty profiles.RoleAlpsProfiles
        && Map.isEmpty profiles.OwlOntologies
        && Map.isEmpty profiles.ShaclShapes
        && Map.isEmpty profiles.JsonSchemas

/// Portable, zero-dependency role representation for spec pipeline consumers.
/// Hierarchy-neutral — does not assume flat or hierarchical state structures.
[<Struct>]
type RoleInfo =
    { Name: string
      Description: string option }

/// Whether a transition is available to all roles or restricted to specific roles.
///
/// MPST terminology note (issue #244): Unrestricted = shared-input, NOT broadcast.
/// Shared-input: any single role may trigger the transition independently (first-come
/// semantics; only one role's trigger is observed by the server per transition firing).
/// Broadcast: the server sends a message to ALL roles simultaneously.
/// These are distinct MPST patterns: shared-input is input choice, broadcast is output.
/// Using "broadcast" here would incorrectly imply the server initiates and sends to all.
///
/// RestrictedTo = directed message: only the listed roles may trigger this transition.
type RoleConstraint =
    | Unrestricted
    | RestrictedTo of string list

/// HTTP method safety classification per RFC 9110 §9.2.1 and ALPS descriptor type.
type TransitionSafety =
    /// Read-only, no side effects. Maps to GET.
    | Safe
    /// Side effects, not idempotent. Maps to POST.
    | Unsafe
    /// Side effects, idempotent (replacement semantics). Maps to PUT.
    | Idempotent

/// A single transition in the extracted statechart.
/// Domain-neutral: no ALPS/SCXML/format-specific concepts.
/// Pre-resolved: extraction resolves guard + state → RoleConstraint eagerly.
type TransitionSpec =
    {
        /// Semantic transition name (e.g., "makeMove", "getGame").
        Event: string
        /// Source state key (e.g., "XTurn").
        Source: string
        /// Target state key (e.g., "OTurn").
        Target: string
        /// Guard name controlling this transition (None = unguarded).
        /// Retained for diagnostics and cross-validation, not used by projection.
        Guard: string option
        /// Pre-resolved role constraint. RestrictedTo [] = dead transition (no role can trigger).
        Constraint: RoleConstraint
        /// HTTP method safety classification. Default: Unsafe (POST).
        Safety: TransitionSafety
    }

/// Structured representation of a single stateful resource extracted from source.
/// String keys (StateNames, InitialStateKey, GuardNames) are F# DU case names
/// derived from the state type at compile time (e.g., "XTurn", "GameOver").
type ExtractedStatechart =
    {
        RouteTemplate: string
        StateNames: string list
        InitialStateKey: string
        GuardNames: string list
        StateMetadata: Map<string, StateInfo>
        Roles: RoleInfo list
        /// All transitions in the statechart. Populated during extraction.
        Transitions: TransitionSpec list
    }

/// HTTP capability for a resource, optionally scoped to a state.
type HttpCapability =
    {
        /// HTTP method (GET, POST, PUT, DELETE, PATCH)
        Method: string
        /// Which state this applies to (None = always available, for plain resources)
        StateKey: string option
        /// IANA or ALPS-derived link relation type URI
        LinkRelation: string
        /// true for GET/HEAD/OPTIONS (safe methods)
        IsSafe: bool
    }

/// Computed invariant checks for structure-behavior consistency.
type DerivedResourceFields =
    {
        /// State DU cases not covered by any inState call
        OrphanStates: string list
        /// DU cases in the state type but not in the statechart
        UnhandledCases: string list
        /// Per-state: which type fields are relevant
        StateStructure: Map<string, AnalyzedField list>
        /// Ratio of mapped types to total types (0.0-1.0)
        TypeCoverage: float
    }

/// A combined description of a single HTTP resource.
type UnifiedResource =
    {
        /// HTTP route pattern (e.g., /games/{gameId})
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
        DerivedFields: DerivedResourceFields
    }

/// The cached state persisted to binary.
type UnifiedExtractionState =
    {
        /// All extracted resources
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
        Profiles: ProjectedProfiles
    }

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
