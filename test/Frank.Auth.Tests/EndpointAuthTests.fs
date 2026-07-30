module Frank.Auth.Tests.EndpointAuthTests

open Microsoft.AspNetCore.Authorization
open Expecto
open Frank.Builder
open Frank.Auth

let private emptyDef = HandlerDefinition.Empty

[<Tests>]
let tests =
    testList
        "EndpointAuth.applyAuthToHandler"
        [ test "empty config leaves the handler definition's metadata unchanged" {
              let result = EndpointAuth.applyAuthToHandler AuthConfig.empty emptyDef
              Expect.isEmpty result.Metadata "No metadata added"
          }

          test "Authenticated requirement adds a single bare AuthorizeAttribute" {
              let config = AuthConfig.empty |> AuthConfig.addRequirement AuthRequirement.Authenticated
              let result = EndpointAuth.applyAuthToHandler config emptyDef
              Expect.hasLength result.Metadata 1 "One metadata object"
              Expect.isTrue (result.Metadata.[0] :? AuthorizeAttribute) "It's an AuthorizeAttribute"
          }

          test "Claim requirement adds an AuthorizeAttribute and a built policy" {
              let config = AuthConfig.empty |> AuthConfig.addRequirement (AuthRequirement.Claim("scope", [ "admin" ]))
              let result = EndpointAuth.applyAuthToHandler config emptyDef
              Expect.hasLength result.Metadata 2 "Two metadata objects"
              Expect.isTrue (result.Metadata |> List.exists (fun m -> m :? AuthorizeAttribute)) "Has an AuthorizeAttribute"
              Expect.isTrue (result.Metadata |> List.exists (fun m -> m :? AuthorizationPolicy)) "Has a built policy"
          }

          test "Role requirement adds an AuthorizeAttribute and a built policy" {
              let config = AuthConfig.empty |> AuthConfig.addRequirement (AuthRequirement.Role "admin")
              let result = EndpointAuth.applyAuthToHandler config emptyDef
              Expect.hasLength result.Metadata 2 "Two metadata objects"
              Expect.isTrue (result.Metadata |> List.exists (fun m -> m :? AuthorizeAttribute)) "Has an AuthorizeAttribute"
              Expect.isTrue (result.Metadata |> List.exists (fun m -> m :? AuthorizationPolicy)) "Has a built policy"
          }

          test "Policy requirement adds a single named AuthorizeAttribute" {
              let config = AuthConfig.empty |> AuthConfig.addRequirement (AuthRequirement.Policy "CanViewReports")
              let result = EndpointAuth.applyAuthToHandler config emptyDef
              Expect.hasLength result.Metadata 1 "One metadata object"
              match result.Metadata.[0] with
              | :? AuthorizeAttribute as attr -> Expect.equal attr.Policy "CanViewReports" "Policy name carried through"
              | _ -> failtest "Expected an AuthorizeAttribute"
          }

          test "multiple requirements accumulate across calls" {
              let config =
                  AuthConfig.empty
                  |> AuthConfig.addRequirement AuthRequirement.Authenticated
                  |> AuthConfig.addRequirement (AuthRequirement.Role "admin")

              let result = EndpointAuth.applyAuthToHandler config emptyDef
              Expect.hasLength result.Metadata 3 "Authenticated (1 object) + Role (2 objects)"
          } ]
