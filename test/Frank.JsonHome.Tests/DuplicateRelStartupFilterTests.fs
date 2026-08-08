module Frank.JsonHome.Tests.DuplicateRelStartupFilterTests

open System
open System.Collections.Generic
open Microsoft.AspNetCore.Builder
open Microsoft.AspNetCore.Hosting
open Microsoft.AspNetCore.Mvc.Abstractions
open Microsoft.AspNetCore.Mvc.ApiExplorer
open Microsoft.Extensions.DependencyInjection
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

/// Drives the filter the way the host does: wrap a no-op `next`, then invoke
/// the returned delegate with a real IApplicationBuilder. The check runs after
/// `next` -- in a real app that is what has just run UseEndpoints.
let private runFilter (descriptions: ApiDescription list) =
    let filter = DuplicateRelStartupFilter(fakeProvider descriptions) :> IStartupFilter
    let wrapped = filter.Configure(Action<IApplicationBuilder>(fun _ -> ()))
    let services = ServiceCollection().BuildServiceProvider()
    wrapped.Invoke(ApplicationBuilder(services))

[<Tests>]
let tests =
    testList
        "DuplicateRelStartupFilter"
        [ test "no duplicates -> no throw" {
              runFilter
                  [ describe "products" "GET" [ { Rel = "tag:example.com,2026:products" } ]
                    describe "orders" "GET" [ { Rel = "tag:example.com,2026:orders" } ] ]
          }

          test "two resources sharing a rel -> throws naming both routes" {
              let metadata: obj list = [ { Rel = "tag:example.com,2026:dup" } ]

              Expect.throwsC
                  (fun () -> runFilter [ describe "first" "GET" metadata; describe "second" "GET" metadata ])
                  (fun ex ->
                      match ex with
                      | :? OptionsValidationException as ove ->
                          Expect.stringContains ove.Message "/first" "Failure names the first route"
                          Expect.stringContains ove.Message "/second" "Failure names the second route"
                      | other -> failwith $"Expected OptionsValidationException, got %s{other.GetType().FullName}")
          }

          test "three resources, two share a rel -> only the colliding pair is reported" {
              let dupMetadata: obj list = [ { Rel = "tag:example.com,2026:dup" } ]
              let uniqueMetadata: obj list = [ { Rel = "tag:example.com,2026:unique" } ]

              Expect.throwsC
                  (fun () ->
                      runFilter
                          [ describe "first" "GET" dupMetadata
                            describe "second" "GET" dupMetadata
                            describe "third" "GET" uniqueMetadata ])
                  (fun ex ->
                      match ex with
                      | :? OptionsValidationException as ove ->
                          Expect.stringContains ove.Message "/first" "Failure names the first route"
                          Expect.stringContains ove.Message "/second" "Failure names the second route"

                          Expect.isFalse
                              (ove.Message.Contains "/third")
                              "Non-colliding third resource is not reported"
                      | other -> failwith $"Expected OptionsValidationException, got %s{other.GetType().FullName}")
          } ]
