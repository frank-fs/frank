module Frank.OpenApi.Tests.HandlerBuilderTests

open System
open System.Threading.Tasks
open Microsoft.AspNetCore.Http
open Expecto
open Frank.OpenApi

// Sample types for testing
type Product = { Name: string; Price: decimal }
type CreateRequest = { Name: string }

[<Tests>]
let tests =
    testList "HandlerBuilder" [
        test "handler with handle operation produces HandlerDefinition with handler set" {
            let def =
                handler {
                    handle (fun (ctx: HttpContext) -> Task.CompletedTask)
                }

            Expect.isNotNull def.Handler "Handler should be set"
            Expect.isNone def.Name "Name should be None"
            Expect.isNone def.Summary "Summary should be None"
            Expect.isNone def.Description "Description should be None"
            Expect.isEmpty def.Tags "Tags should be empty"
            Expect.isEmpty def.Produces "Produces should be empty"
            Expect.isEmpty def.Accepts "Accepts should be empty"
        }

        test "handler with metadata operations populates fields correctly" {
            let def =
                handler {
                    name "createProduct"
                    summary "Creates a new product"
                    description "Detailed description of product creation"
                    tags [ "Products"; "Admin" ]
                    handle (fun (ctx: HttpContext) -> Task.CompletedTask)
                }

            Expect.equal def.Name (Some "createProduct") "Name should be set"
            Expect.equal def.Summary (Some "Creates a new product") "Summary should be set"
            Expect.equal def.Description (Some "Detailed description of product creation") "Description should be set"
            Expect.equal def.Tags [ "Products"; "Admin" ] "Tags should be set"
        }

        test "handler with produces operation populates Produces list" {
            let def =
                handler {
                    produces typeof<Product> 200
                    produces typeof<Product> 201
                    handle (fun (ctx: HttpContext) -> Task.CompletedTask)
                }

            Expect.hasLength def.Produces 2 "Should have 2 produces entries"

            let first = def.Produces.[0]
            Expect.equal first.StatusCode 200 "First status code should be 200"
            Expect.equal first.ResponseType (Some typeof<Product>) "First response type should be Product"
            Expect.equal first.ContentTypes [ "application/json" ] "First content types should be default"

            let second = def.Produces.[1]
            Expect.equal second.StatusCode 201 "Second status code should be 201"
        }

        test "handler with producesEmpty operation for no-content responses" {
            let def =
                handler {
                    producesEmpty 204
                    producesEmpty 404
                    handle (fun (ctx: HttpContext) -> Task.CompletedTask)
                }

            Expect.hasLength def.Produces 2 "Should have 2 produces entries"

            let first = def.Produces.[0]
            Expect.equal first.StatusCode 204 "First status code should be 204"
            Expect.isNone first.ResponseType "First response type should be None"
            Expect.isEmpty first.ContentTypes "First content types should be empty"
            Expect.isNone first.Description "First description should be None"

            let second = def.Produces.[1]
            Expect.equal second.StatusCode 404 "Second status code should be 404"
            Expect.isNone second.ResponseType "Second response type should be None"
        }

        test "handler with accepts operation populates Accepts list" {
            let def =
                handler {
                    accepts typeof<CreateRequest>
                    accepts typeof<Product>
                    handle (fun (ctx: HttpContext) -> Task.CompletedTask)
                }

            Expect.hasLength def.Accepts 2 "Should have 2 accepts entries"

            let first = def.Accepts.[0]
            Expect.equal first.RequestType typeof<CreateRequest> "First request type should be CreateRequest"
            Expect.equal first.ContentTypes [ "application/json" ] "First content types should be default"
            Expect.isFalse first.IsOptional "First should not be optional"

            let second = def.Accepts.[1]
            Expect.equal second.RequestType typeof<Product> "Second request type should be Product"
            Expect.equal second.ContentTypes [ "application/json" ] "Second content types should be default"
        }

        test "handler with all metadata combined accumulates correctly" {
            let handlerDef : HandlerDefinition =
                handler {
                    name "createProduct"
                    summary "Create product"
                    description "Creates a new product in the catalog"
                    tags [ "Products" ]
                    produces typeof<Product> 201
                    producesEmpty 400
                    accepts typeof<CreateRequest>
                    handle (fun (ctx: HttpContext) -> Task.CompletedTask)
                }

            Expect.equal handlerDef.Name (Some "createProduct") "Name should be set"
            Expect.equal handlerDef.Summary (Some "Create product") "Summary should be set"
            Expect.equal handlerDef.Description (Some "Creates a new product in the catalog") "Description should be set"
            Expect.equal handlerDef.Tags [ "Products" ] "Tags should be set"
            Expect.hasLength handlerDef.Produces 2 "Should have 2 produces entries"
            Expect.hasLength handlerDef.Accepts 1 "Should have 1 accepts entry"
            Expect.isNotNull handlerDef.Handler "Handler should be set"
        }

        test "handler without handle operation fails validation" {
            let buildInvalidHandler () =
                handler {
                    name "incomplete"
                }
                |> ignore

            Expect.throws buildInvalidHandler "Should throw when handler is not set"
        }

        test "handler with async<unit> handler converts to Task correctly" {
            let def =
                handler {
                    handle (fun (ctx: HttpContext) -> async { do () })
                }

            Expect.isNotNull def.Handler "Handler should be set"
        }

        test "handler with async<'a> handler converts to Task<'a> correctly" {
            let def =
                handler {
                    handle (fun (ctx: HttpContext) -> async { return "result" })
                }

            Expect.isNotNull def.Handler "Handler should be set"
        }

        test "handler with Task<'a> handler is accepted" {
            let def =
                handler {
                    handle (fun (ctx: HttpContext) -> Task.FromResult("result"))
                }

            Expect.isNotNull def.Handler "Handler should be set"
        }

        test "handler with custom content types for content negotiation" {
            let def =
                handler {
                    produces typeof<Product> 200 ["application/xml"; "application/json"]
                    accepts typeof<CreateRequest> ["application/xml"]
                    handle (fun (ctx: HttpContext) -> Task.CompletedTask)
                }

            Expect.hasLength def.Produces 1 "Should have 1 produces entry"
            let produces = def.Produces.[0]
            Expect.containsAll produces.ContentTypes ["application/xml"; "application/json"] "Should support both XML and JSON"

            Expect.hasLength def.Accepts 1 "Should have 1 accepts entry"
            let accepts = def.Accepts.[0]
            Expect.contains accepts.ContentTypes "application/xml" "Should accept XML"
        }
    ]
