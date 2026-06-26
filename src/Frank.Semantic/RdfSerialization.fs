namespace Frank.Semantic

open System.Text
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
