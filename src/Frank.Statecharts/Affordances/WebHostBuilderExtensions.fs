namespace Frank.Affordances

open System
open System.Collections.Generic
open System.Reflection
open Microsoft.AspNetCore.Builder
open Microsoft.Extensions.DependencyInjection
open Microsoft.Extensions.DependencyInjection.Extensions
open Microsoft.Extensions.Logging
open Frank.Resources.Model
open Frank.Builder
open Frank.Discovery

[<AutoOpen>]
module WebHostBuilderExtensions =

    let private getAffordanceLogger (app: IApplicationBuilder) =
        app.ApplicationServices.GetRequiredService<ILoggerFactory>().CreateLogger<AffordanceMiddleware>()

    let private versionMismatchMessage =
        "Affordance map version '{MapVersion}' does not match expected version '{ExpectedVersion}'. Affordance headers may be incorrect."

    type WebHostBuilder with

        /// Register the affordance middleware with an explicit AffordanceMap.
        /// Registers the pre-computed dictionary as a DI singleton (FW-3).
        /// Middleware runs after routing (and after statechart middleware if present).
        [<CustomOperation("useAffordancesWith")>]
        member _.UseAffordancesWith(spec: WebHostSpec, map: AffordanceMap) : WebHostSpec =
            let preComputed = AffordancePreCompute.preCompute map

            if preComputed.Count = 0 then
                spec
            else
                let version = map.Version

                { spec with
                    Services =
                        spec.Services
                        >> fun services ->
                            services.AddSingleton<Dictionary<string, PreComputedAffordance>>(preComputed)
                            |> ignore

                            services
                    Middleware =
                        spec.Middleware
                        >> fun app ->
                            if version <> AffordanceMap.currentVersion then
                                (getAffordanceLogger app)
                                    .LogWarning(versionMismatchMessage, version, AffordanceMap.currentVersion)

                            app.UseMiddleware<AffordanceMiddleware>() |> ignore
                            app }

        /// Register the affordance middleware with an AffordanceMap generated at startup
        /// from an ExtractedStatechart. For CE-based apps that don't use CLI-generated model.bin.
        /// Transitions become role-scoped Link/Allow headers via AffordancePreCompute.
        [<CustomOperation("useRuntimeAffordances")>]
        member _.UseRuntimeAffordances(spec: WebHostSpec, chart: ExtractedStatechart, baseUri: string) : WebHostSpec =
            let map = AffordanceMap.fromStatechart baseUri chart
            let preComputed = AffordancePreCompute.preCompute map

            if preComputed.Count = 0 then
                spec
            else
                { spec with
                    Services =
                        spec.Services
                        >> fun services ->
                            services.AddSingleton<Dictionary<string, PreComputedAffordance>>(preComputed)
                            |> ignore

                            services
                    Middleware =
                        spec.Middleware
                        >> fun app ->
                            app.UseMiddleware<AffordanceMiddleware>() |> ignore
                            app }

        /// Auto-load the AffordanceMap from the entry assembly's embedded model.bin.
        /// Registers a DI factory that lazily loads from the assembly (FW-3).
        /// Falls back to empty dictionary when the entry assembly is null
        /// or model.bin is not found/readable. For multi-project solutions where
        /// model.bin lives in a library assembly, use useAffordancesWith.
        [<CustomOperation("useAffordances")>]
        member _.UseAffordances(spec: WebHostSpec) : WebHostSpec =
            { spec with
                Services =
                    spec.Services
                    >> fun services ->
                        services.TryAddSingleton<Dictionary<string, PreComputedAffordance>>(
                            Func<IServiceProvider, Dictionary<string, PreComputedAffordance>>(fun sp ->
                                let loggerFactory = sp.GetRequiredService<ILoggerFactory>()
                                let logger = loggerFactory.CreateLogger<AffordanceMiddleware>()

                                match Assembly.GetEntryAssembly() with
                                | null ->
                                    logger.LogWarning(
                                        "Assembly.GetEntryAssembly() returned null; cannot auto-load affordances. Use useAffordancesWith to supply an explicit map."
                                    )

                                    Dictionary<string, PreComputedAffordance>(StringComparer.Ordinal)
                                | assembly ->
                                    match StartupProjection.loadAffordanceMapFromAssembly logger assembly with
                                    | Some map ->
                                        logger.LogInformation(
                                            "Affordance map loaded from assembly '{AssemblyName}' ({EntryCount} entries).",
                                            assembly.GetName().Name,
                                            map.Entries.Length
                                        )

                                        if map.Version <> AffordanceMap.currentVersion then
                                            logger.LogWarning(
                                                versionMismatchMessage,
                                                map.Version,
                                                AffordanceMap.currentVersion
                                            )

                                        AffordancePreCompute.preCompute map
                                    | None ->
                                        logger.LogInformation(
                                            "model.bin not found or unreadable in assembly '{AssemblyName}'; affordances not loaded. Use useAffordancesWith to supply an explicit map.",
                                            assembly.GetName().Name
                                        )

                                        Dictionary<string, PreComputedAffordance>(StringComparer.Ordinal))
                        )
                        |> ignore

                        services
                Middleware =
                    spec.Middleware
                    >> fun app ->
                        app.UseMiddleware<AffordanceMiddleware>() |> ignore
                        app }

        /// Register the projected profile middleware with an explicit RuntimeState.
        /// Swaps the global ALPS profile Link header for a role-specific one when
        /// the authenticated user has resolved roles. Runs after affordance middleware.
        [<CustomOperation("useProjectedProfilesWith")>]
        member _.UseProjectedProfilesWith(spec: WebHostSpec, state: RuntimeState) : WebHostSpec =
            let roleLookup = RoleProfileOverlay.build state

            if roleLookup.Count = 0 then
                spec
            else
                { spec with
                    Services =
                        spec.Services
                        >> fun services ->
                            services.AddSingleton<RoleProfileLookup>(roleLookup) |> ignore
                            services
                    Middleware =
                        spec.Middleware
                        >> fun app ->
                            app.UseMiddleware<ProjectedProfileMiddleware>() |> ignore
                            app }

        /// Auto-load projected profile data from the entry assembly's embedded model.bin.
        /// Registers a DI factory that lazily loads from the assembly.
        /// Falls back to empty lookup when the entry assembly is null or model.bin is not found.
        /// For multi-project solutions, use useProjectedProfilesWith.
        [<CustomOperation("useProjectedProfiles")>]
        member _.UseProjectedProfiles(spec: WebHostSpec) : WebHostSpec =
            { spec with
                Services =
                    spec.Services
                    >> fun services ->
                        services.TryAddSingleton<RoleProfileLookup>(
                            Func<IServiceProvider, RoleProfileLookup>(fun sp ->
                                let loggerFactory = sp.GetRequiredService<ILoggerFactory>()
                                let logger = loggerFactory.CreateLogger<ProjectedProfileMiddleware>()

                                match Assembly.GetEntryAssembly() with
                                | null ->
                                    logger.LogWarning(
                                        "Assembly.GetEntryAssembly() returned null; cannot auto-load projected profiles. Use useProjectedProfilesWith to supply an explicit RuntimeState."
                                    )

                                    RoleProfileLookup(System.StringComparer.Ordinal)
                                | assembly ->
                                    match StartupProjection.loadRuntimeStateFromAssembly logger assembly with
                                    | Some state ->
                                        let roleLookup = RoleProfileOverlay.build state

                                        if roleLookup.Count > 0 then
                                            logger.LogInformation(
                                                "Projected profiles loaded from assembly '{AssemblyName}' ({RouteCount} routes with role projections).",
                                                assembly.GetName().Name,
                                                roleLookup.Count
                                            )

                                        roleLookup
                                    | None ->
                                        logger.LogInformation(
                                            "model.bin not found or unreadable in assembly '{AssemblyName}'; projected profiles not loaded.",
                                            assembly.GetName().Name
                                        )

                                        RoleProfileLookup(System.StringComparer.Ordinal))
                        )
                        |> ignore

                        services
                Middleware =
                    spec.Middleware
                    >> fun app ->
                        app.UseMiddleware<ProjectedProfileMiddleware>() |> ignore
                        app }

        /// Register the dual profile middleware with an explicit DualProfileLookup.
        /// Swaps the ALPS profile Link header for a dual-annotated variant when
        /// the client sends `Prefer: return=dual` with an authenticated request.
        /// Runs after ProjectedProfileMiddleware.
        [<CustomOperation("useDualProfilesWith")>]
        member _.UseDualProfilesWith(spec: WebHostSpec, dualLookup: DualProfileLookup) : WebHostSpec =
            if dualLookup.Count = 0 then
                spec
            else
                { spec with
                    Services =
                        spec.Services
                        >> fun services ->
                            services.AddSingleton<DualProfileLookup>(dualLookup) |> ignore
                            services
                    Middleware =
                        spec.Middleware
                        >> fun app ->
                            app.UseMiddleware<DualProfileMiddleware>() |> ignore
                            app }

        /// Auto-load dual profile data from the entry assembly's embedded model.bin.
        /// Derives client duals at startup from the ExtractedStatechart data.
        /// Falls back to empty lookup when the entry assembly is null or model.bin is not found.
        /// For multi-project solutions, use useDualProfilesWith.
        [<CustomOperation("useDualProfiles")>]
        member _.UseDualProfiles(spec: WebHostSpec) : WebHostSpec =
            { spec with
                Services =
                    spec.Services
                    >> fun services ->
                        services.TryAddSingleton<DualProfileLookup>(
                            Func<IServiceProvider, DualProfileLookup>(fun sp ->
                                let loggerFactory = sp.GetRequiredService<ILoggerFactory>()
                                let logger = loggerFactory.CreateLogger<DualProfileMiddleware>()

                                match Assembly.GetEntryAssembly() with
                                | null ->
                                    logger.LogWarning(
                                        "Assembly.GetEntryAssembly() returned null; cannot auto-load dual profiles. Use useDualProfilesWith to supply an explicit lookup."
                                    )

                                    DualProfileLookup(System.StringComparer.Ordinal)
                                | assembly ->
                                    match StartupProjection.loadUnifiedStateFromAssembly logger assembly with
                                    | Some state ->
                                        let dualLookup =
                                            DualProfileOverlay.buildFromRuntimeState state.Resources state.BaseUri

                                        if dualLookup.Count > 0 then
                                            logger.LogInformation(
                                                "Dual profiles loaded from assembly '{AssemblyName}' ({RouteCount} routes with dual projections).",
                                                assembly.GetName().Name,
                                                dualLookup.Count
                                            )

                                        dualLookup
                                    | None ->
                                        logger.LogInformation(
                                            "model.bin not found or unreadable in assembly '{AssemblyName}'; dual profiles not loaded.",
                                            assembly.GetName().Name
                                        )

                                        DualProfileLookup(System.StringComparer.Ordinal))
                        )
                        |> ignore

                        services
                Middleware =
                    spec.Middleware
                    >> fun app ->
                        app.UseMiddleware<DualProfileMiddleware>() |> ignore
                        app }

        /// Registers the OPTIONS discovery middleware. Endpoints respond to
        /// OPTIONS with an Allow header listing registered HTTP methods and
        /// aggregated DiscoveryMediaType information.
        /// Registers a default empty affordance dictionary via TryAddSingleton
        /// so the middleware resolves from DI without a null secondary constructor (FW-2).
        [<CustomOperation("useOptionsDiscovery")>]
        member _.UseOptionsDiscovery(spec: WebHostSpec) : WebHostSpec =
            { spec with
                Services =
                    spec.Services
                    >> fun services ->
                        services.TryAddSingleton<Dictionary<string, PreComputedAffordance>>(
                            Dictionary<string, PreComputedAffordance>(StringComparer.Ordinal)
                        )

                        services
                Middleware =
                    spec.Middleware
                    >> fun app ->
                        app.UseMiddleware<OptionsDiscoveryMiddleware>() |> ignore
                        app }

        /// Registers the Link header middleware. Responses to GET/HEAD requests
        /// from endpoints with DiscoveryMediaType metadata will include
        /// RFC 8288 Link headers (on 2xx responses only).
        [<CustomOperation("useLinkHeaders")>]
        member _.UseLinkHeaders(spec: WebHostSpec) : WebHostSpec =
            { spec with
                Middleware =
                    spec.Middleware
                    >> fun app ->
                        app.UseMiddleware<LinkHeaderMiddleware>() |> ignore
                        app }

        /// Registers OPTIONS discovery and Link header middlewares (without JSON Home).
        /// Delegates to useOptionsDiscovery + useLinkHeaders to avoid duplicating
        /// DI registration logic. For the full bundle including JSON Home, use useDiscovery.
        [<CustomOperation("useDiscoveryHeaders")>]
        member this.UseDiscoveryHeaders(spec: WebHostSpec) : WebHostSpec =
            spec |> this.UseOptionsDiscovery |> this.UseLinkHeaders

        /// Registers all three discovery middlewares: OPTIONS responses, Link headers,
        /// and JSON Home at the root. This is the recommended default.
        /// JSON Home middleware runs before routing (to avoid root route conflicts);
        /// OPTIONS and Link header middlewares run after routing.
        [<CustomOperation("useDiscovery")>]
        member this.UseDiscovery(spec: WebHostSpec) : WebHostSpec =
            // UseJsonHome → BeforeRoutingMiddleware; UseDiscoveryHeaders → Middleware.
            // Order here is documentation, not semantics — they write to different pipeline slots.
            spec |> this.UseJsonHome |> this.UseDiscoveryHeaders
