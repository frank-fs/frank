module Frank.Cli.Core.Unified.RuntimeProjector

open Frank.Affordances

/// Project a CLI-only HttpCapability to a runtime-safe RuntimeHttpCapability.
let private toRuntimeCapability (cap: HttpCapability) : RuntimeHttpCapability =
    { Method = cap.Method
      StateKey = cap.StateKey |> Option.defaultValue AffordanceMap.WildcardStateKey
      LinkRelation = cap.LinkRelation
      IsSafe = cap.IsSafe }

/// Project a CLI-only UnifiedResource to a runtime-safe RuntimeResource.
let toRuntimeResource (resource: UnifiedResource) : RuntimeResource =
    { RouteTemplate = resource.RouteTemplate
      ResourceSlug = resource.ResourceSlug
      StateNames =
        match resource.Statechart with
        | Some sc -> sc.StateNames
        | None -> []
      HttpCapabilities =
        resource.HttpCapabilities |> List.map toRuntimeCapability }

/// Project a full UnifiedExtractionState to RuntimeState.
/// Pre-computed profile strings are empty — they will be populated
/// when the CLI also generates ALPS/OWL/SHACL/Schema profiles.
let toRuntimeState (unified: UnifiedExtractionState) : RuntimeState =
    { Resources = unified.Resources |> List.map toRuntimeResource
      BaseUri = unified.BaseUri
      Profiles = ProjectedProfiles.empty }
