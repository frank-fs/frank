module Frank.Discovery.Tests.DiscoveryTests

open System
open System.Net
open System.Net.Http
open System.Text.Json
open Microsoft.AspNetCore.Builder
open Microsoft.AspNetCore.Http
open Microsoft.AspNetCore.TestHost
open Microsoft.Extensions.DependencyInjection
open Microsoft.Extensions.Hosting
open Expecto
open Frank.Discovery
open Frank.Discovery.DiscoveryMiddleware

let private makeTestConfig () : DiscoveryConfig =
    { ProfileBaseUri = "/alps"
      HomeRoute = "/"
      AlpsDescriptors =
        Map.ofList
            [ "MyApp.Order",
              [ { Id = "Order"
                  Type = "semantic"
                  Doc = None
                  Href = Some "https://schema.org/Order" }
                { Id = "orderId"
                  Type = "semantic"
                  Doc = None
                  Href = Some "https://schema.org/identifier" } ] ]
      DescribedByLinks =
        Map.ofList [ "MyApp.Order", [ "<https://schema.org/Order>; rel=\"describedby\"" ] ] }

/// Create a test WebApplication with OptionsEnricherMiddleware + ALPS/JSON Home routes + test resource endpoints.
let private createServer (config: DiscoveryConfig) =
    let builder = WebApplication.CreateBuilder([||])
    builder.WebHost.UseTestServer() |> ignore
    builder.Services.AddRouting() |> ignore
    builder.Services.AddSingleton<DiscoveryConfig>(config) |> ignore
    let app = builder.Build()

    (app :> IApplicationBuilder).UseRouting() |> ignore
    app.UseMiddleware<OptionsEnricherMiddleware>() |> ignore

    // ALPS profile endpoint
    app.MapGet(
        config.ProfileBaseUri + "/{slug}",
        Func<HttpContext, System.Threading.Tasks.Task>(fun ctx ->
            task {
                ctx.Response.ContentType <- "application/alps+json"
                do! ctx.Response.WriteAsync(AlpsSerializer.serialize config.AlpsDescriptors)
            }
            :> System.Threading.Tasks.Task)
    )
    |> ignore

    // JSON Home endpoint
    app.MapGet(
        config.HomeRoute,
        Func<HttpContext, System.Threading.Tasks.Task>(fun ctx ->
            task {
                let resources =
                    config.DescribedByLinks
                    |> Map.toList
                    |> List.map (fun (typeName, links) ->
                        let rel =
                            match links with
                            | link :: _ ->
                                let s = link.IndexOf('<') + 1
                                let e = link.IndexOf('>')

                                if s > 0 && e > s then link.[s .. e - 1] else typeName
                            | [] -> typeName

                        { JsonHomeSerializer.Relation = rel
                          JsonHomeSerializer.Href = "/" + typeName.ToLowerInvariant()
                          JsonHomeSerializer.Allow = [ "GET"; "HEAD" ] })

                ctx.Response.ContentType <- "application/json-home+json"
                do! ctx.Response.WriteAsync(JsonHomeSerializer.serialize resources)
            }
            :> System.Threading.Tasks.Task)
    )
    |> ignore

    // Test resource endpoints for OPTIONS testing
    app.MapGet(
        "/orders/{id}",
        Func<HttpContext, System.Threading.Tasks.Task>(fun ctx ->
            task { do! ctx.Response.WriteAsync("order") } :> System.Threading.Tasks.Task)
    )
    |> ignore

    app.MapPost(
        "/orders/{id}",
        Func<HttpContext, System.Threading.Tasks.Task>(fun ctx ->
            task { ctx.Response.StatusCode <- 200 } :> System.Threading.Tasks.Task)
    )
    |> ignore

    app.Start()
    app.GetTestClient()

/// Read Link headers from response as a flat list.
let private getLinkHeaders (response: HttpResponseMessage) =
    response.Headers
    |> Seq.filter (fun h -> h.Key.Equals("Link", StringComparison.OrdinalIgnoreCase))
    |> Seq.collect (fun h -> h.Value)
    |> Seq.toList

/// Read Allow header — checks both response headers and content headers.
/// In ASP.NET Core, Allow is classified as a content header.
let private getAllowHeader (response: HttpResponseMessage) =
    match response.Headers.TryGetValues("Allow") with
    | true, values -> values |> String.concat ", "
    | false, _ ->
        match response.Content.Headers.TryGetValues("Allow") with
        | true, values -> values |> String.concat ", "
        | false, _ -> ""

[<Tests>]
let discoveryTests =
    let config = makeTestConfig ()

    testList
        "Frank.Discovery"
        [
          // AT1: GET / returns JSON Home
          testTask "AT1: GET / returns JSON Home document" {
              let client = createServer config
              let req = new HttpRequestMessage(HttpMethod.Get, "/")
              req.Headers.Add("Accept", "application/json-home")
              let! (response: HttpResponseMessage) = client.SendAsync(req)

              Expect.equal response.StatusCode HttpStatusCode.OK "Should return 200"

              let contentType = response.Content.Headers.ContentType.MediaType
              Expect.equal contentType "application/json-home+json" "Content-Type should be application/json-home+json"

              let! body = response.Content.ReadAsStringAsync()
              Expect.stringContains body "resources" "Body should contain 'resources' key"
              Expect.stringContains body "https://schema.org/Order" "Relation IRI should be schema.org vocab IRI"
              Expect.isFalse (body.Contains("urn:frank:")) "Should not contain urn:frank: IRIs"
          }

          // AT2: GET /alps/orders returns ALPS
          testTask "AT2: GET /alps/orders returns ALPS document" {
              let client = createServer config
              let req = new HttpRequestMessage(HttpMethod.Get, "/alps/orders")
              req.Headers.Add("Accept", "application/alps+json")
              let! (response: HttpResponseMessage) = client.SendAsync(req)

              Expect.equal response.StatusCode HttpStatusCode.OK "Should return 200"

              let contentType = response.Content.Headers.ContentType.MediaType
              Expect.equal contentType "application/alps+json" "Content-Type should be application/alps+json"

              let! body = response.Content.ReadAsStringAsync()
              let doc = JsonDocument.Parse(body : string)
              let alps = doc.RootElement.GetProperty("alps")
              Expect.equal (alps.GetProperty("version").GetString()) "1.0" "version should be 1.0"

              let descriptors = alps.GetProperty("descriptor")
              Expect.isTrue (descriptors.GetArrayLength() > 0) "Should have descriptors"

              for d in descriptors.EnumerateArray() do
                  let mutable hrefEl = Unchecked.defaultof<JsonElement>
                  if d.TryGetProperty("href", &hrefEl) then
                      let href = hrefEl.GetString()
                      Expect.isFalse (href.StartsWith("urn:frank:")) "href should not be urn:frank:*"

              Expect.isFalse (body.Contains("urn:frank:")) "Should not contain urn:frank: IRIs"
          }

          // AT3: OPTIONS /orders/o1 returns Allow + Link headers
          testTask "AT3: OPTIONS /orders/o1 returns Allow + Link headers" {
              let client = createServer config
              let req = new HttpRequestMessage(HttpMethod.Options, "/orders/o1")
              let! (response: HttpResponseMessage) = client.SendAsync(req)

              Expect.equal response.StatusCode HttpStatusCode.OK "Should return 200"

              let allowStr = getAllowHeader response
              Expect.stringContains allowStr "GET" "Allow should include GET"
              Expect.stringContains allowStr "POST" "Allow should include POST"

              let linkHeaders = getLinkHeaders response
              Expect.isNonEmpty linkHeaders "Link headers should be present"

              let hasProfile = linkHeaders |> List.exists (fun l -> l.Contains("rel=\"profile\""))
              Expect.isTrue hasProfile "Link headers should include rel=profile"

              let hasDescribedBy = linkHeaders |> List.exists (fun l -> l.Contains("describedby"))
              Expect.isTrue hasDescribedBy "Link headers should include rel=describedby"
          }

          // AT4: HEAD always present with GET
          testTask "AT4: OPTIONS /orders/o1 Allow includes both GET and HEAD" {
              let client = createServer config
              let req = new HttpRequestMessage(HttpMethod.Options, "/orders/o1")
              let! (response: HttpResponseMessage) = client.SendAsync(req)

              let allowStr = getAllowHeader response
              Expect.stringContains allowStr "GET" "Allow should include GET"
              Expect.stringContains allowStr "HEAD" "Allow should include HEAD (RFC 9110 §9.3.2)"
          }

          // AT5: AlpsSerializer unit test
          testCase "AT5: AlpsSerializer.serialize produces valid ALPS with vocabulary IRIs" <| fun () ->
              let json = AlpsSerializer.serialize config.AlpsDescriptors
              let doc = JsonDocument.Parse(json : string)
              let alps = doc.RootElement.GetProperty("alps")
              Expect.equal (alps.GetProperty("version").GetString()) "1.0" "version should be 1.0"
              Expect.isFalse (json.Contains("urn:frank:")) "No urn:frank: IRIs in ALPS output"

              for d in alps.GetProperty("descriptor").EnumerateArray() do
                  let mutable hrefEl = Unchecked.defaultof<JsonElement>
                  if d.TryGetProperty("href", &hrefEl) then
                      let href = hrefEl.GetString()
                      Expect.stringContains href "schema.org" "href should be a vocabulary IRI" ]
