module Frank.OpenApi.Tests.SchemaTests

open System
open System.Net
open System.Net.Http
open System.Text.Json
open System.Threading.Tasks
open Microsoft.AspNetCore.Builder
open Microsoft.AspNetCore.Http
open Microsoft.AspNetCore.Http.Metadata
open Microsoft.AspNetCore.Routing
open Expecto
open Frank.Builder

// ===== Sample F# Types for Schema Testing =====

type Product =
    { Name: string
      Price: decimal
      Description: string option
      InStock: bool }

type Shape =
    | Circle of radius: float
    | Rectangle of width: float * height: float
    | Point

type OrderWithCollections =
    { Items: string list
      Tags: Set<string>
      Attributes: Map<string, string> }

// ===== Test Infrastructure =====

let simpleHandler : RequestDelegate =
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
        return response, JsonDocument.Parse(body : string)
    }

/// Rebuild endpoints from a resource, adding extra metadata
let addMetadataToEndpoints (extraMeta: obj list) (r: Resource) : Resource =
    let endpoints =
        r.Endpoints
        |> Array.map (fun e ->
            let re = e :?> RouteEndpoint
            let builder = RouteEndpointBuilder(re.RequestDelegate, re.RoutePattern, re.Order)
            builder.DisplayName <- e.DisplayName
            for m in e.Metadata do
                builder.Metadata.Add(m)
            for m in extraMeta do
                builder.Metadata.Add(m)
            builder.Build())
    { Endpoints = endpoints }

/// Find a schema with properties, following $ref into components if needed
let findSchemaWithProps (doc: JsonDocument) (schema: JsonElement) (matchProps: string list) =
    if hasProperty "properties" schema then
        Some schema
    elif hasProperty "$ref" schema then
        if hasProperty "components" doc.RootElement then
            let components = doc.RootElement.GetProperty("components").GetProperty("schemas")
            let mutable result = None
            for prop in components.EnumerateObject() do
                let s = prop.Value
                if hasProperty "properties" s then
                    let p = s.GetProperty("properties")
                    if matchProps |> List.forall (fun name -> hasProperty name p) then
                        result <- Some s
            result
        else None
    else None

// ===== US2: Document Request/Response Types for F# Types =====

[<Tests>]
let us2Tests =
    testList "US2 - F# Type Schemas" [
        testTask "Record type produces correct schema with properties and required fields" {
            let testResource =
                resource "/products" { name "Products"; get simpleHandler }
                |> addMetadataToEndpoints [ ProducesResponseTypeMetadata(200, typeof<Product>, [| "application/json" |]) ]

            let client = OpenApiDocumentTests.createOpenApiTestServer [ testResource ]
            let! result = getOpenApiDoc client
            let (response: HttpResponseMessage), (doc: JsonDocument) = result
            Expect.equal response.StatusCode HttpStatusCode.OK "Should return 200"

            let getOp = doc.RootElement.GetProperty("paths").GetProperty("/products").GetProperty("get")
            let responses = getOp.GetProperty("responses")
            Expect.isTrue (hasProperty "200" responses) "Should have 200 response"

            let resp200 = responses.GetProperty("200")
            Expect.isTrue (hasProperty "content" resp200) "200 response should have content"
            let jsonContent = resp200.GetProperty("content").GetProperty("application/json")
            Expect.isTrue (hasProperty "schema" jsonContent) "Should have schema"

            let schema = jsonContent.GetProperty("schema")
            match findSchemaWithProps doc schema [ "name"; "price" ] with
            | Some s ->
                let props = s.GetProperty("properties")
                Expect.isTrue (hasProperty "name" props) "Should have name property"
                Expect.isTrue (hasProperty "price" props) "Should have price property"
                Expect.isTrue (hasProperty "description" props) "Should have description property"
                Expect.isTrue (hasProperty "inStock" props) "Should have inStock property"
            | None ->
                failtest "Schema should have properties (inline or via $ref)"
        }

        testTask "Option type fields are nullable in schema" {
            let testResource =
                resource "/items" { name "Items"; get simpleHandler }
                |> addMetadataToEndpoints [ ProducesResponseTypeMetadata(200, typeof<Product>, [| "application/json" |]) ]

            let client = OpenApiDocumentTests.createOpenApiTestServer [ testResource ]
            let! result = getOpenApiDoc client
            let (response: HttpResponseMessage), (doc: JsonDocument) = result
            Expect.equal response.StatusCode HttpStatusCode.OK "Should return 200"

            let getOp = doc.RootElement.GetProperty("paths").GetProperty("/items").GetProperty("get")
            let resp200 = getOp.GetProperty("responses").GetProperty("200")
            let jsonContent = resp200.GetProperty("content").GetProperty("application/json")
            let schema = jsonContent.GetProperty("schema")

            match findSchemaWithProps doc schema [ "name"; "description" ] with
            | Some s ->
                let props = s.GetProperty("properties")
                let descProp = props.GetProperty("description")
                // Option types should be nullable — check for "nullable: true" or anyOf/oneOf with null type
                // On .NET 10, nullable is represented in the type field using JsonSchemaType flags
                let isNullable =
                    (hasProperty "nullable" descProp && descProp.GetProperty("nullable").GetBoolean())
                    || (hasProperty "anyOf" descProp)
                    || (hasProperty "oneOf" descProp)
                    || (hasProperty "type" descProp && descProp.GetProperty("type").ToString().Contains("null"))
                Expect.isTrue isNullable "Option field 'description' should be nullable"

                // Required fields should NOT include the option field
                if hasProperty "required" s then
                    let required = s.GetProperty("required")
                    let requiredFields = [
                        for i in 0 .. required.GetArrayLength() - 1 ->
                            required[i].GetString()
                    ]
                    Expect.isFalse (requiredFields |> List.contains "description") "Option field should not be in required"
                    Expect.isTrue (requiredFields |> List.contains "name") "Non-option field 'name' should be required"
            | None ->
                failtest "Could not find schema with properties"
        }

        testTask "Collection types produce array schemas" {
            let testResource =
                resource "/orders" { name "Orders"; get simpleHandler }
                |> addMetadataToEndpoints [ ProducesResponseTypeMetadata(200, typeof<OrderWithCollections>, [| "application/json" |]) ]

            let client = OpenApiDocumentTests.createOpenApiTestServer [ testResource ]
            let! result = getOpenApiDoc client
            let (response: HttpResponseMessage), (doc: JsonDocument) = result
            Expect.equal response.StatusCode HttpStatusCode.OK "Should return 200"

            let getOp = doc.RootElement.GetProperty("paths").GetProperty("/orders").GetProperty("get")
            let resp200 = getOp.GetProperty("responses").GetProperty("200")
            let jsonContent = resp200.GetProperty("content").GetProperty("application/json")
            let schema = jsonContent.GetProperty("schema")

            match findSchemaWithProps doc schema [ "items"; "tags" ] with
            | Some s ->
                let props = s.GetProperty("properties")
                // list<string> should be an array
                let itemsProp = props.GetProperty("items")
                Expect.equal (itemsProp.GetProperty("type").GetString()) "array" "List should be array type"

                // Set<string> should also be an array
                let tagsProp = props.GetProperty("tags")
                Expect.equal (tagsProp.GetProperty("type").GetString()) "array" "Set should be array type"

                // Map<string,string> should be object with additionalProperties
                let attrProp = props.GetProperty("attributes")
                let isMapType =
                    (hasProperty "type" attrProp && attrProp.GetProperty("type").GetString() = "object")
                    || hasProperty "additionalProperties" attrProp
                Expect.isTrue isMapType "Map should be object with additionalProperties"
            | None ->
                failtest "Could not find schema with properties"
        }

        testTask "Discriminated union produces anyOf or oneOf schema" {
            let testResource =
                resource "/shapes" { name "Shapes"; get simpleHandler }
                |> addMetadataToEndpoints [ ProducesResponseTypeMetadata(200, typeof<Shape>, [| "application/json" |]) ]

            let client = OpenApiDocumentTests.createOpenApiTestServer [ testResource ]
            let! result = getOpenApiDoc client
            let (response: HttpResponseMessage), (doc: JsonDocument) = result
            Expect.equal response.StatusCode HttpStatusCode.OK "Should return 200"

            let getOp = doc.RootElement.GetProperty("paths").GetProperty("/shapes").GetProperty("get")
            let resp200 = getOp.GetProperty("responses").GetProperty("200")
            let jsonContent = resp200.GetProperty("content").GetProperty("application/json")
            let schema = jsonContent.GetProperty("schema")

            // DU should produce anyOf/oneOf either inline or via $ref
            let hasUnionSchema =
                hasProperty "anyOf" schema
                || hasProperty "oneOf" schema
                || hasProperty "$ref" schema
            Expect.isTrue hasUnionSchema "DU type should produce anyOf, oneOf, or $ref schema"

            // If it's a $ref, check components for the union schema
            if hasProperty "$ref" schema then
                if hasProperty "components" doc.RootElement then
                    let components = doc.RootElement.GetProperty("components").GetProperty("schemas")
                    let mutable foundUnion = false
                    for prop in components.EnumerateObject() do
                        let s = prop.Value
                        if hasProperty "anyOf" s || hasProperty "oneOf" s then
                            foundUnion <- true
                    Expect.isTrue foundUnion "Should have union schema in components with anyOf/oneOf"
        }
    ]
