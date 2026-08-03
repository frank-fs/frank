namespace Frank.Alps

open System
open System.Threading.Tasks
open Microsoft.AspNetCore.Builder
open Microsoft.AspNetCore.Hosting
open Microsoft.AspNetCore.Http
open Microsoft.AspNetCore.Routing
open Microsoft.Extensions.DependencyInjection
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

    /// Validates every registered resource's bound transitions during host startup -- after
    /// routing has fully built every endpoint, and before the app accepts its first request.
    ///
    /// This is an IStartupFilter, not an IHostedService: an IHostedService's StartAsync runs
    /// BEFORE the host's Configure delegate (the one that calls app.UseEndpoints(...) and
    /// actually populates the EndpointDataSource) -- confirmed empirically with a standalone
    /// probe replicating src/Frank/WebHostBuilder.fs's exact
    /// Host.CreateDefaultBuilder(args).ConfigureWebHost(config) shape, where `config` does
    /// .ConfigureServices(...) then .Configure(fun app -> ... app.UseEndpoints(...) ...):
    /// ConfigureWebHost registers the GenericWebHostService that runs Configure strictly AFTER
    /// any IHostedService the app itself registered via spec.Services has already started.
    /// So an IHostedService-based validate call here would always see zero endpoints and
    /// silently pass, no matter how badly a descriptor's Type mismatched its bound method.
    ///
    /// IStartupFilter.Configure(next) instead runs as part of *building* the middleware
    /// pipeline: calling next.Invoke(app) first runs the rest of that pipeline-building chain
    /// (including the app's own Configure delegate and its app.UseEndpoints(...) call), so by
    /// the time this filter's own code runs after that call, the EndpointDataSource is fully
    /// populated -- and this still happens before the server starts accepting requests, so an
    /// exception here still fails host startup rather than surfacing on first request.
    /// (IHostApplicationLifetime.ApplicationStarted is not a substitute: it fires after the
    /// listener is already accepting connections, and hosting does not fail startup on an
    /// exception raised from an ApplicationStarted callback.)
    ///
    /// internal (not private): a `private` type nested in `module AlpsDocument` is visible only
    /// from within that module itself -- not from a sibling module, even one declared later in
    /// this same file (verified empirically: F# raises FS0491 "The member or object constructor
    /// ... is not accessible" when a sibling module tries to construct a `private`-nested type).
    /// `WebHostBuilderExtensions.install` below needs to name this type as
    /// AddSingleton<IStartupFilter, 'T>'s type argument, and `internal` is the accessibility
    /// level that satisfies that -- by ordinary CLR/.NET semantics `internal` grants access from
    /// anywhere in the compiled Frank.Alps.dll assembly (not just this file); F# has no
    /// accessibility level narrower than that but broader than "this module", so `internal` is
    /// used here even though only one sibling module, in this one file, currently needs it. It
    /// still stays out of AlpsDocument.fsi, so it is never part of the assembly's public API.
    /// Its implicit primary constructor is still emitted public by the F# compiler regardless of
    /// the type's own internal accessibility (verified: `type internal Foo(x) = ...` yields a
    /// public ctor), so Microsoft.Extensions.DependencyInjection's activator (which only looks
    /// at public constructors) can still construct it.
    type internal ValidationStartupFilter() =
        interface IStartupFilter with
            member _.Configure(next: Action<IApplicationBuilder>) : Action<IApplicationBuilder> =
                Action<IApplicationBuilder>(fun app ->
                    next.Invoke(app)
                    EndpointSurface.allDescriptors app.ApplicationServices |> validate)

[<AutoOpen>]
module WebHostBuilderExtensions =
    let private install (options: AlpsOptions) (profile: Descriptor list) (spec: WebHostSpec) =
        let document = AlpsDocument.documentResource options profile

        { spec with
            Services =
                spec.Services
                >> fun services -> services.AddSingleton<IStartupFilter, AlpsDocument.ValidationStartupFilter>()
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
