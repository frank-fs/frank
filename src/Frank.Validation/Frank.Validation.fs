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
            let asm = System.Reflection.Assembly.GetEntryAssembly()

            let shapesGraph =
                if isNull asm then
                    new ShapesGraph(new VDS.RDF.Graph())
                else
                    match asm.GetType("GeneratedValidation") with
                    | null -> new ShapesGraph(new VDS.RDF.Graph())
                    | t ->
                        let prop = t.GetProperty("shapesGraph")

                        if isNull prop then
                            new ShapesGraph(new VDS.RDF.Graph())
                        else
                            prop.GetValue(null) :?> ShapesGraph

            this.UseValidationWith(spec, shapesGraph)
