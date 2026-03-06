namespace Frank.LinkedData.Negotiation

open System.IO
open System.Text.Json
open VDS.RDF
open Frank.LinkedData.Rdf.RdfUriHelpers

/// Serializes an IGraph to JSON-LD format using System.Text.Json.
/// dotNetRdf.Core does not include JsonLdWriter, so we produce a minimal
/// JSON-LD representation manually from the graph triples.
module JsonLdFormatter =

    /// Writes the given graph as JSON-LD to the provided stream.
    let writeJsonLd (graph: IGraph) (stream: Stream) =
        use writer = new Utf8JsonWriter(stream, JsonWriterOptions(Indented = true))
        writer.WriteStartObject()

        // Build @context from predicate namespaces
        let predicateUris =
            graph.Triples
            |> Seq.choose (fun t ->
                match t.Predicate with
                | :? IUriNode as u -> Some(u.Uri.ToString())
                | _ -> None)
            |> Seq.distinct
            |> Seq.toList

        let contextPairs =
            predicateUris
            |> List.map (fun uri -> localName uri, namespaceUri uri)
            |> List.distinctBy fst

        writer.WriteStartObject("@context")

        for (name, ns) in contextPairs do
            writer.WriteString(name, ns + name)

        writer.WriteEndObject()

        // Group triples by subject
        let subjects = graph.Triples |> Seq.groupBy (fun t -> t.Subject) |> Seq.toList

        match subjects with
        | [ (subj, triples) ] ->
            // Single subject: flatten into the root object
            match subj with
            | :? IUriNode as u -> writer.WriteString("@id", u.Uri.ToString())
            | :? IBlankNode as b -> writer.WriteString("@id", sprintf "_:%s" b.InternalID)
            | _ -> ()

            for triple in triples do
                let predName =
                    match triple.Predicate with
                    | :? IUriNode as u -> localName (u.Uri.ToString())
                    | _ -> "unknown"

                match triple.Object with
                | :? ILiteralNode as lit ->
                    if isNull lit.DataType then
                        writer.WriteString(predName, lit.Value)
                    else
                        let dtUri = lit.DataType.ToString()

                        if dtUri.EndsWith("integer") || dtUri.EndsWith("int") || dtUri.EndsWith("long") then
                            match System.Int64.TryParse(lit.Value) with
                            | true, v -> writer.WriteNumber(predName, v)
                            | _ -> writer.WriteString(predName, lit.Value)
                        elif dtUri.EndsWith("double") || dtUri.EndsWith("float") then
                            match
                                System.Double.TryParse(
                                    lit.Value,
                                    System.Globalization.NumberStyles.Float,
                                    System.Globalization.CultureInfo.InvariantCulture
                                )
                            with
                            | true, v -> writer.WriteNumber(predName, v)
                            | _ -> writer.WriteString(predName, lit.Value)
                        elif dtUri.EndsWith("boolean") then
                            match System.Boolean.TryParse(lit.Value) with
                            | true, v -> writer.WriteBoolean(predName, v)
                            | _ -> writer.WriteString(predName, lit.Value)
                        else
                            writer.WriteString(predName, lit.Value)
                | :? IUriNode as u ->
                    writer.WriteStartObject(predName)
                    writer.WriteString("@id", u.Uri.ToString())
                    writer.WriteEndObject()
                | _ -> writer.WriteString(predName, triple.Object.ToString())
        | _ ->
            // Multiple subjects: use @graph
            writer.WriteStartArray("@graph")

            for (subj, triples) in subjects do
                writer.WriteStartObject()

                match subj with
                | :? IUriNode as u -> writer.WriteString("@id", u.Uri.ToString())
                | :? IBlankNode as b -> writer.WriteString("@id", sprintf "_:%s" b.InternalID)
                | _ -> ()

                for triple in triples do
                    let predName =
                        match triple.Predicate with
                        | :? IUriNode as u -> localName (u.Uri.ToString())
                        | _ -> "unknown"

                    match triple.Object with
                    | :? ILiteralNode as lit ->
                        if isNull lit.DataType then
                            writer.WriteString(predName, lit.Value)
                        else
                            let dtUri = lit.DataType.ToString()

                            if dtUri.EndsWith("integer") || dtUri.EndsWith("int") || dtUri.EndsWith("long") then
                                match System.Int64.TryParse(lit.Value) with
                                | true, v -> writer.WriteNumber(predName, v)
                                | _ -> writer.WriteString(predName, lit.Value)
                            elif dtUri.EndsWith("double") || dtUri.EndsWith("float") then
                                match
                                    System.Double.TryParse(
                                        lit.Value,
                                        System.Globalization.NumberStyles.Float,
                                        System.Globalization.CultureInfo.InvariantCulture
                                    )
                                with
                                | true, v -> writer.WriteNumber(predName, v)
                                | _ -> writer.WriteString(predName, lit.Value)
                            elif dtUri.EndsWith("boolean") then
                                match System.Boolean.TryParse(lit.Value) with
                                | true, v -> writer.WriteBoolean(predName, v)
                                | _ -> writer.WriteString(predName, lit.Value)
                            else
                                writer.WriteString(predName, lit.Value)
                    | :? IUriNode as u ->
                        writer.WriteStartObject(predName)
                        writer.WriteString("@id", u.Uri.ToString())
                        writer.WriteEndObject()
                    | _ -> writer.WriteString(predName, triple.Object.ToString())

                writer.WriteEndObject()

            writer.WriteEndArray()

        writer.WriteEndObject()
        writer.Flush()
