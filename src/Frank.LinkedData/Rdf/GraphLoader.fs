namespace Frank.LinkedData.Rdf

open System
open System.IO
open System.Reflection
open System.Text.Json
open FsToolkit.ErrorHandling
open VDS.RDF
open VDS.RDF.Parsing

/// Manifest describing the semantic resources embedded in an assembly.
type SemanticManifest =
    { Version: string
      BaseUri: string
      SourceHash: string
      Vocabularies: string list
      GeneratedAt: DateTimeOffset }

/// The loaded ontology, shapes, and manifest from an assembly's embedded resources.
type LoadedSemantics =
    { OntologyGraph: IGraph
      ShapesGraph: IGraph
      Manifest: SemanticManifest }

/// Loads ontology and SHACL shapes from embedded resources.
module GraphLoader =

    let private manifestResource = "Frank.Semantic.manifest.json"
    let private ontologyResource = "Frank.Semantic.ontology.owl.xml"
    let private shapesResource = "Frank.Semantic.shapes.shacl.ttl"

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
        | s -> Ok s

    let private loadManifest (assembly: Assembly) =
        result {
            let! manifestStream = loadStream assembly manifestResource
            use _ms = manifestStream
            let options = JsonSerializerOptions(PropertyNameCaseInsensitive = true)
            return JsonSerializer.Deserialize<SemanticManifest>(manifestStream, options)
        }

    let private loadOntology (assembly: Assembly) =
        result {
            let! ontologyStream = loadStream assembly ontologyResource
            use _os = ontologyStream
            let ontologyGraph = new Graph()
            let rdfXmlParser = RdfXmlParser()
            use ontologyReader = new StreamReader(ontologyStream)
            rdfXmlParser.Load(ontologyGraph, ontologyReader)
            return ontologyGraph
        }

    let private loadShapes (assembly: Assembly) =
        result {
            let! shapesStream = loadStream assembly shapesResource
            use _ss = shapesStream
            let shapesGraph = new Graph()
            let turtleParser = TurtleParser()
            use shapesReader = new StreamReader(shapesStream)
            turtleParser.Load(shapesGraph, shapesReader)
            return shapesGraph
        }

    let load (assembly: Assembly) : Result<LoadedSemantics, string> =
        try
            result {
                let! manifest = loadManifest assembly
                let! ontologyGraph = loadOntology assembly
                let! shapesGraph = loadShapes assembly

                return
                    { OntologyGraph = ontologyGraph
                      ShapesGraph = shapesGraph
                      Manifest = manifest }
            }
        with :? RdfParseException as ex ->
            Error(sprintf "RDF parse error: %s" ex.Message)
