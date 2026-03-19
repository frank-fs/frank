namespace Frank.Affordances

open System
open System.IO
open System.Reflection
open System.Text.Json

module StartupProjection =

    /// Default embedded resource name for the projected profiles JSON.
    [<Literal>]
    let DefaultEmbeddedResourceName = "projected-profiles.json"

    /// JSON serializer options for reading/writing projected profiles.
    let private jsonOptions =
        let opts = JsonSerializerOptions(JsonSerializerDefaults.Web)
        opts.WriteIndented <- false
        opts

    /// Serialize projected profiles to a JSON string.
    /// Used by frank-cli at extraction time to generate the embedded resource.
    let serialize (profiles: ProjectedProfiles) : string =
        JsonSerializer.Serialize(profiles, jsonOptions)

    /// Deserialize projected profiles from a JSON string.
    let deserialize (json: string) : ProjectedProfiles option =
        try
            let profiles = JsonSerializer.Deserialize<ProjectedProfiles>(json, jsonOptions)
            Some profiles
        with
        | :? JsonException -> None
        | :? ArgumentNullException -> None

    /// Load projected profiles from an embedded resource in the given assembly.
    /// Returns ProjectedProfiles.empty if the embedded resource is not found.
    let loadFromAssembly (assembly: Assembly) : ProjectedProfiles =
        let resourceName =
            assembly.GetManifestResourceNames()
            |> Array.tryFind (fun name -> name.EndsWith(DefaultEmbeddedResourceName, StringComparison.OrdinalIgnoreCase))

        match resourceName with
        | None -> ProjectedProfiles.empty
        | Some name ->
            use stream = assembly.GetManifestResourceStream(name)

            if isNull stream then
                ProjectedProfiles.empty
            else
                use reader = new StreamReader(stream)
                let json = reader.ReadToEnd()

                match deserialize json with
                | Some profiles -> profiles
                | None -> ProjectedProfiles.empty

    /// Load projected profiles from a JSON file on disk.
    /// Useful for development/testing when the embedded resource is not yet built.
    let loadFromFile (filePath: string) : ProjectedProfiles option =
        if File.Exists(filePath) then
            let json = File.ReadAllText(filePath)
            deserialize json
        else
            None

    // ══════════════════════════════════════════════════════════════════════════
    // RuntimeState: full startup state with resource data for AffordanceMap
    // generation plus pre-computed profile strings.
    // ══════════════════════════════════════════════════════════════════════════

    /// Default embedded resource name for the runtime state JSON.
    [<Literal>]
    let DefaultRuntimeStateResourceName = "runtime-state.json"

    /// Serialize runtime state to a JSON string.
    /// Used by frank-cli at compile time to generate the embedded resource.
    let serializeRuntimeState (state: RuntimeState) : string =
        JsonSerializer.Serialize(state, jsonOptions)

    /// Deserialize runtime state from a JSON string.
    let deserializeRuntimeState (json: string) : RuntimeState option =
        try
            let state = JsonSerializer.Deserialize<RuntimeState>(json, jsonOptions)
            Some state
        with
        | :? JsonException -> None
        | :? ArgumentNullException -> None

    /// Load runtime state from an embedded resource in the given assembly.
    /// Returns None if the runtime state is not found or cannot be deserialized.
    let loadRuntimeStateFromAssembly (assembly: Assembly) : RuntimeState option =
        let resourceName =
            assembly.GetManifestResourceNames()
            |> Array.tryFind (fun name ->
                name.EndsWith(DefaultRuntimeStateResourceName, StringComparison.OrdinalIgnoreCase))

        match resourceName with
        | None -> None
        | Some name ->
            use stream = assembly.GetManifestResourceStream(name)

            if isNull stream then
                None
            else
                use reader = new StreamReader(stream)
                let json = reader.ReadToEnd()
                deserializeRuntimeState json

    /// Load an AffordanceMap from the runtime state embedded in the given assembly.
    /// Deserializes the runtime state, then generates the AffordanceMap from
    /// the embedded resource data. Returns None if no runtime state is found.
    let loadAffordanceMapFromAssembly (assembly: Assembly) : AffordanceMap option =
        loadRuntimeStateFromAssembly assembly
        |> Option.map AffordanceMap.fromRuntimeState
