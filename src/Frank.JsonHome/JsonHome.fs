namespace Frank.JsonHome

open System.IO
open System.Text.Json
open System.Threading.Tasks
open Microsoft.AspNetCore.Http

type JsonHomeOptions =
    { Path: string
      Rel: string
      Title: string option
      Links: (string * string) list }

    static member Default =
        { Path = "/.well-known/home.json"
          Rel = "home"
          Title = None
          Links = [] }

module JsonHome =

    [<Literal>]
    let MediaType = "application/json-home"

    /// draft-06 names the accept hints per method; other methods contribute none.
    let private acceptHintName httpMethod =
        match httpMethod with
        | "POST" -> Some "acceptPost"
        | "PUT" -> Some "acceptPut"
        | "PATCH" -> Some "acceptPatch"
        | _ -> None

    let private statusName status =
        match status with
        | ResourceStatus.Deprecated -> "deprecated"
        | ResourceStatus.Gone -> "gone"

    let private writeStringArray (writer: Utf8JsonWriter) name values =
        writer.WriteStartArray(name: string)

        for value in values do
            writer.WriteStringValue(value: string)

        writer.WriteEndArray()

    let private writeHints (writer: Utf8JsonWriter) (resource: ResourceDescription) =
        writer.WriteStartObject "hints"

        if not (List.isEmpty resource.Methods) then
            writeStringArray writer "allow" resource.Methods

        if not (List.isEmpty resource.Formats) then
            writer.WriteStartObject "formats"
            // Each media type maps to an empty object, per the draft.
            for mediaType in List.distinct resource.Formats do
                writer.WriteStartObject(mediaType)
                writer.WriteEndObject()

            writer.WriteEndObject()

        for httpMethod, contentTypes in resource.Accepts do
            match acceptHintName httpMethod with
            | Some hint when not (List.isEmpty contentTypes) -> writeStringArray writer hint contentTypes
            | _ -> ()

        resource.Docs |> Option.iter (fun uri -> writer.WriteString("docs", uri))
        resource.Status |> Option.iter (fun s -> writer.WriteString("status", statusName s))

        writer.WriteEndObject()

    let private writeResource (writer: Utf8JsonWriter) (resource: ResourceDescription) =
        writer.WriteStartObject(resource.Rel)

        if resource.IsTemplated then
            writer.WriteString("hrefTemplate", resource.Href)

            let hrefVars = resource.HrefVars |> List.distinctBy fst

            if not (List.isEmpty hrefVars) then
                writer.WriteStartObject "hrefVars"

                for name, uri in hrefVars do
                    writer.WriteString(name, uri)

                writer.WriteEndObject()
        else
            writer.WriteString("href", resource.Href)

        writeHints writer resource
        writer.WriteEndObject()

    let private writeDocument (writer: Utf8JsonWriter) options resources =
        writer.WriteStartObject()

        if options.Title.IsSome || not (List.isEmpty options.Links) then
            writer.WriteStartObject "api"
            options.Title |> Option.iter (fun t -> writer.WriteString("title", t))

            if not (List.isEmpty options.Links) then
                writer.WriteStartObject "links"

                for rel, target in options.Links do
                    writer.WriteString(rel, target)

                writer.WriteEndObject()

            writer.WriteEndObject()

        writer.WriteStartObject "resources"
        // Later duplicates would overwrite earlier ones in a JSON object, so
        // duplicate rels are rejected at startup rather than silently merged.
        for resource in resources do
            writeResource writer resource

        writer.WriteEndObject()

        writer.WriteEndObject()

    let serialize (options: JsonHomeOptions) (resources: ResourceDescription list) =
        use stream = new MemoryStream()
        use writer = new Utf8JsonWriter(stream)
        writeDocument writer options resources
        writer.Flush()
        System.Text.Encoding.UTF8.GetString(stream.ToArray())

    let write (options: JsonHomeOptions) (resources: ResourceDescription list) (ctx: HttpContext) : Task =
        ctx.Response.ContentType <- MediaType
        ctx.Response.WriteAsync(serialize options resources)
