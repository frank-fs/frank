namespace Frank.LinkedData.Negotiation

open System.IO
open VDS.RDF
open VDS.RDF.Writing

/// Serializes an IGraph to Turtle format using dotNetRdf's CompressingTurtleWriter.
module TurtleFormatter =

    /// Writes the given graph as Turtle to the provided stream.
    let writeTurtle (graph: IGraph) (stream: Stream) =
        let writer = CompressingTurtleWriter()
        use textWriter = new StreamWriter(stream, leaveOpen = true)
        writer.Save(graph, textWriter)
        textWriter.Flush()
