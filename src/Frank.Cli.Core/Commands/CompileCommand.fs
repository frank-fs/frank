namespace Frank.Cli.Core.Commands

open System
open System.IO
open System.Text.Json
open VDS.RDF.Writing
open Frank.Cli.Core.State

/// Generates final OWL/XML and SHACL artifacts from extraction state.
module CompileCommand =

    type CompileResult =
        { OntologyPath: string
          ShapesPath: string
          ManifestPath: string }

    let execute (projectPath: string) (outputDir: string option) : Result<CompileResult, string> =
        let projectDir = Path.GetDirectoryName projectPath
        let statePath = ExtractionState.defaultStatePath projectDir

        match ExtractionState.load statePath with
        | Error e -> Error $"Failed to load state: {e}"
        | Ok state ->

        try
            let outDir =
                outputDir
                |> Option.defaultValue (Path.Combine(projectDir, "obj", "frank-cli"))

            if not (Directory.Exists outDir) then
                Directory.CreateDirectory outDir |> ignore

            // Write ontology.owl.xml
            let ontologyPath = Path.Combine(outDir, "ontology.owl.xml")
            let rdfXmlWriter = RdfXmlWriter()
            use ontologyStream = File.Create ontologyPath
            use ontologySw = new StreamWriter(ontologyStream)
            rdfXmlWriter.Save(state.Ontology, ontologySw :> TextWriter)

            // Write shapes.shacl.ttl
            let shapesPath = Path.Combine(outDir, "shapes.shacl.ttl")
            let turtleWriter = CompressingTurtleWriter()
            use shapesStream = File.Create shapesPath
            use shapesSw = new StreamWriter(shapesStream)
            turtleWriter.Save(state.Shapes, shapesSw :> TextWriter)

            // Write manifest.json
            let manifestPath = Path.Combine(outDir, "manifest.json")
            use manifestStream = File.Create manifestPath

            use writer =
                new Utf8JsonWriter(manifestStream, JsonWriterOptions(Indented = true))

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

            Ok
                { OntologyPath = ontologyPath
                  ShapesPath = shapesPath
                  ManifestPath = manifestPath }
        with ex ->
            Error $"Compile failed: {ex.Message}"
