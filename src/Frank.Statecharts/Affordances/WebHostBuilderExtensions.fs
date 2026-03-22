namespace Frank.Affordances

open System.Collections.Generic
open System.Reflection
open Microsoft.AspNetCore.Builder
open Microsoft.Extensions.DependencyInjection
open Microsoft.Extensions.Logging
open Frank.Resources.Model
open Frank.Builder

[<AutoOpen>]
module WebHostBuilderExtensions =

    let private getAffordanceLogger (app: IApplicationBuilder) =
        app.ApplicationServices.GetRequiredService<ILoggerFactory>()
            .CreateLogger<AffordanceMiddleware>()

    let private registerAffordanceMiddleware
        (logger: ILogger)
        (preComputed: Dictionary<string, PreComputedAffordance>)
        (version: string)
        (app: IApplicationBuilder)
        =
        if version <> AffordanceMap.currentVersion then
            logger.LogWarning(
                "Affordance map version '{MapVersion}' does not match expected version '{ExpectedVersion}'. Affordance headers may be incorrect.",
                version,
                AffordanceMap.currentVersion)

        app.UseMiddleware<AffordanceMiddleware>(preComputed) |> ignore

    type WebHostBuilder with

        /// Register the affordance middleware with an explicit AffordanceMap.
        /// Injects Link and Allow headers based on the pre-computed affordance map.
        /// Middleware runs after routing (and after statechart middleware if present).
        [<CustomOperation("useAffordancesWith")>]
        member _.UseAffordancesWith(spec: WebHostSpec, map: AffordanceMap) : WebHostSpec =
            let preComputed = AffordancePreCompute.preCompute map

            if preComputed.Count = 0 then
                spec
            else
                let version = map.Version

                { spec with
                    Middleware =
                        spec.Middleware
                        >> fun app ->
                            registerAffordanceMiddleware (getAffordanceLogger app) preComputed version app
                            app }

        /// Auto-load the AffordanceMap from the entry assembly's embedded model.bin.
        /// Falls back to no-op with a log message when the entry assembly is null
        /// or model.bin is not found/readable. For multi-project solutions where
        /// model.bin lives in a library assembly, use useAffordancesWith.
        [<CustomOperation("useAffordances")>]
        member _.UseAffordances(spec: WebHostSpec) : WebHostSpec =
            match Assembly.GetEntryAssembly() with
            | null ->
                { spec with
                    Middleware =
                        spec.Middleware
                        >> fun app ->
                            (getAffordanceLogger app).LogWarning(
                                "Assembly.GetEntryAssembly() returned null; cannot auto-load affordances. Use useAffordancesWith to supply an explicit map.")

                            app }
            | assembly ->
                { spec with
                    Middleware =
                        spec.Middleware
                        >> fun app ->
                            let logger = getAffordanceLogger app

                            match StartupProjection.loadAffordanceMapFromAssembly logger assembly with
                            | Some map ->
                                logger.LogInformation(
                                    "Affordance map loaded from assembly '{AssemblyName}' ({EntryCount} entries).",
                                    assembly.GetName().Name,
                                    map.Entries.Length)

                                let preComputed = AffordancePreCompute.preCompute map

                                if preComputed.Count > 0 then
                                    registerAffordanceMiddleware logger preComputed map.Version app

                            | None ->
                                logger.LogInformation(
                                    "model.bin not found or unreadable in assembly '{AssemblyName}'; affordances not loaded. Use useAffordancesWith to supply an explicit map.",
                                    assembly.GetName().Name)

                            app }
