module Frank.Discovery.AlpsSerializer

open System.Text
open System.Text.Json

/// Serialize all descriptors for a given type slug to ALPS+JSON string.
/// Flat list — no state/transition nesting (v7.4.0 Track A is out of scope here).
let serialize (descriptors: Map<string, AlpsDescriptor list>) : string =
    let allDescriptors = descriptors |> Map.toSeq |> Seq.collect snd |> Seq.toList

    use ms = new System.IO.MemoryStream()
    use writer = new Utf8JsonWriter(ms)
    writer.WriteStartObject()
    writer.WritePropertyName("alps")
    writer.WriteStartObject()
    writer.WriteString("version", "1.0")
    writer.WritePropertyName("descriptor")
    writer.WriteStartArray()

    for d in allDescriptors do
        writer.WriteStartObject()
        writer.WriteString("id", d.Id)
        writer.WriteString("type", d.Type)

        match d.Href with
        | Some href -> writer.WriteString("href", href)
        | None -> ()

        match d.Doc with
        | Some doc -> writer.WriteString("doc", doc)
        | None -> ()

        writer.WriteEndObject()

    writer.WriteEndArray()
    writer.WriteEndObject()
    writer.WriteEndObject()
    writer.Flush()
    Encoding.UTF8.GetString(ms.ToArray())
