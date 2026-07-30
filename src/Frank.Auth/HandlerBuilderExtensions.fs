namespace Frank.Auth

open Microsoft.AspNetCore.Authorization
open Frank.Builder

[<AutoOpen>]
module HandlerBuilderExtensions =
    type HandlerBuilder with
        [<CustomOperation("requireAuth")>]
        member _.RequireAuth(def: HandlerDefinition) : HandlerDefinition =
            EndpointAuth.applyAuthToHandler (AuthConfig.single AuthRequirement.Authenticated) def

        [<CustomOperation("requireClaim")>]
        member _.RequireClaim(def: HandlerDefinition, claimType: string, claimValue: string) : HandlerDefinition =
            EndpointAuth.applyAuthToHandler (AuthConfig.single (AuthRequirement.Claim(claimType, [ claimValue ]))) def

        member _.RequireClaim(def: HandlerDefinition, claimType: string, claimValues: string list) : HandlerDefinition =
            EndpointAuth.applyAuthToHandler (AuthConfig.single (AuthRequirement.Claim(claimType, claimValues))) def

        [<CustomOperation("requireRole")>]
        member _.RequireRole(def: HandlerDefinition, role: string) : HandlerDefinition =
            EndpointAuth.applyAuthToHandler (AuthConfig.single (AuthRequirement.Role role)) def

        [<CustomOperation("requirePolicy")>]
        member _.RequirePolicy(def: HandlerDefinition, policyName: string) : HandlerDefinition =
            EndpointAuth.applyAuthToHandler (AuthConfig.single (AuthRequirement.Policy policyName)) def

        [<CustomOperation("allowAnonymous")>]
        member _.AllowAnonymous(def: HandlerDefinition) : HandlerDefinition =
            HandlerDefinition.addMetadata (AllowAnonymousAttribute()) def
