namespace Frank.Auth

open Microsoft.AspNetCore.Authorization
open Microsoft.AspNetCore.Builder
open Frank.Builder

module EndpointAuth =
    let toMetadataObjects (requirement: AuthRequirement) : obj list =
        match requirement with
        | AuthRequirement.Authenticated -> [ AuthorizeAttribute() ]
        | AuthRequirement.Claim(claimType, claimValues) ->
            let policy =
                let pb = AuthorizationPolicyBuilder()
                if claimValues |> List.isEmpty then
                    pb.RequireClaim(claimType) |> ignore
                else
                    pb.RequireClaim(claimType, claimValues |> List.toArray) |> ignore
                pb.Build()
            [ AuthorizeAttribute(); policy ]
        | AuthRequirement.Role name ->
            let policy =
                let pb = AuthorizationPolicyBuilder()
                pb.RequireRole(name) |> ignore
                pb.Build()
            [ AuthorizeAttribute(); policy ]
        | AuthRequirement.Policy name -> [ AuthorizeAttribute(name) ]

    let private toConvention (requirement: AuthRequirement) : EndpointBuilder -> unit =
        let metadataObjects = toMetadataObjects requirement
        fun b -> metadataObjects |> List.iter b.Metadata.Add

    let applyAuth (config: AuthConfig) (spec: ResourceSpec) : ResourceSpec =
        if AuthConfig.isEmpty config then
            spec
        else
            config.Requirements
            |> List.fold (fun s req -> ResourceBuilder.AddMetadata(s, toConvention req)) spec

    let applyAuthToHandler (config: AuthConfig) (def: HandlerDefinition) : HandlerDefinition =
        if AuthConfig.isEmpty config then
            def
        else
            config.Requirements
            |> List.collect toMetadataObjects
            |> List.fold (fun d m -> HandlerDefinition.addMetadata m d) def
