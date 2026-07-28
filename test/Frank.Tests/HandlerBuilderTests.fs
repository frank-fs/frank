module Frank.Tests.HandlerBuilderTests

open System
open System.Threading.Tasks
open Microsoft.AspNetCore.Http
open Microsoft.AspNetCore.Http.Metadata
open Microsoft.AspNetCore.Routing
open Expecto
open Frank.Builder

// Sample types for testing
type Product = { Name: string; Price: decimal }
type CreateRequest = { Name: string }

/// Stands in for metadata an external library would attach.
type CustomMarker(label: string) =
    member _.Label = label

[<Tests>]
let tests =
    testList
        "HandlerBuilder"
        [ test "handler with handle operation produces HandlerDefinition with handler set" {
              let def = handler { handle (fun (ctx: HttpContext) -> Task.CompletedTask) }

              Expect.isNotNull def.Handler "Handler should be set"
              Expect.isEmpty def.Metadata "Metadata should be empty"
          }

          test "handler with metadata operations emits endpoint metadata" {
              let def =
                  handler {
                      name "createProduct"
                      summary "Creates a new product"
                      description "Detailed description of product creation"
                      tags [ "Products"; "Admin" ]
                      handle (fun (ctx: HttpContext) -> Task.CompletedTask)
                  }

              let nameMeta = HandlerDefinition.tryFind<IEndpointNameMetadata> def
              Expect.isSome nameMeta "Name metadata should be present"
              Expect.equal nameMeta.Value.EndpointName "createProduct" "Name should be set"

              let summaryMeta = HandlerDefinition.tryFind<IEndpointSummaryMetadata> def
              Expect.isSome summaryMeta "Summary metadata should be present"
              Expect.equal summaryMeta.Value.Summary "Creates a new product" "Summary should be set"

              let descMeta = HandlerDefinition.tryFind<IEndpointDescriptionMetadata> def
              Expect.isSome descMeta "Description metadata should be present"

              Expect.equal
                  descMeta.Value.Description
                  "Detailed description of product creation"
                  "Description should be set"

              let tagsMeta = HandlerDefinition.tryFind<ITagsMetadata> def
              Expect.isSome tagsMeta "Tags metadata should be present"
              Expect.sequenceEqual tagsMeta.Value.Tags [ "Products"; "Admin" ] "Tags should be set"
          }

          test "handler with produces operation emits response metadata" {
              let def =
                  handler {
                      produces typeof<Product> 200
                      produces typeof<Product> 201
                      handle (fun (ctx: HttpContext) -> Task.CompletedTask)
                  }

              let produces = HandlerDefinition.findAll<IProducesResponseTypeMetadata> def
              Expect.hasLength produces 2 "Should have 2 produces entries"

              Expect.equal produces.[0].StatusCode 200 "First status code should be 200"
              Expect.equal produces.[0].Type (typeof<Product>) "First response type should be Product"

              Expect.sequenceEqual
                  produces.[0].ContentTypes
                  [ "application/json" ]
                  "First content types should be default"

              Expect.equal produces.[1].StatusCode 201 "Second status code should be 201"
          }

          test "handler with producesEmpty operation emits Void response metadata" {
              let def =
                  handler {
                      producesEmpty 204
                      producesEmpty 404
                      handle (fun (ctx: HttpContext) -> Task.CompletedTask)
                  }

              let produces = HandlerDefinition.findAll<IProducesResponseTypeMetadata> def
              Expect.hasLength produces 2 "Should have 2 produces entries"

              Expect.equal produces.[0].StatusCode 204 "First status code should be 204"
              Expect.equal produces.[0].Type (typeof<Void>) "First response type should be Void"

              Expect.sequenceEqual
                  produces.[0].ContentTypes
                  [ "application/json" ]
                  "Empty responses still carry the JSON default content type"

              Expect.equal produces.[1].StatusCode 404 "Second status code should be 404"
          }

          test "handler with accepts operation emits request metadata" {
              let def =
                  handler {
                      accepts typeof<CreateRequest>
                      accepts typeof<Product>
                      handle (fun (ctx: HttpContext) -> Task.CompletedTask)
                  }

              let accepts = HandlerDefinition.findAll<IAcceptsMetadata> def
              Expect.hasLength accepts 2 "Should have 2 accepts entries"

              Expect.equal accepts.[0].RequestType (typeof<CreateRequest>) "First request type should be CreateRequest"

              Expect.sequenceEqual
                  accepts.[0].ContentTypes
                  [ "application/json" ]
                  "First content types should be default"

              Expect.isFalse accepts.[0].IsOptional "First should not be optional"
              Expect.equal accepts.[1].RequestType (typeof<Product>) "Second request type should be Product"
          }

          test "handler with all metadata combined accumulates correctly" {
              let handlerDef: HandlerDefinition =
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

              Expect.hasLength handlerDef.Metadata 7 "Should have 7 metadata entries"

              Expect.hasLength
                  (HandlerDefinition.findAll<IProducesResponseTypeMetadata> handlerDef)
                  2
                  "Should have 2 produces entries"

              Expect.hasLength
                  (HandlerDefinition.findAll<IAcceptsMetadata> handlerDef)
                  1
                  "Should have 1 accepts entry"

              Expect.isNotNull handlerDef.Handler "Handler should be set"
          }

          test "metadata is retained in declaration order" {
              let def =
                  handler {
                      name "first"
                      tags [ "second" ]
                      producesEmpty 204
                      handle (fun (ctx: HttpContext) -> Task.CompletedTask)
                  }

              let kinds =
                  def.Metadata
                  |> List.map (fun m ->
                      match m with
                      | :? IEndpointNameMetadata -> "name"
                      | :? ITagsMetadata -> "tags"
                      | :? IProducesResponseTypeMetadata -> "produces"
                      | _ -> "other")

              Expect.equal kinds [ "name"; "tags"; "produces" ] "Order should match declaration order"
          }

          test "external metadata can be attached and read back" {
              let def =
                  handler { handle (fun (ctx: HttpContext) -> Task.CompletedTask) }
                  |> HandlerDefinition.addMetadata (CustomMarker "discovery")

              let marker = HandlerDefinition.tryFind<CustomMarker> def
              Expect.isSome marker "Custom metadata should be readable"
              Expect.equal marker.Value.Label "discovery" "Custom metadata should round-trip"
          }

          test "handler without handle operation fails validation" {
              let buildInvalidHandler () = handler { name "incomplete" } |> ignore

              Expect.throws buildInvalidHandler "Should throw when handler is not set"
          }

          test "handler with async<unit> handler converts to Task correctly" {
              let def = handler { handle (fun (ctx: HttpContext) -> async { do () }) }

              Expect.isNotNull def.Handler "Handler should be set"
          }

          test "handler with async<'a> handler converts to Task<'a> correctly" {
              let def = handler { handle (fun (ctx: HttpContext) -> async { return "result" }) }

              Expect.isNotNull def.Handler "Handler should be set"
          }

          test "handler with Task<'a> handler is accepted" {
              let def = handler { handle (fun (ctx: HttpContext) -> Task.FromResult("result")) }

              Expect.isNotNull def.Handler "Handler should be set"
          }

          test "handler with custom content types for content negotiation" {
              let def =
                  handler {
                      produces typeof<Product> 200 [ "application/xml"; "application/json" ]
                      accepts typeof<CreateRequest> [ "application/xml" ]
                      handle (fun (ctx: HttpContext) -> Task.CompletedTask)
                  }

              let produces = HandlerDefinition.findAll<IProducesResponseTypeMetadata> def
              Expect.hasLength produces 1 "Should have 1 produces entry"

              Expect.containsAll
                  produces.[0].ContentTypes
                  [ "application/xml"; "application/json" ]
                  "Should support both XML and JSON"

              let accepts = HandlerDefinition.findAll<IAcceptsMetadata> def
              Expect.hasLength accepts 1 "Should have 1 accepts entry"
              Expect.contains accepts.[0].ContentTypes "application/xml" "Should accept XML"
          }

          test "handler with empty tags does not emit tags metadata" {
              let def =
                  handler {
                      tags []
                      handle (fun (ctx: HttpContext) -> Task.CompletedTask)
                  }

              let tagsMeta = HandlerDefinition.tryFind<ITagsMetadata> def
              Expect.isNone tagsMeta "Empty tags should not add ITagsMetadata"
              Expect.isEmpty def.Metadata "Metadata should remain empty when tags is []"
          } ]
