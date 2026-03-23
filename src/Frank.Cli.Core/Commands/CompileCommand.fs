namespace Frank.Cli.Core.Commands

open System
open System.IO
open System.Text.Json
open VDS.RDF
open VDS.RDF.Parsing
open VDS.RDF.Writing
open Frank.Resources.Model
open Frank.Cli.Core.State
open Frank.Cli.Core.Output
open Frank.Cli.Core.Unified

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
        use ontologyGraph = new Graph()
        let rdfXmlParser = RdfXmlParser()
        use ontologyReader = new StreamReader(ontologyPath)
        rdfXmlParser.Load(ontologyGraph, ontologyReader)

        use shapesGraph = new Graph()
        let turtleParser = TurtleParser()
        use shapesReader = new StreamReader(shapesPath)
        turtleParser.Load(shapesGraph, shapesReader)

        let manifestJson = File.ReadAllText(manifestPath)
        use _ = JsonDocument.Parse(manifestJson)
        ()

    /// Populate the Profiles field on the unified extraction state binary.
    /// Generates ALPS per resource, runs per-role projection where applicable,
    /// serializes OWL/SHACL as whole-project strings, and re-saves model.bin.
    let private populateProfilesInBinary (projectDir: string) (legacyState: ExtractionState option) : unit =
        let unifiedPath = UnifiedCache.cachePath projectDir

        match UnifiedCache.load unifiedPath with
        | Ok(Some unified) ->
            // Run projection first so we know which resource slugs get
            // their global profile overridden with cross-links.
            let batchResult, projectionResults =
                ProjectionPipeline.projectAllResources unified.Resources unified.BaseUri

            for result in projectionResults do
                for err in result.Errors do
                    eprintfn "%s" err

                for orphan in result.Orphans do
                    eprintfn
                        "Warning: transition '%s' (%s -> %s) is not covered by any role projection"
                        orphan.Event
                        orphan.Source
                        orphan.Target

            // Generate plain ALPS for resources that did NOT receive projection treatment.
            let alpsProfiles =
                unified.Resources
                |> List.choose (fun resource ->
                    if Set.contains resource.ResourceSlug batchResult.ProjectedSlugs then
                        None
                    else
                        match UnifiedAlpsGenerator.generate resource unified.BaseUri with
                        | Ok alpsJson -> Some(resource.ResourceSlug, alpsJson)
                        | Error errs ->
                            eprintfn
                                "Warning: failed to generate ALPS profile for %s: %s"
                                resource.ResourceSlug
                                (String.concat "; " errs)

                            None)
                |> Map.ofList

            let mergedAlpsProfiles =
                batchResult.GlobalProfileOverrides
                |> Map.fold (fun acc k v -> Map.add k v acc) alpsProfiles

            let owlTurtle =
                match legacyState with
                | Some s -> ExtractionState.graphToTurtle s.Ontology
                | None -> ""

            let shaclTurtle =
                match legacyState with
                | Some s -> ExtractionState.graphToTurtle s.Shapes
                | None -> ""

            let profiles: ProjectedProfiles =
                { AlpsProfiles = mergedAlpsProfiles
                  RoleAlpsProfiles = batchResult.RoleAlpsProfiles
                  OwlOntologies =
                    if String.IsNullOrEmpty owlTurtle then
                        Map.empty
                    else
                        Map.ofList [ ("_project", owlTurtle) ]
                  ShaclShapes =
                    if String.IsNullOrEmpty shaclTurtle then
                        Map.empty
                    else
                        Map.ofList [ ("_project", shaclTurtle) ]
                  JsonSchemas = Map.empty }

            let updatedState = { unified with Profiles = profiles }
            UnifiedCache.save unifiedPath updatedState |> ignore
        | _ -> ()

    let execute (projectPath: string) (outputDir: string option) : Result<CompileResult, string> =
        let projectDir = Path.GetDirectoryName projectPath

        match ExtractionStateProjector.UnifiedStateLoader.loadExtractionState projectDir with
        | Error e -> Error e
        | Ok state ->

            try
                let outDir =
                    outputDir |> Option.defaultValue (Path.Combine(projectDir, "obj", "frank"))

                let statePath = ExtractionState.defaultStatePath projectDir
                backupState statePath

                let (ontologyPath, shapesPath, manifestPath) =
                    ArtifactSerializer.writeArtifacts state outDir

                verifyRoundTrip ontologyPath shapesPath manifestPath
                populateProfilesInBinary projectDir (Some state)

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
                            outputDir |> Option.defaultValue (Path.Combine(projectDir, "obj", "frank"))

                        let (ontologyPath, shapesPath, manifestPath) =
                            ArtifactSerializer.writeArtifacts state outDir

                        verifyRoundTrip ontologyPath shapesPath manifestPath
                        populateProfilesInBinary projectDir (Some state)

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
