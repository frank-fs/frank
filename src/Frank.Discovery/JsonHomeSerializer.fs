module Frank.Discovery.JsonHomeSerializer

open System.Text
open System.Text.Json

/// Each resource entry: relation URI maps to href + allow hints.
type JsonHomeResource =
    { Relation: string
      Href: string
      Allow: string list }

/// Serialize resource entries to a JSON Home document string.
let serialize (resources: JsonHomeResource list) : string =
    use ms = new System.IO.MemoryStream()
    use writer = new Utf8JsonWriter(ms)
    writer.WriteStartObject()
    writer.WritePropertyName("resources")
    writer.WriteStartObject()

    for r in resources do
        writer.WritePropertyName(r.Relation)
        writer.WriteStartObject()
        writer.WriteString("href", r.Href)
        writer.WritePropertyName("hints")
        writer.WriteStartObject()
        writer.WritePropertyName("allow")
        writer.WriteStartArray()

        for m in r.Allow do
            writer.WriteStringValue(m)

        writer.WriteEndArray()
        writer.WriteEndObject()
        writer.WriteEndObject()

    writer.WriteEndObject()
    writer.WriteEndObject()
    writer.Flush()
    Encoding.UTF8.GetString(ms.ToArray())
