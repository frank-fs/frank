module Frank.Cli.Core.Unified.ProjectionPipeline

open Frank.Resources.Model

/// Filename-safe slug for a role-scoped profile (e.g., "games-playerx").
let roleSlug (resourceSlug: string) (roleName: string) : string =
    $"{resourceSlug}-{roleName.ToLowerInvariant()}"

/// Keep only capabilities whose state key exists in the projected statechart
/// (or that have no state key at all, meaning they apply globally).
let filterCapabilitiesByStates (stateNames: string list) (capabilities: HttpCapability list) : HttpCapability list =
    let stateSet = Set.ofList stateNames

    capabilities
    |> List.filter (fun cap ->
        match cap.StateKey with
        | None -> true
        | Some sk -> Set.contains sk stateSet)

/// Filter capabilities by projected transitions: only keep unsafe capabilities
/// (POST, PUT, PATCH, DELETE) in states where the role has an unsafe transition.
/// Safe capabilities (GET, HEAD, OPTIONS) always survive — all roles can observe.
let filterCapabilitiesByTransitions
    (transitions: TransitionSpec list)
    (capabilities: HttpCapability list)
    : HttpCapability list =
    // States where this role has an unsafe (non-self-loop) transition
    let unsafeTransitionSources =
        transitions
        |> List.filter (fun t -> t.Source <> t.Target) // exclude self-loops (getGame)
        |> List.map _.Source
        |> Set.ofList

    capabilities
    |> List.filter (fun cap ->
        if cap.IsSafe then
            true // Safe methods always available (observation)
        else
            match cap.StateKey with
            | None -> true // No state scope — keep it
            | Some sk -> Set.contains sk unsafeTransitionSources)

/// Result of running the projection pipeline for a single resource.
type ProjectionResult =
    { ResourceSlug: string
      RoleProfiles: Map<string, string>
      RoleNameByProfileSlug: Map<string, string>
      UpdatedGlobalProfile: string option
      Orphans: TransitionSpec list
      Errors: string list }

/// Run per-role projection for a resource that has roles and transitions.
/// Generates a role-scoped ALPS profile per role, checks for orphaned
/// transitions, and regenerates the global profile with cross-links.
/// Pure: errors and orphans are captured in the result, not printed.
let projectResource (resource: UnifiedResource) (baseUri: string) : ProjectionResult =
    match resource.Statechart with
    | Some sc when not sc.Roles.IsEmpty && not sc.Transitions.IsEmpty ->
        let projections = Projection.projectAll sc

        let roleSlugs =
            projections
            |> Map.toList
            |> List.map (fun (roleName, _) -> roleSlug resource.ResourceSlug roleName)

        let roleProfiles, roleNameMap, errors =
            projections
            |> Map.toList
            |> List.fold
                (fun (profiles, names, errs) (roleName, projectedChart) ->
                    let slug = roleSlug resource.ResourceSlug roleName

                    let projectedResource =
                        { resource with
                            ResourceSlug = slug
                            Statechart = Some projectedChart
                            HttpCapabilities =
                                resource.HttpCapabilities
                                |> filterCapabilitiesByStates projectedChart.StateNames
                                |> filterCapabilitiesByTransitions projectedChart.Transitions }

                    let ctx: UnifiedAlpsGenerator.ProjectionContext =
                        { ProjectedRole = Some roleName
                          RelatedProfileSlugs = []
                          ProfileSlug = Some resource.ResourceSlug }

                    match UnifiedAlpsGenerator.generateWithContext projectedResource baseUri (Some ctx) with
                    | Ok alpsJson -> (Map.add slug alpsJson profiles, Map.add slug roleName names, errs)
                    | Error genErrs ->
                        let detail = String.concat "; " genErrs
                        let msg = $"Warning: failed to generate role profile for {slug}: {detail}"
                        (profiles, names, msg :: errs))
                (Map.empty, Map.empty, [])

        let orphans = Projection.findOrphanedTransitions sc projections

        let globalCtx: UnifiedAlpsGenerator.ProjectionContext =
            { ProjectedRole = None
              RelatedProfileSlugs = roleSlugs
              ProfileSlug = None }

        let updatedGlobal, globalErrors =
            match UnifiedAlpsGenerator.generateWithContext resource baseUri (Some globalCtx) with
            | Ok alpsJson -> (Some alpsJson, [])
            | Error genErrs ->
                let detail = String.concat "; " genErrs

                let msg =
                    $"Warning: failed to regenerate global profile for {resource.ResourceSlug}: {detail}"

                (None, [ msg ])

        { ResourceSlug = resource.ResourceSlug
          RoleProfiles = roleProfiles
          RoleNameByProfileSlug = roleNameMap
          UpdatedGlobalProfile = updatedGlobal
          Orphans = orphans
          Errors = List.rev errors @ globalErrors }

    | _ ->
        { ResourceSlug = resource.ResourceSlug
          RoleProfiles = Map.empty
          RoleNameByProfileSlug = Map.empty
          UpdatedGlobalProfile = None
          Orphans = []
          Errors = [] }

/// Aggregate result from projecting all resources.
type BatchProjectionResult =
    { RoleAlpsProfiles: Map<string, string>
      GlobalProfileOverrides: Map<string, string>
      ProjectedSlugs: Set<string> }

/// Run projection across all resources, merging role profiles and tracking
/// which global slugs were overridden. Callers handle warning output.
let projectAllResources
    (resources: UnifiedResource list)
    (baseUri: string)
    : BatchProjectionResult * ProjectionResult list =
    let results =
        resources
        |> List.map (fun resource -> resource, projectResource resource baseUri)

    let roleAcc, globalAcc, slugAcc =
        results
        |> List.fold
            (fun (roles, globals, slugs) (resource, result) ->
                let roles' = result.RoleProfiles |> Map.fold (fun acc k v -> Map.add k v acc) roles

                let globals', slugs' =
                    match result.UpdatedGlobalProfile with
                    | Some profile ->
                        (Map.add resource.ResourceSlug profile globals, Set.add resource.ResourceSlug slugs)
                    | None -> (globals, slugs)

                (roles', globals', slugs'))
            (Map.empty, Map.empty, Set.empty)

    let allResults = results |> List.map snd

    ({ RoleAlpsProfiles = roleAcc
       GlobalProfileOverrides = globalAcc
       ProjectedSlugs = slugAcc },
     allResults)
