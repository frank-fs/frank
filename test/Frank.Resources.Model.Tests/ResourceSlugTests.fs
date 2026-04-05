module Frank.Resources.Model.Tests.ResourceSlugTests

open Expecto
open Frank.Resources.Model

[<Tests>]
let resourceSlugTests =
    testList
        "resourceSlug"
        [ testCase "derives slug from simple route"
          <| fun _ ->
              let slug = ResourceModel.resourceSlug "/health"
              Expect.equal slug "health" "Simple route"

          testCase "derives slug from parameterized route"
          <| fun _ ->
              let slug = ResourceModel.resourceSlug "/games/{gameId}"
              Expect.equal slug "games" "Parameterized route"

          testCase "derives slug from root route"
          <| fun _ ->
              let slug = ResourceModel.resourceSlug "/"
              Expect.equal slug "resource" "Root route"

          testCase "derives slug from nested route"
          <| fun _ ->
              let slug = ResourceModel.resourceSlug "/api/games"
              Expect.equal slug "api-games" "Nested route includes all segments"

          testCase "parameter-only route falls back to resource"
          <| fun _ ->
              let slug = ResourceModel.resourceSlug "/{id}"
              Expect.equal slug "resource" "Parameter-only route falls back to resource"

          testCase "consecutive parameters filtered leaving single static segment"
          <| fun _ ->
              let slug = ResourceModel.resourceSlug "/api/{tenant}/{region}/{id}"
              Expect.equal slug "api" "Consecutive parameters leave only the static prefix"

          testCase "spaces in segments replaced with hyphens"
          <| fun _ ->
              let slug = ResourceModel.resourceSlug "/api/v1 beta/items"
              Expect.equal slug "api-v1-beta-items" "Spaces in segments become hyphens"

          testCase "deeply nested route strips all parameter segments"
          <| fun _ ->
              let slug = ResourceModel.resourceSlug "/api/v2/items/{itemId}/versions/{versionId}"
              Expect.equal slug "api-v2-items-versions" "Deeply nested route keeps only static segments"

          testCase "route without leading slash is handled"
          <| fun _ ->
              let slug = ResourceModel.resourceSlug "games/{id}"
              Expect.equal slug "games" "Route without leading slash strips parameter segment"

          testCase "empty string falls back to resource"
          <| fun _ ->
              let slug = ResourceModel.resourceSlug ""
              Expect.equal slug "resource" "Empty string falls back to resource" ]
