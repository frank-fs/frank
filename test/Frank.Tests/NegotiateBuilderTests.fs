module Frank.Tests.NegotiateBuilderTests

open System.Net
open System.Net.Http
open System.Threading.Tasks
open Microsoft.AspNetCore.Builder
open Microsoft.AspNetCore.Hosting
open Microsoft.AspNetCore.Http
open Microsoft.AspNetCore.Routing
open Microsoft.AspNetCore.Routing.Matching
open Microsoft.AspNetCore.TestHost
open Microsoft.Extensions.DependencyInjection
open Microsoft.Extensions.FileProviders
open Microsoft.Extensions.Hosting
open Expecto
open Frank.Builder

type private TestEndpointDataSource(endpoints: Endpoint[]) =
    inherit EndpointDataSource()
    override _.Endpoints = endpoints :> _
    override _.GetChangeToken() = NullChangeToken.Singleton :> _

let private buildHost (resource: Resource) (configureServices: IServiceCollection -> unit) : IHost =
    Host
        .CreateDefaultBuilder([||])
        .ConfigureWebHost(fun webBuilder ->
            webBuilder
                .UseTestServer()
                .ConfigureServices(fun services ->
                    services.AddRouting() |> ignore
                    services.AddSingleton<MatcherPolicy, FrankProducesMatcherPolicy>() |> ignore
                    configureServices services)
                .Configure(fun app ->
                    app.UseRouting() |> ignore
                    app.UseEndpoints(fun endpoints -> endpoints.DataSources.Add(TestEndpointDataSource resource.Endpoints))
                    |> ignore)
            |> ignore)
        .Build()

let private noServices (_: IServiceCollection) = ()

/// Registers the MVC formatter pipeline (`viaOutputFormatter`'s dependency) without
/// XML support -- used by the value-returning `Task<'a>`/`Async<'a>` auto-format cases.
let private withMvc (services: IServiceCollection) =
    services.AddLogging() |> ignore
    services.AddMvcCore() |> ignore

/// Same as `withMvc`, plus the XML formatter -- needed by cases that exercise
/// `ctx.Negotiate`/`Frank.ContentNegotiation.negotiate` against a non-JSON Accept.
let private withMvcXml (services: IServiceCollection) =
    services.AddLogging() |> ignore
    services.AddMvcCore().AddXmlSerializerFormatters() |> ignore

let private getWithAccept (host: IHost) (accept: string option) : Task<HttpResponseMessage> =
    task {
        use client = host.GetTestClient()
        use request = new HttpRequestMessage(HttpMethod.Get, "/x")
        accept |> Option.iter (fun a -> request.Headers.Accept.ParseAdd(a))
        return! client.SendAsync(request)
    }

let writeText (text: string) (ctx: HttpContext) : Task =
    task { do! ctx.Response.WriteAsync(text) }

/// Runs `build` and returns the message of the exception it must raise, so a test can
/// assert on WHICH failure occurred rather than merely that something threw.
let messageOfThrow (build: unit -> unit) : string =
    try
        build ()
        failtest "Expected an exception, but none was raised"
    with
    | :? Expecto.AssertException -> reraise ()
    | ex -> ex.Message

// CLIMutable: XmlSerializer (used by AddXmlSerializerFormatters) requires a public
// parameterless constructor and settable properties, which an F# anonymous record
// (used elsewhere in this file for JSON-only cases) doesn't have.
[<CLIMutable>]
type Widget = { Name: string }

type Product = { Name: string; Price: decimal }

[<Tests>]
let tests =
    testList
        "NegotiateBuilder (routed through FrankProducesMatcherPolicy)"
        [ testCaseTask "selects the representation matching an exact Accept header"
          <| fun () -> task {
              let built =
                  (resource "/x") {
                      get (
                          negotiate {
                              accepts "application/json" (writeText "json")
                              accepts "text/html" (writeText "html")
                          }
                      )
                  }
              use host = buildHost built noServices
              do! host.StartAsync()
              let! response = getWithAccept host (Some "application/json")
              let! body = response.Content.ReadAsStringAsync()
              Expect.equal body "json" "Body should come from the JSON representation"
              Expect.equal response.Content.Headers.ContentType.MediaType "application/json" "Content-Type should match the winning representation"
          }

          testCaseTask "quality values pick the higher-preference representation"
          <| fun () -> task {
              let built =
                  (resource "/x") {
                      get (
                          negotiate {
                              accepts "text/html" (writeText "html")
                              accepts "application/json" (writeText "json")
                          }
                      )
                  }
              use host = buildHost built noServices
              do! host.StartAsync()
              let! response = getWithAccept host (Some "text/html;q=0.3, application/json;q=0.8")
              let! body = response.Content.ReadAsStringAsync()
              Expect.equal body "json" "Higher quality value should win regardless of registration order"
          }

          testCaseTask "responds 406 with no body when nothing matches"
          <| fun () -> task {
              let built =
                  (resource "/x") {
                      get (
                          negotiate {
                              accepts "application/json" (writeText "json")
                              accepts "text/html" (writeText "html")
                          }
                      )
                  }
              use host = buildHost built noServices
              do! host.StartAsync()
              let! response = getWithAccept host (Some "application/xml")
              Expect.equal response.StatusCode HttpStatusCode.NotAcceptable "Should be Not Acceptable"
              let! body = response.Content.ReadAsStringAsync()
              Expect.equal body "" "No body should be written"
          }

          testCaseTask "absent Accept header selects the first-registered representation"
          <| fun () -> task {
              let built =
                  (resource "/x") {
                      get (
                          negotiate {
                              accepts "application/json" (writeText "json")
                              accepts "text/html" (writeText "html")
                          }
                      )
                  }
              use host = buildHost built noServices
              do! host.StartAsync()
              let! response = getWithAccept host None
              let! body = response.Content.ReadAsStringAsync()
              Expect.equal body "json" "First-registered representation is the default"
          }

          testCaseTask "Accept: */* selects the first-registered representation"
          <| fun () -> task {
              let built =
                  (resource "/x") {
                      get (
                          negotiate {
                              accepts "application/json" (writeText "json")
                              accepts "text/html" (writeText "html")
                          }
                      )
                  }
              use host = buildHost built noServices
              do! host.StartAsync()
              let! response = getWithAccept host (Some "*/*")
              let! body = response.Content.ReadAsStringAsync()
              Expect.equal body "json" "Wildcard Accept resolves the same way as absent"
          }

          testCaseTask "a malformed Accept header falls back to the first-registered representation"
          <| fun () -> task {
              let built =
                  (resource "/x") {
                      get (
                          negotiate {
                              accepts "application/json" (writeText "json")
                              accepts "text/html" (writeText "html")
                          }
                      )
                  }
              use host = buildHost built noServices
              do! host.StartAsync()
              use client = host.GetTestClient()
              use request = new HttpRequestMessage(HttpMethod.Get, "/x")
              // TryAddWithoutValidation (not the shared getWithAccept, which uses
              // Accept.ParseAdd) -- ParseAdd validates client-side and would throw on
              // this deliberately unparseable value before the request is even sent,
              // defeating the point of testing the SERVER's fallback behavior.
              request.Headers.TryAddWithoutValidation("Accept", "not a media type at all;;;") |> ignore
              let! response = client.SendAsync(request)
              Expect.equal response.StatusCode HttpStatusCode.OK "Should not be a 500"
              let! body = response.Content.ReadAsStringAsync()
              Expect.equal body "json" "Falls back to the default representation"
          }

          testCaseTask "only the selected representation's producer runs"
          <| fun () -> task {
              let mutable htmlRan = false
              let mutable jsonRan = false

              let built =
                  (resource "/x") {
                      get (
                          negotiate {
                              accepts "application/json" (fun (ctx: HttpContext) -> jsonRan <- true; writeText "json" ctx)
                              accepts "text/html" (fun (ctx: HttpContext) -> htmlRan <- true; writeText "html" ctx)
                          }
                      )
                  }
              use host = buildHost built noServices
              do! host.StartAsync()
              let! _ = getWithAccept host (Some "application/json")

              Expect.isTrue jsonRan "Selected representation's producer should run"
              Expect.isFalse htmlRan "Non-selected representation's producer should never run"
          }

          testCaseTask "a wildcard representation catches an Accept that matches nothing more specific"
          <| fun () -> task {
              let built =
                  (resource "/x") {
                      get (
                          negotiate {
                              accepts "application/json" (writeText "json")
                              accepts "*/*" (fun (ctx: HttpContext) ->
                                  task {
                                      ctx.Response.ContentType <- "image/png"
                                      do! ctx.Response.WriteAsync("image-bytes")
                                  }
                                  : Task)
                          }
                      )
                  }
              use host = buildHost built noServices
              do! host.StartAsync()
              let! response = getWithAccept host (Some "image/png")
              let! body = response.Content.ReadAsStringAsync()
              Expect.equal response.Content.Headers.ContentType.MediaType "image/png" "Wildcard representation must set its own Content-Type"
              Expect.equal body "image-bytes" "Wildcard representation's own producer ran"
          }

          testCaseTask "a wildcard representation registered first shadows a later, more specific one"
          <| fun () -> task {
              let built =
                  (resource "/x") {
                      get (
                          negotiate {
                              accepts "*/*" (writeText "wildcard")
                              accepts "application/json" (writeText "json")
                          }
                      )
                  }
              use host = buildHost built noServices
              do! host.StartAsync()
              let! response = getWithAccept host (Some "application/json")
              let! body = response.Content.ReadAsStringAsync()
              Expect.equal body "wildcard" "A wildcard registered first always wins -- documented footgun"
          }

          testCase "a representation registered via handler{} contributes its metadata"
          <| fun () ->
              let defs =
                  negotiate {
                      accepts "application/json" (handler {
                          producesEmpty 200
                          handle (writeText "json")
                      })
                      accepts "text/html" (writeText "html")
                  }

              Expect.hasLength defs 2 "Two representations"

              for def in defs do
                  let produces = HandlerDefinition.findAll<Microsoft.AspNetCore.Http.Metadata.IProducesResponseTypeMetadata> def
                  Expect.hasLength produces 1 "Only the handler{}-based representation's metadata should exist, broadcast to every representation"

          testCase "non-produces metadata stays on its own representation and is NOT broadcast to siblings"
          <| fun () ->
              // Only `produces` metadata is broadcast across representations (the OpenAPI
              // mitigation). Anything else -- an ALPS `Descriptor` from `binds`, an
              // authorization marker -- belongs to the one representation that declared it;
              // broadcasting it duplicated it onto every sibling endpoint (verified against
              // Frank.Alps.Sample: the alps+json representation served `viewGame` twice).
              let marker = box "marker-only-on-rep-0"

              let jsonRepresentation =
                  handler {
                      produces typeof<Product> 200 [ "application/json" ]
                      handle (writeText "json")
                  }
                  |> HandlerDefinition.addMetadata marker

              let defs =
                  negotiate {
                      accepts "application/json" jsonRepresentation

                      accepts "text/html" (handler {
                          produces typeof<Product> 200 [ "text/html" ]
                          handle (writeText "html")
                      })
                  }

              Expect.hasLength defs 2 "Two representations"

              Expect.isTrue
                  (defs.[0].Metadata |> List.exists (fun m -> System.Object.ReferenceEquals(m, marker)))
                  "The declaring representation keeps its own non-produces metadata"

              Expect.isFalse
                  (defs.[1].Metadata |> List.exists (fun m -> System.Object.ReferenceEquals(m, marker)))
                  "A sibling representation must NOT inherit another representation's non-produces metadata"

              // ... while produces metadata IS still merged and broadcast to both (Task 4/6).
              for def in defs do
                  let produces = HandlerDefinition.findAll<Microsoft.AspNetCore.Http.Metadata.IProducesResponseTypeMetadata> def
                  Expect.hasLength produces 1 "Same status code + type still merge into one broadcast metadata object"
                  Expect.containsAll produces.[0].ContentTypes [ "application/json"; "text/html" ] "Every representation still carries the full merged content-type union"

          testCase "negotiate {} with no accepts calls throws"
          <| fun () ->
              let buildTrulyEmpty () =
                  (NegotiateBuilder()).Run(NegotiateSpec.Empty) |> ignore

              Expect.throws buildTrulyEmpty "Should throw when no representations are registered"

          testCaseTask "a representation registered via a bare HttpContext -> unit function is selected and runs its side effect"
          <| fun () -> task {
              let built =
                  (resource "/x") {
                      get (
                          negotiate {
                              accepts "application/json" (fun (ctx: HttpContext) ->
                                  ctx.Response.Headers.["X-Sync"] <- Microsoft.Extensions.Primitives.StringValues("1")
                                  ctx.Response.StatusCode <- 200)
                              accepts "text/html" (writeText "html")
                          }
                      )
                  }
              use host = buildHost built noServices
              do! host.StartAsync()
              let! response = getWithAccept host (Some "application/json")
              Expect.equal response.StatusCode HttpStatusCode.OK "The HttpContext -> unit representation should have been selected"
              Expect.equal (response.Headers.GetValues("X-Sync") |> Seq.head) "1" "Its side effect should have run"
          }

          testCaseTask "Accept with q=0 on the only registered media type is rejected, not merely deprioritized"
          <| fun () -> task {
              let built =
                  (resource "/x") { get (negotiate { accepts "application/json" (writeText "json") }) }
              use host = buildHost built noServices
              do! host.StartAsync()
              let! response = getWithAccept host (Some "application/json;q=0")
              Expect.equal response.StatusCode HttpStatusCode.NotAcceptable "q=0 must exclude the representation entirely, per RFC 9110 12.5.1"
              let! body = response.Content.ReadAsStringAsync()
              Expect.equal body "" "No body should be written"
          }

          testCaseTask "Accept: */*;q=0.5, text/html;q=0 rejects text/html even though */* is present"
          <| fun () -> task {
              let built =
                  (resource "/x") { get (negotiate { accepts "text/html" (writeText "html") }) }
              use host = buildHost built noServices
              do! host.StartAsync()
              let! response = getWithAccept host (Some "*/*;q=0.5, text/html;q=0")
              Expect.equal response.StatusCode HttpStatusCode.NotAcceptable "The */*;q=0.5 entry doesn't name text/html, and the text/html;q=0 entry explicitly excludes it"
              let! body = response.Content.ReadAsStringAsync()
              Expect.equal body "" "No body should be written"
          }

          testCaseTask "Accept: */*;q=0, text/html;q=0.8 selects text/html -- the more specific positive entry overrides the broader rejection"
          <| fun () -> task {
              let built =
                  (resource "/x") { get (negotiate { accepts "text/html" (writeText "html") }) }
              use host = buildHost built noServices
              do! host.StartAsync()
              let! response = getWithAccept host (Some "*/*;q=0, text/html;q=0.8")
              Expect.equal response.StatusCode HttpStatusCode.OK "The more specific text/html;q=0.8 entry governs, not the broader */*;q=0"
              let! body = response.Content.ReadAsStringAsync()
              Expect.equal body "html" "text/html should have been selected and served"
          }

          testCaseTask "a Task<'a>-returning accepts handler has its value auto-formatted, not discarded"
          <| fun () -> task {
              let built =
                  (resource "/x") {
                      get (
                          negotiate {
                              accepts "application/json" (fun (_: HttpContext) -> task { return {| Name = "Widget" |} })
                              accepts "text/html" (writeText "html")
                          }
                      )
                  }
              use host = buildHost built withMvc
              do! host.StartAsync()
              let! response = getWithAccept host (Some "application/json")
              let! body = response.Content.ReadAsStringAsync()
              Expect.equal response.Content.Headers.ContentType.MediaType "application/json" "Value should be written via viaOutputFormatter"
              Expect.stringContains body "Widget" "Serialized value should appear in the body"
          }

          testCaseTask "an inline task { do! ... } handler with no return dispatches directly, not via viaOutputFormatter"
          <| fun () -> task {
              // Regression test for frank-fs/frank#492: an ordinary `task { ... }`
              // computation expression with only `do!` statements (no `return`) infers
              // as `HttpContext -> Task<unit>`. Before the dedicated overload existed,
              // this was a direct match for `HttpContext -> Task<'a>` (no delegate
              // conversion needed), which F# prefers over the `RequestDelegate`
              // overload -- silently routing a self-writing handler through
              // `viaOutputFormatter`. `viaOutputFormatter` sets `ContentType`
              // unconditionally, which throws ("Headers are read-only, response has
              // already started") once the handler has already written to the body,
              // exactly the `getGame`/JSON-LD crash the issue reports. No MVC
              // services are registered here at all (`noServices`) -- if this
              // silently fell through to viaOutputFormatter, resolving
              // OutputFormatterSelector would itself throw, since AddMvcCore was
              // never called for this host's service provider.
              let built =
                  (resource "/x") {
                      get (
                          negotiate {
                              accepts "application/ld+json" (fun (ctx: HttpContext) ->
                                  task {
                                      ctx.Response.ContentType <- "application/ld+json"
                                      do! ctx.Response.WriteAsync("jsonld-body")
                                  })
                          }
                      )
                  }
              use host = buildHost built noServices
              do! host.StartAsync()
              let! response = getWithAccept host (Some "application/ld+json")
              let! body = response.Content.ReadAsStringAsync()
              Expect.equal response.StatusCode HttpStatusCode.OK "Should not throw or 500"
              Expect.equal response.Content.Headers.ContentType.MediaType "application/ld+json" "The handler's own Content-Type assignment must survive, not be overwritten"
              Expect.equal body "jsonld-body" "The handler's own body write must survive"
          }

          testCaseTask "an Async<'a>-returning accepts handler has its value auto-formatted"
          <| fun () -> task {
              let built =
                  (resource "/x") {
                      get (negotiate { accepts "application/json" (fun (_: HttpContext) -> async { return {| Name = "Widget" |} }) })
                  }
              use host = buildHost built withMvc
              do! host.StartAsync()
              let! response = getWithAccept host (Some "application/json")
              let! body = response.Content.ReadAsStringAsync()
              Expect.stringContains body "Widget" "Serialized value should appear in the body"
          }

          testCaseTask "a value-returning accepts entry composes with an independent-producer entry"
          <| fun () -> task {
              let mutable jsonRan = false

              let built =
                  (resource "/x") {
                      get (
                          negotiate {
                              accepts "application/json" (fun (_: HttpContext) -> jsonRan <- true; task { return {| Name = "Widget" |} })
                              accepts "application/ld+json" (writeText "jsonld")
                          }
                      )
                  }
              use host = buildHost built withMvc
              do! host.StartAsync()
              let! response = getWithAccept host (Some "application/ld+json")
              let! body = response.Content.ReadAsStringAsync()
              Expect.isFalse jsonRan "The value-returning representation should not run when a different one is selected"
              Expect.equal body "jsonld" "The independent producer should have run instead"
          }

          testCaseTask "Accept: application/ld+json never matches a concrete application/json registration, regardless of registration order"
          <| fun () -> task {
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
              let mutable jsonRan = false

              let built =
                  (resource "/x") {
                      get (
                          negotiate {
                              accepts "application/ld+json" (writeText "jsonld")
                              accepts "application/json" (fun (_: HttpContext) -> jsonRan <- true; task { return {| Name = "Widget" |} })
                          }
                      )
                  }
              use host = buildHost built withMvc
              do! host.StartAsync()
              let! response = getWithAccept host (Some "application/ld+json")
              let! body = response.Content.ReadAsStringAsync()
              Expect.isFalse jsonRan "application/json must not be selected for an Accept: application/ld+json request"
              Expect.equal body "jsonld" "application/ld+json should have been selected, matching registration order this time too"
          }

          testCaseTask "Accept: application/json never matches a registered application/ld+json via suffix leniency"
          <| fun () -> task {
              // Critical regression test: MediaTypeHeaderValue.MatchesMediaType is lenient
              // about RFC 6839 structured-syntax suffixes in the *client entry -> registered
              // type* direction too, so a plain "application/json" Accept used to match a
              // registered "application/ld+json". A client that asked only for plain JSON
              // must not silently receive JSON-LD; with nothing else registered that means
              // 406, not a JSON-LD body.
              let built =
                  (resource "/x") { get (negotiate { accepts "application/ld+json" (writeText "jsonld") }) }
              use host = buildHost built noServices
              do! host.StartAsync()
              let! response = getWithAccept host (Some "application/json")
              Expect.equal response.StatusCode HttpStatusCode.NotAcceptable "application/json must not match a registered application/ld+json"
              let! body = response.Content.ReadAsStringAsync()
              Expect.equal body "" "No body should be written"
          }

          testCaseTask "a JSON-LD-only block still serves a client that ranked JSON-LD below JSON"
          <| fun () -> task {
              // The companion boundary to the case above: "application/ld+json;q=0.5" is a
              // positive quality, so the client DOES accept JSON-LD -- just less than plain
              // JSON. With no plain-JSON representation registered there is nothing better to
              // serve, and RFC 9110 says a q>0 entry naming the representation makes it
              // acceptable. What the leniency fix changes here is the JSON-LD entry's
              // EFFECTIVE quality (0.5, its own -- not 1.0 borrowed from the plain-JSON
              // entry), which is what makes the next test's comparison come out right.
              let built =
                  (resource "/x") { get (negotiate { accepts "application/ld+json" (writeText "jsonld") }) }
              use host = buildHost built noServices
              do! host.StartAsync()
              let! response = getWithAccept host (Some "application/json;q=1, application/ld+json;q=0.5")
              Expect.equal response.StatusCode HttpStatusCode.OK "The client listed application/ld+json at q=0.5 -- it is acceptable"
              let! body = response.Content.ReadAsStringAsync()
              Expect.equal body "jsonld" "The only registered representation is served"
          }

          testCaseTask "with both JSON and JSON-LD registered, the higher-quality JSON entry wins"
          <| fun () -> task {
              // The companion to the case above: once a genuine plain-JSON representation
              // exists, quality-based selection must pick it over the lower-ranked JSON-LD
              // one -- and must do so regardless of registration order, so JSON-LD is
              // deliberately registered FIRST here.
              let built =
                  (resource "/x") {
                      get (
                          negotiate {
                              accepts "application/ld+json" (writeText "jsonld")
                              accepts "application/json" (writeText "json")
                          }
                      )
                  }
              use host = buildHost built noServices
              do! host.StartAsync()
              let! response = getWithAccept host (Some "application/json;q=1, application/ld+json;q=0.5")
              let! body = response.Content.ReadAsStringAsync()
              Expect.equal response.Content.Headers.ContentType.MediaType "application/json" "The q=1 entry's representation should be selected"
              Expect.equal body "json" "Plain JSON outranks JSON-LD at q=0.5"
          }

          testCaseTask "a successful dispatch sends Vary: Accept"
          <| fun () -> task {
              let built =
                  (resource "/x") {
                      get (
                          negotiate {
                              accepts "application/json" (writeText "json")
                              accepts "text/html" (writeText "html")
                          }
                      )
                  }
              use host = buildHost built noServices
              do! host.StartAsync()
              let! response = getWithAccept host (Some "application/json")

              Expect.contains
                  (response.Headers.Vary |> List.ofSeq)
                  "Accept"
                  "RFC 9110 12.5.5: a negotiated response must advertise that it varies by Accept"
          }

          testCaseTask "a 406 response also sends Vary: Accept"
          <| fun () -> task {
              let built =
                  (resource "/x") { get (negotiate { accepts "application/json" (writeText "json") }) }
              use host = buildHost built noServices
              do! host.StartAsync()
              let! response = getWithAccept host (Some "application/xml")
              Expect.equal response.StatusCode HttpStatusCode.NotAcceptable "Nothing matches application/xml"

              Expect.contains
                  (response.Headers.Vary |> List.ofSeq)
                  "Accept"
                  "The 406 varies by Accept too -- a cache must not replay it for a different client"
          }

          testCaseTask "a wildcard representation can delegate to ctx.Negotiate for full MVC negotiation"
          <| fun () -> task {
              // The headline composition from the design doc: an independent producer for one
              // exact type, plus a "*/*" catch-all that hands everything else to the existing
              // Frank.ContentNegotiation.negotiate / ctx.Negotiate function. The wildcard entry
              // sets no Content-Type of its own -- ctx.Negotiate's chosen IOutputFormatter does.
              let widget = { Name = "Widget" }

              let built =
                  (resource "/x") {
                      get (
                          negotiate {
                              accepts "application/ld+json" (writeText "jsonld")
                              accepts "*/*" (fun (ctx: HttpContext) -> Frank.ContentNegotiation.negotiate 200 widget ctx)
                          }
                      )
                  }
              use host = buildHost built withMvcXml
              do! host.StartAsync()

              // Exact type -> the independent producer, not the catch-all.
              let! jsonLdResponse = getWithAccept host (Some "application/ld+json")
              let! jsonLdBody = jsonLdResponse.Content.ReadAsStringAsync()
              Expect.equal jsonLdResponse.Content.Headers.ContentType.MediaType "application/ld+json" "The concrete entry sets its own Content-Type"
              Expect.equal jsonLdBody "jsonld" "The independent producer ran"

              // Anything else -> the catch-all, negotiated by MVC's formatter registry.
              let! xmlResponse = getWithAccept host (Some "application/xml")
              let! xmlBody = xmlResponse.Content.ReadAsStringAsync()
              Expect.equal xmlResponse.StatusCode HttpStatusCode.OK "The wildcard entry caught the otherwise-unmatched Accept"

              Expect.equal
                  xmlResponse.Content.Headers.ContentType.MediaType
                  "application/xml"
                  "ctx.Negotiate's selected formatter sets Content-Type -- dispatch must NOT set it to \"*/*\""

              Expect.stringContains xmlBody "Widget" "The MVC XML formatter wrote the body"
          }

          testCase "accepts \"*/*\" with a Task<'a>-returning handler throws rather than emitting Content-Type: */*"
          <| fun () ->
              // viaOutputFormatter sets ctx.Response.ContentType unconditionally to whatever
              // media type it is handed, bypassing dispatch's own isWildcard guard -- so this
              // combination would emit a literally invalid `Content-Type: */*`. It also has no
              // concrete type to give MVC's formatter selector. Rejected at registration time.
              let message =
                  messageOfThrow (fun () ->
                      negotiate { accepts "*/*" (fun (_: HttpContext) -> task { return { Name = "Widget" } }) }
                      |> ignore)

              Expect.stringContains message "*/*" "The message should name the offending media type"

              Expect.stringContains
                  message
                  "viaOutputFormatter"
                  "The message should explain that auto-formatting is what can't accept a wildcard"

          testCase "accepts \"*/*\" with an Async<'a>-returning handler throws rather than emitting Content-Type: */*"
          <| fun () ->
              let message =
                  messageOfThrow (fun () ->
                      negotiate { accepts "*/*" (fun (_: HttpContext) -> async { return { Name = "Widget" } }) }
                      |> ignore)

              Expect.stringContains message "viaOutputFormatter" "The Async overload must carry the same guard"

          testCase "accepts \"application/*\" with a value-returning handler throws too"
          <| fun () ->
              let message =
                  messageOfThrow (fun () ->
                      negotiate {
                          accepts "application/*" (fun (_: HttpContext) -> task { return { Name = "Widget" } })
                      }
                      |> ignore)

              Expect.stringContains
                  message
                  "application/*"
                  "A subtype wildcard is just as unusable for the formatter selector as \"*/*\""

          testCaseTask "accepts [mediaTypes] handler registers one representation per media type"
          <| fun () -> task {
              let widgetHandler =
                  fun (_: HttpContext) -> task { return { Name = "Widget" } }

              // `negotiate { }` always runs Run at the end, producing a HandlerDefinition
              // list (one Handler + Metadata per representation) -- Representations lives
              // on the intermediate NegotiateSpec, which only exists before Run. Call the
              // builder's own Accepts member directly to inspect that intermediate value.
              let spec =
                  NegotiateBuilder().Accepts(NegotiateSpec.Empty, [ "application/json"; "application/xml" ], widgetHandler)

              Expect.hasLength spec.Representations 2 "Should expand to two representations"

              let built =
                  (resource "/x") { get (negotiate { accepts [ "application/json"; "application/xml" ] widgetHandler }) }
              use host = buildHost built withMvcXml
              do! host.StartAsync()

              let! jsonResponse = getWithAccept host (Some "application/json")
              Expect.equal jsonResponse.Content.Headers.ContentType.MediaType "application/json" "JSON entry should format as JSON"

              let! xmlResponse = getWithAccept host (Some "application/xml")
              Expect.equal xmlResponse.Content.Headers.ContentType.MediaType "application/xml" "XML entry should format as XML, not the whole list"
          }

          testCase "produces metadata from a single representation is unaffected by the merge"
          <| fun () ->
              let defs =
                  negotiate {
                      accepts "application/json" (handler {
                          produces typeof<Product> 200
                          handle (writeText "json")
                      })
                      accepts "text/html" (writeText "html")
                  }

              Expect.hasLength defs 2 "Two representations"

              for def in defs do
                  let produces = HandlerDefinition.findAll<Microsoft.AspNetCore.Http.Metadata.IProducesResponseTypeMetadata> def
                  Expect.hasLength produces 1 "One representation's metadata should pass through unmerged, broadcast to every representation"
                  Expect.sequenceEqual produces.[0].ContentTypes [ "application/json" ] "Content types unchanged for a single representation"

          testCase "produces metadata from two representations sharing status code and type is merged and broadcast to every representation"
          <| fun () ->
              let defs =
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

              Expect.hasLength defs 2 "Two representations"

              for def in defs do
                  let produces = HandlerDefinition.findAll<Microsoft.AspNetCore.Http.Metadata.IProducesResponseTypeMetadata> def
                  Expect.hasLength produces 1 "Same status code + type merge into one metadata object"
                  Expect.containsAll produces.[0].ContentTypes [ "text/html"; "application/json" ] "Every representation's endpoint carries the full merged content-type union"

          testCase "produces metadata from two representations sharing status code but different types is NOT merged"
          <| fun () ->
              let defs =
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

              Expect.hasLength defs 2 "Two representations"

              for def in defs do
                  let produces = HandlerDefinition.findAll<Microsoft.AspNetCore.Http.Metadata.IProducesResponseTypeMetadata> def
                  Expect.hasLength produces 2 "Different response types sharing a status code must stay separate -- documented remaining limitation, broadcast to every representation"

          testCase "produces metadata with no colliding status/type survives negotiate by reference, not rebuilt"
          <| fun () ->
              // HandlerDefinition.Metadata is documented as an open extension point --
              // some other IProducesResponseTypeMetadata implementation could carry data
              // beyond StatusCode/Type/ContentTypes. When a representation's metadata has
              // nothing to merge with (its (status, type) pair is unique among the
              // representations), mergeProducesMetadata must pass the ORIGINAL object
              // through unchanged rather than rebuilding it as a bare
              // ProducesResponseTypeMetadata -- proven here via reference identity, for
              // every representation the merged metadata is now broadcast to.
              let handlerDef =
                  handler {
                      produces typeof<Product> 200
                      handle (writeText "json")
                  }

              let original =
                  HandlerDefinition.findAll<Microsoft.AspNetCore.Http.Metadata.IProducesResponseTypeMetadata> handlerDef
                  |> List.exactlyOne

              let defs =
                  negotiate {
                      accepts "application/json" handlerDef
                      accepts "text/html" (writeText "html")
                  }

              Expect.hasLength defs 2 "Two representations"

              for def in defs do
                  let merged =
                      HandlerDefinition.findAll<Microsoft.AspNetCore.Http.Metadata.IProducesResponseTypeMetadata> def
                      |> List.exactlyOne

                  Expect.isTrue
                      (System.Object.ReferenceEquals(original, merged))
                      "A non-colliding representation's metadata object should pass through negotiate unchanged, not be rebuilt"

          testCaseTask "webHost { } auto-registers FrankProducesMatcherPolicy -- negotiate { } works with zero explicit DI setup"
          <| fun () -> task {
              let resourceSpec =
                  (resource "/x") {
                      get (
                          negotiate {
                              accepts "application/json" (writeText "json")
                              accepts "text/html" (writeText "html")
                          }
                      )
                  }

              // Build directly off WebHostSpec.Empty (production defaults), substituting
              // only UseTestServer() for the real listener -- same pattern as
              // AlpsDocumentIntegrationTests.buildHost, but starting from the actual
              // WebHostSpec.Empty.Services this task modifies, not a hand-rolled one.
              let spec =
                  { WebHostSpec.Empty with
                      Endpoints = resourceSpec.Endpoints }

              use host =
                  Host
                      .CreateDefaultBuilder([||])
                      .ConfigureWebHost(fun webBuilder ->
                          webBuilder
                              .UseTestServer()
                              .ConfigureServices(fun services ->
                                  services.AddRouting() |> ignore
                                  spec.Services services |> ignore)
                              .Configure(fun app ->
                                  app.UseRouting() |> ignore
                                  app.UseEndpoints(fun endpoints ->
                                      endpoints.DataSources.Add(TestEndpointDataSource spec.Endpoints))
                                  |> ignore)
                          |> ignore)
                      .Build()

              do! host.StartAsync()
              use client = host.GetTestClient()
              use request = new HttpRequestMessage(HttpMethod.Get, "/x")
              request.Headers.Accept.ParseAdd("text/html")
              let! response = client.SendAsync(request)
              let! body = response.Content.ReadAsStringAsync()
              Expect.equal body "html" "Negotiation worked without the test explicitly registering FrankProducesMatcherPolicy"
          }

          testCaseTask
              "without FrankProducesMatcherPolicy registered, negotiate { } with multiple representations throws AmbiguousMatchException -- the RELEASE_NOTES caveat for non-webHost {} hosts"
          <| fun () -> task {
              let built =
                  (resource "/x") {
                      get (
                          negotiate {
                              accepts "application/json" (writeText "json")
                              accepts "text/html" (writeText "html")
                          }
                      )
                  }

              // Deliberately bypasses `buildHost`'s own `services.AddSingleton<MatcherPolicy,
              // FrankProducesMatcherPolicy>()` -- a bare TestServer with routing but no
              // disambiguating policy, reproducing the exact non-webHost {} scenario
              // RELEASE_NOTES documents: two RouteEndpoints at the same route+method with no
              // policy to pick between them.
              use host =
                  Host
                      .CreateDefaultBuilder([||])
                      .ConfigureWebHost(fun webBuilder ->
                          webBuilder
                              .UseTestServer()
                              .ConfigureServices(fun services -> services.AddRouting() |> ignore)
                              .Configure(fun app ->
                                  app.UseRouting() |> ignore
                                  app.UseEndpoints(fun endpoints -> endpoints.DataSources.Add(TestEndpointDataSource built.Endpoints))
                                  |> ignore)
                          |> ignore)
                      .Build()

              do! host.StartAsync()
              use client = host.GetTestClient()

              let! thrown =
                  task {
                      try
                          let! _ = client.GetAsync("/x")
                          return None
                      with ex ->
                          return Some ex
                  }

              match thrown with
              | Some ex ->
                  Expect.stringContains
                      (ex.ToString())
                      "AmbiguousMatchException"
                      "Without FrankProducesMatcherPolicy registered, routing cannot disambiguate the representations"
              | None -> failtest "expected an ambiguous match exception proving the policy's absence is unguarded without it"
          } ]
