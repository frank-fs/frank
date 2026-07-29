namespace Frank.JsonHome

open System.IO
open System.Text.Json
open System.Threading.Tasks
open Microsoft.AspNetCore.Http
open Microsoft.AspNetCore.Mvc.ApiExplorer
open Microsoft.Extensions.DependencyInjection
open Microsoft.Extensions.Primitives

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

    let private preconditionName precondition =
        match precondition with
        | Precondition.ETag -> "etag"
        | Precondition.LastModified -> "last-modified"

    let private writeStringArray (writer: Utf8JsonWriter) name values =
        writer.WriteStartArray(name: string)

        for value in values do
            writer.WriteStringValue(value: string)

        writer.WriteEndArray()

    let private acceptHints (resource: ResourceDescription) =
        resource.Accepts
        |> List.choose (fun (httpMethod, contentTypes) ->
            match acceptHintName httpMethod with
            | Some hint when not (List.isEmpty contentTypes) -> Some(hint, contentTypes)
            | _ -> None)

    /// hints is optional, so an entry with nothing to hint omits it rather than
    /// carrying an empty object.
    let private hasHints (resource: ResourceDescription) =
        not (List.isEmpty resource.Methods)
        || not (List.isEmpty resource.Formats)
        || not (List.isEmpty (acceptHints resource))
        || not (List.isEmpty resource.AcceptRanges)
        || not (List.isEmpty resource.AcceptPrefer)
        || not (List.isEmpty resource.PreconditionRequired)
        || not (List.isEmpty resource.AuthSchemes)
        || resource.Docs.IsSome
        || resource.Status.IsSome

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

        for hint, contentTypes in acceptHints resource do
            writeStringArray writer hint contentTypes

        if not (List.isEmpty resource.AcceptRanges) then
            writeStringArray writer "acceptRanges" resource.AcceptRanges

        if not (List.isEmpty resource.AcceptPrefer) then
            writeStringArray writer "acceptPrefer" resource.AcceptPrefer

        resource.Docs |> Option.iter (fun uri -> writer.WriteString("docs", uri))

        if not (List.isEmpty resource.PreconditionRequired) then
            writeStringArray writer "preconditionRequired" (resource.PreconditionRequired |> List.map preconditionName)

        if not (List.isEmpty resource.AuthSchemes) then
            writer.WriteStartArray "authSchemes"

            for scheme, realms in resource.AuthSchemes do
                writer.WriteStartObject()
                writer.WriteString("scheme", scheme)

                // realms is optional, so a scheme covering none omits it.
                if not (List.isEmpty realms) then
                    writeStringArray writer "realms" realms

                writer.WriteEndObject()

            writer.WriteEndArray()

        resource.Status |> Option.iter (fun s -> writer.WriteString("status", statusName s))

        writer.WriteEndObject()

    let private writeResource (writer: Utf8JsonWriter) (resource: ResourceDescription) =
        writer.WriteStartObject(resource.Rel)

        if resource.IsTemplated then
            writer.WriteString("hrefTemplate", resource.Href)

            // draft-06 section 4: "When hrefTemplate is present, the Resource
            // Object MUST have a hrefVars property." It is written even when the
            // author declared no variable semantics, so the entry stays valid.
            writer.WriteStartObject "hrefVars"

            for name, uri in resource.HrefVars |> List.distinctBy fst do
                writer.WriteString(name, uri)

            writer.WriteEndObject()
        else
            writer.WriteString("href", resource.Href)

        if hasHints resource then
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

    /// RFC 8288 parameter values are quoted strings, so a backslash or quote in
    /// the relation type has to be escaped.
    let private escapeParam (value: string) =
        value.Replace("\\", "\\\\").Replace("\"", "\\\"")

    let middleware (options: JsonHomeOptions) =
        // The advertised link never varies by request, so it is built once here
        // rather than formatted on every response.
        let link =
            StringValues("<" + options.Path + ">; rel=\"" + escapeParam options.Rel + "\"")

        fun (ctx: HttpContext) (next: unit -> Task) ->
            // Append rather than assign: other packages may advertise links too,
            // and Link is a multi-value header.
            ctx.Response.Headers.Append("Link", link)

            if ctx.Request.Path.Equals(PathString options.Path) then
                task {
                    let provider =
                        ctx.RequestServices.GetRequiredService<IApiDescriptionGroupCollectionProvider>()

                    let all =
                        provider.ApiDescriptionGroups.Items
                        |> Seq.collect (fun g -> g.Items)
                        |> ApiSurface.ofApiDescriptions

                    let! resources = AuthorizationFilter.apply ctx all

                    if AuthorizationFilter.varies all then
                        // A shared cache must never serve one principal's view to another.
                        ctx.Response.Headers.CacheControl <- "private, no-cache"
                        ctx.Response.Headers.Vary <- "Authorization"

                    do! write options resources ctx
                }
                :> Task
            else
                next ()
