namespace Frank.Affordances

open System
open System.Reflection
open Frank.Resources.Model
open MessagePack
open MessagePack.Resolvers
open MessagePack.FSharp

module StartupProjection =

    /// Embedded resource logical name for the unified state binary.
    [<Literal>]
    let DefaultEmbeddedResourceName = "Frank.Descriptors.bin"

    /// MessagePack deserialization options matching the CLI's serialization.
    let private msgpackOptions =
        MessagePackSerializerOptions
            .Standard
            .WithResolver(
                CompositeResolver.Create(
                    FSharpResolver.Instance,
                    ContractlessStandardResolver.Instance))

    /// Project a HttpCapability to a RuntimeHttpCapability.
    let private projectCapability (cap: HttpCapability) : RuntimeHttpCapability =
        { Method = cap.Method
          StateKey = cap.StateKey |> Option.defaultValue AffordanceMap.WildcardStateKey
          LinkRelation = cap.LinkRelation
          IsSafe = cap.IsSafe }

    /// Project a UnifiedResource to a RuntimeResource.
    let private projectResource (resource: UnifiedResource) : RuntimeResource =
        { RouteTemplate = resource.RouteTemplate
          ResourceSlug = resource.ResourceSlug
          Statechart =
            match resource.Statechart with
            | Some sc ->
                { StateNames = sc.StateNames
                  InitialStateKey = sc.InitialStateKey
                  GuardNames = sc.GuardNames
                  StateMetadata =
                    sc.StateMetadata
                    |> Map.map (fun _ (v: StateInfo) ->
                        { AllowedMethods = v.AllowedMethods
                          IsFinal = v.IsFinal
                          Description = v.Description |> Option.defaultValue "" }) }
            | None -> RuntimeStatechart.empty
          HttpCapabilities = resource.HttpCapabilities |> List.map projectCapability }

    /// Load the UnifiedExtractionState from the embedded binary resource.
    /// Returns None if the resource is not found or cannot be deserialized.
    let loadUnifiedStateFromAssembly (assembly: Assembly) : UnifiedExtractionState option =
        let resourceName =
            assembly.GetManifestResourceNames()
            |> Array.tryFind (fun name ->
                name.EndsWith("Descriptors.bin", StringComparison.OrdinalIgnoreCase))

        match resourceName with
        | None -> None
        | Some name ->
            try
                use stream = assembly.GetManifestResourceStream(name)

                if isNull stream then
                    None
                else
                    let state = MessagePackSerializer.Deserialize<UnifiedExtractionState>(stream, msgpackOptions)
                    Some state
            with _ ->
                None

    /// Load an AffordanceMap from the unified state embedded in the given assembly.
    /// Deserializes the binary, projects resources to runtime types, and generates
    /// the AffordanceMap. Returns None if no unified state is found.
    let loadAffordanceMapFromAssembly (assembly: Assembly) : AffordanceMap option =
        match loadUnifiedStateFromAssembly assembly with
        | None -> None
        | Some state ->
            let runtimeResources = state.Resources |> List.map projectResource
            Some(AffordanceMap.generateFromResources runtimeResources state.BaseUri)

    /// Load a full RuntimeState from the unified state embedded in the given assembly.
    /// Includes both runtime resource data (for AffordanceMap/statechart format generation)
    /// and pre-computed profile strings (ALPS, OWL, SHACL, JSON Schema).
    let loadRuntimeStateFromAssembly (assembly: Assembly) : RuntimeState option =
        match loadUnifiedStateFromAssembly assembly with
        | None -> None
        | Some state ->
            let runtimeResources = state.Resources |> List.map projectResource

            Some
                { Resources = runtimeResources
                  BaseUri = state.BaseUri
                  Profiles = state.Profiles }
