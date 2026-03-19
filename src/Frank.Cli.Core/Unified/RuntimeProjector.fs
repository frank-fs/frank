module Frank.Cli.Core.Unified.RuntimeProjector

open Frank.Affordances
open Frank.Cli.Core.Statechart

/// Project a CLI-only HttpCapability to a runtime-safe RuntimeHttpCapability.
let private toRuntimeCapability (cap: HttpCapability) : RuntimeHttpCapability =
    { Method = cap.Method
      StateKey = cap.StateKey |> Option.defaultValue AffordanceMap.WildcardStateKey
      LinkRelation = cap.LinkRelation
      IsSafe = cap.IsSafe }

/// Project a CLI-only StateInfo to a runtime-safe RuntimeStateInfo.
let private toRuntimeStateInfo (info: Frank.Statecharts.StateInfo) : RuntimeStateInfo =
    { AllowedMethods = info.AllowedMethods
      IsFinal = info.IsFinal
      Description = info.Description |> Option.defaultValue "" }

/// Project an ExtractedStatechart to a RuntimeStatechart.
let private toRuntimeStatechart (sc: ExtractedStatechart) : RuntimeStatechart =
    { StateNames = sc.StateNames
      InitialStateKey = sc.InitialStateKey
      GuardNames = sc.GuardNames
      StateMetadata = sc.StateMetadata |> Map.map (fun _ v -> toRuntimeStateInfo v) }

/// Project a CLI-only UnifiedResource to a runtime-safe RuntimeResource.
let toRuntimeResource (resource: UnifiedResource) : RuntimeResource =
    { RouteTemplate = resource.RouteTemplate
      ResourceSlug = resource.ResourceSlug
      Statechart =
        match resource.Statechart with
        | Some sc -> toRuntimeStatechart sc
        | None -> RuntimeStatechart.empty
      HttpCapabilities =
        resource.HttpCapabilities |> List.map toRuntimeCapability }

/// Project a full UnifiedExtractionState to RuntimeState with empty profiles.
let toRuntimeState (unified: UnifiedExtractionState) : RuntimeState =
    { Resources = unified.Resources |> List.map toRuntimeResource
      BaseUri = unified.BaseUri
      Profiles = ProjectedProfiles.empty }

/// Project a full UnifiedExtractionState to RuntimeState with pre-computed profiles.
let toRuntimeStateWithProfiles (unified: UnifiedExtractionState) (profiles: ProjectedProfiles) : RuntimeState =
    { Resources = unified.Resources |> List.map toRuntimeResource
      BaseUri = unified.BaseUri
      Profiles = profiles }
