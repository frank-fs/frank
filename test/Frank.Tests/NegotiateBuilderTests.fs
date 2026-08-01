module Frank.Tests.NegotiateBuilderTests

open System.IO
open System.Threading.Tasks
open Microsoft.AspNetCore.Http
open Microsoft.Extensions.DependencyInjection
open Expecto
open Frank.Builder

let createMockContext () =
    let context = DefaultHttpContext()
    let responseStream = new MemoryStream()
    context.Response.Body <- responseStream
    context

let setAccept (ctx: HttpContext) (value: string) =
    ctx.Request.Headers.Accept <- Microsoft.Extensions.Primitives.StringValues(value)

let getResponseBody (ctx: HttpContext) =
    ctx.Response.Body.Position <- 0L
    use reader = new StreamReader(ctx.Response.Body)
    reader.ReadToEnd()

let writeText (text: string) (ctx: HttpContext) : Task =
    task { do! ctx.Response.WriteAsync(text) }

// CLIMutable: XmlSerializer (used by AddXmlSerializerFormatters) requires a public
// parameterless constructor and settable properties, which an F# anonymous record
// (used elsewhere in this file for JSON-only cases) doesn't have.
[<CLIMutable>]
type Widget = { Name: string }

type Product = { Name: string; Price: decimal }

[<Tests>]
let tests =
    testList
        "NegotiateBuilder"
        [ testCase "selects the representation matching an exact Accept header"
          <| fun () ->
              let ctx = createMockContext ()
              setAccept ctx "application/json"

              let def =
                  negotiate {
                      accepts "application/json" (writeText "json")
                      accepts "text/html" (writeText "html")
                  }

              def.Handler.Invoke(ctx).Wait()

              Expect.equal ctx.Response.ContentType "application/json" "Content-Type should match the winning representation"
              Expect.equal (getResponseBody ctx) "json" "Body should come from the JSON representation"

          testCase "quality values pick the higher-preference representation"
          <| fun () ->
              let ctx = createMockContext ()
              setAccept ctx "text/html;q=0.3, application/json;q=0.8"

              let def =
                  negotiate {
                      accepts "text/html" (writeText "html")
                      accepts "application/json" (writeText "json")
                  }

              def.Handler.Invoke(ctx).Wait()

              Expect.equal (getResponseBody ctx) "json" "Higher quality value should win regardless of registration order"

          testCase "responds 406 with no body when nothing matches"
          <| fun () ->
              let ctx = createMockContext ()
              setAccept ctx "application/xml"

              let def =
                  negotiate {
                      accepts "application/json" (writeText "json")
                      accepts "text/html" (writeText "html")
                  }

              def.Handler.Invoke(ctx).Wait()

              Expect.equal ctx.Response.StatusCode 406 "Should be Not Acceptable"
              Expect.equal (getResponseBody ctx) "" "No body should be written"

          testCase "absent Accept header selects the first-registered representation"
          <| fun () ->
              let ctx = createMockContext ()

              let def =
                  negotiate {
                      accepts "application/json" (writeText "json")
                      accepts "text/html" (writeText "html")
                  }

              def.Handler.Invoke(ctx).Wait()

              Expect.equal (getResponseBody ctx) "json" "First-registered representation is the default"

          testCase "Accept: */* selects the first-registered representation"
          <| fun () ->
              let ctx = createMockContext ()
              setAccept ctx "*/*"

              let def =
                  negotiate {
                      accepts "application/json" (writeText "json")
                      accepts "text/html" (writeText "html")
                  }

              def.Handler.Invoke(ctx).Wait()

              Expect.equal (getResponseBody ctx) "json" "Wildcard Accept resolves the same way as absent"

          testCase "a malformed Accept header falls back to the first-registered representation"
          <| fun () ->
              let ctx = createMockContext ()
              setAccept ctx "not a media type at all;;;"

              let def =
                  negotiate {
                      accepts "application/json" (writeText "json")
                      accepts "text/html" (writeText "html")
                  }

              def.Handler.Invoke(ctx).Wait()

              Expect.equal ctx.Response.StatusCode 200 "Should not be a 500"
              Expect.equal (getResponseBody ctx) "json" "Falls back to the default representation"

          testCase "only the selected representation's producer runs"
          <| fun () ->
              let ctx = createMockContext ()
              setAccept ctx "application/json"
              let mutable htmlRan = false
              let mutable jsonRan = false

              let def =
                  negotiate {
                      accepts "application/json" (fun (ctx: HttpContext) -> jsonRan <- true; writeText "json" ctx)
                      accepts "text/html" (fun (ctx: HttpContext) -> htmlRan <- true; writeText "html" ctx)
                  }

              def.Handler.Invoke(ctx).Wait()

              Expect.isTrue jsonRan "Selected representation's producer should run"
              Expect.isFalse htmlRan "Non-selected representation's producer should never run"

          testCase "a wildcard representation catches an Accept that matches nothing more specific"
          <| fun () ->
              let ctx = createMockContext ()
              setAccept ctx "image/png"

              let def =
                  negotiate {
                      accepts "application/json" (writeText "json")
                      accepts "*/*" (fun (ctx: HttpContext) ->
                          task {
                              ctx.Response.ContentType <- "image/png"
                              do! ctx.Response.WriteAsync("image-bytes")
                          }
                          : Task)
                  }

              def.Handler.Invoke(ctx).Wait()

              Expect.equal ctx.Response.ContentType "image/png" "Wildcard representation must set its own Content-Type"
              Expect.equal (getResponseBody ctx) "image-bytes" "Wildcard representation's own producer ran"

          testCase "a wildcard representation registered first shadows a later, more specific one"
          <| fun () ->
              let ctx = createMockContext ()
              setAccept ctx "application/json"

              let def =
                  negotiate {
                      accepts "*/*" (writeText "wildcard")
                      accepts "application/json" (writeText "json")
                  }

              def.Handler.Invoke(ctx).Wait()

              Expect.equal (getResponseBody ctx) "wildcard" "A wildcard registered first always wins -- documented footgun"

          testCase "a representation registered via handler{} contributes its metadata"
          <| fun () ->
              let def =
                  negotiate {
                      accepts "application/json" (handler {
                          producesEmpty 200
                          handle (writeText "json")
                      })
                      accepts "text/html" (writeText "html")
                  }

              Expect.hasLength def.Metadata 1 "Only the handler{}-based representation contributes metadata"

          testCase "negotiate {} with no accepts calls throws"
          <| fun () ->
              let buildEmpty () = negotiate { accepts "unused" (writeText "unused") } |> ignore |> ignore
              // (kept non-empty above to prove the builder compiles; the real empty-block case:)
              let buildTrulyEmpty () =
                  (NegotiateBuilder()).Run(NegotiateSpec.Empty) |> ignore

              Expect.throws buildTrulyEmpty "Should throw when no representations are registered"

          testCase "a representation registered via a bare HttpContext -> unit function is selected and runs its side effect"
          <| fun () ->
              let ctx = createMockContext ()
              setAccept ctx "application/json"

              let def =
                  negotiate {
                      accepts "application/json" (fun (ctx: HttpContext) ->
                          ctx.Response.Headers.["X-Sync"] <- Microsoft.Extensions.Primitives.StringValues("1")
                          ctx.Response.StatusCode <- 200)
                      accepts "text/html" (writeText "html")
                  }

              def.Handler.Invoke(ctx).Wait()

              Expect.equal ctx.Response.StatusCode 200 "The HttpContext -> unit representation should have been selected"
              Expect.equal (ctx.Response.Headers.["X-Sync"].ToString()) "1" "Its side effect should have run"

          testCase "Accept with q=0 on the only registered media type is rejected, not merely deprioritized"
          <| fun () ->
              let ctx = createMockContext ()
              setAccept ctx "application/json;q=0"

              let def =
                  negotiate { accepts "application/json" (writeText "json") }

              def.Handler.Invoke(ctx).Wait()

              Expect.equal ctx.Response.StatusCode 406 "q=0 must exclude the representation entirely, per RFC 9110 12.5.1"
              Expect.equal (getResponseBody ctx) "" "No body should be written"

          testCase "Accept: */*;q=0.5, text/html;q=0 rejects text/html even though */* is present"
          <| fun () ->
              let ctx = createMockContext ()
              setAccept ctx "*/*;q=0.5, text/html;q=0"

              let def =
                  negotiate { accepts "text/html" (writeText "html") }

              def.Handler.Invoke(ctx).Wait()

              Expect.equal ctx.Response.StatusCode 406 "The */*;q=0.5 entry doesn't name text/html, and the text/html;q=0 entry explicitly excludes it"
              Expect.equal (getResponseBody ctx) "" "No body should be written"

          testCase "Accept: */*;q=0, text/html;q=0.8 selects text/html -- the more specific positive entry overrides the broader rejection"
          <| fun () ->
              let ctx = createMockContext ()
              setAccept ctx "*/*;q=0, text/html;q=0.8"

              let def =
                  negotiate { accepts "text/html" (writeText "html") }

              def.Handler.Invoke(ctx).Wait()

              Expect.equal ctx.Response.StatusCode 200 "The more specific text/html;q=0.8 entry governs, not the broader */*;q=0"
              Expect.equal (getResponseBody ctx) "html" "text/html should have been selected and served"

          testCase "a Task<'a>-returning accepts handler has its value auto-formatted, not discarded"
          <| fun () ->
              let ctx = createMockContext ()
              setAccept ctx "application/json"
              let services = Microsoft.Extensions.DependencyInjection.ServiceCollection()
              services.AddLogging() |> ignore
              services.AddMvcCore() |> ignore
              ctx.RequestServices <- services.BuildServiceProvider()

              let def =
                  negotiate {
                      accepts "application/json" (fun (_: HttpContext) -> task { return {| Name = "Widget" |} })
                      accepts "text/html" (writeText "html")
                  }

              def.Handler.Invoke(ctx).Wait()

              // The JSON formatter's own WriteAsync appends a charset to the Content-Type it
              // sets, overriding the plain media type assigned beforehand (mirrors
              // ContentNegotiationTests.fs's viaOutputFormatter assertions) -- hence a prefix
              // match rather than exact equality.
              Expect.stringStarts ctx.Response.ContentType "application/json" "Value should be written via viaOutputFormatter"
              Expect.stringContains (getResponseBody ctx) "Widget" "Serialized value should appear in the body"

          testCase "an Async<'a>-returning accepts handler has its value auto-formatted"
          <| fun () ->
              let ctx = createMockContext ()
              setAccept ctx "application/json"
              let services = Microsoft.Extensions.DependencyInjection.ServiceCollection()
              services.AddLogging() |> ignore
              services.AddMvcCore() |> ignore
              ctx.RequestServices <- services.BuildServiceProvider()

              let def =
                  negotiate {
                      accepts "application/json" (fun (_: HttpContext) -> async { return {| Name = "Widget" |} })
                  }

              def.Handler.Invoke(ctx).Wait()

              Expect.stringContains (getResponseBody ctx) "Widget" "Serialized value should appear in the body"

          testCase "a value-returning accepts entry composes with an independent-producer entry"
          <| fun () ->
              let ctx = createMockContext ()
              setAccept ctx "application/ld+json"
              let services = Microsoft.Extensions.DependencyInjection.ServiceCollection()
              services.AddLogging() |> ignore
              services.AddMvcCore() |> ignore
              ctx.RequestServices <- services.BuildServiceProvider()
              let mutable jsonRan = false

              let def =
                  negotiate {
                      accepts "application/json" (fun (_: HttpContext) -> jsonRan <- true; task { return {| Name = "Widget" |} })
                      accepts "application/ld+json" (writeText "jsonld")
                  }

              def.Handler.Invoke(ctx).Wait()

              Expect.isFalse jsonRan "The value-returning representation should not run when a different one is selected"
              Expect.equal (getResponseBody ctx) "jsonld" "The independent producer should have run instead"

          testCase "Accept: application/ld+json never matches a concrete application/json registration, regardless of registration order"
          <| fun () ->
              // Regression test for a real defect in Negotiation.matches: the reverse-direction
              // MatchesMediaType clause (needed so a wildcard-*registered* representation like
              // `accepts "*/*"` can match a concrete Accept entry) was previously applied
              // unconditionally, which let a concrete registered "application/json" be treated
              // as if it were itself a wildcard pattern -- via the BCL's own leniency, an Accept
              // of "application/ld+json" would tie-match a registered "application/json", and
              // whichever was registered first won. That's exactly backwards: a client asking
              // for JSON-LD should never silently receive plain JSON. This test registers
              // "application/ld+json" FIRST -- the opposite order from the test above -- to
              // prove the fix is order-independent, not merely "reverted to the lucky order".
              let ctx = createMockContext ()
              setAccept ctx "application/ld+json"
              let services = Microsoft.Extensions.DependencyInjection.ServiceCollection()
              services.AddLogging() |> ignore
              services.AddMvcCore() |> ignore
              ctx.RequestServices <- services.BuildServiceProvider()
              let mutable jsonRan = false

              let def =
                  negotiate {
                      accepts "application/ld+json" (writeText "jsonld")
                      accepts "application/json" (fun (_: HttpContext) -> jsonRan <- true; task { return {| Name = "Widget" |} })
                  }

              def.Handler.Invoke(ctx).Wait()

              Expect.isFalse jsonRan "application/json must not be selected for an Accept: application/ld+json request"
              Expect.equal (getResponseBody ctx) "jsonld" "application/ld+json should have been selected, matching registration order this time too"

          testCase "accepts [mediaTypes] handler registers one representation per media type"
          <| fun () ->
              let services = Microsoft.Extensions.DependencyInjection.ServiceCollection()
              services.AddLogging() |> ignore
              services.AddMvcCore().AddXmlSerializerFormatters() |> ignore
              let provider = services.BuildServiceProvider()

              let widgetHandler =
                  fun (_: HttpContext) -> task { return { Name = "Widget" } }

              // `negotiate { }` always runs Run at the end, producing a HandlerDefinition
              // (Handler + Metadata only) -- Representations lives on the intermediate
              // NegotiateSpec, which only exists before Run. Call the builder's own Accepts
              // member directly to inspect that intermediate value.
              let spec =
                  NegotiateBuilder().Accepts(NegotiateSpec.Empty, [ "application/json"; "application/xml" ], widgetHandler)

              Expect.hasLength spec.Representations 2 "Should expand to two representations"

              let def =
                  negotiate {
                      accepts [ "application/json"; "application/xml" ] widgetHandler
                  }

              let jsonCtx = createMockContext ()
              jsonCtx.RequestServices <- provider
              setAccept jsonCtx "application/json"
              def.Handler.Invoke(jsonCtx).Wait()
              // The output formatter's own WriteAsync appends a charset to the Content-Type
              // it sets, overriding the plain media type assigned beforehand (mirrors the
              // "Task<'a>-returning accepts handler" test above) -- hence a prefix match.
              Expect.stringStarts jsonCtx.Response.ContentType "application/json" "JSON entry should format as JSON"

              let xmlCtx = createMockContext ()
              xmlCtx.RequestServices <- provider
              setAccept xmlCtx "application/xml"
              def.Handler.Invoke(xmlCtx).Wait()
              Expect.stringStarts xmlCtx.Response.ContentType "application/xml" "XML entry should format as XML, not the whole list"

          testCase "produces metadata from a single representation is unaffected by the merge"
          <| fun () ->
              let def =
                  negotiate {
                      accepts "application/json" (handler {
                          produces typeof<Product> 200
                          handle (writeText "json")
                      })
                      accepts "text/html" (writeText "html")
                  }

              let produces = HandlerDefinition.findAll<Microsoft.AspNetCore.Http.Metadata.IProducesResponseTypeMetadata> def
              Expect.hasLength produces 1 "One representation's metadata should pass through unmerged"
              Expect.sequenceEqual produces.[0].ContentTypes [ "application/json" ] "Content types unchanged for a single representation"

          testCase "produces metadata from two representations sharing status code and type is merged into one"
          <| fun () ->
              let def =
                  negotiate {
                      accepts "text/html" (handler {
                          produces typeof<Product> 200 [ "text/html" ]
                          handle (writeText "html")
                      })
                      accepts "application/json" (handler {
                          produces typeof<Product> 200 [ "application/json" ]
                          handle (writeText "json")
                      })
                  }

              let produces = HandlerDefinition.findAll<Microsoft.AspNetCore.Http.Metadata.IProducesResponseTypeMetadata> def
              Expect.hasLength produces 1 "Same status code + same type should merge into one metadata object"
              Expect.containsAll produces.[0].ContentTypes [ "text/html"; "application/json" ] "Merged entry should carry both content types"

          testCase "produces metadata from two representations sharing status code but different types is NOT merged"
          <| fun () ->
              let def =
                  negotiate {
                      accepts "application/json" (handler {
                          produces typeof<Product> 200
                          handle (writeText "json")
                      })
                      accepts "application/xml" (handler {
                          produces typeof<string> 200 [ "application/xml" ]
                          handle (writeText "xml")
                      })
                  }

              let produces = HandlerDefinition.findAll<Microsoft.AspNetCore.Http.Metadata.IProducesResponseTypeMetadata> def
              Expect.hasLength produces 2 "Different response types sharing a status code must stay separate -- documented remaining limitation" ]
