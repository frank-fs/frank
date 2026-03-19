namespace Frank.Affordances

open System
open System.IO
open System.Reflection
open System.Text.Json

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
