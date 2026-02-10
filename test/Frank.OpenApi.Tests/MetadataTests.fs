module Frank.OpenApi.Tests.MetadataTests

open System
open System.Threading.Tasks
open Microsoft.AspNetCore.Http
open Microsoft.AspNetCore.Http.Metadata
open Microsoft.AspNetCore.Routing
open Expecto
open Frank.Builder
open Frank.OpenApi

// Sample types for testing
type Product = { Name: string; Price: decimal }
type CreateRequest = { Name: string }

[<Tests>]
let tests =
    testList "Metadata Integration" [
        test "HandlerDefinition passed to resource get operation adds metadata to endpoint" {
            let handlerDef =
                handler {
                    name "getProducts"
                    summary "List products"
                    description "Returns all products"
                    tags [ "Products"; "Public" ]
                    produces typeof<Product> 200
                    handle (fun (ctx: HttpContext) -> Task.CompletedTask)
                }

            let builtResource =
                resource "/products" {
                    name "Products"
                    get handlerDef
                }

            Expect.hasLength builtResource.Endpoints 1 "Should have 1 endpoint"

            let endpoint = builtResource.Endpoints.[0]

            // Check endpoint name metadata
            let nameMetadata = endpoint.Metadata.GetMetadata<EndpointNameMetadata>()
            Expect.isNotNull nameMetadata "Should have name metadata"
            Expect.equal nameMetadata.EndpointName "getProducts" "Endpoint name should match"

            // Check summary
            let summaryMetadata = endpoint.Metadata.GetMetadata<EndpointSummaryAttribute>()
            Expect.isNotNull summaryMetadata "Should have summary metadata"
            Expect.equal summaryMetadata.Summary "List products" "Summary should match"

            // Check description
            let descMetadata = endpoint.Metadata.GetMetadata<EndpointDescriptionAttribute>()
            Expect.isNotNull descMetadata "Should have description metadata"
            Expect.equal descMetadata.Description "Returns all products" "Description should match"

            // Check tags
            let tagsMetadata = endpoint.Metadata.GetMetadata<TagsAttribute>()
            Expect.isNotNull tagsMetadata "Should have tags metadata"
            Expect.containsAll tagsMetadata.Tags [ "Products"; "Public" ] "Tags should match"

            // Check produces response type
            let producesMetadata = endpoint.Metadata.GetMetadata<ProducesResponseTypeMetadata>()
            Expect.isNotNull producesMetadata "Should have produces metadata"
        }

        test "HandlerDefinition with accepts metadata adds request body metadata" {
            let handlerDef =
                handler {
                    accepts typeof<CreateRequest>
                    handle (fun (ctx: HttpContext) -> Task.CompletedTask)
                }

            let builtResource =
                resource "/products" {
                    post handlerDef
                }

            let endpoint = builtResource.Endpoints.[0]

            let acceptsMetadata = endpoint.Metadata.GetMetadata<AcceptsMetadata>()
            Expect.isNotNull acceptsMetadata "Should have accepts metadata"
            Expect.equal acceptsMetadata.RequestType typeof<CreateRequest> "Request type should match"
            Expect.contains acceptsMetadata.ContentTypes "application/json" "Should have JSON content type"
            Expect.isFalse acceptsMetadata.IsOptional "Should not be optional"
        }

        test "Resource with plain handler and HandlerDefinition both work" {
            let plainHandler : RequestDelegate = RequestDelegate(fun ctx -> Task.CompletedTask)

            let handlerDef =
                handler {
                    name "createProduct"
                    produces typeof<Product> 201
                    handle (fun (ctx: HttpContext) -> Task.CompletedTask)
                }

            let builtResource =
                resource "/products" {
                    get plainHandler
                    post handlerDef
                }

            Expect.hasLength builtResource.Endpoints 2 "Should have 2 endpoints"

            // Find endpoints by HTTP method
            let getEndpoint =
                builtResource.Endpoints
                |> Array.find (fun e ->
                    let meta = e.Metadata.GetMetadata<HttpMethodMetadata>()
                    meta <> null && (meta.HttpMethods |> Seq.contains "GET"))

            let postEndpoint =
                builtResource.Endpoints
                |> Array.find (fun e ->
                    let meta = e.Metadata.GetMetadata<HttpMethodMetadata>()
                    meta <> null && (meta.HttpMethods |> Seq.contains "POST"))

            // Plain handler should have HttpMethodMetadata but no name metadata
            let getHttpMethod = getEndpoint.Metadata.GetMetadata<HttpMethodMetadata>()
            Expect.isNotNull getHttpMethod "GET should have HTTP method metadata"
            let getName = getEndpoint.Metadata.GetMetadata<EndpointNameMetadata>()
            Expect.isNull getName "GET should not have name metadata"

            // HandlerDefinition should have both HttpMethodMetadata and name metadata
            let postHttpMethod = postEndpoint.Metadata.GetMetadata<HttpMethodMetadata>()
            Expect.isNotNull postHttpMethod "POST should have HTTP method metadata"
            let postName = postEndpoint.Metadata.GetMetadata<EndpointNameMetadata>()
            Expect.isNotNull postName "POST should have name metadata"
            Expect.equal postName.EndpointName "createProduct" "POST name should match"
        }

        test "HandlerDefinition works with all HTTP methods" {
            let createHandler methodName =
                handler {
                    name methodName
                    handle (fun (ctx: HttpContext) -> Task.CompletedTask)
                }

            let builtResource =
                resource "/test" {
                    get (createHandler "testGet")
                    post (createHandler "testPost")
                    put (createHandler "testPut")
                    delete (createHandler "testDelete")
                    patch (createHandler "testPatch")
                }

            Expect.hasLength builtResource.Endpoints 5 "Should have 5 endpoints"

            // Verify each endpoint has the correct name
            let endpoints = builtResource.Endpoints |> Array.toList
            let names = endpoints |> List.map (fun e ->
                let meta = e.Metadata.GetMetadata<EndpointNameMetadata>()
                if isNull meta then null else meta.EndpointName)

            Expect.contains names "testGet" "Should have GET endpoint"
            Expect.contains names "testPost" "Should have POST endpoint"
            Expect.contains names "testPut" "Should have PUT endpoint"
            Expect.contains names "testDelete" "Should have DELETE endpoint"
            Expect.contains names "testPatch" "Should have PATCH endpoint"
        }

        test "HandlerDefinition with multiple produces entries creates multiple metadata entries" {
            let handlerDef =
                handler {
                    produces typeof<Product> 200
                    produces typeof<Product> 201
                    producesEmpty 404
                    handle (fun (ctx: HttpContext) -> Task.CompletedTask)
                }

            let builtResource =
                resource "/products" {
                    get handlerDef
                }

            let endpoint = builtResource.Endpoints.[0]

            // Count ProducesResponseTypeMetadata instances
            let producesMetadata =
                endpoint.Metadata
                |> Seq.filter (fun m -> m :? ProducesResponseTypeMetadata)
                |> Seq.cast<ProducesResponseTypeMetadata>
                |> Seq.toList

            Expect.hasLength producesMetadata 3 "Should have 3 produces metadata entries"

            let statusCodes = producesMetadata |> List.map (fun m -> m.StatusCode) |> List.sort
            Expect.equal statusCodes [ 200; 201; 404 ] "Should have all status codes"
        }

        test "HandlerDefinition with custom content types" {
            let handlerDef =
                handler {
                    produces typeof<Product> 200 ["application/xml"; "application/json"]
                    accepts typeof<CreateRequest> ["application/xml"]
                    handle (fun (ctx: HttpContext) -> Task.CompletedTask)
                }

            let builtResource =
                resource "/products" {
                    post handlerDef
                }

            let endpoint = builtResource.Endpoints.[0]

            let producesMetadata = endpoint.Metadata.GetMetadata<ProducesResponseTypeMetadata>()
            Expect.isNotNull producesMetadata "Should have produces metadata"
            Expect.containsAll producesMetadata.ContentTypes ["application/xml"; "application/json"] "Should have custom content types"

            let acceptsMetadata = endpoint.Metadata.GetMetadata<AcceptsMetadata>()
            Expect.isNotNull acceptsMetadata "Should have accepts metadata"
            Expect.contains acceptsMetadata.ContentTypes "application/xml" "Should accept XML"
        }
    ]
