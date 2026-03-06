namespace Frank.Cli.Core.Commands

open System
open System.IO
open System.Text.Json
open VDS.RDF
open VDS.RDF.Parsing
open VDS.RDF.Writing
open Frank.Cli.Core.State

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

                if not (Directory.Exists outDir) then
                    Directory.CreateDirectory outDir |> ignore

                // Back up existing state before overwriting
                backupState statePath

                // Write ontology.owl.xml
                let ontologyPath = Path.Combine(outDir, "ontology.owl.xml")

                do
                    let rdfXmlWriter = RdfXmlWriter()
                    use ontologyStream = File.Create ontologyPath
                    use ontologySw = new StreamWriter(ontologyStream)
                    rdfXmlWriter.Save(state.Ontology, ontologySw :> TextWriter)

                // Write shapes.shacl.ttl
                let shapesPath = Path.Combine(outDir, "shapes.shacl.ttl")

                do
                    let turtleWriter = CompressingTurtleWriter()
                    use shapesStream = File.Create shapesPath
                    use shapesSw = new StreamWriter(shapesStream)
                    turtleWriter.Save(state.Shapes, shapesSw :> TextWriter)

                // Write manifest.json
                let manifestPath = Path.Combine(outDir, "manifest.json")

                do
                    use manifestStream = File.Create manifestPath

                    use writer = new Utf8JsonWriter(manifestStream, JsonWriterOptions(Indented = true))

                    writer.WriteStartObject()
                    writer.WriteString("version", state.Metadata.ToolVersion)
                    writer.WriteString("baseUri", state.Metadata.BaseUri.AbsoluteUri)
                    writer.WriteString("sourceHash", state.Metadata.SourceHash)

                    writer.WriteStartArray("vocabularies")

                    for v in state.Metadata.Vocabularies do
                        writer.WriteStringValue v

                    writer.WriteEndArray()

                    writer.WriteString("generatedAt", DateTimeOffset.UtcNow)
                    writer.WriteEndObject()
                    writer.Flush()

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
