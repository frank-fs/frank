namespace Frank.Affordances

open System
open Frank.Resources.Model
open Microsoft.AspNetCore.Builder
open Microsoft.AspNetCore.Http
open Microsoft.AspNetCore.Routing

/// Extension methods for mapping profile endpoints that serve
/// pre-generated ALPS, OWL, SHACL, and JSON Schema documents.
/// All content is served from pre-computed strings loaded at startup --
/// zero I/O and zero computation at request time.
[<AutoOpen>]
module ProfileMiddlewareExtensions =

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

            // OWL ontology endpoints
            for slug, owlTurtle in Map.toSeq profiles.OwlOntologies do
                mapProfileEndpoint endpoints "/ontology" slug owlTurtle "text/turtle"

            // SHACL shape endpoints
            for slug, shaclTurtle in Map.toSeq profiles.ShaclShapes do
                mapProfileEndpoint endpoints "/shapes" slug shaclTurtle "text/turtle"

            // JSON Schema endpoints
            for slug, jsonSchema in Map.toSeq profiles.JsonSchemas do
                mapProfileEndpoint endpoints "/schemas" slug jsonSchema "application/schema+json"

        /// Map a discovery endpoint at /.well-known/frank-profiles that lists
        /// all available profile URLs for machine discoverability.
        member endpoints.MapProfileDiscovery(profiles: ProjectedProfiles) =
            let discoveryJson =
                let alps =
                    profiles.AlpsProfiles
                    |> Map.toList
                    |> List.map (fun (slug, _) -> sprintf """{"slug":"%s","url":"/alps/%s","type":"application/alps+json"}""" slug slug)

                let ontology =
                    profiles.OwlOntologies
                    |> Map.toList
                    |> List.map (fun (slug, _) -> sprintf """{"slug":"%s","url":"/ontology/%s","type":"text/turtle"}""" slug slug)

                let shapes =
                    profiles.ShaclShapes
                    |> Map.toList
                    |> List.map (fun (slug, _) -> sprintf """{"slug":"%s","url":"/shapes/%s","type":"text/turtle"}""" slug slug)

                let schemas =
                    profiles.JsonSchemas
                    |> Map.toList
                    |> List.map (fun (slug, _) -> sprintf """{"slug":"%s","url":"/schemas/%s","type":"application/schema+json"}""" slug slug)

                let all = alps @ ontology @ shapes @ schemas

                sprintf """{"profiles":[%s]}""" (String.Join(",", all))

            endpoints.MapGet(
                "/.well-known/frank-profiles",
                RequestDelegate(fun ctx ->
                    ctx.Response.ContentType <- "application/json"
                    ctx.Response.WriteAsync(discoveryJson))
            )
            |> ignore
