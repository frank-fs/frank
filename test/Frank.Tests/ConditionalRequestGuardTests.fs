/// #467: structural enforcement of the R10 ordering contract (#426) -- a marker set by
/// useConditionalRequests on IApplicationBuilder.Properties, plus a guard that any
/// Link-header-emitting middleware registration point can call BEFORE registering itself.
/// If useConditionalRequests already ran on the same IApplicationBuilder, the caller is being
/// registered too late (inner to it) -- the wrong order per R10 -- and the guard throws
/// immediately at app-startup/configuration time instead of silently dropping Link headers on
/// a future 304/412 short-circuit.
module Frank.Tests.ConditionalRequestGuardTests

open System
open Microsoft.AspNetCore.Builder
open Microsoft.AspNetCore.Http
open Expecto
open Frank

[<Tests>]
let conditionalRequestGuardTests =
    testList
        "guardAgainstInnerLinkMiddleware (R10 structural enforcement, #467)"
        [ testCase "guard does not throw when useConditionalRequests has not yet been registered (correct order)"
          <| fun _ ->
              use app = WebApplication.CreateBuilder([||]).Build()
              // Must not throw -- ProvenanceMiddleware/etc. registering BEFORE
              // useConditionalRequests is the correct, unguarded order.
              guardAgainstInnerLinkMiddleware (app :> IApplicationBuilder) "SomeLinkMiddleware"

          // -- Negative control: proves the guard actually catches the wrong order, not just
          // that the correct order happens to pass (#467 AC6). --
          testCase
              "guard throws a clear, actionable error naming the offending middleware when registered AFTER useConditionalRequests (wrong order)"
          <| fun _ ->
              use app = WebApplication.CreateBuilder([||]).Build()
              let appBuilder = app :> IApplicationBuilder
              useConditionalRequests appBuilder |> ignore

              try
                  guardAgainstInnerLinkMiddleware appBuilder "SomeLinkMiddleware"
                  failtest "guard should have thrown InvalidOperationException when the marker is already set"
              with :? InvalidOperationException as ex ->
                  Expect.stringContains
                      ex.Message
                      "SomeLinkMiddleware"
                      "error message must name the offending middleware"

                  Expect.stringContains
                      ex.Message
                      "useConditionalRequests"
                      "error message must name the ordering-contract function so the fix is obvious" ]
