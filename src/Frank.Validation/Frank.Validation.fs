namespace Frank.Validation

open Microsoft.AspNetCore.Builder
open Microsoft.Extensions.DependencyInjection
open VDS.RDF.Shacl
open Frank.Builder
open Frank.Validation.ValidationMiddleware

[<AutoOpen>]
module ValidationExtensions =

    type WebHostBuilder with

        [<CustomOperation("useValidationWith")>]
        member _.UseValidationWith(spec: WebHostSpec, shapesGraph: ShapesGraph) : WebHostSpec =
            let addServices (services: IServiceCollection) =
                services.AddSingleton<ShapesGraph>(shapesGraph) |> ignore
                spec.Services(services)

            let addMiddleware (app: IApplicationBuilder) =
                let configured = spec.Middleware(app)
                configured.UseMiddleware<ValidationMiddleware>()

            { spec with
                Services = addServices
                Middleware = addMiddleware }

        [<CustomOperation("useValidation")>]
        member this.UseValidation(spec: WebHostSpec) : WebHostSpec =
            let generatedType =
                System.AppDomain.CurrentDomain.GetAssemblies()
                |> Array.tryPick (fun asm ->
                    if asm.IsDynamic then
                        None
                    else
                        match asm.GetType("GeneratedValidation") with
                        | null -> None
                        | t -> Some t)

            let shapesGraph =
                match generatedType with
                | None -> new ShapesGraph(new VDS.RDF.Graph())
                | Some t ->
                    match t.GetProperty("shapesGraph") with
                    | null -> new ShapesGraph(new VDS.RDF.Graph())
                    | prop -> prop.GetValue(null) :?> ShapesGraph

            this.UseValidationWith(spec, shapesGraph)
