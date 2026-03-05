namespace Frank.LinkedData.Negotiation

open System.IO
open VDS.RDF
open VDS.RDF.Writing

/// Serializes an IGraph to RDF/XML format using dotNetRdf's RdfXmlWriter.
module RdfXmlFormatter =

    /// Writes the given graph as RDF/XML to the provided stream.
    let writeRdfXml (graph: IGraph) (stream: Stream) =
        let writer = RdfXmlWriter()
        use textWriter = new StreamWriter(stream, leaveOpen = true)
        writer.Save(graph, textWriter)
        textWriter.Flush()
