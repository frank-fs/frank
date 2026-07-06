module Frank.Discovery.Tests.ValidatorTests

open System
open Microsoft.AspNetCore.Http
open Microsoft.AspNetCore.Routing
open Expecto
open Frank.Builder
open Frank.Discovery
open Frank.Tests.Shared.TestEndpointDataSource

let private badConfig =
    { DiscoveryConfig.Empty with
        ResourceHrefVars = Map.ofList [ "https://schema.org/Game", Map.empty ] }

let private goodConfig =
    { DiscoveryConfig.Empty with
        ResourceHrefVars =
            Map.ofList [ "https://schema.org/Game", Map.ofList [ "id", "https://schema.org/identifier" ] ] }

let private buildBadVarEndpoints () =
    let res =
        resource "/games/{gameId}" {
            relation "https://schema.org/Game"
            get (RequestDelegate(fun ctx -> ctx.Response.WriteAsync("game")))
        }

    res.Endpoints

let private buildGoodVarEndpoints () =
    let res =
        resource "/games/{id}" {
            relation "https://schema.org/Game"
            get (RequestDelegate(fun ctx -> ctx.Response.WriteAsync("game")))
        }

    res.Endpoints

[<Tests>]
let validatorTests =
    testList
        "HrefVarsValidator (AT3)"
        [ testCase "bad var throws InvalidOperationException naming the variable"
          <| fun _ ->
              let dataSource =
                  TestEndpointDataSource(buildBadVarEndpoints ()) :> EndpointDataSource

              let validator = HrefVarsValidator(badConfig) :> IStartupValidator

              try
                  validator.Validate dataSource
                  failwith "expected InvalidOperationException but no exception was thrown"
              with
              | :? InvalidOperationException as ex ->
                  Expect.stringContains ex.Message "gameId" "message names the variable"
              | ex -> failwithf "expected InvalidOperationException but got %s" (ex.GetType().Name)

          testCase "good var does not throw"
          <| fun _ ->
              let dataSource =
                  TestEndpointDataSource(buildGoodVarEndpoints ()) :> EndpointDataSource

              let validator = HrefVarsValidator(goodConfig) :> IStartupValidator
              validator.Validate dataSource

          testCase "empty EndpointDataSource (no relation endpoints) does not throw"
          <| fun _ ->
              let dataSource = TestEndpointDataSource([||]) :> EndpointDataSource
              let validator = HrefVarsValidator(badConfig) :> IStartupValidator
              validator.Validate dataSource ]
