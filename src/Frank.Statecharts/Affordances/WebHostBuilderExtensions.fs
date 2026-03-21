namespace Frank.Affordances

open System.Reflection
open Microsoft.AspNetCore.Builder
open Microsoft.Extensions.DependencyInjection
open Microsoft.Extensions.Logging
open Frank.Resources.Model
open Frank.Builder

[<AutoOpen>]
module WebHostBuilderExtensions =
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
                            if version <> AffordanceMap.currentVersion then
                                let logger =
                                    app.ApplicationServices.GetRequiredService<ILoggerFactory>()
                                        .CreateLogger<AffordanceMiddleware>()

                                logger.LogWarning(
                                    "Affordance map version '{MapVersion}' does not match expected version '{ExpectedVersion}'. Affordance headers may be incorrect.",
                                    version,
                                    AffordanceMap.currentVersion
                                )

                            app.UseMiddleware<AffordanceMiddleware>(preComputed) |> ignore
                            app }

        /// Auto-load the AffordanceMap from the entry assembly's embedded model.bin.
        /// Falls back to no-op with a log message when the entry assembly is null
        /// or model.bin is not found/readable. For multi-project solutions where
        /// model.bin lives in a library assembly, use useAffordancesWith.
        [<CustomOperation("useAffordances")>]
        member this.UseAffordances(spec: WebHostSpec) : WebHostSpec =
            let logAtStartup (level: string) (msg: string) =
                { spec with
                    Middleware =
                        spec.Middleware
                        >> fun app ->
                            let logger =
                                app.ApplicationServices.GetRequiredService<ILoggerFactory>()
                                    .CreateLogger<AffordanceMiddleware>()

                            if level = "warn" then
                                logger.LogWarning(msg)
                            else
                                logger.LogInformation(msg)

                            app }

            match Assembly.GetEntryAssembly() with
            | null ->
                logAtStartup
                    "warn"
                    "Assembly.GetEntryAssembly() returned null; cannot auto-load affordances. Use useAffordancesWith to supply an explicit map."
            | assembly ->
                match StartupProjection.loadAffordanceMapFromAssembly assembly with
                | Some map -> this.UseAffordancesWith(spec, map)
                | None ->
                    logAtStartup
                        "info"
                        (sprintf
                            "model.bin not found or unreadable in assembly '%s'; affordances not loaded. Use useAffordancesWith to supply an explicit map."
                            (assembly.GetName().Name))
