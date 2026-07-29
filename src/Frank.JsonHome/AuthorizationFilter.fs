namespace Frank.JsonHome

open Microsoft.AspNetCore.Authorization
open Microsoft.AspNetCore.Http
open Microsoft.Extensions.DependencyInjection

module AuthorizationFilter =

    let private authorizeData (resource: ResourceDescription) =
        resource.Metadata
        |> List.choose (fun m ->
            match m with
            | :? IAuthorizeData as d -> Some d
            | _ -> None)

    let private policies (resource: ResourceDescription) =
        resource.Metadata
        |> List.choose (fun m ->
            match m with
            | :? AuthorizationPolicy as p -> Some p
            | _ -> None)

    let varies (resources: ResourceDescription list) =
        resources |> List.exists (fun r -> not (List.isEmpty (authorizeData r)))

    let private resolvePolicy (ctx: HttpContext) (resource: ResourceDescription) =
        task {
            match policies resource with
            | [] ->
                let provider = ctx.RequestServices.GetRequiredService<IAuthorizationPolicyProvider>()
                return! AuthorizationPolicy.CombineAsync(provider, authorizeData resource)
            | explicitPolicies -> return AuthorizationPolicy.Combine(explicitPolicies)
        }

    let private isAllowed (ctx: HttpContext) (resource: ResourceDescription) =
        task {
            if List.isEmpty (authorizeData resource) then
                return true
            else
                try
                    match! resolvePolicy ctx resource with
                    | null -> return true
                    | policy ->
                        let service = ctx.RequestServices.GetRequiredService<IAuthorizationService>()
                        let! result = service.AuthorizeAsync(ctx.User, box resource, policy)
                        return result.Succeeded
                with _ ->
                    // Fail closed: an evaluation error must never widen access.
                    return false
        }

    let apply (ctx: HttpContext) (resources: ResourceDescription list) =
        task {
            let kept = ResizeArray()

            for resource in resources do
                let! allowed = isAllowed ctx resource
                if allowed then kept.Add resource

            return List.ofSeq kept
        }
