namespace Frank.Affordances

open Microsoft.AspNetCore.Builder
open Microsoft.Extensions.Logging
open Frank.Builder

[<AutoOpen>]
module WebHostBuilderExtensions =
    type WebHostBuilder with

        /// Register the affordance middleware. Injects Link and Allow headers
        /// based on the pre-computed affordance map built from an AffordanceMap.
        /// When no affordance map is provided, logs a warning and passes through.
        /// Middleware runs after routing (and after statechart middleware if present).
        [<CustomOperation("useAffordances")>]
        member _.UseAffordances(spec: WebHostSpec, map: AffordanceMap) : WebHostSpec =
            let preComputed = AffordanceMap.preCompute map

            if preComputed.Count = 0 then
                spec
            else
                let version = map.Version

                { spec with
                    Middleware =
                        spec.Middleware
                        >> fun app ->
                            if version <> AffordanceMap.currentVersion then
                                let loggerFactory = app.ApplicationServices.GetService(typeof<ILoggerFactory>)

                                if not (isNull loggerFactory) then
                                    let logger =
                                        (loggerFactory :?> ILoggerFactory)
                                            .CreateLogger<AffordanceMiddleware>()

                                    logger.LogWarning(
                                        "Affordance map version '{MapVersion}' does not match expected version '{ExpectedVersion}'. Affordance headers may be incorrect.",
                                        version,
                                        AffordanceMap.currentVersion
                                    )

                            app.UseMiddleware<AffordanceMiddleware>(preComputed) |> ignore
                            app }
