namespace Frank.Discovery

open System.Text.Json

/// Input types for JSON Home document generation.
/// Pure F# records — no ASP.NET Core or framework dependencies.

type JsonHomeHints =
    { Allow: string list
      /// GET response media types. Serialized as JSON object {"media/type": {}} per JSON Home spec.
      Formats: string list
      AcceptPost: string list option
      AcceptPut: string list option
      AcceptPatch: string list option
      DocsUrl: string option }

type JsonHomeResource =
    { RelationType: string
      /// If RouteVariables is empty → "href". If non-empty → "hrefTemplate" + "hrefVars".
      RouteTemplate: string
      RouteVariables: Map<string, string>
      Hints: JsonHomeHints }

type JsonHomeInput =
    { Title: string
      DescribedByUrl: string option
      Resources: JsonHomeResource list }

module JsonHomeDocument =

    let private writeOptionalArray (w: Utf8JsonWriter) (name: string) (types: string list option) =
        match types with
        | Some ts when not ts.IsEmpty ->
            w.WriteStartArray(name)
            for t in ts do w.WriteStringValue(t)
            w.WriteEndArray()
        | _ -> ()

    let private writeHints (w: Utf8JsonWriter) (hints: JsonHomeHints) =
        w.WriteStartObject("hints")

        w.WriteStartArray("allow")
        for m in hints.Allow do w.WriteStringValue(m)
        w.WriteEndArray()

        if not hints.Formats.IsEmpty then
            w.WriteStartObject("formats")
            for fmt in hints.Formats do
                w.WriteStartObject(fmt)
                w.WriteEndObject()
            w.WriteEndObject()

        writeOptionalArray w "accept-post" hints.AcceptPost
        writeOptionalArray w "accept-put" hints.AcceptPut
        writeOptionalArray w "accept-patch" hints.AcceptPatch

        match hints.DocsUrl with
        | Some url -> w.WriteString("docs", url)
        | None -> ()

        w.WriteEndObject()

    let private writeResource (w: Utf8JsonWriter) (res: JsonHomeResource) =
        w.WriteStartObject(res.RelationType)

        if Map.isEmpty res.RouteVariables then
            w.WriteString("href", res.RouteTemplate)
        else
            w.WriteString("hrefTemplate", res.RouteTemplate)
            w.WriteStartObject("hrefVars")
            for kv in res.RouteVariables do
                w.WriteString(kv.Key, kv.Value)
            w.WriteEndObject()

        writeHints w res.Hints
        w.WriteEndObject()

    /// Build a JSON Home document string from the given input.
    let build (input: JsonHomeInput) : string =
        use stream = new System.IO.MemoryStream()
        use w = new Utf8JsonWriter(stream, JsonWriterOptions(Indented = false))

        w.WriteStartObject()

        w.WriteStartObject("api")
        w.WriteString("title", input.Title)
        match input.DescribedByUrl with
        | Some url ->
            w.WriteStartObject("links")
            w.WriteStartObject("describedBy")
            w.WriteString("href", url)
            w.WriteEndObject()
            w.WriteEndObject()
        | None -> ()
        w.WriteEndObject()

        w.WriteStartObject("resources")
        for res in input.Resources do
            writeResource w res
        w.WriteEndObject()

        w.WriteEndObject()
        w.Flush()
        System.Text.Encoding.UTF8.GetString(stream.GetBuffer(), 0, int stream.Length)
