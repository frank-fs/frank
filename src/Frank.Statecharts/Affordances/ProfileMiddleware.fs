namespace Frank.Affordances

open System
open System.IO
open Frank.Resources.Model
open Microsoft.AspNetCore.Builder
open Microsoft.AspNetCore.Http
open Microsoft.AspNetCore.Routing
open Microsoft.Net.Http.Headers
open VDS.RDF
open VDS.RDF.Parsing
open Frank.LinkedData.Negotiation

/// Extension methods for mapping profile endpoints that serve
/// pre-generated ALPS, OWL, SHACL, and JSON Schema documents.
/// All content is served from pre-computed strings loaded at startup --
/// zero I/O and zero computation at request time.
[<AutoOpen>]
module ProfileMiddlewareExtensions =

    /// Pre-compute all RDF serialization formats from a Turtle source string.
    /// Parses once, serializes to all formats, disposes the graph. Returns a
    /// Map<mediaType, byte[]> for zero-alloc serving at request time.
    let private preComputeShapeFormats (turtleContent: string) : Map<string, byte[]> =
        use g = new Graph() :> IGraph
        let parser = TurtleParser()
        use reader = new StringReader(turtleContent)
        parser.Load(g, reader)

        let serializeTo (writeFn: IGraph -> Stream -> unit) =
            use ms = new MemoryStream()
            writeFn g ms
            ms.ToArray()

        Map.ofList
            [ "text/turtle", Text.Encoding.UTF8.GetBytes(turtleContent)
              "application/ld+json", serializeTo JsonLdFormatter.writeJsonLd
              "application/rdf+xml", serializeTo RdfXmlFormatter.writeRdfXml ]

    /// Supported RDF media types for shape content negotiation.
    let private shapeMediaTypes =
        [ "text/turtle"; "application/ld+json"; "application/rdf+xml" ]

    /// Negotiate RDF media type from Accept header, defaulting to text/turtle.
    /// Note: Frank.LinkedData has a similar negotiateRdfType (private), kept
    /// separate because it returns Option and lives in a different module scope.
    let private negotiateShapeType (accept: string) : string =
        if String.IsNullOrWhiteSpace accept then
            "text/turtle"
        else
            let success, mediaTypes =
                MediaTypeHeaderValue.TryParseList(Microsoft.Extensions.Primitives.StringValues(accept))

            if not success || isNull mediaTypes then
                "text/turtle"
            else
                mediaTypes
                |> Seq.sortByDescending (fun mt -> mt.Quality |> Option.ofNullable |> Option.defaultValue 1.0)
                |> Seq.tryPick (fun mt ->
                    shapeMediaTypes
                    |> List.tryFind (fun s -> mt.MediaType.Equals(s, StringComparison.OrdinalIgnoreCase)))
                |> Option.defaultValue "text/turtle"

    /// Serve a pre-computed string at the given path with the specified content type.
    /// Sets Vary: Accept header for correct caching behavior.
    let private mapProfileEndpoint
        (endpoints: IEndpointRouteBuilder)
        (pathPrefix: string)
        (slug: string)
        (content: string)
        (contentType: string)
        =
        endpoints.MapGet(
            sprintf "%s/%s" pathPrefix slug,
            RequestDelegate(fun ctx ->
                ctx.Response.ContentType <- contentType
                ctx.Response.Headers["Vary"] <- Microsoft.Extensions.Primitives.StringValues("Accept")
                ctx.Response.WriteAsync(content))
        )
        |> ignore

    /// Serve SHACL shapes with content negotiation at /shapes/{slug}.
    /// All formats pre-computed at startup — zero parsing/serialization at request time.
    let private mapShapeEndpoint (endpoints: IEndpointRouteBuilder) (slug: string) (turtleContent: string) =
        let formats = preComputeShapeFormats turtleContent

        endpoints.MapGet(
            sprintf "/shapes/%s" slug,
            RequestDelegate(fun ctx ->
                let accept = ctx.Request.Headers.Accept.ToString()
                let mediaType = negotiateShapeType accept
                let bytes = formats |> Map.find mediaType
                ctx.Response.ContentType <- mediaType
                ctx.Response.Headers["Vary"] <- Microsoft.Extensions.Primitives.StringValues("Accept")
                ctx.Response.Body.WriteAsync(bytes, 0, bytes.Length))
        )
        |> ignore

    type IEndpointRouteBuilder with

        /// Map all profile endpoints from projected profiles.
        /// Registers endpoints at:
        ///   GET /alps/{slug}     -> ALPS JSON (application/alps+json)
        ///   GET /ontology/{slug} -> OWL Turtle (text/turtle)
        ///   GET /shapes/{slug}   -> SHACL Turtle (text/turtle)
        ///   GET /schemas/{slug}  -> JSON Schema (application/schema+json)
        ///
        /// Unknown slugs will return 404 automatically since no endpoint matches.
        member endpoints.MapProfiles(profiles: ProjectedProfiles) =
            // ALPS endpoints
            for slug, alpsJson in Map.toSeq profiles.AlpsProfiles do
                mapProfileEndpoint endpoints "/alps" slug alpsJson "application/alps+json"

            // Per-role ALPS endpoints
            for slug, alpsJson in Map.toSeq profiles.RoleAlpsProfiles do
                mapProfileEndpoint endpoints "/alps" slug alpsJson "application/alps+json"

            // OWL ontology endpoints
            for slug, owlTurtle in Map.toSeq profiles.OwlOntologies do
                mapProfileEndpoint endpoints "/ontology" slug owlTurtle "text/turtle"

            // SHACL shape endpoints (with content negotiation: Turtle, JSON-LD, RDF/XML)
            for slug, shaclTurtle in Map.toSeq profiles.ShaclShapes do
                mapShapeEndpoint endpoints slug shaclTurtle

            // JSON Schema endpoints
            for slug, jsonSchema in Map.toSeq profiles.JsonSchemas do
                mapProfileEndpoint endpoints "/schemas" slug jsonSchema "application/schema+json"

        /// Map a discovery endpoint at /.well-known/frank-profiles that lists
        /// all available profile URLs for machine discoverability.
        member endpoints.MapProfileDiscovery(profiles: ProjectedProfiles) =
            let discoveryJson =
                let alps =
                    (Map.toList profiles.AlpsProfiles @ Map.toList profiles.RoleAlpsProfiles)
                    |> List.map (fun (slug, _) ->
                        sprintf """{"slug":"%s","url":"/alps/%s","type":"application/alps+json"}""" slug slug)

                let ontology =
                    profiles.OwlOntologies
                    |> Map.toList
                    |> List.map (fun (slug, _) ->
                        sprintf """{"slug":"%s","url":"/ontology/%s","type":"text/turtle"}""" slug slug)

                let shapes =
                    profiles.ShaclShapes
                    |> Map.toList
                    |> List.map (fun (slug, _) ->
                        sprintf """{"slug":"%s","url":"/shapes/%s","type":"text/turtle"}""" slug slug)

                let schemas =
                    profiles.JsonSchemas
                    |> Map.toList
                    |> List.map (fun (slug, _) ->
                        sprintf """{"slug":"%s","url":"/schemas/%s","type":"application/schema+json"}""" slug slug)

                let all = alps @ ontology @ shapes @ schemas

                sprintf """{"profiles":[%s]}""" (String.Join(",", all))

            endpoints.MapGet(
                "/.well-known/frank-profiles",
                RequestDelegate(fun ctx ->
                    ctx.Response.ContentType <- "application/json"
                    ctx.Response.WriteAsync(discoveryJson))
            )
            |> ignore
