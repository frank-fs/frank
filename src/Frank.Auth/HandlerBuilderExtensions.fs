namespace Frank.Auth

open Microsoft.AspNetCore.Authorization
open Frank.Builder

[<AutoOpen>]
module HandlerBuilderExtensions =
    type HandlerBuilder with
        [<CustomOperation("requireAuth")>]
        member _.RequireAuth(def: HandlerDefinition) : HandlerDefinition =
            let config = AuthConfig.empty |> AuthConfig.addRequirement AuthRequirement.Authenticated
            EndpointAuth.applyAuthToHandler config def

        [<CustomOperation("requireClaim")>]
        member _.RequireClaim(def: HandlerDefinition, claimType: string, claimValue: string) : HandlerDefinition =
            let config = AuthConfig.empty |> AuthConfig.addRequirement (AuthRequirement.Claim(claimType, [ claimValue ]))
            EndpointAuth.applyAuthToHandler config def

        member _.RequireClaim(def: HandlerDefinition, claimType: string, claimValues: string list) : HandlerDefinition =
            let config = AuthConfig.empty |> AuthConfig.addRequirement (AuthRequirement.Claim(claimType, claimValues))
            EndpointAuth.applyAuthToHandler config def

        [<CustomOperation("requireRole")>]
        member _.RequireRole(def: HandlerDefinition, role: string) : HandlerDefinition =
            let config = AuthConfig.empty |> AuthConfig.addRequirement (AuthRequirement.Role role)
            EndpointAuth.applyAuthToHandler config def

        [<CustomOperation("requirePolicy")>]
        member _.RequirePolicy(def: HandlerDefinition, policyName: string) : HandlerDefinition =
            let config = AuthConfig.empty |> AuthConfig.addRequirement (AuthRequirement.Policy policyName)
            EndpointAuth.applyAuthToHandler config def

        [<CustomOperation("allowAnonymous")>]
        member _.AllowAnonymous(def: HandlerDefinition) : HandlerDefinition =
            HandlerDefinition.addMetadata (AllowAnonymousAttribute()) def
