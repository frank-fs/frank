module Frank.Discovery.AlpsSerializer

open System.Text
open System.Text.Json

/// Serialize a flat descriptor list to an ALPS+JSON document. No state/transition
/// nesting (Track A). Descriptor IRIs are vocabulary IRIs supplied by the caller.
let serialize (descriptors: AlpsDescriptor list) : string =
    use ms = new System.IO.MemoryStream()
    use writer = new Utf8JsonWriter(ms)
    writer.WriteStartObject()
    writer.WritePropertyName("alps")
    writer.WriteStartObject()
    writer.WriteString("version", "1.0")
    writer.WritePropertyName("descriptor")
    writer.WriteStartArray()

    for d in descriptors do
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
