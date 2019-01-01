namespace Test

open Microsoft.AspNetCore.Builder
open Microsoft.AspNetCore.Hosting
open Microsoft.AspNetCore.Http
open Microsoft.AspNetCore.Mvc
open Microsoft.AspNetCore.Routing
open Microsoft.Extensions.Configuration
open Microsoft.Extensions.DependencyInjection
open Microsoft.Extensions.Logging
open Newtonsoft.Json.Linq
open FSharp.Control.Tasks.V2.ContextInsensitive
open Test.ContentNegotiation
open Test.Builder

type Startup private () =
    new (configuration: IConfiguration) as this =
        Startup() then
        this.Configuration <- configuration

    // This method gets called by the runtime. Use this method to add services to the container.
    member this.ConfigureServices(services: IServiceCollection) =
        // Add framework services.
        services.AddLogging(fun options ->
            options.AddConsole() |> ignore
            options.AddDebug() |> ignore) |> ignore
        services.AddMvcCore(fun options -> options.ReturnHttpNotAcceptable <- true)
                .SetCompatibilityVersion(CompatibilityVersion.Version_2_2) |> ignore
        services.AddRouting() |> ignore
        services.AddResponseCompression() |> ignore
        services.AddResponseCaching() |> ignore
    
    // This method gets called by the runtime. Use this method to configure the HTTP request pipeline.
    member this.Configure(app: IApplicationBuilder, env: IHostingEnvironment) =

        let helloName =
            resource app {
                name "Hello Name"
                template "hello/{name}"

                get (fun ctx ->
                    let name = ctx.GetRouteValue("name") |> string
                    ctx.Response.WriteAsync(sprintf "Hi, %s!" name))

                put (fun ctx ->
                    let name = ctx.GetRouteValue("name") |> string
                    ctx.Negotiate(201, name))
            }

        let hello =
            resource app {
                name "Hello"
                template "hello"

                get (fun ctx ->
                    ctx.Response.WriteAsync("Hello, world!"))

                post (fun ctx ->
                    task {
                        ctx.Request.EnableBuffering()
                        if ctx.Request.HasFormContentType then
                            let! form = ctx.Request.ReadFormAsync()
                            ctx.Response.StatusCode <- 201
                            use writer = new System.IO.StreamWriter(ctx.Response.Body)
                            do! writer.WriteLineAsync("Received form data:")
                            for KeyValue(key, value) in form do
                                do! writer.WriteLineAsync(sprintf "%s: %A" key (value.ToArray()))
                            do! writer.FlushAsync()
                        elif ctx.Request.ContentType = "application/json" then
                            ctx.Request.Body.Seek(0L, System.IO.SeekOrigin.Begin) |> ignore
                            use reader = new System.IO.StreamReader(ctx.Request.Body)
                            let! input = reader.ReadToEndAsync()
                            let json = JObject.Parse input
                            do! ContentNegotiation.negotiate 201 json ctx
                        else
                            ctx.Response.StatusCode <- 500
                            do! ctx.Response.WriteAsync("Could not seek")
                    })
            }

        router app {
            plugWhen (env.IsDevelopment()) DeveloperExceptionPageExtensions.UseDeveloperExceptionPage
            // The default HSTS value is 30 days. You may want to change this for production scenarios, see https://aka.ms/aspnetcore-hsts.
            plugWhenNot (env.IsDevelopment()) HstsBuilderExtensions.UseHsts

            plug HttpsPolicyBuilderExtensions.UseHttpsRedirection
            plug ResponseCachingExtensions.UseResponseCaching
            plug ResponseCompressionBuilderExtensions.UseResponseCompression
            plug StaticFileExtensions.UseStaticFiles

            route helloName
            route hello
        }

    member val Configuration : IConfiguration = null with get, set
