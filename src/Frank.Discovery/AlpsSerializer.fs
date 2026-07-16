module internal Frank.Discovery.AlpsSerializer

open System.Text
open System.Text.Json

/// Write one descriptor (and recurse into its Descriptors children).
/// Only emits `descriptor` array and `rt` when non-empty / Some.
let private writeDescriptor (writer: Utf8JsonWriter) (d: AlpsDescriptor) =
    let rec write (d: AlpsDescriptor) =
        writer.WriteStartObject()
        writer.WriteString("id", d.Id)
        writer.WriteString("type", d.Type)

        match d.Href with
        | Some href -> writer.WriteString("href", href)
        | None -> ()

        match d.Doc with
        | Some doc -> writer.WriteString("doc", doc)
        | None -> ()

        match d.Rt with
        | Some rt -> writer.WriteString("rt", rt)
        | None -> ()

        if not d.Descriptors.IsEmpty then
            writer.WritePropertyName("descriptor")
            writer.WriteStartArray()

            for child in d.Descriptors do
                write child

            writer.WriteEndArray()

        writer.WriteEndObject()

    write d

/// Serialize a descriptor list to an ALPS+JSON document. Field descriptors
/// are nested inside their class descriptor (AC1). Action descriptors carry
/// `rt` for the return type. Leaf descriptors emit no `descriptor` array.
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
        writeDescriptor writer d

    writer.WriteEndArray()
    writer.WriteEndObject()
    writer.WriteEndObject()
    writer.Flush()
    Encoding.UTF8.GetString(ms.ToArray())
