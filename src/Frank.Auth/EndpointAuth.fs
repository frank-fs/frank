namespace Frank.Auth

open Microsoft.AspNetCore.Authorization
open Microsoft.AspNetCore.Builder
open Frank.Builder

module EndpointAuth =
    let private toConvention (requirement: AuthRequirement) : EndpointBuilder -> unit =
        match requirement with
        | AuthRequirement.Authenticated -> fun b -> b.Metadata.Add(AuthorizeAttribute())
        | AuthRequirement.Claim(claimType, claimValues) ->
            fun b ->
                b.Metadata.Add(AuthorizeAttribute())

                let policy =
                    let pb = AuthorizationPolicyBuilder()

                    if claimValues |> List.isEmpty then
                        pb.RequireClaim(claimType) |> ignore
                    else
                        pb.RequireClaim(claimType, claimValues |> List.toArray) |> ignore

                    pb.Build()

                b.Metadata.Add(policy)
        | AuthRequirement.Role name ->
            fun b ->
                b.Metadata.Add(AuthorizeAttribute())

                let policy =
                    let pb = AuthorizationPolicyBuilder()
                    pb.RequireRole(name) |> ignore
                    pb.Build()

                b.Metadata.Add(policy)
        | AuthRequirement.Policy name -> fun b -> b.Metadata.Add(AuthorizeAttribute(name))

    let applyAuth (config: AuthConfig) (spec: ResourceSpec) : ResourceSpec =
        if AuthConfig.isEmpty config then
            spec
        else
            config.Requirements
            |> List.fold (fun s req -> ResourceBuilder.AddMetadata(s, toConvention req)) spec
