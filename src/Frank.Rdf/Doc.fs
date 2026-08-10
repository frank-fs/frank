namespace Frank.Rdf

open System
open System.Buffers
open System.IO
open System.Text
open VDS.RDF
open VDS.RDF.Writing

// TextWriter adapter for IBufferWriter<byte>, enabling direct writes to PipeWriter
// without intermediate buffering. Used by writeJsonLdAsync for streaming to response bodies.
type private PipeTextWriter(bufferWriter: IBufferWriter<byte>) =
    inherit TextWriter()
    override _.Encoding = Encoding.UTF8
    override _.Write(value: string) =
        if not (isNull value) && value.Length > 0 then
            let byteCount = Encoding.UTF8.GetByteCount(value)
            let buffer = bufferWriter.GetSpan(byteCount)
            let bytesWritten = Encoding.UTF8.GetBytes(value, buffer)
            bufferWriter.Advance(bytesWritten)

type Doc =
    { Prefixes: (string * string) list
      Statements: (Node * string * Value) list }

    static member Empty = { Prefixes = []; Statements = [] }

module Doc =
    let private toGraphNode (graph: Graph) (prefixes: (string * string) list) (node: Node) : INode =
        match node with
        | Node.Iri s -> graph.CreateUriNode(Uri(resolveIri prefixes s)) :> INode
        | Node.Blank id -> graph.CreateBlankNode(id) :> INode

    let private toLiteralNode (graph: Graph) (literal: Literal) : INode =
        match literal with
        | Literal.String s -> graph.CreateLiteralNode(s) :> INode
        | Literal.Int i -> i.ToLiteral(graph)
        | Literal.Bool b -> b.ToLiteral(graph)
        | Literal.DateTime dt -> dt.ToLiteral(graph)
        | Literal.LangString(value, lang) -> graph.CreateLiteralNode(value, lang) :> INode

    let private toObjectNode (graph: Graph) (prefixes: (string * string) list) (value: Value) : INode =
        match value with
        | Value.Node n -> toGraphNode graph prefixes n
        | Value.Literal l -> toLiteralNode graph l

    let toGraph (doc: Doc) : Graph =
        validatePrefixes doc.Prefixes

        let graph = new Graph()

        for prefixName, uri in doc.Prefixes do
            graph.NamespaceMap.AddNamespace(prefixName, Uri(uri))

        for subject, predicate, value in doc.Statements do
            let s = toGraphNode graph doc.Prefixes subject
            let p = graph.CreateUriNode(Uri(resolveIri doc.Prefixes predicate))
            let o = toObjectNode graph doc.Prefixes value
            graph.Assert(Triple(s, p, o)) |> ignore

        graph

    let writeJsonLd (doc: Doc) (writer: System.IO.TextWriter) : unit =
        let graph = toGraph doc
        let store = new TripleStore()
        store.Add(graph) |> ignore
        (new JsonLdWriter()).Save(store, writer, true)

    let writeJsonLdAsync (doc: Doc) (bufferWriter: System.Buffers.IBufferWriter<byte>) : System.Threading.Tasks.Task =
        task {
            use writer = new PipeTextWriter(bufferWriter)
            writeJsonLd doc writer
        }

    let toJsonLd (doc: Doc) : string =
        use writer = new System.IO.StringWriter()
        writeJsonLd doc writer
        writer.ToString()

    let merge (a: Doc) (b: Doc) : Doc =
        { Prefixes = a.Prefixes @ b.Prefixes
          Statements = a.Statements @ b.Statements }
