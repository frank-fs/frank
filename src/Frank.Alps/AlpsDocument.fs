namespace Frank.Alps

open System.Threading
open System.Threading.Tasks
open Microsoft.AspNetCore.Http
open Microsoft.AspNetCore.Routing
open Microsoft.Extensions.DependencyInjection
open Microsoft.Extensions.Hosting
open Frank.Builder

type AlpsOptions =
    { Path: string
      Rel: string }

    static member Default =
        { Path = "/.well-known/alps.json"
          Rel = "profile" }

module AlpsDocument =

    [<Literal>]
    let MediaType = "application/alps+json"

    let private validMethods (t: DescriptorType) : string list =
        match t with
        | DescriptorType.Safe -> [ "GET"; "HEAD" ]
        | DescriptorType.Idempotent -> [ "PUT"; "DELETE" ]
        | DescriptorType.Unsafe -> [ "POST" ]
        | DescriptorType.Semantic -> []

    let validate (pairs: (Endpoint * Descriptor) list) : unit =
        for endpoint, descriptor in pairs do
            let allowed = validMethods descriptor.Type

            if not (List.isEmpty allowed) then
                let actual =
                    match endpoint.Metadata.GetMetadata<HttpMethodMetadata>() with
                    | null -> []
                    | m -> m.HttpMethods |> List.ofSeq

                let ok = not (List.isEmpty actual) && actual |> List.forall (fun m -> List.contains m allowed)

                if not ok then
                    failwithf
                        "Frank.Alps: descriptor '%s' (%A) is bound to HTTP method(s) %A, expected one of %A"
                        descriptor.Id
                        descriptor.Type
                        actual
                        allowed

    let private documentHandler (profile: Descriptor list) (ctx: HttpContext) : Task =
        task {
            let pairs =
                EndpointSurface.allDescriptors ctx.RequestServices
                |> List.filter (fun (_, d) -> profile |> List.exists (fun p -> p.Id = d.Id))

            let! allowed = AuthorizationFilter.filter ctx pairs
            let allowedIds = allowed |> List.map (fun d -> d.Id) |> Set.ofList

            let served =
                profile
                |> List.filter (fun d -> d.Type = DescriptorType.Semantic || Set.contains d.Id allowedIds)

            if AuthorizationFilter.varies pairs then
                // A shared cache must never serve one principal's view to another. Mirrors
                // src/Frank.JsonHome/JsonHome.fs's own documentHandler verbatim.
                ctx.Response.Headers.CacheControl <- "private, no-cache"
                ctx.Response.Headers.Vary <- "Authorization"

            ctx.Response.ContentType <- MediaType
            do! ctx.Response.WriteAsync(Serialization.toJson served)
        }

    /// Wraps the private documentHandler into a Resource -- the same shape as
    /// src/Frank.JsonHome/JsonHome.fs's own documentResource. documentHandler itself stays
    /// private (an implementation detail); documentResource is the public surface
    /// WebHostBuilderExtensions.install (a different module, later in this same file) calls to
    /// splice the resource's Endpoints into the WebHostSpec.
    let documentResource (options: AlpsOptions) (profile: Descriptor list) : Resource =
        // "options" would collide with ResourceBuilder's own OPTIONS custom operation if
        // referenced as a bare identifier inside the computation expression below, hence the
        // rebinding before entering it -- same reasoning as JsonHome.documentResource.
        let alpsOptions = options
        resource alpsOptions.Path { get (RequestDelegate(documentHandler profile)) }

    /// Resolves the DI-registered EndpointDataSource and validates every registered resource's
    /// bound transitions during host startup -- after routing has fully built every endpoint
    /// regardless of webHost {} block order, and before the app accepts its first request.
    /// internal (not private): WebHostBuilderExtensions.install, a sibling module in this same
    /// file, needs to name this type as AddHostedService<'T>'s type argument; internal is visible
    /// assembly-wide (including that sibling module) while staying out of AlpsDocument.fsi, so it
    /// never becomes part of the public API. Its implicit primary constructor is still emitted
    /// public by the F# compiler regardless of the type's own internal accessibility (verified:
    /// `type internal Foo(x) = ...` yields a public ctor), so
    /// Microsoft.Extensions.DependencyInjection's activator (which only looks at public
    /// constructors) can still construct it.
    type internal ValidationHostedService(services: System.IServiceProvider) =
        interface IHostedService with
            member _.StartAsync(_: CancellationToken) : Task =
                EndpointSurface.allDescriptors services |> validate
                Task.CompletedTask

            member _.StopAsync(_: CancellationToken) : Task = Task.CompletedTask

[<AutoOpen>]
module WebHostBuilderExtensions =
    let private install (options: AlpsOptions) (profile: Descriptor list) (spec: WebHostSpec) =
        let document = AlpsDocument.documentResource options profile

        { spec with
            Services =
                spec.Services
                >> fun services -> services.AddHostedService<AlpsDocument.ValidationHostedService>()
            LinkProviders =
                spec.LinkProviders
                @ [ fun (_: HttpContext) -> Seq.singleton { Target = options.Path; Rel = options.Rel; Params = [] } ]
            // Dispatched through the app's own, single, structurally-last UseEndpoints(...) call in
            // WebHostBuilder.Run -- after every Middleware-composed stage, including
            // useAuthentication and useAuthorization, regardless of where useAlps sits in the
            // webHost {} block. AuthorizationFilter.filter reads ctx.User, and that must already
            // reflect the real principal by the time it runs.
            Endpoints = Array.append spec.Endpoints document.Endpoints }

    type WebHostBuilder with

        [<CustomOperation("useAlps")>]
        member _.UseAlps(spec: WebHostSpec, profile: Descriptor list) : WebHostSpec =
            install AlpsOptions.Default profile spec

        [<CustomOperation("useAlps")>]
        member _.UseAlps(spec: WebHostSpec, profile: Descriptor list, configure: AlpsOptions -> AlpsOptions) : WebHostSpec =
            install (configure AlpsOptions.Default) profile spec
