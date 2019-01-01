namespace Test

open System.Collections.Generic
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
open Test.ApplicationBuilderExtensions
open Test.Builder

type HelloLinkMiddleware (next:RequestDelegate, linkGenerator:LinkGenerator) =
    member this.InvokeAsync(httpContext:HttpContext) =
        task {
            let path = linkGenerator.GetPathByRouteValues(httpContext=httpContext, routeName="Hello", values=null)
            let url = linkGenerator.GetUriByName(httpContext=httpContext, endpointName="Hello", values=null)
            httpContext.Response.ContentType <- "text/plain"
            return! httpContext.Response.WriteAsync(sprintf "Go to %s or %s." path url)
        }

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

        if (env.IsDevelopment()) then
            app.UseDeveloperExceptionPage() |> ignore
        else
            app.UseExceptionHandler("/Home/Error") |> ignore
            // The default HSTS value is 30 days. You may want to change this for production scenarios, see https://aka.ms/aspnetcore-hsts.
            app.UseHsts() |> ignore

        app.UseHttpsRedirection() |> ignore
        app.UseResponseCompression() |> ignore
        app.UseResponseCaching() |> ignore
        app.UseStaticFiles() |> ignore

        (*
        // hello resource
        let helloName = [|
            HttpMethods.Get, fun (ctx:HttpContext) ->
                let name = ctx.GetRouteValue("name") |> string
                ctx.Response.WriteAsync(sprintf "Hi, %s!" name)
            HttpMethods.Put, fun (ctx:HttpContext) ->
                let name = ctx.GetRouteValue("name") |> string
                ctx.Negotiate(201, name)
        |]
        app.UseResource(name="Hello Name", template="hello/{name}", handlers=helloName) |> ignore
        *)

        resource app "Hello Name" "hello/{name}" {
            get (fun ctx ->
                let name = ctx.GetRouteValue("name") |> string
                ctx.Response.WriteAsync(sprintf "Hi, %s!" name))
            put (fun ctx ->
                let name = ctx.GetRouteValue("name") |> string
                ctx.Negotiate(201, name))
        }

        (*
        let hello = [|
            HttpMethods.Get, fun (ctx:HttpContext) ->
                ctx.Response.WriteAsync("Hello, world!")
            HttpMethods.Post, fun (ctx:HttpContext) ->
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
                        do! ctx.Negotiate(201, json)
                    else
                        ctx.Response.StatusCode <- 500
                        do! ctx.Response.WriteAsync("Could not seek")
                } :> _
        |]
        app.UseResource(name="Hello", template="hello", handlers=hello) |> ignore
        *)

        resource app "Hello" "hello" {
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
                        do! ctx.Negotiate(201, json)
                    else
                        ctx.Response.StatusCode <- 500
                        do! ctx.Response.WriteAsync("Could not seek")
                })
        }

        app.UseMiddleware<HelloLinkMiddleware>() |> ignore

    member val Configuration : IConfiguration = null with get, set
