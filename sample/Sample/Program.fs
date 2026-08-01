module Sample.Program

open System.IO
open System.Runtime.Serialization
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

// Without any DataContract attributes, `DataContractSerializer` still serializes this
// record fine (no 500, no empty element -- unlike `XmlSerializer`, it doesn't need a
// public parameterless constructor), but the XML element name comes out mangled as
// `Message_x0040_` -- the record's compiled backing-field name -- instead of `Message`.
// `[<CLIMutable>]` alone does NOT fix this (verified empirically: identical mangled
// output with or without it), since it only adds a public parameterless constructor
// and property setters, it doesn't change what name `DataContractSerializer` picks.
//
// The actual fix is `[<DataContract>]` on the type plus a field-targeted `DataMember`:
//
//   [<field: DataMember(Name = "Message")>]
//
// The `field:` target matters. Putting `[<DataMember(Name = "Message")>]` directly on
// the record field with no target attaches it to the compiler-generated property
// *getter*, which for an immutable F# record has no setter --
// `DataContractSerializer` throws `InvalidDataContractException: No set method for
// property 'Message'` at serialize time (worse than the mangled-name bug this
// replaces). Targeting the backing field instead sidesteps the missing setter
// entirely, so this works without `[<CLIMutable>]`. (An untargeted `DataMember` does
// work if the type also has `[<CLIMutable>]`, since that adds the missing setter --
// either combination is valid; this one keeps the type immutable.)
//
// Also note: `[<DataContract>]` with no `[<DataMember>]` anywhere is a different trap
// -- it switches `DataContractSerializer` to opt-in mode, and since nothing would be
// opted in, it silently produces an empty `<Greeting/>`.
[<DataContract>]
type Greeting = { [<field: DataMember(Name = "Message")>] Message: string }

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
