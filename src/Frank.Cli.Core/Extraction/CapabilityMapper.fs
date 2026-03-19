namespace Frank.Cli.Core.Extraction

open System
open VDS.RDF
open Frank.Cli.Core.Rdf
open Frank.Cli.Core.Rdf.FSharpRdf
open Frank.Cli.Core.Rdf.Vocabularies
open Frank.Cli.Core.Analysis
open Frank.Statecharts.Unified
open Frank.Cli.Core.Extraction.UriHelpers

/// Maps HTTP methods to Schema.org Actions and Hydra operations.
module CapabilityMapper =

    let private httpMethodToSchemaAction (method: HttpMethod) : string =
        match method with
        | Get -> SchemaOrg.ReadAction
        | Post -> SchemaOrg.CreateAction
        | Put -> SchemaOrg.UpdateAction
        | Delete -> SchemaOrg.DeleteAction
        | Patch -> SchemaOrg.UpdateAction
        | Head -> SchemaOrg.ReadAction
        | Options -> SchemaOrg.ReadAction

    let private httpMethodToString (method: HttpMethod) : string =
        match method with
        | Get -> "GET"
        | Post -> "POST"
        | Put -> "PUT"
        | Delete -> "DELETE"
        | Patch -> "PATCH"
        | Head -> "HEAD"
        | Options -> "OPTIONS"

    let private baseStr (config: TypeMapper.MappingConfig) = config.BaseUri.ToString().TrimEnd('/')

    let mapCapabilities (config: TypeMapper.MappingConfig) (resources: AnalyzedResource list) : IGraph =
        let graph = createGraph ()
        let rdfType = createUriNode graph (Uri Rdf.Type)

        for res in resources do
            let resUri = resourceUri (baseStr config) res.RouteTemplate
            let resNode = createUriNode graph resUri
            let slug = routeToSlug res.RouteTemplate

            for method in res.HttpMethods do
                let methodStr = httpMethodToString method
                let blankId = sprintf "op_%s_%s" slug (methodStr.ToLowerInvariant())
                let opNode = createBlankNode graph blankId

                // rdf:type hydra:Operation
                assertTriple graph (opNode, rdfType, createUriNode graph (Uri Hydra.Operation))

                // rdf:type schema:{ActionType}
                let actionUri = httpMethodToSchemaAction method
                assertTriple graph (opNode, rdfType, createUriNode graph (Uri actionUri))

                // hydra:method
                assertTriple
                    graph
                    (opNode,
                     createUriNode graph (Uri Hydra.Method),
                     createLiteralNode graph methodStr (Some(Uri Xsd.String)))

                // link resource to operation
                assertTriple graph (resNode, createUriNode graph (Uri Hydra.SupportedOperation), opNode)

        graph
