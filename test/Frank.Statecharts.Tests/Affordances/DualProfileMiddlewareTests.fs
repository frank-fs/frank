module Frank.Affordances.Tests.DualProfileMiddlewareTests

open System
open System.Collections.Generic
open System.Net
open System.Net.Http
open System.Threading.Tasks
open Expecto
open Microsoft.AspNetCore.Builder
open Microsoft.AspNetCore.Http
open Microsoft.AspNetCore.TestHost
open Microsoft.Extensions.DependencyInjection
open Microsoft.Extensions.Hosting
open Microsoft.Extensions.Primitives
open Frank.Affordances
open Frank.Resources.Model
open Frank.Statecharts
open Frank.Statecharts.Dual
open Frank.Affordances.Tests.AffordanceTestHelpers

// -- Helpers --

/// Build a test DualProfileLookup.
let private buildDualLookup
    (entries: (string * (string * (string * (string * string)) list) list) list)
    : DualProfileLookup =
    let lookup = DualProfileLookup(StringComparer.Ordinal)

    for routeTemplate, states in entries do
        let stateDict =
            Dictionary<string, Dictionary<string, DualProfileEntry>>(StringComparer.Ordinal)

        for state, roles in states do
            let roleDict = Dictionary<string, DualProfileEntry>(StringComparer.OrdinalIgnoreCase)

            for roleName, (alpsJson, linkHeaderValue) in roles do
                roleDict.[roleName] <-
                    { AlpsJson = alpsJson
                      LinkHeaderValue = linkHeaderValue }

            stateDict.[state] <- roleDict

        lookup.[routeTemplate] <- stateDict

    lookup

/// Sample dual ALPS JSON and Link header value for tests.
let private sampleDualEntry role state =
    let alpsJson =
        sprintf
            """{"alps":{"version":"1.0","descriptor":[{"id":"%s","type":"semantic","ext":[{"id":"https://frank-fs.github.io/alps-ext/clientObligation","value":"must-select"}]}]}}"""
            (sprintf "%s-%s-dual" role state)

    let linkHeaderValue =
        sprintf "<https://example.com/alps/games-%s-%s-dual>; rel=\"profile\"" role state

    (alpsJson, linkHeaderValue)

/// Run a test against a server with AffordanceMiddleware, ProjectedProfileMiddleware, and DualProfileMiddleware.
let private withDualServer
    (affordanceLookup: Dictionary<string, PreComputedAffordance>)
    (roleLookup: RoleProfileLookup)
    (dualLookup: DualProfileLookup)
    (featureSetter: HttpContext -> unit)
    (f: HttpClient -> Task)
    =
    task {
        let builder = WebApplication.CreateBuilder([||])
        builder.WebHost.UseTestServer() |> ignore
        builder.Services.AddRouting() |> ignore
        // Register lookups in DI so AffordanceMiddleware's OnStarting callback can resolve them.
        builder.Services.AddSingleton<RoleProfileLookup>(roleLookup) |> ignore
        builder.Services.AddSingleton<DualProfileLookup>(dualLookup) |> ignore
        let app = builder.Build()

        app.UseRouting() |> ignore

        (app :> IApplicationBuilder)
            .Use(fun ctx (next: Func<Task>) ->
                featureSetter ctx
                next.Invoke())
        |> ignore

        (app :> IApplicationBuilder).UseMiddleware<AffordanceMiddleware>(affordanceLookup)
        |> ignore

        (app :> IApplicationBuilder).UseMiddleware<ProjectedProfileMiddleware>(roleLookup)
        |> ignore

        (app :> IApplicationBuilder).UseMiddleware<DualProfileMiddleware>(dualLookup)
        |> ignore

        app.UseEndpoints(fun endpoints -> defaultEndpoints endpoints) |> ignore

        app.Start()
        let server = app.GetTestServer()
        let client = server.CreateClient()

        try
            do! f client
        finally
            client.Dispose()
            server.Dispose()
            (app :> IDisposable).Dispose()
    }
    :> Task

// -- Test data --

let private gamesRoleLookup =
    let lookup = RoleProfileLookup(StringComparer.Ordinal)
    let roleMap = Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
    roleMap.["playerx"] <- "<https://example.com/alps/games-playerx>; rel=\"profile\""
    roleMap.["playero"] <- "<https://example.com/alps/games-playero>; rel=\"profile\""
    lookup.["/games/{gameId}"] <- roleMap
    lookup

let private gamesDualLookup =
    buildDualLookup
        [ "/games/{gameId}",
          [ "XTurn",
            [ "playerx", sampleDualEntry "playerx" "XTurn"
              "playero", sampleDualEntry "playero" "XTurn" ]
            "OTurn",
            [ "playerx", sampleDualEntry "playerx" "OTurn"
              "playero", sampleDualEntry "playero" "OTurn" ] ] ]

// -- Tests --

[<Tests>]
let dualProfileMiddlewareTests =
    testList
        "DualProfileMiddleware"
        [ testCase "Prefer: return=dual with authenticated request swaps profile link to dual"
          <| fun _ ->
              let affordances = buildAffordanceLookup [ "/games/{gameId}|XTurn", xTurnAffordance ]

              (withDualServer
                  affordances
                  gamesRoleLookup
                  gamesDualLookup
                  (fun ctx ->
                      ctx.SetStatechartState("XTurn", "XTurn", 0)
                      ctx.SetRoles(Set [ "PlayerX" ]))
                  (fun client ->
                      task {
                          use req = new HttpRequestMessage(HttpMethod.Get, "/games/abc")
                          req.Headers.Add("Prefer", "return=dual")
                          let! (response: HttpResponseMessage) = client.SendAsync(req)

                          Expect.equal response.StatusCode HttpStatusCode.OK "Should return 200"

                          let links = getHeaderValues response "Link"
                          let allLinks = links |> String.concat " "

                          // Profile link should point to dual variant
                          Expect.isTrue (allLinks.Contains("dual")) "Profile link should reference dual endpoint"

                          // Preference-Applied header per RFC 7240
                          let prefApplied = getHeaderValues response "Preference-Applied"
                          Expect.isNonEmpty prefApplied "Should have Preference-Applied header"

                          let prefValue = prefApplied |> String.concat ""

                          Expect.isTrue
                              (prefValue.Contains("return=dual"))
                              "Preference-Applied should contain return=dual"
                      }))
                  .GetAwaiter()
                  .GetResult()

          testCase "no Prefer header preserves projected profile link"
          <| fun _ ->
              let affordances = buildAffordanceLookup [ "/games/{gameId}|XTurn", xTurnAffordance ]

              (withDualServer
                  affordances
                  gamesRoleLookup
                  gamesDualLookup
                  (fun ctx ->
                      ctx.SetStatechartState("XTurn", "XTurn", 0)
                      ctx.SetRoles(Set [ "PlayerX" ]))
                  (fun client ->
                      task {
                          let! (response: HttpResponseMessage) = client.GetAsync("/games/abc")

                          Expect.equal response.StatusCode HttpStatusCode.OK "Should return 200"

                          let links = getHeaderValues response "Link"
                          let allLinks = links |> String.concat " "

                          // Should have role-projected profile, not dual
                          Expect.isTrue
                              (allLinks.Contains("alps/games-playerx>"))
                              "Should have role-projected profile link"

                          Expect.isFalse (allLinks.Contains("dual")) "Should NOT contain dual in link"

                          // No Preference-Applied header
                          let prefApplied = getHeaderValues response "Preference-Applied"
                          Expect.isEmpty prefApplied "Should NOT have Preference-Applied header"
                      }))
                  .GetAwaiter()
                  .GetResult()

          testCase "unauthenticated request with Prefer: return=dual is ignored"
          <| fun _ ->
              let affordances = buildAffordanceLookup [ "/games/{gameId}|XTurn", xTurnAffordance ]

              (withDualServer
                  affordances
                  gamesRoleLookup
                  gamesDualLookup
                  (fun ctx -> ctx.SetStatechartState("XTurn", "XTurn", 0))
                  (fun client ->
                      task {
                          use req = new HttpRequestMessage(HttpMethod.Get, "/games/abc")
                          req.Headers.Add("Prefer", "return=dual")
                          let! (response: HttpResponseMessage) = client.SendAsync(req)

                          Expect.equal response.StatusCode HttpStatusCode.OK "Should return 200"

                          let links = getHeaderValues response "Link"
                          let allLinks = links |> String.concat " "

                          // Should have global profile, not dual
                          Expect.isTrue (allLinks.Contains("alps/games>")) "Should have global profile link"

                          Expect.isFalse (allLinks.Contains("dual")) "Should NOT contain dual in link"

                          // No Preference-Applied header
                          let prefApplied = getHeaderValues response "Preference-Applied"
                          Expect.isEmpty prefApplied "Should NOT have Preference-Applied header"
                      }))
                  .GetAwaiter()
                  .GetResult()

          testCase "Vary header includes Prefer when route has dual profiles"
          <| fun _ ->
              let affordances = buildAffordanceLookup [ "/games/{gameId}|XTurn", xTurnAffordance ]

              (withDualServer
                  affordances
                  gamesRoleLookup
                  gamesDualLookup
                  (fun ctx ->
                      ctx.SetStatechartState("XTurn", "XTurn", 0)
                      ctx.SetRoles(Set [ "PlayerX" ]))
                  (fun client ->
                      task {
                          use req = new HttpRequestMessage(HttpMethod.Get, "/games/abc")
                          req.Headers.Add("Prefer", "return=dual")
                          let! (response: HttpResponseMessage) = client.SendAsync(req)

                          Expect.equal response.StatusCode HttpStatusCode.OK "Should return 200"

                          let vary = getHeaderValues response "Vary"
                          let varyValue = vary |> String.concat ", "
                          Expect.isTrue (varyValue.Contains("Prefer")) "Vary should include Prefer"
                      }))
                  .GetAwaiter()
                  .GetResult()

          testCase "authenticated request with non-matching role and Prefer: return=dual falls back"
          <| fun _ ->
              let affordances = buildAffordanceLookup [ "/games/{gameId}|XTurn", xTurnAffordance ]

              (withDualServer
                  affordances
                  gamesRoleLookup
                  gamesDualLookup
                  (fun ctx ->
                      ctx.SetStatechartState("XTurn", "XTurn", 0)
                      ctx.SetRoles(Set [ "Spectator" ]))
                  (fun client ->
                      task {
                          use req = new HttpRequestMessage(HttpMethod.Get, "/games/abc")
                          req.Headers.Add("Prefer", "return=dual")
                          let! (response: HttpResponseMessage) = client.SendAsync(req)

                          Expect.equal response.StatusCode HttpStatusCode.OK "Should return 200"

                          // No dual available for Spectator, so no Preference-Applied
                          let prefApplied = getHeaderValues response "Preference-Applied"
                          Expect.isEmpty prefApplied "Should NOT have Preference-Applied for non-matching role"
                      }))
                  .GetAwaiter()
                  .GetResult()

          testCase "route without dual profiles is a no-op"
          <| fun _ ->
              let affordances = buildAffordanceLookup [ "/health|*", healthAffordance ]

              let emptyRoleLookup = RoleProfileLookup(StringComparer.Ordinal)
              let emptyDualLookup = DualProfileLookup(StringComparer.Ordinal)

              (withDualServer
                  affordances
                  emptyRoleLookup
                  emptyDualLookup
                  (fun ctx -> ctx.SetRoles(Set [ "Admin" ]))
                  (fun client ->
                      task {
                          use req = new HttpRequestMessage(HttpMethod.Get, "/health")
                          req.Headers.Add("Prefer", "return=dual")
                          let! (response: HttpResponseMessage) = client.SendAsync(req)

                          Expect.equal response.StatusCode HttpStatusCode.OK "Should return 200"

                          let links = getHeaderValues response "Link"
                          let allLinks = links |> String.concat " "

                          Expect.isTrue (allLinks.Contains("alps/health>")) "Should contain original profile link"

                          let prefApplied = getHeaderValues response "Preference-Applied"
                          Expect.isEmpty prefApplied "Should NOT have Preference-Applied"
                      }))
                  .GetAwaiter()
                  .GetResult() ]

// -- PreferHeader parsing unit tests --

[<Tests>]
let preferHeaderParsingTests =
    testList
        "PreferHeader.hasReturnDual"
        [ testCase "return=dual is detected"
          <| fun _ -> Expect.isTrue (PreferHeader.hasReturnDual "return=dual") "should detect return=dual"

          testCase "multiple preferences with return=dual"
          <| fun _ ->
              Expect.isTrue
                  (PreferHeader.hasReturnDual "respond-async, return=dual")
                  "should detect return=dual among multiple preferences"

          testCase "return=minimal is not matched"
          <| fun _ -> Expect.isFalse (PreferHeader.hasReturnDual "return=minimal") "return=minimal should not match"

          testCase "empty string returns false"
          <| fun _ -> Expect.isFalse (PreferHeader.hasReturnDual "") "empty string should return false"

          testCase "null string returns false"
          <| fun _ -> Expect.isFalse (PreferHeader.hasReturnDual null) "null should return false"

          testCase "case insensitive matching"
          <| fun _ -> Expect.isTrue (PreferHeader.hasReturnDual "Return=Dual") "should be case-insensitive" ]

// -- DualProfileOverlay.build unit tests --

[<Tests>]
let dualProfileOverlayBuildTests =
    testList
        "DualProfileOverlay.build"
        [ testCase "builds lookup from extracted statechart with roles"
          <| fun _ ->
              let chart: ExtractedStatechart =
                  { RouteTemplate = "/orders/{orderId}"
                    StateNames = [ "Submitted"; "Confirmed"; "Completed" ]
                    InitialStateKey = "Submitted"
                    GuardNames = [ "SellerGuard"; "BuyerGuard" ]
                    StateMetadata =
                      Map.ofList
                          [ "Submitted",
                            { AllowedMethods = [ "GET" ]
                              IsFinal = false
                              Description = None }
                            "Confirmed",
                            { AllowedMethods = [ "GET"; "PUT" ]
                              IsFinal = false
                              Description = None }
                            "Completed",
                            { AllowedMethods = [ "GET" ]
                              IsFinal = true
                              Description = None } ]
                    Roles =
                      [ { Name = "Buyer"; Description = None }
                        { Name = "Seller"; Description = None } ]
                    Transitions =
                      [ { Event = "viewOrder"
                          Source = "Submitted"
                          Target = "Submitted"
                          Guard = None
                          Constraint = Unrestricted }
                        { Event = "confirmOrder"
                          Source = "Submitted"
                          Target = "Confirmed"
                          Guard = Some "SellerGuard"
                          Constraint = RestrictedTo [ "Seller" ] }
                        { Event = "viewOrder"
                          Source = "Confirmed"
                          Target = "Confirmed"
                          Guard = None
                          Constraint = Unrestricted }
                        { Event = "submitPayment"
                          Source = "Confirmed"
                          Target = "Completed"
                          Guard = Some "BuyerGuard"
                          Constraint = RestrictedTo [ "Buyer" ] }
                        { Event = "viewOrder"
                          Source = "Completed"
                          Target = "Completed"
                          Guard = None
                          Constraint = Unrestricted } ] }

              let lookup =
                  DualProfileOverlay.buildFromStatechart chart "orders" "https://example.com/alps"

              // Should have entries for the route template
              Expect.isTrue (lookup.ContainsKey("/orders/{orderId}")) "Should have route template key"

              let stateDict = lookup.["/orders/{orderId}"]

              // Non-final states should have dual profiles
              Expect.isTrue (stateDict.ContainsKey("Submitted")) "Should have Submitted state"
              Expect.isTrue (stateDict.ContainsKey("Confirmed")) "Should have Confirmed state"

              // Seller should have dual in Submitted (they can confirmOrder)
              let submittedRoles = stateDict.["Submitted"]
              Expect.isTrue (submittedRoles.ContainsKey("Seller")) "Seller should have dual in Submitted"

              // The dual ALPS JSON should contain clientObligation annotation
              let sellerDual = submittedRoles.["Seller"]
              Expect.isTrue (sellerDual.AlpsJson.Contains("clientObligation")) "Dual should contain clientObligation"

              // The pre-computed Link header value should be a valid URI
              Expect.isTrue
                  (sellerDual.LinkHeaderValue.StartsWith("<https://"))
                  "Link header value should start with valid URI"

              Expect.isTrue
                  (sellerDual.LinkHeaderValue.Contains("dual"))
                  "Link header value should reference dual profile"

          testCase "empty roles produces empty lookup"
          <| fun _ ->
              let chart: ExtractedStatechart =
                  { RouteTemplate = "/health"
                    StateNames = [ "Active" ]
                    InitialStateKey = "Active"
                    GuardNames = []
                    StateMetadata =
                      Map.ofList
                          [ "Active",
                            { AllowedMethods = [ "GET" ]
                              IsFinal = false
                              Description = None } ]
                    Roles = []
                    Transitions =
                      [ { Event = "check"
                          Source = "Active"
                          Target = "Active"
                          Guard = None
                          Constraint = Unrestricted } ] }

              let lookup =
                  DualProfileOverlay.buildFromStatechart chart "health" "https://example.com/alps"

              Expect.equal lookup.Count 0 "Empty roles should produce empty lookup" ]

// -- Integration test with Order Fulfillment fixture --

[<Tests>]
let orderFulfillmentDualIntegrationTests =
    testList
        "DualProfile.OrderFulfillment integration"
        [ testCase "Seller in Submitted gets must-select obligation for confirmOrder"
          <| fun _ ->
              let chart: ExtractedStatechart =
                  { RouteTemplate = "/orders/{orderId}"
                    StateNames = [ "Submitted"; "Confirmed"; "Paid"; "Completed"; "Cancelled" ]
                    InitialStateKey = "Submitted"
                    GuardNames = [ "SellerGuard"; "BuyerGuard" ]
                    StateMetadata =
                      Map.ofList
                          [ "Submitted",
                            { AllowedMethods = [ "GET" ]
                              IsFinal = false
                              Description = None }
                            "Confirmed",
                            { AllowedMethods = [ "GET"; "PUT" ]
                              IsFinal = false
                              Description = None }
                            "Paid",
                            { AllowedMethods = [ "GET" ]
                              IsFinal = false
                              Description = None }
                            "Completed",
                            { AllowedMethods = [ "GET" ]
                              IsFinal = true
                              Description = None }
                            "Cancelled",
                            { AllowedMethods = [ "GET" ]
                              IsFinal = true
                              Description = None } ]
                    Roles =
                      [ { Name = "Buyer"; Description = None }
                        { Name = "Seller"; Description = None } ]
                    Transitions =
                      [ { Event = "viewOrder"
                          Source = "Submitted"
                          Target = "Submitted"
                          Guard = None
                          Constraint = Unrestricted }
                        { Event = "confirmOrder"
                          Source = "Submitted"
                          Target = "Confirmed"
                          Guard = Some "SellerGuard"
                          Constraint = RestrictedTo [ "Seller" ] }
                        { Event = "rejectOrder"
                          Source = "Submitted"
                          Target = "Cancelled"
                          Guard = Some "SellerGuard"
                          Constraint = RestrictedTo [ "Seller" ] }
                        { Event = "viewOrder"
                          Source = "Confirmed"
                          Target = "Confirmed"
                          Guard = None
                          Constraint = Unrestricted }
                        { Event = "submitPayment"
                          Source = "Confirmed"
                          Target = "Paid"
                          Guard = Some "BuyerGuard"
                          Constraint = RestrictedTo [ "Buyer" ] }
                        { Event = "viewOrder"
                          Source = "Paid"
                          Target = "Paid"
                          Guard = None
                          Constraint = Unrestricted }
                        { Event = "viewOrder"
                          Source = "Completed"
                          Target = "Completed"
                          Guard = None
                          Constraint = Unrestricted }
                        { Event = "viewOrder"
                          Source = "Cancelled"
                          Target = "Cancelled"
                          Guard = None
                          Constraint = Unrestricted } ] }

              let lookup =
                  DualProfileOverlay.buildFromStatechart chart "orders" "https://example.com/alps"

              let stateDict = lookup.["/orders/{orderId}"]
              let sellerSubmitted = stateDict.["Submitted"].["Seller"]

              // Parse and verify the dual ALPS JSON contains obligation annotations
              Expect.isTrue
                  (sellerSubmitted.AlpsJson.Contains("must-select"))
                  "Seller in Submitted should have must-select obligation (confirmOrder advances protocol)"

              Expect.isTrue
                  (sellerSubmitted.AlpsJson.Contains("advancesProtocol"))
                  "Seller in Submitted should have advancesProtocol annotation"

          testCase "Buyer in Submitted gets may-poll obligation only"
          <| fun _ ->
              let chart: ExtractedStatechart =
                  { RouteTemplate = "/orders/{orderId}"
                    StateNames = [ "Submitted"; "Confirmed"; "Completed"; "Cancelled" ]
                    InitialStateKey = "Submitted"
                    GuardNames = [ "SellerGuard"; "BuyerGuard" ]
                    StateMetadata =
                      Map.ofList
                          [ "Submitted",
                            { AllowedMethods = [ "GET" ]
                              IsFinal = false
                              Description = None }
                            "Confirmed",
                            { AllowedMethods = [ "GET"; "PUT" ]
                              IsFinal = false
                              Description = None }
                            "Completed",
                            { AllowedMethods = [ "GET" ]
                              IsFinal = true
                              Description = None }
                            "Cancelled",
                            { AllowedMethods = [ "GET" ]
                              IsFinal = true
                              Description = None } ]
                    Roles =
                      [ { Name = "Buyer"; Description = None }
                        { Name = "Seller"; Description = None } ]
                    Transitions =
                      [ { Event = "viewOrder"
                          Source = "Submitted"
                          Target = "Submitted"
                          Guard = None
                          Constraint = Unrestricted }
                        { Event = "confirmOrder"
                          Source = "Submitted"
                          Target = "Confirmed"
                          Guard = Some "SellerGuard"
                          Constraint = RestrictedTo [ "Seller" ] }
                        { Event = "viewOrder"
                          Source = "Confirmed"
                          Target = "Confirmed"
                          Guard = None
                          Constraint = Unrestricted }
                        { Event = "submitPayment"
                          Source = "Confirmed"
                          Target = "Completed"
                          Guard = Some "BuyerGuard"
                          Constraint = RestrictedTo [ "Buyer" ] }
                        { Event = "viewOrder"
                          Source = "Completed"
                          Target = "Completed"
                          Guard = None
                          Constraint = Unrestricted }
                        { Event = "viewOrder"
                          Source = "Cancelled"
                          Target = "Cancelled"
                          Guard = None
                          Constraint = Unrestricted } ] }

              let lookup =
                  DualProfileOverlay.buildFromStatechart chart "orders" "https://example.com/alps"

              let stateDict = lookup.["/orders/{orderId}"]
              let buyerSubmitted = stateDict.["Submitted"].["Buyer"]

              // Buyer in Submitted is observer: only may-poll
              Expect.isTrue
                  (buyerSubmitted.AlpsJson.Contains("may-poll"))
                  "Buyer in Submitted should have may-poll obligation"

              Expect.isFalse
                  (buyerSubmitted.AlpsJson.Contains("must-select"))
                  "Buyer in Submitted should NOT have must-select" ]
