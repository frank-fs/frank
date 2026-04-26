module Frank.Datastar.AffordanceHelper

/// A link relation entry from the affordance map.
type AffordanceLinkRelation =
    { Rel: string
      Href: string
      Method: string
      Title: string option }

/// A single entry in the affordance map.
type AffordanceMapEntry =
    { AllowedMethods: string list
      LinkRelations: AffordanceLinkRelation list
      ProfileUrl: string option }

/// The affordance map: keyed by composite "{routeTemplate}|{stateKey}" strings.
type AffordanceMap =
    { Version: string
      BaseUri: string
      Entries: Map<string, AffordanceMapEntry> }

/// Result of an affordance lookup. Provides both raw data and convenience booleans.
type AffordanceResult =
    { AllowedMethods: string list
      LinkRelations: AffordanceLinkRelation list
      CanGet: bool
      CanPost: bool
      CanPut: bool
      CanDelete: bool
      CanPatch: bool }

// DESIGN: Permissive default (FR-024)
//
// When the affordance map is not loaded or a key is not found, ALL methods
// are reported as available. This ensures that:
//   1. Applications work without an affordance map (graceful degradation)
//   2. New states added to code but not yet in the map still show controls
//   3. The helper never causes a "broken" UI by hiding all actions
//
// The only way to restrict methods is to have them explicitly listed in the
// affordance map. Absence = permissive. Presence = authoritative.

/// Default when no affordance map is loaded or key not found.
/// All methods are permitted -- never hide controls when data is missing.
let private permissiveDefault: AffordanceResult =
    { AllowedMethods = [ "GET"; "POST"; "PUT"; "DELETE"; "PATCH" ]
      LinkRelations = []
      CanGet = true
      CanPost = true
      CanPut = true
      CanDelete = true
      CanPatch = true }

/// Convert an AffordanceMapEntry to an AffordanceResult with computed booleans.
let private entryToResult (entry: AffordanceMapEntry) : AffordanceResult =
    let methods = entry.AllowedMethods |> List.map (fun m -> m.ToUpperInvariant())

    { AllowedMethods = entry.AllowedMethods
      LinkRelations = entry.LinkRelations
      CanGet = List.contains "GET" methods
      CanPost = List.contains "POST" methods
      CanPut = List.contains "PUT" methods
      CanDelete = List.contains "DELETE" methods
      CanPatch = List.contains "PATCH" methods }

/// Build the composite key used to look up entries in the affordance map.
let private compositeKey (routeTemplate: string) (stateKey: string) : string = $"{routeTemplate}|{stateKey}"

/// Look up the available affordances for a given route template and state key.
/// Returns AffordanceResult with available methods and convenience booleans.
/// If the map is None or the key is not found, returns a permissive default.
let affordancesFor (routeTemplate: string) (stateKey: string) (map: AffordanceMap option) : AffordanceResult =
    match map with
    | None -> permissiveDefault
    | Some m ->
        let key = compositeKey routeTemplate stateKey

        match Map.tryFind key m.Entries with
        | Some entry -> entryToResult entry
        | None ->
            // Try wildcard key for stateless resources
            let wildcardKey = compositeKey routeTemplate "*"

            match Map.tryFind wildcardKey m.Entries with
            | Some entry -> entryToResult entry
            | None -> permissiveDefault
