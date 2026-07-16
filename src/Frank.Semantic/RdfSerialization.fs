namespace Frank.Semantic

open System.Text
open System.Text.Json
open VDS.RDF
open VDS.RDF.JsonLd
open VDS.RDF.Writing
open Newtonsoft.Json
open Newtonsoft.Json.Linq

module internal RdfSerialization =

    let serializeGraphJsonLd (graph: IGraph) : string =
        use store = new TripleStore()
        store.Add(graph) |> ignore
        let sb = StringBuilder()
        use sw = new System.IO.StringWriter(sb)
        let writer = JsonLdWriter()
        writer.Save(store :> ITripleStore, sw :> System.IO.TextWriter)
        sb.ToString()

    /// Compact the graph's JSON-LD representation against the given context object.
    /// Returns the compacted JSON-LD as a compact (non-indented) string.
    let compactWithContext (graph: IGraph) (ctx: JObject) : string =
        if isNull (box graph) then
            invalidArg (nameof graph) "graph must not be null"

        let expanded = serializeGraphJsonLd graph
        let input = JToken.Parse expanded
        JsonLdProcessor.Compact(input, ctx, JsonLdProcessorOptions()).ToString(Formatting.None)

    /// Compact the graph's JSON-LD representation against a context built from the given
    /// prefix pairs and @base IRI. Returns the compacted JSON-LD as a string.
    let compactGraphJsonLd (graph: IGraph) (prefixPairs: (string * string) list) (base': string) : string =
        if isNull (box graph) then
            invalidArg (nameof graph) "graph must not be null"

        let ctx = JObject()
        ctx.["@base"] <- JToken.op_Implicit base'

        for (prefix, iri) in prefixPairs do
            ctx.[prefix] <- JToken.op_Implicit iri

        compactWithContext graph ctx

    let serializeGraphJsonLdWithContext (graph: IGraph) (contextJson: string) : string =
        let graphJson = serializeGraphJsonLd graph

        let contextElement =
            use doc = JsonDocument.Parse(contextJson)
            doc.RootElement.GetProperty("@context").Clone()

        let opts = JsonWriterOptions(Indented = false)
        use outStream = new System.IO.MemoryStream()
        use jsonWriter = new Utf8JsonWriter(outStream, opts)
        jsonWriter.WriteStartObject()
        jsonWriter.WritePropertyName("@context")
        contextElement.WriteTo(jsonWriter)
        jsonWriter.WritePropertyName("@graph")

        use graphDoc = JsonDocument.Parse(graphJson)
        graphDoc.RootElement.WriteTo(jsonWriter)
        jsonWriter.WriteEndObject()
        jsonWriter.Flush()
        Encoding.UTF8.GetString(outStream.ToArray())
