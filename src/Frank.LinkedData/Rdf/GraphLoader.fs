namespace Frank.LinkedData.Rdf

open System
open System.IO
open System.Reflection
open System.Text.Json
open VDS.RDF
open VDS.RDF.Parsing

/// Manifest describing the semantic resources embedded in an assembly.
type SemanticManifest =
    { Version      : string
      BaseUri      : string
      SourceHash   : string
      Vocabularies : string list
      GeneratedAt  : DateTimeOffset }

/// The loaded ontology, shapes, and manifest from an assembly's embedded resources.
type LoadedSemantics =
    { OntologyGraph : IGraph
      ShapesGraph   : IGraph
      Manifest      : SemanticManifest }

/// Loads ontology and SHACL shapes from embedded resources.
module GraphLoader =

    let private manifestResource = "Frank.Semantic.manifest.json"
    let private ontologyResource = "Frank.Semantic.ontology.owl.xml"
    let private shapesResource   = "Frank.Semantic.shapes.shacl.ttl"

    let private missingResourceError (assembly: Assembly) (resource: string) =
        let msg =
            sprintf
                "Assembly '%s' does not contain embedded resource '%s'. Run 'frank-cli compile' and add the output files as EmbeddedResource items."
                (assembly.GetName().Name)
                resource
        Error msg

    let private loadStream (assembly: Assembly) resource =
        match assembly.GetManifestResourceStream(resource) with
        | null -> missingResourceError assembly resource
        | s    -> Ok s

    let load (assembly: Assembly) : Result<LoadedSemantics, string> =
        try
            // 1. Load and deserialize manifest
            match loadStream assembly manifestResource with
            | Error e -> Error e
            | Ok manifestStream ->
            use _ms = manifestStream
            let options = JsonSerializerOptions(PropertyNameCaseInsensitive = true)
            let manifest = JsonSerializer.Deserialize<SemanticManifest>(manifestStream, options)

            // 2. Load ontology (RDF/XML)
            match loadStream assembly ontologyResource with
            | Error e -> Error e
            | Ok ontologyStream ->
            use _os = ontologyStream
            let ontologyGraph = new Graph()
            let rdfXmlParser = RdfXmlParser()
            rdfXmlParser.Load(ontologyGraph, new StreamReader(ontologyStream))

            // 3. Load shapes (Turtle)
            match loadStream assembly shapesResource with
            | Error e -> Error e
            | Ok shapesStream ->
            use _ss = shapesStream
            let shapesGraph = new Graph()
            let turtleParser = TurtleParser()
            turtleParser.Load(shapesGraph, new StreamReader(shapesStream))

            Ok { OntologyGraph = ontologyGraph
                 ShapesGraph   = shapesGraph
                 Manifest      = manifest }
        with
        | :? RdfParseException as ex ->
            Error (sprintf "RDF parse error: %s" ex.Message)
