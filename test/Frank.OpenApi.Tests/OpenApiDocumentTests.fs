module Frank.OpenApi.Tests.OpenApiDocumentTests

open System.Net
open System.Net.Http
open System.Text.Json
open System.Threading.Tasks
open Microsoft.AspNetCore.Builder
open Microsoft.AspNetCore.Hosting
open Microsoft.AspNetCore.Http
open Microsoft.AspNetCore.OpenApi
open Microsoft.AspNetCore.Routing
open Microsoft.AspNetCore.TestHost
open Microsoft.Extensions.DependencyInjection
open Microsoft.Extensions.Hosting
open FSharp.Data.JsonSchema.OpenApi
open Expecto
open Frank.Builder
open Frank.OpenApi
open Scalar.AspNetCore
open Frank.Tests.Shared.TestEndpointDataSource

/// Creates a test server with Frank resources and OpenAPI enabled
let createOpenApiTestServer (resources: Resource list) =
    let allEndpoints =
        resources |> List.collect (fun r -> r.Endpoints |> Array.toList) |> List.toArray

    let builder =
        Host
            .CreateDefaultBuilder([||])
            .ConfigureWebHost(fun webBuilder ->
                webBuilder
                    .UseTestServer()
                    .ConfigureServices(fun services ->
                        services.AddRouting() |> ignore

                        services.AddOpenApi(fun options ->
                            options.AddSchemaTransformer(FSharpSchemaTransformer()) |> ignore

                            options.AddOperationTransformer(fun operation context _ct ->
                                if System.String.IsNullOrEmpty operation.OperationId then
                                    let hasExplicitName =
                                        context.Description.ActionDescriptor.EndpointMetadata
                                        |> Seq.exists (fun m -> m :? EndpointNameMetadata)

                                    if not hasExplicitName then
                                        let displayName = context.Description.ActionDescriptor.DisplayName

                                        if not (System.String.IsNullOrEmpty displayName) then
                                            let parts = displayName.Split(' ', 2)

                                            if parts.Length = 2 && not (parts[1].StartsWith("/")) then
                                                let httpMethod = parts[0].ToLowerInvariant()
                                                let resourceName = parts[1].Replace(" ", "")
                                                operation.OperationId <- httpMethod + resourceName

                                Task.CompletedTask)
                            |> ignore)
                        |> ignore)
                    .Configure(fun app ->
                        app
                            .UseRouting()
                            .UseEndpoints(fun endpoints ->
                                endpoints.DataSources.Add(TestEndpointDataSource(allEndpoints))
                                endpoints.MapOpenApi() |> ignore
                                endpoints.MapScalarApiReference() |> ignore)
                        |> ignore)
                |> ignore)

    let host = builder.Build()
    host.Start()
    host.GetTestClient()

/// Creates a test server WITHOUT OpenAPI enabled
let createPlainTestServer (resources: Resource list) =
    let allEndpoints =
        resources |> List.collect (fun r -> r.Endpoints |> Array.toList) |> List.toArray

    let builder =
        Host
            .CreateDefaultBuilder([||])
            .ConfigureWebHost(fun webBuilder ->
                webBuilder
                    .UseTestServer()
                    .ConfigureServices(fun services -> services.AddRouting() |> ignore)
                    .Configure(fun app ->
                        app
                            .UseRouting()
                            .UseEndpoints(fun endpoints ->
                                endpoints.DataSources.Add(TestEndpointDataSource(allEndpoints)))
                        |> ignore)
                |> ignore)

    let host = builder.Build()
    host.Start()
    host.GetTestClient()

let simpleHandler: RequestDelegate =
    RequestDelegate(fun ctx -> ctx.Response.WriteAsync("OK"))

/// Helper to check if a JSON element has a property
let hasProperty (name: string) (element: JsonElement) =
    let mutable value = Unchecked.defaultof<JsonElement>
    element.TryGetProperty(name, &value)

/// Helper to get and parse the OpenAPI document
let getOpenApiDoc (client: HttpClient) =
    task {
        let! (response: HttpResponseMessage) = client.GetAsync("/openapi/v1.json")
        let! (body: string) = response.Content.ReadAsStringAsync()
        return response, JsonDocument.Parse(body: string)
    }

// ===== US1: Serve OpenAPI Document =====

[<Tests>]
let us1Tests =
    testList
        "US1 - Serve OpenAPI Document"
        [ testTask "GET /openapi/v1.json returns 200 with valid OpenAPI document" {
              let products =
                  resource "/products" {
                      name "Products"
                      get simpleHandler
                  }

              let client = createOpenApiTestServer [ products ]
              let! (response: HttpResponseMessage) = client.GetAsync("/openapi/v1.json")
              Expect.equal response.StatusCode HttpStatusCode.OK "Should return 200"

              let! (body: string) = response.Content.ReadAsStringAsync()
              let doc = JsonDocument.Parse(body: string)
              let root = doc.RootElement

              // Verify it's a valid OpenAPI document
              Expect.isTrue (hasProperty "openapi" root) "Should have openapi version field"
              Expect.isTrue (hasProperty "paths" root) "Should have paths field"

              // Verify the /products path exists
              let paths = root.GetProperty("paths")
              Expect.isTrue (hasProperty "/products" paths) "Should contain /products path"

              // Verify GET method exists under /products
              let productsPath = paths.GetProperty("/products")
              Expect.isTrue (hasProperty "get" productsPath) "Should have GET operation for /products"
          }

          testTask "OpenAPI document contains auto-derived operationId from resource name" {
              let products =
                  resource "/products" {
                      name "Products"
                      get simpleHandler
                  }

              let client = createOpenApiTestServer [ products ]
              let! result = getOpenApiDoc client
              let (response: HttpResponseMessage), (doc: JsonDocument) = result
              Expect.equal response.StatusCode HttpStatusCode.OK "Should return 200"

              let getOp =
                  doc.RootElement.GetProperty("paths").GetProperty("/products").GetProperty("get")

              // R8: operationId should be auto-derived from DisplayName "GET Products" -> "getProducts"
              Expect.isTrue (hasProperty "operationId" getOp) "Should have operationId"

              Expect.equal
                  (getOp.GetProperty("operationId").GetString())
                  "getProducts"
                  "operationId should be auto-derived from resource name"
          }

          testTask "Multi-segment inferred name produces correct operationId" {
              let adminUsers = resource "/admin/users" { get simpleHandler }
              let client = createOpenApiTestServer [ adminUsers ]
              let! result = getOpenApiDoc client
              let (response: HttpResponseMessage), (doc: JsonDocument) = result
              Expect.equal response.StatusCode HttpStatusCode.OK "Should return 200"

              let getOp =
                  doc.RootElement.GetProperty("paths").GetProperty("/admin/users").GetProperty("get")

              Expect.isTrue (hasProperty "operationId" getOp) "Should have operationId"

              Expect.equal
                  (getOp.GetProperty("operationId").GetString())
                  "getAdminUsers"
                  "Multi-word inferred name should collapse spaces for operationId"
          }

          testTask "OpenAPI document includes multiple endpoints from multiple resources" {
              let products =
                  resource "/products" {
                      name "Products"
                      get simpleHandler
                      post simpleHandler
                  }

              let users =
                  resource "/users" {
                      name "Users"
                      get simpleHandler
                  }

              let client = createOpenApiTestServer [ products; users ]
              let! result = getOpenApiDoc client
              let (_, (doc: JsonDocument)) = result
              let paths = doc.RootElement.GetProperty("paths")

              Expect.isTrue (hasProperty "/products" paths) "Should contain /products"
              Expect.isTrue (hasProperty "/users" paths) "Should contain /users"

              let productsPath = paths.GetProperty("/products")
              Expect.isTrue (hasProperty "get" productsPath) "Should have GET /products"
              Expect.isTrue (hasProperty "post" productsPath) "Should have POST /products"

              let usersPath = paths.GetProperty("/users")
              Expect.isTrue (hasProperty "get" usersPath) "Should have GET /users"
          }

          testTask "App without OpenAPI does not expose /openapi/v1.json" {
              let products =
                  resource "/products" {
                      name "Products"
                      get simpleHandler
                  }

              let client = createPlainTestServer [ products ]
              let! (response: HttpResponseMessage) = client.GetAsync("/openapi/v1.json")

              Expect.equal
                  response.StatusCode
                  HttpStatusCode.NotFound
                  "Should return 404 when OpenAPI is not configured"
          } ]

// ===== US3: End-to-End Tests with HandlerDefinitions =====

type Product = { Name: string; Price: decimal }
type CreateProductRequest = { Name: string; Price: decimal }

[<Tests>]
let us3EndToEndTests =
    testList
        "US3 - HandlerDefinitions End-to-End"
        [ testTask "HandlerDefinition with name and tags appears in OpenAPI document" {
              let createProductHandler =
                  handler {
                      name "createProduct"
                      summary "Create a new product"
                      description "Creates a new product in the catalog"
                      tags [ "Products"; "Admin" ]
                      produces typeof<Product> 201
                      accepts typeof<CreateProductRequest>
                      handle (fun (ctx: HttpContext) -> Task.CompletedTask)
                  }

              let products =
                  resource "/products" {
                      name "Products"
                      post createProductHandler
                  }

              let client = createOpenApiTestServer [ products ]
              let! result = getOpenApiDoc client
              let (response: HttpResponseMessage), (doc: JsonDocument) = result
              Expect.equal response.StatusCode HttpStatusCode.OK "Should return 200"

              let postOp =
                  doc.RootElement.GetProperty("paths").GetProperty("/products").GetProperty("post")

              // Check operationId from HandlerDefinition name
              Expect.equal
                  (postOp.GetProperty("operationId").GetString())
                  "createProduct"
                  "operationId should match handler name"

              // Check summary and description
              Expect.equal (postOp.GetProperty("summary").GetString()) "Create a new product" "Summary should match"

              Expect.equal
                  (postOp.GetProperty("description").GetString())
                  "Creates a new product in the catalog"
                  "Description should match"

              // Check tags
              let tags = postOp.GetProperty("tags")
              Expect.equal (tags.GetArrayLength()) 2 "Should have 2 tags"
              let tagsList = [ for i in 0 .. tags.GetArrayLength() - 1 -> tags[i].GetString() ]
              Expect.containsAll tagsList [ "Products"; "Admin" ] "Tags should match"
          }

          testTask "HandlerDefinition produces metadata appears as response in OpenAPI" {
              let getProductHandler =
                  handler {
                      name "getProduct"
                      produces typeof<Product> 200
                      produces typeof<Product> 201
                      producesEmpty 404
                      handle (fun (ctx: HttpContext) -> Task.CompletedTask)
                  }

              let products = resource "/products/{id}" { get getProductHandler }

              let client = createOpenApiTestServer [ products ]
              let! result = getOpenApiDoc client
              let (response: HttpResponseMessage), (doc: JsonDocument) = result
              Expect.equal response.StatusCode HttpStatusCode.OK "Should return 200"

              let getOp =
                  doc.RootElement.GetProperty("paths").GetProperty("/products/{id}").GetProperty("get")

              let responses = getOp.GetProperty("responses")

              // Check 200 response with Product schema
              Expect.isTrue (hasProperty "200" responses) "Should have 200 response"
              let resp200 = responses.GetProperty("200")
              Expect.isTrue (hasProperty "content" resp200) "200 should have content"

              // Check 201 response
              Expect.isTrue (hasProperty "201" responses) "Should have 201 response"

              // Check 404 empty response
              Expect.isTrue (hasProperty "404" responses) "Should have 404 response"
              let resp404 = responses.GetProperty("404")
              // Empty responses typically don't have content or have empty content
              let has404Content = hasProperty "content" resp404

              if has404Content then
                  let content404 = resp404.GetProperty("content")
                  Expect.equal (content404.EnumerateObject() |> Seq.length) 0 "404 should have no content types"
          }

          testTask "HandlerDefinition accepts metadata appears as requestBody in OpenAPI" {
              let createHandler =
                  handler {
                      name "createProduct"
                      accepts typeof<CreateProductRequest>
                      produces typeof<Product> 201
                      handle (fun (ctx: HttpContext) -> Task.CompletedTask)
                  }

              let products = resource "/products" { post createHandler }

              let client = createOpenApiTestServer [ products ]
              let! result = getOpenApiDoc client
              let (response: HttpResponseMessage), (doc: JsonDocument) = result
              Expect.equal response.StatusCode HttpStatusCode.OK "Should return 200"

              let postOp =
                  doc.RootElement.GetProperty("paths").GetProperty("/products").GetProperty("post")

              // Check requestBody
              Expect.isTrue (hasProperty "requestBody" postOp) "Should have requestBody"
              let requestBody = postOp.GetProperty("requestBody")
              Expect.isTrue (hasProperty "content" requestBody) "RequestBody should have content"
              let content = requestBody.GetProperty("content")
              Expect.isTrue (hasProperty "application/json" content) "Should accept application/json"
          }

          testTask "Resource with mixed plain handlers and HandlerDefinitions" {
              let plainGetHandler: RequestDelegate =
                  RequestDelegate(fun ctx -> ctx.Response.WriteAsync("OK"))

              let createHandler =
                  handler {
                      name "createProduct"
                      summary "Create product"
                      produces typeof<Product> 201
                      handle (fun (ctx: HttpContext) -> Task.CompletedTask)
                  }

              let products =
                  resource "/products" {
                      name "Products"
                      get plainGetHandler
                      post createHandler
                  }

              let client = createOpenApiTestServer [ products ]
              let! result = getOpenApiDoc client
              let (response: HttpResponseMessage), (doc: JsonDocument) = result
              Expect.equal response.StatusCode HttpStatusCode.OK "Should return 200"

              let productsPath = doc.RootElement.GetProperty("paths").GetProperty("/products")

              // Plain handler should use auto-derived operationId
              let getOp = productsPath.GetProperty("get")

              Expect.equal
                  (getOp.GetProperty("operationId").GetString())
                  "getProducts"
                  "GET should have auto-derived operationId"

              Expect.isFalse (hasProperty "summary" getOp) "GET should not have summary"

              // HandlerDefinition should have its metadata
              let postOp = productsPath.GetProperty("post")

              Expect.equal
                  (postOp.GetProperty("operationId").GetString())
                  "createProduct"
                  "POST should have handler-defined operationId"

              Expect.equal (postOp.GetProperty("summary").GetString()) "Create product" "POST should have summary"
          }

          testTask "HandlerDefinition with custom content types for content negotiation" {
              let getHandler =
                  handler {
                      name "getProduct"
                      produces typeof<Product> 200 [ "application/xml"; "application/json" ]
                      handle (fun (ctx: HttpContext) -> Task.CompletedTask)
                  }

              let createHandler =
                  handler {
                      name "createProduct"
                      accepts typeof<CreateProductRequest> [ "application/xml" ]
                      produces typeof<Product> 201
                      handle (fun (ctx: HttpContext) -> Task.CompletedTask)
                  }

              let products =
                  resource "/products" {
                      get getHandler
                      post createHandler
                  }

              let client = createOpenApiTestServer [ products ]
              let! result = getOpenApiDoc client
              let (response: HttpResponseMessage), (doc: JsonDocument) = result
              Expect.equal response.StatusCode HttpStatusCode.OK "Should return 200"

              let productsPath = doc.RootElement.GetProperty("paths").GetProperty("/products")

              // Check GET response supports both XML and JSON
              let getOp = productsPath.GetProperty("get")
              let getResp200 = getOp.GetProperty("responses").GetProperty("200")
              let getContent = getResp200.GetProperty("content")
              Expect.isTrue (hasProperty "application/xml" getContent) "GET 200 should support XML"
              Expect.isTrue (hasProperty "application/json" getContent) "GET 200 should support JSON"

              // Check POST requestBody accepts XML
              let postOp = productsPath.GetProperty("post")
              let postRequestBody = postOp.GetProperty("requestBody")
              let postContent = postRequestBody.GetProperty("content")
              Expect.isTrue (hasProperty "application/xml" postContent) "POST should accept XML"
          } ]

// ===== Scalar UI Tests =====

[<Tests>]
let scalarTests =
    testList
        "Scalar UI"
        [ testTask "GET /scalar/v1 returns Scalar UI when OpenAPI is enabled" {
              let products =
                  resource "/products" {
                      name "Products"
                      get simpleHandler
                  }

              let client = createOpenApiTestServer [ products ]
              let! (response: HttpResponseMessage) = client.GetAsync("/scalar/v1")
              Expect.equal response.StatusCode HttpStatusCode.OK "Should return 200"

              let! (body: string) = response.Content.ReadAsStringAsync()
              Expect.stringContains body "scalar" "Should contain Scalar UI content"
          }

          testTask "App without OpenAPI does not expose /scalar/v1" {
              let products =
                  resource "/products" {
                      name "Products"
                      get simpleHandler
                  }

              let client = createPlainTestServer [ products ]
              let! (response: HttpResponseMessage) = client.GetAsync("/scalar/v1")

              Expect.equal
                  response.StatusCode
                  HttpStatusCode.NotFound
                  "Should return 404 when OpenAPI is not configured"
          } ]
