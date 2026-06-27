namespace Frank.Semantic

open System.Text
open System.Text.Json
open VDS.RDF
open VDS.RDF.Writing

module RdfSerialization =

    let serializeGraphJsonLd (graph: IGraph) : string =
        use store = new TripleStore()
        store.Add(graph) |> ignore
        let sb = StringBuilder()
        use sw = new System.IO.StringWriter(sb)
        let writer = JsonLdWriter()
        writer.Save(store :> ITripleStore, sw :> System.IO.TextWriter)
        sb.ToString()

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

        try
            use graphDoc = JsonDocument.Parse(graphJson)
            graphDoc.RootElement.WriteTo(jsonWriter)
        with _ ->
            jsonWriter.WriteStartArray()
            jsonWriter.WriteEndArray()

        jsonWriter.WriteEndObject()
        jsonWriter.Flush()
        Encoding.UTF8.GetString(outStream.ToArray())
