module Frank.LinkedData.RdfSerializer

open System.IO
open VDS.RDF
open VDS.RDF.Writing

type RdfFormat =
    | JsonLd
    | Turtle
    | RdfXml

/// Serializes an IGraph to a string in the requested RDF format.
/// JSON-LD requires wrapping the graph in a TripleStore (JsonLdWriter implements IStoreWriter).
let serialize (format: RdfFormat) (graph: IGraph) : string =
    let sb = System.Text.StringBuilder()
    use sw = new System.IO.StringWriter(sb)

    match format with
    | JsonLd ->
        let store = new TripleStore()
        store.Add(graph) |> ignore
        let writer = new JsonLdWriter()
        writer.Save(store, sw :> TextWriter)
    | Turtle ->
        let writer = new CompressingTurtleWriter()
        writer.Save(graph, sw :> TextWriter)
    | RdfXml ->
        let writer = new RdfXmlWriter()
        writer.Save(graph, sw :> TextWriter)

    sw.Flush()
    sb.ToString()

let contentTypeFor (format: RdfFormat) =
    match format with
    | JsonLd -> "application/ld+json"
    | Turtle -> "text/turtle"
    | RdfXml -> "application/rdf+xml"
