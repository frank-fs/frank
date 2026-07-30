namespace Frank.Auth

open Frank.Builder

[<AutoOpen>]
module ResourceBuilderExtensions =
    type ResourceBuilder with
        [<CustomOperation("requireAuth")>]
        member _.RequireAuth(spec: ResourceSpec) : ResourceSpec =
            EndpointAuth.applyAuth (AuthConfig.single AuthRequirement.Authenticated) spec

        [<CustomOperation("requireClaim")>]
        member _.RequireClaim(spec: ResourceSpec, claimType: string, claimValue: string) : ResourceSpec =
            EndpointAuth.applyAuth (AuthConfig.single (AuthRequirement.Claim(claimType, [ claimValue ]))) spec

        member _.RequireClaim(spec: ResourceSpec, claimType: string, claimValues: string list) : ResourceSpec =
            EndpointAuth.applyAuth (AuthConfig.single (AuthRequirement.Claim(claimType, claimValues))) spec

        [<CustomOperation("requireRole")>]
        member _.RequireRole(spec: ResourceSpec, role: string) : ResourceSpec =
            EndpointAuth.applyAuth (AuthConfig.single (AuthRequirement.Role role)) spec

        [<CustomOperation("requirePolicy")>]
        member _.RequirePolicy(spec: ResourceSpec, policyName: string) : ResourceSpec =
            EndpointAuth.applyAuth (AuthConfig.single (AuthRequirement.Policy policyName)) spec
