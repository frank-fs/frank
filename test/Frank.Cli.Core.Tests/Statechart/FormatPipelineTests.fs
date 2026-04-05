module Frank.Cli.Core.Tests.Statechart.FormatPipelineTests

open Expecto
open Frank.Resources.Model
open Frank.Cli.Core.Statechart.FormatPipeline

[<Tests>]
let tests =
    testList "FormatPipeline" [
        testList "ResourceModel.resourceSlug" [
            testCase "extracts slug from simple route" <| fun _ ->
                Expect.equal (ResourceModel.resourceSlug "/games/{id}") "games" ""

            testCase "extracts slug from multi-segment route" <| fun _ ->
                Expect.equal (ResourceModel.resourceSlug "/api/orders/{orderId}") "api-orders" ""

            testCase "handles root route" <| fun _ ->
                Expect.equal (ResourceModel.resourceSlug "/") "resource" ""

            testCase "handles route with no parameters" <| fun _ ->
                Expect.equal (ResourceModel.resourceSlug "/health") "health" ""

            testCase "filters multiple parameter segments" <| fun _ ->
                Expect.equal (ResourceModel.resourceSlug "/api/{version}/items/{id}") "api-items" ""
        ]

        testCase "allFormats contains exactly 6 formats" <| fun _ ->
            Expect.equal allFormats.Length 6 "should have 6 formats"
    ]
