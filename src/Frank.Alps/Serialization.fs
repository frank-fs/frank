namespace Frank.Alps

open System.IO
open System.Text
open System.Text.Json

[<AutoOpen>]
module SerializationExt =
    [<Literal>]
    let ProtocolStateExtId = "https://frank-fs.github.io/alps-ext/protocolState"

    [<Literal>]
    let AvailableInStatesExtId = "https://frank-fs.github.io/alps-ext/availableInStates"

module Serialization =
    let private formatToString (f: DocFormat) : string =
        match f with
        | DocFormat.Text -> "text"
        | DocFormat.Html -> "html"
        | DocFormat.Asciidoc -> "asciidoc"
        | DocFormat.Markdown -> "markdown"

    let private resolveHref (r: DescriptorRef) : string =
        match r with
        | DescriptorRef.Local target -> "#" + target.Id
        | DescriptorRef.External uri -> uri.ToString()

    let private writeDoc (writer: Utf8JsonWriter) (doc: Doc) : unit =
        writer.WriteStartObject("doc")
        writer.WriteString("value", doc.Value)
        doc.Href |> Option.iter (fun h -> writer.WriteString("href", h.ToString()))
        doc.Format |> Option.iter (fun f -> writer.WriteString("format", formatToString f))
        doc.ContentType |> Option.iter (fun c -> writer.WriteString("contentType", c))
        if not (List.isEmpty doc.Tag) then
            writer.WriteString("tag", String.concat " " doc.Tag)
        writer.WriteEndObject()

    let private writeLinkElement (writer: Utf8JsonWriter) (l: Link) : unit =
        writer.WriteStartObject()
        writer.WriteString("href", l.Href.ToString())
        writer.WriteString("rel", l.Rel)
        l.Title |> Option.iter (fun t -> writer.WriteString("title", t))
        if not (List.isEmpty l.Tag) then
            writer.WriteString("tag", String.concat " " l.Tag)
        writer.WriteEndObject()

    let private writeExtElement (writer: Utf8JsonWriter) (e: Ext) : unit =
        writer.WriteStartObject()
        writer.WriteString("id", e.Id)
        e.Href |> Option.iter (fun h -> writer.WriteString("href", h.ToString()))
        e.Value |> Option.iter (fun v -> writer.WriteString("value", v))
        if not (List.isEmpty e.Tag) then
            writer.WriteString("tag", String.concat " " e.Tag)
        writer.WriteEndObject()

    let private stateExtPairs (from_: Descriptor list) : Ext list =
        from_
        |> List.collect (fun state ->
            let value = Some("#" + state.Id)

            [ { Id = ProtocolStateExtId
                Href = None
                Value = value
                Tag = [] }
              { Id = AvailableInStatesExtId
                Href = None
                Value = value
                Tag = [] } ])

    let rec private writeDescriptor (writer: Utf8JsonWriter) (d: Descriptor) : unit =
        writer.WriteStartObject()
        writer.WriteString("id", d.Id)
        d.Name |> Option.iter (fun n -> writer.WriteString("name", n))

        match d.Type with
        | DescriptorType.Semantic -> ()
        | DescriptorType.Safe -> writer.WriteString("type", "safe")
        | DescriptorType.Unsafe -> writer.WriteString("type", "unsafe")
        | DescriptorType.Idempotent -> writer.WriteString("type", "idempotent")

        d.Def |> Option.iter (fun uri -> writer.WriteString("def", uri.ToString()))
        d.Doc |> Option.iter (writeDoc writer)

        let allExt = d.Ext @ stateExtPairs d.From

        if not (List.isEmpty allExt) then
            writer.WriteStartArray("ext")
            allExt |> List.iter (writeExtElement writer)
            writer.WriteEndArray()

        d.InheritsFrom |> Option.iter (fun r -> writer.WriteString("href", resolveHref r))
        d.Rt |> Option.iter (fun target -> writer.WriteString("rt", "#" + target.Id))
        d.Rel |> Option.iter (fun r -> writer.WriteString("rel", r))

        if not (List.isEmpty d.Tag) then
            writer.WriteString("tag", String.concat " " d.Tag)

        if not (List.isEmpty d.Link) then
            writer.WriteStartArray("link")
            d.Link |> List.iter (writeLinkElement writer)
            writer.WriteEndArray()

        if not (List.isEmpty d.Descriptors) then
            writer.WriteStartArray("descriptor")
            d.Descriptors |> List.iter (writeDescriptor writer)
            writer.WriteEndArray()

        writer.WriteEndObject()

    let toJson (profile: Descriptor list) : string =
        use stream = new MemoryStream()

        (use writer = new Utf8JsonWriter(stream)
         writer.WriteStartObject()
         writer.WriteStartObject("alps")
         writer.WriteString("version", "1.0")
         writer.WriteStartArray("descriptor")
         profile |> List.iter (writeDescriptor writer)
         writer.WriteEndArray()
         writer.WriteEndObject()
         writer.WriteEndObject())

        Encoding.UTF8.GetString(stream.ToArray())
