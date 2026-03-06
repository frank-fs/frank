namespace Frank.Cli.Core.Extraction

open System
open VDS.RDF
open Frank.Cli.Core.Rdf
open Frank.Cli.Core.Rdf.FSharpRdf
open Frank.Cli.Core.Rdf.Vocabularies
open Frank.Cli.Core.Analysis

/// Maps Frank routes to RDF resource identities.
module RouteMapper =

    type MappedResource =
        { ResourceUri: Uri
          RouteTemplate: string
          UriTemplate: string
          Name: string option
          LinkedClass: Uri option }

    let private routeToSlug (route: string) =
        route.TrimStart('/').Replace("/", "_").Replace("{", "").Replace("}", "")

    let private resourceUri (config: TypeMapper.MappingConfig) (route: string) =
        let base' = config.BaseUri.ToString().TrimEnd('/')
        let cleanRoute = route.TrimStart('/')
        Uri(base' + "/resources/" + cleanRoute)

    let private uriTemplate (config: TypeMapper.MappingConfig) (route: string) =
        let base' = config.BaseUri.ToString().TrimEnd('/')
        let cleanRoute = route.TrimStart('/')
        base' + "/resources/" + cleanRoute

    let private findLinkedClass
        (config: TypeMapper.MappingConfig)
        (resource: AnalyzedResource)
        (types: AnalyzedType list)
        : Uri option =
        if not resource.HasLinkedData then
            None
        else
            // Try to match resource name or route segment to a type
            let candidateName =
                resource.Name
                |> Option.defaultWith (fun () ->
                    let parts = resource.RouteTemplate.TrimStart('/').Split('/')
                    parts |> Array.tryHead |> Option.defaultValue "")

            types
            |> List.tryFind (fun t ->
                t.ShortName.ToLowerInvariant() = candidateName.ToLowerInvariant()
                || t.ShortName.ToLowerInvariant() + "s" = candidateName.ToLowerInvariant())
            |> Option.map (fun t -> Uri(config.BaseUri.ToString().TrimEnd('/') + "/types/" + t.ShortName))

    let mapRoutes
        (config: TypeMapper.MappingConfig)
        (resources: AnalyzedResource list)
        (types: AnalyzedType list)
        : IGraph =
        let graph = createGraph ()
        let rdfType = createUriNode graph (Uri Rdf.Type)

        for res in resources do
            let resUri = resourceUri config res.RouteTemplate
            let resNode = createUriNode graph resUri
            let template = uriTemplate config res.RouteTemplate

            // rdf:type hydra:Resource
            assertTriple graph (resNode, rdfType, createUriNode graph (Uri Hydra.Resource))

            // rdfs:label
            let label = res.Name |> Option.defaultValue res.RouteTemplate
            assertTriple graph (resNode, createUriNode graph (Uri Rdfs.Label), createLiteralNode graph label None)

            // hydra:template
            assertTriple
                graph
                (resNode,
                 createUriNode graph (Uri Hydra.Template),
                 createLiteralNode graph template (Some(Uri Xsd.String)))

            // hydra:supportedClass if linked
            let linkedClass = findLinkedClass config res types

            linkedClass
            |> Option.iter (fun classUri ->
                assertTriple
                    graph
                    (resNode, createUriNode graph (Uri Hydra.SupportedClass), createUriNode graph classUri))

        graph
