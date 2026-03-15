namespace Frank.Cli.Core.Commands

open System
open System.IO
open System.Text.Json
open VDS.RDF
open VDS.RDF.Parsing
open VDS.RDF.Writing
open Frank.Cli.Core.State
open Frank.Cli.Core.Output

/// Generates final OWL/XML and SHACL artifacts from extraction state.
module CompileCommand =

    type CompileResult =
        { OntologyPath: string
          ShapesPath: string
          ManifestPath: string
          EmbeddedResourceNames: string list }

    let private backupState (statePath: string) =
        if File.Exists statePath then
            let dir = Path.GetDirectoryName statePath
            let backupDir = Path.Combine(dir, "backups")

            if not (Directory.Exists backupDir) then
                Directory.CreateDirectory backupDir |> ignore

            let timestamp = DateTimeOffset.UtcNow.ToString("yyyyMMddTHHmmssZ")
            let backupPath = Path.Combine(backupDir, $"extraction-state-%s{timestamp}.json")
            File.Copy(statePath, backupPath)

    let private verifyRoundTrip (ontologyPath: string) (shapesPath: string) (manifestPath: string) =
        // Re-parse ontology
        let ontologyGraph = new Graph()
        let rdfXmlParser = RdfXmlParser()
        use ontologyReader = new StreamReader(ontologyPath)
        rdfXmlParser.Load(ontologyGraph, ontologyReader)

        // Re-parse shapes
        let shapesGraph = new Graph()
        let turtleParser = TurtleParser()
        use shapesReader = new StreamReader(shapesPath)
        turtleParser.Load(shapesGraph, shapesReader)

        // Re-parse manifest
        let manifestJson = File.ReadAllText(manifestPath)
        use _ = JsonDocument.Parse(manifestJson)
        ()

    let execute (projectPath: string) (outputDir: string option) : Result<CompileResult, string> =
        let projectDir = Path.GetDirectoryName projectPath
        let statePath = ExtractionState.defaultStatePath projectDir

        match ExtractionState.load statePath with
        | Error _ -> Error "No extraction state found. Run 'frank-cli extract' first."
        | Ok state ->

            try
                let outDir =
                    outputDir |> Option.defaultValue (Path.Combine(projectDir, "obj", "frank-cli"))

                // Back up existing state before overwriting
                backupState statePath

                // Write all three artifacts via shared serializer
                let (ontologyPath, shapesPath, manifestPath) =
                    ArtifactSerializer.writeArtifacts state outDir

                // Round-trip verification: re-parse each file to confirm validity
                verifyRoundTrip ontologyPath shapesPath manifestPath

                Ok
                    { OntologyPath = ontologyPath
                      ShapesPath = shapesPath
                      ManifestPath = manifestPath
                      EmbeddedResourceNames =
                        [ "Frank.Semantic.ontology.owl.xml"
                          "Frank.Semantic.shapes.shacl.ttl"
                          "Frank.Semantic.manifest.json" ] }
            with ex ->
                Error $"Compile failed: {ex.Message}"

    /// Unified entrypoint for MSBuild auto-invoke: runs extraction and emits artifacts in one shot.
    /// Accepts the project path, base URI, vocabularies, and optional output directory.
    /// Internally calls ExtractCommand.execute to obtain the state, then writes artifacts.
    let compileFromProject
        (projectPath: string)
        (baseUri: Uri)
        (vocabularies: string list)
        (outputDir: string option)
        : Async<Result<CompileResult, string>> =
        async {
            // Step 1: Extract — runs full pipeline and persists state
            let! extractResult = ExtractCommand.execute projectPath baseUri vocabularies

            match extractResult with
            | Error e -> return Error $"Extraction failed: {e}"
            | Ok extractedState ->

                let projectDir = Path.GetDirectoryName projectPath
                let statePath = extractedState.StateFilePath

                try
                    let outDir =
                        outputDir |> Option.defaultValue (Path.Combine(projectDir, "obj", "frank-cli"))

                    // Step 2: Load the just-persisted state and emit artifacts
                    match ExtractionState.load statePath with
                    | Error e -> return Error $"Failed to load state after extraction: {e}"
                    | Ok state ->

                        // Back up existing state before overwriting
                        backupState statePath

                        // Write all three artifacts via shared serializer
                        let (ontologyPath, shapesPath, manifestPath) =
                            ArtifactSerializer.writeArtifacts state outDir

                        // Round-trip verification: re-parse each file to confirm validity
                        verifyRoundTrip ontologyPath shapesPath manifestPath

                        return
                            Ok
                                { OntologyPath = ontologyPath
                                  ShapesPath = shapesPath
                                  ManifestPath = manifestPath
                                  EmbeddedResourceNames =
                                    [ "Frank.Semantic.ontology.owl.xml"
                                      "Frank.Semantic.shapes.shacl.ttl"
                                      "Frank.Semantic.manifest.json" ] }
                with ex ->
                    return Error $"Compile failed: {ex.Message}"
        }
