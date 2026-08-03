namespace Frank.Alps

open System.Threading.Tasks
open Microsoft.AspNetCore.Authorization
open Microsoft.AspNetCore.Http
open Microsoft.Extensions.DependencyInjection

module AuthorizationFilter =
    let private authorizeData (endpoint: Endpoint) : IAuthorizeData list =
        endpoint.Metadata.GetOrderedMetadata<IAuthorizeData>() |> List.ofSeq

    let private policies (endpoint: Endpoint) : AuthorizationPolicy list =
        endpoint.Metadata.GetOrderedMetadata<AuthorizationPolicy>() |> List.ofSeq

    let private isAnonymous (endpoint: Endpoint) : bool =
        not (isNull (endpoint.Metadata.GetMetadata<IAllowAnonymous>()))

    let private hasAuthorizationMetadata (endpoint: Endpoint) : bool =
        not (List.isEmpty (authorizeData endpoint)) || not (List.isEmpty (policies endpoint))

    let varies (pairs: (Endpoint * Descriptor) list) : bool =
        pairs |> List.exists (fun (endpoint, _) -> hasAuthorizationMetadata endpoint)

    let private resolvePolicy (ctx: HttpContext) (data: IAuthorizeData list) (pols: AuthorizationPolicy list) =
        task {
            if List.isEmpty data then
                return AuthorizationPolicy.Combine(pols)
            else
                let provider = ctx.RequestServices.GetRequiredService<IAuthorizationPolicyProvider>()
                return! AuthorizationPolicy.CombineAsync(provider, data, pols)
        }

    let isAllowed (ctx: HttpContext) (endpoint: Endpoint) : Task<bool> =
        task {
            if isAnonymous endpoint then
                return true
            else
                let data = authorizeData endpoint
                let pols = policies endpoint

                if List.isEmpty data && List.isEmpty pols then
                    return true
                else
                    try
                        match! resolvePolicy ctx data pols with
                        | null -> return true
                        | policy ->
                            let service = ctx.RequestServices.GetRequiredService<IAuthorizationService>()
                            let! result = service.AuthorizeAsync(ctx.User, box endpoint, policy)
                            return result.Succeeded
                    with _ ->
                        // Fail closed: an evaluation error must never widen access.
                        return false
        }

    let filter (ctx: HttpContext) (pairs: (Endpoint * Descriptor) list) : Task<Descriptor list> =
        task {
            let kept = ResizeArray()

            for endpoint, descriptor in pairs do
                let! ok = isAllowed ctx endpoint
                if ok then kept.Add descriptor

            return List.ofSeq kept
        }
