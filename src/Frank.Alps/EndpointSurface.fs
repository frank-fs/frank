namespace Frank.Alps

open System
open Microsoft.AspNetCore.Http
open Microsoft.AspNetCore.Routing
open Microsoft.Extensions.DependencyInjection

module EndpointSurface =
    let allDescriptors (services: IServiceProvider) : (Endpoint * Descriptor) list =
        let dataSource = services.GetRequiredService<EndpointDataSource>()

        [ for endpoint in dataSource.Endpoints do
              for descriptor in endpoint.Metadata.GetOrderedMetadata<Descriptor>() do
                  yield endpoint, descriptor ]

    let descriptorsForRoute (services: IServiceProvider) (routePattern: string) : (Endpoint * Descriptor) list =
        allDescriptors services
        |> List.filter (fun (endpoint, _) ->
            match endpoint with
            | :? RouteEndpoint as re -> re.RoutePattern.RawText = routePattern
            | _ -> false)
