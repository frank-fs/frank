namespace Frank.Cli.Core.Commands

open System
open System.IO
open System.Text.Json
open VDS.RDF
open VDS.RDF.Parsing
open VDS.RDF.Writing
open Frank.Affordances
open Frank.Cli.Core.State
open Frank.Cli.Core.Output
open Frank.Cli.Core.Unified

/// Generates final OWL/XML and SHACL artifacts from extraction state.
module CompileCommand =

    type CompileResult =
        { OntologyPath: string
          ShapesPath: string
          ManifestPath: string
          RuntimeStatePath: string option
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

    /// Generate ALPS profile strings per resource slug.
    let private generateAlpsProfiles (unified: UnifiedExtractionState) : Map<string, string> =
        unified.Resources
        |> List.choose (fun resource ->
            match UnifiedAlpsGenerator.generate resource unified.BaseUri with
            | Ok alpsJson -> Some(resource.ResourceSlug, alpsJson)
            | Error _ -> None)
        |> Map.ofList

    /// Serialize OWL ontology graph to Turtle string.
    let private serializeOwlTurtle (state: ExtractionState) : string =
        ExtractionState.graphToTurtle state.Ontology

    /// Serialize SHACL shapes graph to Turtle string.
    let private serializeShaclTurtle (state: ExtractionState) : string =
        ExtractionState.graphToTurtle state.Shapes

    /// Generate and write the runtime state file from the unified extraction state.
    /// Populates ALPS profiles per resource, plus OWL/SHACL as whole-project strings.
    /// Returns the path if successful, None if the unified state is not available.
    let private generateRuntimeState
        (projectDir: string)
        (outDir: string)
        (legacyState: ExtractionState option)
        : string option =
        let unifiedPath = Path.Combine(projectDir, "obj", "frank-cli", "unified-state.bin")

        match UnifiedCache.load unifiedPath with
        | Ok(Some unified) ->
            let alpsProfiles = generateAlpsProfiles unified

            let owlTurtle =
                match legacyState with
                | Some s -> serializeOwlTurtle s
                | None -> ""

            let shaclTurtle =
                match legacyState with
                | Some s -> serializeShaclTurtle s
                | None -> ""

            let profiles: ProjectedProfiles =
                { AlpsProfiles = alpsProfiles
                  OwlOntologies =
                    if System.String.IsNullOrEmpty owlTurtle then
                        Map.empty
                    else
                        Map.ofList [ ("_project", owlTurtle) ]
                  ShaclShapes =
                    if System.String.IsNullOrEmpty shaclTurtle then
                        Map.empty
                    else
                        Map.ofList [ ("_project", shaclTurtle) ]
                  JsonSchemas = Map.empty }

            let runtimeState = RuntimeProjector.toRuntimeStateWithProfiles unified profiles
            let json = StartupProjection.serializeRuntimeState runtimeState
            let runtimeStatePath = Path.Combine(outDir, StartupProjection.DefaultRuntimeStateResourceName)
            File.WriteAllText(runtimeStatePath, json)
            Some runtimeStatePath
        | _ -> None

    let execute (projectPath: string) (outputDir: string option) : Result<CompileResult, string> =
        let projectDir = Path.GetDirectoryName projectPath
        match ExtractionStateProjector.UnifiedStateLoader.loadExtractionState projectDir with
        | Error e -> Error e
        | Ok state ->

            try
                let outDir =
                    outputDir |> Option.defaultValue (Path.Combine(projectDir, "obj", "frank-cli"))

                // Back up existing state before overwriting
                let statePath = ExtractionState.defaultStatePath projectDir
                backupState statePath

                // Write all three artifacts via shared serializer
                let (ontologyPath, shapesPath, manifestPath) =
                    ArtifactSerializer.writeArtifacts state outDir

                // Round-trip verification: re-parse each file to confirm validity
                verifyRoundTrip ontologyPath shapesPath manifestPath

                // Generate runtime state for embedded resource
                let runtimeStatePath = generateRuntimeState projectDir outDir (Some state)

                Ok
                    { OntologyPath = ontologyPath
                      ShapesPath = shapesPath
                      ManifestPath = manifestPath
                      RuntimeStatePath = runtimeStatePath
                      EmbeddedResourceNames =
                        [ "Frank.Semantic.ontology.owl.xml"
                          "Frank.Semantic.shapes.shacl.ttl"
                          "Frank.Semantic.manifest.json" ] }
            with ex ->
                Error $"Compile failed: {ex.Message}"

    /// Unified entrypoint for MSBuild auto-invoke: runs extraction and emits artifacts in one shot.
    /// Accepts the project path, base URI, vocabularies, and optional output directory.
    /// Uses a capturing pipeline to avoid a redundant disk round-trip for the ExtractionState.
    let compileFromProject
        (projectPath: string)
        (baseUri: Uri)
        (vocabularies: string list)
        (outputDir: string option)
        : Async<Result<CompileResult, string>> =
        async {
            // Capture the ExtractionState in memory via the pipeline's SaveState hook,
            // avoiding the need to re-load it from disk after extraction.
            let capturedState = ref None

            let capturingPipeline =
                { ExtractCommand.defaultPipeline with
                    SaveState =
                        fun path state ->
                            capturedState.Value <- Some state
                            ExtractCommand.defaultPipeline.SaveState path state }

            let! extractResult = ExtractCommand.executeWithPipeline capturingPipeline projectPath baseUri vocabularies

            match extractResult with
            | Error e -> return Error $"Extraction failed: {e}"
            | Ok _ ->

                match capturedState.Value with
                | None -> return Error "Internal error: extraction succeeded but state was not captured"
                | Some state ->

                    try
                        let projectDir = Path.GetDirectoryName projectPath

                        let outDir =
                            outputDir |> Option.defaultValue (Path.Combine(projectDir, "obj", "frank-cli"))

                        let (ontologyPath, shapesPath, manifestPath) =
                            ArtifactSerializer.writeArtifacts state outDir

                        verifyRoundTrip ontologyPath shapesPath manifestPath

                        // Generate runtime state for embedded resource
                        let runtimeStatePath = generateRuntimeState projectDir outDir (Some state)

                        return
                            Ok
                                { OntologyPath = ontologyPath
                                  ShapesPath = shapesPath
                                  ManifestPath = manifestPath
                                  RuntimeStatePath = runtimeStatePath
                                  EmbeddedResourceNames =
                                    [ "Frank.Semantic.ontology.owl.xml"
                                      "Frank.Semantic.shapes.shacl.ttl"
                                      "Frank.Semantic.manifest.json" ] }
                    with ex ->
                        return Error $"Compile failed: {ex.Message}"
        }
