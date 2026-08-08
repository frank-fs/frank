module Frank.JsonHome.Tests.DuplicateRelValidatorTests

open System.Collections.Generic
open Microsoft.AspNetCore.Mvc.Abstractions
open Microsoft.AspNetCore.Mvc.ApiExplorer
open Microsoft.Extensions.Options
open Expecto
open Frank.JsonHome

/// Builds an ApiDescription the way ApiExplorer would for one endpoint+method.
let private describe (relativePath: string) (httpMethod: string) (metadata: obj list) =
    let action = ActionDescriptor()
    action.EndpointMetadata <- ResizeArray metadata

    let description = ApiDescription()
    description.RelativePath <- relativePath
    description.HttpMethod <- httpMethod
    description.ActionDescriptor <- action
    description

/// A minimal IApiDescriptionGroupCollectionProvider exposing the given
/// descriptions as a single group, matching what JsonHome.documentHandler reads.
let private fakeProvider (descriptions: ApiDescription list) : IApiDescriptionGroupCollectionProvider =
    let group =
        ApiDescriptionGroup(null, ResizeArray descriptions :> IReadOnlyList<ApiDescription>)

    let groups =
        ApiDescriptionGroupCollection(ResizeArray [ group ] :> IReadOnlyList<ApiDescriptionGroup>, 1)

    { new IApiDescriptionGroupCollectionProvider with
        member _.ApiDescriptionGroups = groups }

let private validate (descriptions: ApiDescription list) : ValidateOptionsResult =
    let validator = DuplicateRelValidator(fakeProvider descriptions) :> IValidateOptions<JsonHomeOptions>
    validator.Validate(null, JsonHomeOptions.Default)

[<Tests>]
let tests =
    testList
        "DuplicateRelValidator"
        [ test "no duplicates -> Success" {
              let result =
                  validate
                      [ describe "products" "GET" [ { Rel = "tag:example.com,2026:products" } ]
                        describe "orders" "GET" [ { Rel = "tag:example.com,2026:orders" } ] ]

              Expect.isTrue result.Succeeded "Distinct rels should validate successfully"
          }

          test "two resources sharing a rel -> Fail naming both routes" {
              let metadata: obj list = [ { Rel = "tag:example.com,2026:dup" } ]

              let result = validate [ describe "first" "GET" metadata; describe "second" "GET" metadata ]

              Expect.isFalse result.Succeeded "Shared rel should fail validation"
              let message = result.FailureMessage
              Expect.stringContains message "/first" "Failure names the first route"
              Expect.stringContains message "/second" "Failure names the second route"
          }

          test "three resources, two share a rel -> only the colliding pair is reported" {
              let dupMetadata: obj list = [ { Rel = "tag:example.com,2026:dup" } ]
              let uniqueMetadata: obj list = [ { Rel = "tag:example.com,2026:unique" } ]

              let result =
                  validate
                      [ describe "first" "GET" dupMetadata
                        describe "second" "GET" dupMetadata
                        describe "third" "GET" uniqueMetadata ]

              Expect.isFalse result.Succeeded "Colliding pair should fail validation"
              let message = result.FailureMessage
              Expect.stringContains message "/first" "Failure names the first route"
              Expect.stringContains message "/second" "Failure names the second route"
              Expect.isFalse (message.Contains "/third") "Non-colliding third resource is not reported"
          } ]
