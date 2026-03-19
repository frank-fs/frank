module Frank.Affordances.Tests.AffordanceMiddlewareTests

open System
open System.Collections.Generic
open System.Linq
open System.Net
open System.Net.Http
open Expecto
open Microsoft.AspNetCore.Builder
open Microsoft.AspNetCore.Hosting
open Microsoft.AspNetCore.Http
open Microsoft.AspNetCore.Routing
open Microsoft.AspNetCore.TestHost
open Microsoft.Extensions.DependencyInjection
open Microsoft.Extensions.Primitives
open Frank.Affordances

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

/// Create a test server with the affordance middleware and configurable state key injection.
let private buildTestServer
    (lookup: Dictionary<string, PreComputedAffordance>)
    (stateKeySetter: HttpContext -> unit)
    =
    let builder =
        WebHostBuilder()
            .Configure(fun app ->
                app.UseRouting() |> ignore

                // Simulate statechart middleware by setting HttpContext.Items
                app.Use(fun ctx (next: Func<System.Threading.Tasks.Task>) ->
                    stateKeySetter ctx
                    next.Invoke())
                |> ignore

                // Register the affordance middleware with pre-computed lookup
                app.UseMiddleware<AffordanceMiddleware>(lookup) |> ignore

                // Endpoints
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
                |> ignore)
            .ConfigureServices(fun services -> services.AddRouting() |> ignore)

    new TestServer(builder)

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

              use server =
                  buildTestServer lookup (fun ctx ->
                      ctx.Items.[AffordanceMap.StateKeyItemsKey] <- "XTurn")

              use client = server.CreateClient()

              let response =
                  client.GetAsync("/games/abc").Result

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

          // T040: Different state yields different headers
          testCase "injects correct headers for Won state (no POST)"
          <| fun _ ->
              let lookup =
                  buildLookup [ "/games/{gameId}|Won", wonAffordance ]

              use server =
                  buildTestServer lookup (fun ctx ->
                      ctx.Items.[AffordanceMap.StateKeyItemsKey] <- "Won")

              use client = server.CreateClient()

              let response =
                  client.GetAsync("/games/abc").Result

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

          // T041: Plain resource uses wildcard state key
          testCase "uses wildcard state key for plain resources"
          <| fun _ ->
              let lookup =
                  buildLookup [ "/health|*", healthAffordance ]

              use server = buildTestServer lookup ignore
              use client = server.CreateClient()

              let response =
                  client.GetAsync("/health").Result

              Expect.equal response.StatusCode HttpStatusCode.OK "Should return 200"

              let allow = getHeaderValues response "Allow"
              Expect.isNonEmpty allow "Should have Allow header"
              let allowValue = allow |> String.concat ", "
              Expect.isTrue (allowValue.Contains("GET")) "Allow header should list GET for health endpoint"

              let links = getHeaderValues response "Link"
              Expect.isNonEmpty links "Should have Link header"
              let allLinks = links |> String.concat " "
              Expect.isTrue (allLinks.Contains("rel=\"profile\"")) "Should contain profile link for health"

          // T042: Graceful degradation -- no matching entry
          testCase "passes through when no matching affordance entry exists"
          <| fun _ ->
              let lookup =
                  buildLookup [ "/other|*", healthAffordance ]

              use server =
                  buildTestServer lookup (fun ctx ->
                      ctx.Items.[AffordanceMap.StateKeyItemsKey] <- "SomeState")

              use client = server.CreateClient()

              let response =
                  client.GetAsync("/games/abc").Result

              Expect.equal response.StatusCode HttpStatusCode.OK "Should return 200"
              Expect.isFalse (hasHeader response "Link") "Should not have Link header"

          // T042: Graceful degradation -- empty lookup
          testCase "passes through with empty affordance lookup"
          <| fun _ ->
              let lookup = buildLookup []
              use server = buildTestServer lookup ignore
              use client = server.CreateClient()

              let response =
                  client.GetAsync("/games/abc").Result

              Expect.equal response.StatusCode HttpStatusCode.OK "Should return 200"
              Expect.isFalse (hasHeader response "Link") "Should not have Link header"

          // T040: Unknown state key -- no fallback to wildcard
          testCase "does not fall back to wildcard when state key is present but unmatched"
          <| fun _ ->
              let lookup =
                  buildLookup
                      [ "/games/{gameId}|XTurn", xTurnAffordance
                        "/games/{gameId}|*", wonAffordance ]

              use server =
                  buildTestServer lookup (fun ctx ->
                      ctx.Items.[AffordanceMap.StateKeyItemsKey] <- "UnknownState")

              use client = server.CreateClient()

              let response =
                  client.GetAsync("/games/abc").Result

              Expect.equal response.StatusCode HttpStatusCode.OK "Should return 200"
              // Should NOT use the wildcard entry -- state key was present but didn't match
              Expect.isFalse (hasHeader response "Link") "Should not have Link header for unknown state" ]

[<Tests>]
let preComputeTests =
    testList
        "AffordanceMap.preCompute"
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

              let result = AffordanceMap.preCompute map
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

              let result = AffordanceMap.preCompute map
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

              let result = AffordanceMap.preCompute map
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

              let result = AffordanceMap.preCompute map
              Expect.equal result.Count 2 "Should have two entries"

              let xTurnKey = AffordanceMap.lookupKey "/games/{gameId}" "XTurn"
              let wonKey = AffordanceMap.lookupKey "/games/{gameId}" "Won"
              Expect.isTrue (result.ContainsKey(xTurnKey)) "Should contain XTurn key"
              Expect.isTrue (result.ContainsKey(wonKey)) "Should contain Won key"
              Expect.equal (result.[xTurnKey].AllowHeaderValue.ToString()) "GET, POST" "XTurn allows GET, POST"
              Expect.equal (result.[wonKey].AllowHeaderValue.ToString()) "GET" "Won allows only GET" ]
