module Frank.Tests.ContentNegotiationTests

open System.IO
open Microsoft.AspNetCore.Http
open Microsoft.Extensions.DependencyInjection
open Expecto
open Frank
open Frank.ContentNegotiation

let createMockContext (services: System.IServiceProvider) =
    let context = DefaultHttpContext()
    let responseStream = new MemoryStream()
    context.Response.Body <- responseStream
    context.RequestServices <- services
    context

let getResponseBody (ctx: HttpContext) =
    ctx.Response.Body.Position <- 0L
    use reader = new StreamReader(ctx.Response.Body)
    reader.ReadToEnd()

// CLIMutable: XmlSerializer (used by AddXmlSerializerFormatters) requires a public
// parameterless constructor and settable properties, which plain F# records don't have.
[<CLIMutable>]
type Product = { Name: string; Price: decimal }

let servicesWithJsonOnly () =
    let services = ServiceCollection()
    services.AddLogging() |> ignore
    // ReturnHttpNotAcceptable: without this, DefaultOutputFormatterSelector falls back to
    // picking any registered formatter when none match Accept, instead of signalling 406 --
    // needed for the "negotiate responds 406" case below to exercise real behavior.
    services.AddMvcCore(fun options -> options.ReturnHttpNotAcceptable <- true) |> ignore
    services.BuildServiceProvider()

let servicesWithJsonAndXml () =
    let services = ServiceCollection()
    services.AddLogging() |> ignore
    services.AddMvcCore().AddXmlSerializerFormatters() |> ignore
    services.BuildServiceProvider()

[<Tests>]
let tests =
    testList
        "ContentNegotiation"
        [ testCase "viaOutputFormatter writes JSON when a JSON formatter is registered"
          <| fun () ->
              let ctx = createMockContext (servicesWithJsonOnly ())
              let product = { Name = "Widget"; Price = 9.99m }

              ContentNegotiation.viaOutputFormatter "application/json" product ctx
              |> Async.AwaitTask
              |> Async.RunSynchronously

              // The formatter's own WriteAsync sets the final Content-Type (including
              // charset), overriding the plain media type we assign beforehand -- so we
              // assert a prefix match rather than exact equality.
              Expect.stringStarts ctx.Response.ContentType "application/json" "Content-Type should be set"
              Expect.stringContains (getResponseBody ctx) "Widget" "Body should contain the serialized product"

          testCase "viaOutputFormatter throws when no formatter supports the requested media type"
          <| fun () ->
              let ctx = createMockContext (servicesWithJsonOnly ())
              let product = { Name = "Widget"; Price = 9.99m }

              let callIt () =
                  ContentNegotiation.viaOutputFormatter "application/xml" product ctx
                  |> Async.AwaitTask
                  |> Async.RunSynchronously

              Expect.throws callIt "No XML formatter is registered -- this is a server misconfiguration, not a 406"

          testCase "viaOutputFormatter writes XML once AddXmlSerializerFormatters is registered"
          <| fun () ->
              let ctx = createMockContext (servicesWithJsonAndXml ())
              let product = { Name = "Widget"; Price = 9.99m }

              ContentNegotiation.viaOutputFormatter "application/xml" product ctx
              |> Async.AwaitTask
              |> Async.RunSynchronously

              Expect.stringStarts ctx.Response.ContentType "application/xml" "Content-Type should be set"
              Expect.stringContains (getResponseBody ctx) "Widget" "Body should contain the serialized product"

          testCase "negotiate (the existing IOutputFormatter mechanism) selects by Accept across formatters"
          <| fun () ->
              let ctx = createMockContext (servicesWithJsonAndXml ())
              ctx.Request.Headers.Accept <- Microsoft.Extensions.Primitives.StringValues("application/xml")
              let product = { Name = "Widget"; Price = 9.99m }

              ctx.Negotiate(200, product) |> Async.AwaitTask |> Async.RunSynchronously

              Expect.equal ctx.Response.StatusCode 200 "Status code should be as requested"
              Expect.stringContains (getResponseBody ctx) "Widget" "Body should contain the serialized product"

          testCase "negotiate responds 406 when Accept matches no registered formatter"
          <| fun () ->
              let ctx = createMockContext (servicesWithJsonOnly ())
              ctx.Request.Headers.Accept <- Microsoft.Extensions.Primitives.StringValues("application/xml")
              let product = { Name = "Widget"; Price = 9.99m }

              ctx.Negotiate(200, product) |> Async.AwaitTask |> Async.RunSynchronously

              Expect.equal ctx.Response.StatusCode 406 "No XML formatter registered -- should be Not Acceptable" ]
