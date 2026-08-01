module Sample.Program

open System.IO
open System.Text
open System.Text.Json
open Microsoft.AspNetCore.Builder
open Microsoft.AspNetCore.Http
open Microsoft.AspNetCore.Routing
open Microsoft.AspNetCore.Routing.Internal
open Microsoft.Extensions.DependencyInjection
open Microsoft.Extensions.Logging
open Frank
open Frank.Builder
open Sample.Extensions

let home =
    resource "/" {
        name "Home"

        get (fun (ctx: HttpContext) -> ctx.Response.WriteAsync("Welcome!"))
    }

let helloName =
    resource "hello/{name}" {
        name "Hello Name"

        get (fun (ctx: HttpContext) ->
            let name = ctx.GetRouteValue("name") |> string
            ctx.Response.WriteAsync(sprintf "Hi, %s!" name))

        put (negotiate {
            accepts [ "application/json"; "application/xml" ] (fun (ctx: HttpContext) -> task {
                let name = ctx.GetRouteValue("name") |> string
                ctx.Response.StatusCode <- 201
                return name
            })
        })
    }

let hello =
    resource "hello" {
        name "Hello"

        // Using HttpContext -> () overload
        get (fun (ctx: HttpContext) -> ctx.Response.WriteAsync("Hello, world!"))

        // Using HttpContext -> Task<'a> overload
        post (fun (ctx: HttpContext) ->
            task {
                ctx.Request.EnableBuffering()

                if ctx.Request.HasFormContentType then
                    let! form = ctx.Request.ReadFormAsync()
                    ctx.Response.StatusCode <- 201

                    use writer =
                        new System.IO.StreamWriter(ctx.Response.Body, encoding = Encoding.UTF8, leaveOpen = true)

                    do! writer.WriteLineAsync("Received form data:")

                    for KeyValue(key, value) in form do
                        do! writer.WriteLineAsync(sprintf "%s: %A" key (value.ToArray()))

                    do! writer.FlushAsync()
                elif ctx.Request.ContentType = "application/json" then
                    ctx.Request.Body.Seek(0L, System.IO.SeekOrigin.Begin) |> ignore
                    use reader = new System.IO.StreamReader(ctx.Request.Body)
                    let! input = reader.ReadToEndAsync()
                    let json = JsonSerializer.Deserialize input
                    // NOT migrated to `negotiate { }` -- attempted and reverted. Giving `json`
                    // an explicit `JsonElement` type argument (required for `accepts`' generic
                    // inference; the original bare `Deserialize input` infers `obj`) and routing
                    // both representations through `negotiate { accepts [...] }` works for
                    // "application/json", but "application/xml" via
                    // `AddXmlDataContractSerializerFormatters()` writes an empty `<JsonElement />`
                    // element -- no actual data -- because `JsonElement`'s public shape doesn't
                    // expose its underlying data as DataContract-visible members. That's a
                    // genuine, empirically-verified `DataContractSerializer`/`JsonElement`
                    // limitation, not a Frank bug. (For context: the untouched code below,
                    // calling `ContentNegotiation.negotiate` with `json` inferred as `obj`, is
                    // WORSE for this same Accept header -- it throws a 500, since
                    // `DataContractSerializer` requires the declared type to match the runtime
                    // type for polymorphic values, and `obj` isn't a known type for
                    // `JsonElement`. So `application/xml` was already broken here before this
                    // task; not fixed now, since a correct fix needs a hand-authored wire DTO,
                    // which is exactly the "contorted fit to force migration" this task says not
                    // to do for this branch. `application/json` -- the case that actually
                    // matters for this echo demo -- works correctly either way.)
                    do! ContentNegotiation.negotiate 201 json ctx
                else
                    ctx.Response.StatusCode <- 500
                    do! ctx.Response.WriteAsync("Could not seek")
            })
    }

let graph =
    resource "graph" {
        name "Graph"

        get (fun (ctx: HttpContext) ->
            let graphWriter = ctx.RequestServices.GetRequiredService<DfaGraphWriter>()

            let endpointDataSource =
                ctx.RequestServices.GetRequiredService<EndpointDataSource>()

            use sw = new StringWriter()
            graphWriter.Write(endpointDataSource, sw)
            ctx.Response.WriteAsync(sw.ToString()))
    }

[<EntryPoint>]
let main args =
    webHost args {
        useDefaults

        logging (fun options -> options.AddConsole().AddDebug())

        service (fun services -> services.AddResponseCompression().AddResponseCaching())

        // The new `negotiate { }`/`viaOutputFormatter` path (helloName's PUT) goes
        // through MVC's OutputFormatterSelector, which for "application/json" resolves
        // SystemTextJsonOutputFormatter -- that formatter
        // reads Microsoft.AspNetCore.Mvc.JsonOptions.JsonSerializerOptions, NOT
        // Microsoft.AspNetCore.Http.Json.JsonOptions (the minimal-API options type used
        // by WriteAsJsonAsync/ReadFromJsonAsync). Registering JsonFSharpConverter on the
        // wrong JsonOptions type would silently do nothing for this path -- see
        // Frank.OpenApi.Sample/Program.fs's CategoryJsonConverter doc comment for the
        // same mixup. This project has no DU/option-bearing types of its own today, so
        // this doesn't visibly change behavior yet -- it's still the correct
        // infrastructure to have in place for when it does.
        service (fun (services: IServiceCollection) ->
            services.Configure<Microsoft.AspNetCore.Mvc.JsonOptions>(fun (options: Microsoft.AspNetCore.Mvc.JsonOptions) ->
                options.JsonSerializerOptions.Converters.Add(
                    System.Text.Json.Serialization.JsonFSharpConverter()))
            |> ignore
            services)

        useContentNegotiation

        plugBeforeRoutingWhen isDevelopment DeveloperExceptionPageExtensions.UseDeveloperExceptionPage
        // The default HSTS value is 30 days. You may want to change this for production scenarios, see https://aka.ms/aspnetcore-hsts.
        plugBeforeRoutingWhenNot isDevelopment HstsBuilderExtensions.UseHsts

        plugBeforeRouting HttpsPolicyBuilderExtensions.UseHttpsRedirection
        plugBeforeRouting ResponseCachingExtensions.UseResponseCaching
        plugBeforeRouting ResponseCompressionBuilderExtensions.UseResponseCompression
        plugBeforeRouting StaticFileExtensions.UseStaticFiles

        resource home
        resource helloName
        resource hello
        resource graph
    }

    0
