module Sample.Program

open System.Text
open Microsoft.AspNetCore.Builder
open Microsoft.AspNetCore.Http
open Microsoft.AspNetCore.Routing
open Microsoft.Extensions.DependencyInjection
open Microsoft.Extensions.Logging
open FSharp.Control.Tasks.V2.ContextInsensitive
open Frank
open Frank.Builder
open Newtonsoft.Json.Linq
open Sample.Extensions

let home =
    resource "/" {
        name "Home"

        get (fun (ctx:HttpContext) ->
            ctx.Response.WriteAsync("Welcome!"))
    }

let helloName =
    resource "hello/{name}" {
        name "Hello Name"

        get (fun (ctx:HttpContext) ->
            let name = ctx.GetRouteValue("name") |> string
            ctx.Response.WriteAsync(sprintf "Hi, %s!" name))

        put (fun (ctx:HttpContext) ->
            let name = ctx.GetRouteValue("name") |> string
            ContentNegotiation.negotiate 201 name ctx)
    }

let hello =
    resource "hello" {
        name "Hello"

        // Using HttpContext -> () overload
        get (fun (ctx:HttpContext) ->
            ctx.Response.WriteAsync("Hello, world!"))

        // Using HttpContext -> Task<'a> overload
        post (fun (ctx:HttpContext) ->
            task {
                ctx.Request.EnableBuffering()
                if ctx.Request.HasFormContentType then
                    let! form = ctx.Request.ReadFormAsync()
                    ctx.Response.StatusCode <- 201
                    use writer = new System.IO.StreamWriter(ctx.Response.Body, encoding=Encoding.UTF8, leaveOpen=true)
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

[<EntryPoint>]
let main args =
    webHost args {
        useDefaults

        logging (fun options-> options.AddConsole().AddDebug())

        service (fun services -> services.AddResponseCompression().AddResponseCaching())

        useContentNegotiation

        plugWhen isDevelopment DeveloperExceptionPageExtensions.UseDeveloperExceptionPage
        // The default HSTS value is 30 days. You may want to change this for production scenarios, see https://aka.ms/aspnetcore-hsts.
        plugWhenNot isDevelopment HstsBuilderExtensions.UseHsts

        plug HttpsPolicyBuilderExtensions.UseHttpsRedirection
        plug ResponseCachingExtensions.UseResponseCaching
        plug ResponseCompressionBuilderExtensions.UseResponseCompression
        plug StaticFileExtensions.UseStaticFiles

        resource home
        resource helloName
        resource hello
    }

    0
