module internal Frank.Statecharts.Alps.JsonGenerator

open System.IO
open System.Text
open System.Text.Json
open Frank.Statecharts.Alps.Types

/// Convert a DescriptorType DU case to its ALPS string representation.
let private descriptorTypeString (dt: DescriptorType) : string =
    match dt with
    | Semantic -> "semantic"
    | Safe -> "safe"
    | Unsafe -> "unsafe"
    | Idempotent -> "idempotent"

/// Write an ALPS documentation element as a JSON object.
let private writeDocumentation (writer: Utf8JsonWriter) (doc: AlpsDocumentation) =
    writer.WritePropertyName("doc")
    writer.WriteStartObject()
    doc.Format |> Option.iter (fun f -> writer.WriteString("format", f))
    writer.WriteString("value", doc.Value)
    writer.WriteEndObject()

/// Write an ALPS extension element as a JSON object.
let private writeExtension (writer: Utf8JsonWriter) (ext: AlpsExtension) =
    writer.WriteStartObject()
    writer.WriteString("id", ext.Id)
    ext.Href |> Option.iter (fun h -> writer.WriteString("href", h))
    ext.Value |> Option.iter (fun v -> writer.WriteString("value", v))
    writer.WriteEndObject()

/// Write an ALPS link element as a JSON object.
let private writeLink (writer: Utf8JsonWriter) (link: AlpsLink) =
    writer.WriteStartObject()
    writer.WriteString("rel", link.Rel)
    writer.WriteString("href", link.Href)
    writer.WriteEndObject()

/// Write a single descriptor recursively, including all nested children.
let rec private writeDescriptor (writer: Utf8JsonWriter) (d: Descriptor) =
    writer.WriteStartObject()

    d.Id |> Option.iter (fun id -> writer.WriteString("id", id))
    writer.WriteString("type", descriptorTypeString d.Type)
    d.Href |> Option.iter (fun h -> writer.WriteString("href", h))
    d.ReturnType |> Option.iter (fun rt -> writer.WriteString("rt", rt))

    d.Documentation |> Option.iter (fun doc -> writeDocumentation writer doc)

    if not d.Descriptors.IsEmpty then
        writer.WritePropertyName("descriptor")
        writer.WriteStartArray()
        d.Descriptors |> List.iter (writeDescriptor writer)
        writer.WriteEndArray()

    if not d.Extensions.IsEmpty then
        writer.WritePropertyName("ext")
        writer.WriteStartArray()
        d.Extensions |> List.iter (writeExtension writer)
        writer.WriteEndArray()

    if not d.Links.IsEmpty then
        writer.WritePropertyName("link")
        writer.WriteStartArray()
        d.Links |> List.iter (writeLink writer)
        writer.WriteEndArray()

    writer.WriteEndObject()

/// Generate an ALPS JSON string from an AlpsDocument AST.
let generate (doc: AlpsDocument) : string =
    use stream = new MemoryStream()
    use writer = new Utf8JsonWriter(stream, JsonWriterOptions(Indented = true))

    writer.WriteStartObject()
    writer.WritePropertyName("alps")
    writer.WriteStartObject()

    match doc.Version with
    | Some v -> writer.WriteString("version", v)
    | None -> ()

    doc.Documentation |> Option.iter (fun d -> writeDocumentation writer d)

    if not doc.Descriptors.IsEmpty then
        writer.WritePropertyName("descriptor")
        writer.WriteStartArray()
        doc.Descriptors |> List.iter (writeDescriptor writer)
        writer.WriteEndArray()

    if not doc.Links.IsEmpty then
        writer.WritePropertyName("link")
        writer.WriteStartArray()
        doc.Links |> List.iter (writeLink writer)
        writer.WriteEndArray()

    if not doc.Extensions.IsEmpty then
        writer.WritePropertyName("ext")
        writer.WriteStartArray()
        doc.Extensions |> List.iter (writeExtension writer)
        writer.WriteEndArray()

    writer.WriteEndObject()
    writer.WriteEndObject()
    writer.Flush()

    Encoding.UTF8.GetString(stream.ToArray())
