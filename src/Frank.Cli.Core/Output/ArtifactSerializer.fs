namespace Frank.Cli.Core.Output

open System
open System.IO
open System.Text.Json
open VDS.RDF
open VDS.RDF.Writing
open Frank.Cli.Core.State

/// Shared serialization logic for writing OWL/XML, SHACL/Turtle, and manifest JSON artifacts.
/// Used by CompileCommand (both the state-file path and the unified compile --project path).
module ArtifactSerializer =

    /// Write a SHACL shapes graph to Turtle format at the given path.
    let writeShapesTurtle (shapesGraph: IGraph) (outputPath: string) : unit =
        let turtleWriter = CompressingTurtleWriter()
        use shapesStream = File.Create outputPath
        use shapesSw = new StreamWriter(shapesStream)
        turtleWriter.Save(shapesGraph, shapesSw :> TextWriter)

    /// Write an OWL ontology graph to RDF/XML format at the given path.
    let writeOntologyXml (ontologyGraph: IGraph) (outputPath: string) : unit =
        let rdfXmlWriter = RdfXmlWriter()
        use ontologyStream = File.Create outputPath
        use ontologySw = new StreamWriter(ontologyStream)
        rdfXmlWriter.Save(ontologyGraph, ontologySw :> TextWriter)

    /// Write a manifest JSON file at the given path using the supplied metadata.
    let writeManifestJson (metadata: ExtractionMetadata) (outputPath: string) : unit =
        use manifestStream = File.Create outputPath
        use writer = new Utf8JsonWriter(manifestStream, JsonWriterOptions(Indented = true))

        writer.WriteStartObject()
        writer.WriteString("version", metadata.ToolVersion)
        writer.WriteString("baseUri", metadata.BaseUri.AbsoluteUri)
        writer.WriteString("sourceHash", metadata.SourceHash)

        writer.WriteStartArray("vocabularies")

        for v in metadata.Vocabularies do
            writer.WriteStringValue v

        writer.WriteEndArray()

        writer.WriteString("generatedAt", DateTimeOffset.UtcNow)
        writer.WriteEndObject()
        writer.Flush()

    /// Write all three artifacts (shapes.shacl.ttl, ontology.owl.xml, manifest.json) to the given directory.
    /// Returns (ontologyPath, shapesPath, manifestPath).
    let writeArtifacts (state: ExtractionState) (outputDir: string) : string * string * string =
        if not (Directory.Exists outputDir) then
            Directory.CreateDirectory outputDir |> ignore

        let ontologyPath = Path.Combine(outputDir, "ontology.owl.xml")
        let shapesPath = Path.Combine(outputDir, "shapes.shacl.ttl")
        let manifestPath = Path.Combine(outputDir, "manifest.json")

        writeOntologyXml state.Ontology ontologyPath
        writeShapesTurtle state.Shapes shapesPath
        writeManifestJson state.Metadata manifestPath

        (ontologyPath, shapesPath, manifestPath)
