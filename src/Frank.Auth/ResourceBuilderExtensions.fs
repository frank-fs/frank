namespace Frank.Auth

open Frank.Builder

[<AutoOpen>]
module ResourceBuilderExtensions =
    type ResourceBuilder with
        [<CustomOperation("requireAuth")>]
        member _.RequireAuth(spec: ResourceSpec) : ResourceSpec =
            let config = AuthConfig.empty |> AuthConfig.addRequirement AuthRequirement.Authenticated
            EndpointAuth.applyAuth config spec

        [<CustomOperation("requireClaim")>]
        member _.RequireClaim(spec: ResourceSpec, claimType: string, claimValue: string) : ResourceSpec =
            let config = AuthConfig.empty |> AuthConfig.addRequirement (AuthRequirement.Claim(claimType, [ claimValue ]))
            EndpointAuth.applyAuth config spec

        member _.RequireClaim(spec: ResourceSpec, claimType: string, claimValues: string list) : ResourceSpec =
            let config = AuthConfig.empty |> AuthConfig.addRequirement (AuthRequirement.Claim(claimType, claimValues))
            EndpointAuth.applyAuth config spec

        [<CustomOperation("requireRole")>]
        member _.RequireRole(spec: ResourceSpec, role: string) : ResourceSpec =
            let config = AuthConfig.empty |> AuthConfig.addRequirement (AuthRequirement.Role role)
            EndpointAuth.applyAuth config spec

        [<CustomOperation("requirePolicy")>]
        member _.RequirePolicy(spec: ResourceSpec, policyName: string) : ResourceSpec =
            let config = AuthConfig.empty |> AuthConfig.addRequirement (AuthRequirement.Policy policyName)
            EndpointAuth.applyAuth config spec
