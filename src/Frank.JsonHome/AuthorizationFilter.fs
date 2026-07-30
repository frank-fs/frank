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

    let varies (resources: ResourceDescription list) =
        resources |> List.exists (fun r -> not (List.isEmpty (authorizeData r.Metadata)))

    let private resolvePolicy (ctx: HttpContext) (metadata: obj list) =
        task {
            match policies metadata with
            | [] ->
                let provider = ctx.RequestServices.GetRequiredService<IAuthorizationPolicyProvider>()
                return! AuthorizationPolicy.CombineAsync(provider, authorizeData metadata)
            | explicitPolicies -> return AuthorizationPolicy.Combine(explicitPolicies)
        }

    let private isMethodAllowed (ctx: HttpContext) (metadata: obj list) =
        task {
            if isAnonymous metadata then
                return true
            elif List.isEmpty (authorizeData metadata) then
                return true
            else
                try
                    match! resolvePolicy ctx metadata with
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
