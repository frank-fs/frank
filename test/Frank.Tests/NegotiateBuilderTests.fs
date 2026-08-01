module Frank.Tests.NegotiateBuilderTests

open System.IO
open System.Threading.Tasks
open Microsoft.AspNetCore.Http
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
              Expect.equal (getResponseBody ctx) "" "No body should be written" ]
