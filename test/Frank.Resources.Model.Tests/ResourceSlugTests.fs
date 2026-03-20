module Frank.Resources.Model.Tests.ResourceSlugTests

open Expecto
open Frank.Resources.Model

[<Tests>]
let resourceSlugTests =
    testList
        "resourceSlug"
        [ testCase "derives slug from simple route"
          <| fun _ ->
              let slug = UnifiedModel.resourceSlug "/health"
              Expect.equal slug "health" "Simple route"

          testCase "derives slug from parameterized route"
          <| fun _ ->
              let slug = UnifiedModel.resourceSlug "/games/{gameId}"
              Expect.equal slug "games" "Parameterized route"

          testCase "derives slug from root route"
          <| fun _ ->
              let slug = UnifiedModel.resourceSlug "/"
              Expect.equal slug "root" "Root route"

          testCase "derives slug from nested route"
          <| fun _ ->
              let slug = UnifiedModel.resourceSlug "/api/games"
              Expect.equal slug "api" "Nested route uses first segment" ]
