namespace Frank.JsonHome

open Microsoft.AspNetCore.Authorization
open Microsoft.AspNetCore.Http
open Microsoft.Extensions.DependencyInjection

module AuthorizationFilter =

    let private authorizeData (metadata: obj list) =
        metadata
        |> List.choose (fun m ->
            match m with
            | :? IAuthorizeData as d -> Some d
            | _ -> None)

    let private policies (metadata: obj list) =
        metadata
        |> List.choose (fun m ->
            match m with
            | :? AuthorizationPolicy as p -> Some p
            | _ -> None)

    let private isAnonymous (metadata: obj list) =
        metadata |> List.exists (fun m -> m :? IAllowAnonymous)

    let private hasAuthorizationMetadata (metadata: obj list) =
        metadata |> List.exists (fun m -> m :? IAuthorizeData || m :? AuthorizationPolicy)

    let varies (resources: ResourceDescription list) =
        resources |> List.exists (fun r -> hasAuthorizationMetadata r.Metadata)

    let private resolvePolicy (ctx: HttpContext) (data: IAuthorizeData list) (pols: AuthorizationPolicy list) =
        task {
            if List.isEmpty data then
                // Nothing to resolve via the policy provider -- pols is
                // guaranteed non-empty here (isMethodAllowed already
                // short-circuits when both are empty), so this is just a
                // synchronous combine of the explicit policies, same as
                // AuthorizationPolicy.CombineAsync would produce with no
                // IAuthorizeData to fold in, without the provider round-trip.
                return AuthorizationPolicy.Combine(pols)
            else
                let provider = ctx.RequestServices.GetRequiredService<IAuthorizationPolicyProvider>()
                return! AuthorizationPolicy.CombineAsync(provider, data, pols)
        }

    let private isMethodAllowed (ctx: HttpContext) (metadata: obj list) =
        task {
            if isAnonymous metadata then
                return true
            else
                let data = authorizeData metadata
                let pols = policies metadata

                if List.isEmpty data && List.isEmpty pols then
                    return true
                else
                    try
                        match! resolvePolicy ctx data pols with
                        | null -> return true
                        | policy ->
                            let service = ctx.RequestServices.GetRequiredService<IAuthorizationService>()
                            let! result = service.AuthorizeAsync(ctx.User, box metadata, policy)
                            return result.Succeeded
                    with _ ->
                        // Fail closed: an evaluation error must never widen access.
                        return false
        }

    let private allowedMethods (ctx: HttpContext) (resource: ResourceDescription) =
        task {
            let allowed = ResizeArray()

            for httpMethod, metadata in resource.MethodMetadata do
                let! ok = isMethodAllowed ctx metadata
                if ok then allowed.Add httpMethod

            return Set.ofSeq allowed
        }

    let private filterResource (ctx: HttpContext) (resource: ResourceDescription) =
        task {
            let! allowed = allowedMethods ctx resource
            let methods = resource.Methods |> List.filter (fun m -> Set.contains m allowed)

            if List.isEmpty methods then
                return None
            else
                return
                    Some
                        { resource with
                            Methods = methods
                            Accepts = resource.Accepts |> List.filter (fun (m, _) -> Set.contains m allowed)
                            Formats = if Set.contains "GET" allowed then resource.Formats else [] }
        }

    let apply (ctx: HttpContext) (resources: ResourceDescription list) =
        task {
            let kept = ResizeArray()

            for resource in resources do
                match! filterResource ctx resource with
                | Some filtered -> kept.Add filtered
                | None -> ()

            return List.ofSeq kept
        }
