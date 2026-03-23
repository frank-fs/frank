module Frank.Affordances.Tests.AffordanceMiddlewareTests

open System
open System.Collections.Generic
open System.Linq
open System.Net
open System.Net.Http
open System.Threading.Tasks
open Expecto
open Microsoft.AspNetCore.Builder
open Microsoft.AspNetCore.Http
open Microsoft.AspNetCore.Routing
open Microsoft.AspNetCore.TestHost
open Microsoft.Extensions.DependencyInjection
open Microsoft.Extensions.Hosting
open Microsoft.Extensions.Primitives
open Frank.Affordances
open Frank.Resources.Model
open Frank.Statecharts

// -- Helpers --

/// Build a pre-computed lookup dictionary for test scenarios.
let private buildLookup (entries: (string * PreComputedAffordance) list) =
    let dict = Dictionary<string, PreComputedAffordance>(StringComparer.Ordinal)

    for key, value in entries do
        dict.[key] <- value

    dict

/// Get a header value from the response, checking both response and content headers.
/// HttpClient splits Allow into content headers and Link into response headers.
let private getHeaderValues (response: HttpResponseMessage) (name: string) : string list =
    let mutable values = Seq.empty

    if response.Headers.TryGetValues(name, &values) then
        values |> Seq.toList
    elif not (isNull response.Content) && response.Content.Headers.TryGetValues(name, &values) then
        values |> Seq.toList
    else
        []

/// Check whether a header exists in either response or content headers.
let private hasHeader (response: HttpResponseMessage) (name: string) : bool =
    getHeaderValues response name |> List.isEmpty |> not

/// Run a test against a configured test server, disposing all resources on completion.
let private withServer
    (lookup: Dictionary<string, PreComputedAffordance>)
    (stateKeySetter: HttpContext -> unit)
    (f: HttpClient -> Task)
    =
    task {
        let builder = WebApplication.CreateBuilder([||])
        builder.WebHost.UseTestServer() |> ignore
        builder.Services.AddRouting() |> ignore
        let app = builder.Build()

        app.UseRouting() |> ignore

        (app :> IApplicationBuilder).Use(fun ctx (next: Func<System.Threading.Tasks.Task>) ->
            stateKeySetter ctx
            next.Invoke())
        |> ignore

        (app :> IApplicationBuilder).UseMiddleware<AffordanceMiddleware>(lookup) |> ignore

        app.UseEndpoints(fun endpoints ->
            endpoints.MapGet(
                "/games/{gameId}",
                RequestDelegate(fun ctx -> ctx.Response.WriteAsync("OK"))
            )
            |> ignore

            endpoints.MapGet(
                "/health",
                RequestDelegate(fun ctx -> ctx.Response.WriteAsync("healthy"))
            )
            |> ignore)
        |> ignore

        app.Start()
        let server = app.GetTestServer()
        let client = server.CreateClient()

        try
            do! f client
        finally
            client.Dispose()
            server.Dispose()
            (app :> System.IDisposable).Dispose()
    }
    :> Task

let private xTurnAffordance =
    { AllowHeaderValue = StringValues("GET, POST")
      LinkHeaderValues =
        StringValues(
            [| "<https://example.com/alps/games>; rel=\"profile\""
               "</games/123/move>; rel=\"makeMove\"" |]
        ) }

let private wonAffordance =
    { AllowHeaderValue = StringValues("GET")
      LinkHeaderValues = StringValues([| "<https://example.com/alps/games>; rel=\"profile\"" |]) }

let private healthAffordance =
    { AllowHeaderValue = StringValues("GET")
      LinkHeaderValues = StringValues([| "<https://example.com/alps/health>; rel=\"profile\"" |]) }

// -- Tests --

[<Tests>]
let affordanceMiddlewareTests =
    testList
        "AffordanceMiddleware"
        [
          // T040: Stateful resource header injection
          testCase "injects Allow and Link headers for stateful resource (XTurn)"
          <| fun _ ->
              let lookup =
                  buildLookup [ "/games/{gameId}|XTurn", xTurnAffordance ]

              (withServer lookup (fun ctx -> ctx.SetStatechartState("XTurn", "XTurn", 0)) (fun client -> task {
                  let! (response: HttpResponseMessage) = client.GetAsync("/games/abc")

                  Expect.equal response.StatusCode HttpStatusCode.OK "Should return 200"

                  let allow = getHeaderValues response "Allow"
                  Expect.isNonEmpty allow "Should have Allow header"
                  let allowValue = allow |> String.concat ", "

                  Expect.isTrue
                      (allowValue.Contains("GET") && allowValue.Contains("POST"))
                      "Allow header should list GET and POST for XTurn state"

                  let links = getHeaderValues response "Link"
                  Expect.isNonEmpty links "Should have Link header"
                  let allLinks = links |> String.concat " "

                  Expect.isTrue
                      (allLinks.Contains("rel=\"profile\""))
                      "Should contain profile link"

                  Expect.isTrue
                      (allLinks.Contains("rel=\"makeMove\""))
                      "Should contain makeMove transition link"
              }))
                  .GetAwaiter()
                  .GetResult()

          // T040: Different state yields different headers
          testCase "injects correct headers for Won state (no POST)"
          <| fun _ ->
              let lookup =
                  buildLookup [ "/games/{gameId}|Won", wonAffordance ]

              (withServer lookup (fun ctx -> ctx.SetStatechartState("Won", "Won", 0)) (fun client -> task {
                  let! (response: HttpResponseMessage) = client.GetAsync("/games/abc")

                  Expect.equal response.StatusCode HttpStatusCode.OK "Should return 200"

                  let allow = getHeaderValues response "Allow"
                  Expect.isNonEmpty allow "Should have Allow header"
                  let allowValue = allow |> String.concat ", "
                  Expect.isTrue (allowValue.Contains("GET")) "Allow header should list GET"
                  Expect.isFalse (allowValue.Contains("POST")) "Allow header should NOT list POST for Won state"

                  let links = getHeaderValues response "Link"
                  Expect.isNonEmpty links "Should have Link header"
                  let allLinks = links |> String.concat " "
                  Expect.isTrue (allLinks.Contains("rel=\"profile\"")) "Should contain profile link"
                  Expect.isFalse (allLinks.Contains("makeMove")) "Should NOT contain makeMove link"
              }))
                  .GetAwaiter()
                  .GetResult()

          // T041: Plain resource uses wildcard state key
          testCase "uses wildcard state key for plain resources"
          <| fun _ ->
              let lookup =
                  buildLookup [ "/health|*", healthAffordance ]

              (withServer lookup ignore (fun client -> task {
                  let! (response: HttpResponseMessage) = client.GetAsync("/health")

                  Expect.equal response.StatusCode HttpStatusCode.OK "Should return 200"

                  let allow = getHeaderValues response "Allow"
                  Expect.isNonEmpty allow "Should have Allow header"
                  let allowValue = allow |> String.concat ", "
                  Expect.isTrue (allowValue.Contains("GET")) "Allow header should list GET for health endpoint"

                  let links = getHeaderValues response "Link"
                  Expect.isNonEmpty links "Should have Link header"
                  let allLinks = links |> String.concat " "
                  Expect.isTrue (allLinks.Contains("rel=\"profile\"")) "Should contain profile link for health"
              }))
                  .GetAwaiter()
                  .GetResult()

          // T042: Graceful degradation -- no matching entry
          testCase "passes through when no matching affordance entry exists"
          <| fun _ ->
              let lookup =
                  buildLookup [ "/other|*", healthAffordance ]

              (withServer lookup (fun ctx -> ctx.SetStatechartState("SomeState", "SomeState", 0)) (fun client -> task {
                  let! (response: HttpResponseMessage) = client.GetAsync("/games/abc")

                  Expect.equal response.StatusCode HttpStatusCode.OK "Should return 200"
                  Expect.isFalse (hasHeader response "Link") "Should not have Link header"
              }))
                  .GetAwaiter()
                  .GetResult()

          // T042: Graceful degradation -- empty lookup
          testCase "passes through with empty affordance lookup"
          <| fun _ ->
              let lookup = buildLookup []

              (withServer lookup ignore (fun client -> task {
                  let! (response: HttpResponseMessage) = client.GetAsync("/games/abc")

                  Expect.equal response.StatusCode HttpStatusCode.OK "Should return 200"
                  Expect.isFalse (hasHeader response "Link") "Should not have Link header"
              }))
                  .GetAwaiter()
                  .GetResult()

          // T040: Unknown state key -- no fallback to wildcard
          testCase "does not fall back to wildcard when state key is present but unmatched"
          <| fun _ ->
              let lookup =
                  buildLookup
                      [ "/games/{gameId}|XTurn", xTurnAffordance
                        "/games/{gameId}|*", wonAffordance ]

              (withServer lookup (fun ctx -> ctx.SetStatechartState("UnknownState", "UnknownState", 0)) (fun client -> task {
                  let! (response: HttpResponseMessage) = client.GetAsync("/games/abc")

                  Expect.equal response.StatusCode HttpStatusCode.OK "Should return 200"
                  // Should NOT use the wildcard entry -- state key was present but didn't match
                  Expect.isFalse (hasHeader response "Link") "Should not have Link header for unknown state"
              }))
                  .GetAwaiter()
                  .GetResult() ]

[<Tests>]
let preComputeTests =
    testList
        "AffordancePreCompute.preCompute"
        [ testCase "produces correct Allow header"
          <| fun _ ->
              let map =
                  { Version = AffordanceMap.currentVersion
                    Entries =
                      [ { RouteTemplate = "/games/{gameId}"
                          StateKey = "XTurn"
                          AllowedMethods = [ "GET"; "POST" ]
                          LinkRelations = []
                          ProfileUrl = "https://example.com/alps/games" } ] }

              let result = AffordancePreCompute.preCompute map
              let key = AffordanceMap.lookupKey "/games/{gameId}" "XTurn"
              Expect.isTrue (result.ContainsKey(key)) "Should contain the key"
              let entry = result.[key]
              Expect.equal (entry.AllowHeaderValue.ToString()) "GET, POST" "Allow header should be pre-formatted"

          testCase "produces correct Link headers with profile and transitions"
          <| fun _ ->
              let map =
                  { Version = AffordanceMap.currentVersion
                    Entries =
                      [ { RouteTemplate = "/games/{gameId}"
                          StateKey = "XTurn"
                          AllowedMethods = [ "GET"; "POST" ]
                          LinkRelations =
                            [ { Rel = "makeMove"
                                Href = "/games/{gameId}/move"
                                Method = "POST"
                                Title = Some "Make a move" } ]
                          ProfileUrl = "https://example.com/alps/games" } ] }

              let result = AffordancePreCompute.preCompute map
              let key = AffordanceMap.lookupKey "/games/{gameId}" "XTurn"
              let entry = result.[key]
              let linkValues = entry.LinkHeaderValues.ToArray()
              Expect.equal linkValues.Length 2 "Should have profile + transition link"

              Expect.equal
                  linkValues.[0]
                  "<https://example.com/alps/games>; rel=\"profile\""
                  "First link should be profile"

              Expect.equal
                  linkValues.[1]
                  "</games/{gameId}/move>; rel=\"makeMove\""
                  "Second link should be transition"

          testCase "omits profile link when ProfileUrl is empty"
          <| fun _ ->
              let map =
                  { Version = AffordanceMap.currentVersion
                    Entries =
                      [ { RouteTemplate = "/health"
                          StateKey = "*"
                          AllowedMethods = [ "GET" ]
                          LinkRelations = []
                          ProfileUrl = "" } ] }

              let result = AffordancePreCompute.preCompute map
              let key = AffordanceMap.lookupKey "/health" "*"
              let entry = result.[key]
              let linkValues = entry.LinkHeaderValues.ToArray()
              Expect.equal linkValues.Length 0 "Should have no link values when profile is empty and no transitions"

          testCase "handles multiple entries"
          <| fun _ ->
              let map =
                  { Version = AffordanceMap.currentVersion
                    Entries =
                      [ { RouteTemplate = "/games/{gameId}"
                          StateKey = "XTurn"
                          AllowedMethods = [ "GET"; "POST" ]
                          LinkRelations = []
                          ProfileUrl = "https://example.com/alps/games" }
                        { RouteTemplate = "/games/{gameId}"
                          StateKey = "Won"
                          AllowedMethods = [ "GET" ]
                          LinkRelations = []
                          ProfileUrl = "https://example.com/alps/games" } ] }

              let result = AffordancePreCompute.preCompute map
              Expect.equal result.Count 2 "Should have two entries"

              let xTurnKey = AffordanceMap.lookupKey "/games/{gameId}" "XTurn"
              let wonKey = AffordanceMap.lookupKey "/games/{gameId}" "Won"
              Expect.isTrue (result.ContainsKey(xTurnKey)) "Should contain XTurn key"
              Expect.isTrue (result.ContainsKey(wonKey)) "Should contain Won key"
              Expect.equal (result.[xTurnKey].AllowHeaderValue.ToString()) "GET, POST" "XTurn allows GET, POST"
              Expect.equal (result.[wonKey].AllowHeaderValue.ToString()) "GET" "Won allows only GET" ]
