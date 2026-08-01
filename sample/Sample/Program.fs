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

// Deliberately a plain record, no [<CLIMutable>]: empirically, `DataContractSerializer`
// serializes this without it (no 500, no empty element -- unlike `XmlSerializer`,
// which needs a public parameterless constructor `DataContractSerializer` doesn't
// require). The XML element name it emits is mangled (`Message_x0040_`, from the
// record's compiled backing field) either with or without `[<CLIMutable>]`, so
// `CLIMutable` buys nothing here and is intentionally omitted.
type Greeting = { Message: string }

// System.Text.Json.JsonSerializer.Deserialize is case-sensitive by default, and F#
// record property names are PascalCase (`Message`) while this demo's example
// requests use lowercase JSON keys (`"message"`, matching the camelCase convention
// ASP.NET Core's own JSON output uses). Without this, deserializing
// `{"message": "..."}` into `Greeting` silently leaves `Message` as `null` --
// no exception, just a swallowed field -- which is easy to miss since it's not
// a crash. This is unrelated to `JsonFSharpConverter` (registered separately, only
// on `Mvc.JsonOptions` for the *output* formatter, not this manual *input* parse).
let private caseInsensitiveJsonOptions = JsonSerializerOptions(PropertyNameCaseInsensitive = true)

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
                    let greeting = JsonSerializer.Deserialize<Greeting>(input, caseInsensitiveJsonOptions)

                    let negotiated =
                        negotiate {
                            accepts [ "application/json"; "application/xml" ] (fun (ctx: HttpContext) -> task {
                                ctx.Response.StatusCode <- 201
                                return greeting
                            })
                        }

                    do! negotiated.Handler.Invoke(ctx)
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
