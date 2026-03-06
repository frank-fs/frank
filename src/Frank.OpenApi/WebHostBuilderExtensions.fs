namespace Frank.OpenApi

open System.Threading.Tasks
open Microsoft.AspNetCore.Builder
open Microsoft.AspNetCore.Http
open Microsoft.AspNetCore.OpenApi
open Microsoft.AspNetCore.Routing
open Microsoft.Extensions.DependencyInjection
open FSharp.Data.JsonSchema.OpenApi
open Frank.Builder
open Scalar.AspNetCore

[<AutoOpen>]
module WebHostBuilderExtensions =

    let private configureOpenApiDefaults (options: OpenApiOptions) =
        options.AddSchemaTransformer(FSharpSchemaTransformer()) |> ignore

        options.AddOperationTransformer(fun operation context _ ->
            // R8: Auto-map DisplayName to operationId as fallback
            if System.String.IsNullOrEmpty operation.OperationId then
                let endpoint =
                    context.Description.ActionDescriptor.EndpointMetadata
                    |> Seq.tryPick (fun m ->
                        match m with
                        | :? EndpointNameMetadata as enm -> Some enm.EndpointName
                        | _ -> None)

                match endpoint with
                | Some _ -> () // Already has an explicit EndpointNameMetadata, OpenAPI will use it
                | None ->
                    // Derive operationId from DisplayName (which carries ResourceSpec.Name)
                    let displayName = context.Description.ActionDescriptor.DisplayName

                    if not (System.String.IsNullOrEmpty displayName) then
                        // DisplayName format is "GET Products" or "POST Products"
                        let parts = displayName.Split(' ', 2)

                        if parts.Length = 2 && not (parts[1].StartsWith("/")) then
                            let httpMethod = parts[0].ToLowerInvariant()
                            let name = parts[1].Replace(" ", "")
                            operation.OperationId <- httpMethod + name

            Task.CompletedTask)
        |> ignore

    type WebHostBuilder with
        [<CustomOperation("useOpenApi")>]
        member _.UseOpenApi(spec: WebHostSpec) : WebHostSpec =
            { spec with
                Services =
                    spec.Services
                    >> fun services ->
                        services.AddOpenApi(fun options -> configureOpenApiDefaults options) |> ignore
                        services
                Middleware =
                    spec.Middleware
                    >> fun app ->
                        app.UseEndpoints(fun endpoints ->
                            endpoints.MapOpenApi() |> ignore
                            endpoints.MapScalarApiReference() |> ignore)
                        |> ignore

                        app }

        [<CustomOperation("useOpenApi")>]
        member _.UseOpenApi(spec: WebHostSpec, configure: OpenApiOptions -> unit) : WebHostSpec =
            { spec with
                Services =
                    spec.Services
                    >> fun services ->
                        services.AddOpenApi(fun options -> configure options) |> ignore
                        services
                Middleware =
                    spec.Middleware
                    >> fun app ->
                        app.UseEndpoints(fun endpoints ->
                            endpoints.MapOpenApi() |> ignore
                            endpoints.MapScalarApiReference() |> ignore)
                        |> ignore

                        app }
