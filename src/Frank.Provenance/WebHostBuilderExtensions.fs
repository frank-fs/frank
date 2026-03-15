namespace Frank.Provenance

open System
open System.Threading
open System.Threading.Tasks
open Microsoft.AspNetCore.Builder
open Microsoft.AspNetCore.Http
open Microsoft.Extensions.DependencyInjection
open Microsoft.Extensions.DependencyInjection.Extensions
open Microsoft.Extensions.Hosting
open Microsoft.Extensions.Logging
open Frank.Builder

/// Manages provenance observer subscriptions to statechart transition events.
/// Subscribes on start, disposes subscriptions on stop.
type ProvenanceSubscriptionManager(serviceProvider: IServiceProvider, logger: ILogger<ProvenanceSubscriptionManager>) =

    let subscriptions = ResizeArray<IDisposable>()

    interface IHostedService with

        member _.StartAsync(_: CancellationToken) : Task =
            task {
                match serviceProvider.GetService<IObservable<TransitionEvent>>() with
                | null ->
                    logger.LogInformation(
                        "No IObservable<TransitionEvent> registered. Provenance tracking requires Frank.Statecharts integration."
                    )
                | transitionSource ->
                    let store = serviceProvider.GetRequiredService<IProvenanceStore>()

                    let observerLogger =
                        serviceProvider.GetRequiredService<ILogger<TransitionObserver>>()

                    let observer =
                        TransitionObserver(store, observerLogger) :> IObserver<TransitionEvent>

                    let sub = transitionSource.Subscribe(observer)
                    subscriptions.Add(sub)

                    logger.LogInformation(
                        "Provenance subscription manager started, {Count} subscription(s) active",
                        subscriptions.Count
                    )
            }

        member _.StopAsync(_: CancellationToken) : Task =
            task {
                for sub in subscriptions do
                    try
                        sub.Dispose()
                    with ex ->
                        logger.LogWarning(ex, "Error disposing provenance subscription")

                subscriptions.Clear()
                logger.LogInformation("Provenance subscription manager stopped")
            }

[<AutoOpen>]
module WebHostBuilderProvenanceExtensions =

    type WebHostBuilder with

        /// Enable PROV-O provenance tracking for all stateful resources.
        /// Registers IProvenanceStore (default MailboxProcessorProvenanceStore),
        /// TransitionObserver, ProvenanceSubscriptionManager, and provenance middleware.
        [<CustomOperation("useProvenance")>]
        member this.UseProvenance(spec: WebHostSpec) : WebHostSpec =
            this.UseProvenanceWith(spec, ProvenanceStoreConfig.defaults)

        /// Enable PROV-O provenance tracking with custom store configuration.
        [<CustomOperation("useProvenanceWith")>]
        member _.UseProvenanceWith(spec: WebHostSpec, config: ProvenanceStoreConfig) : WebHostSpec =
            { spec with
                Services =
                    spec.Services
                    >> fun services ->
                        services.TryAddSingleton<IProvenanceStore>(fun sp ->
                            let logger = sp.GetRequiredService<ILogger<MailboxProcessorProvenanceStore>>()

                            new MailboxProcessorProvenanceStore(config, logger) :> IProvenanceStore)

                        services.TryAddEnumerable(
                            ServiceDescriptor.Singleton<IHostedService, ProvenanceSubscriptionManager>())
                        |> ignore
                        services
                Middleware =
                    spec.Middleware
                    >> fun app ->
                        let loggerFactory = app.ApplicationServices.GetRequiredService<ILoggerFactory>()

                        app.Use(
                            Func<RequestDelegate, RequestDelegate>(fun next ->
                                ProvenanceMiddleware.createProvenanceMiddleware loggerFactory next)
                        )
                        |> ignore

                        app }
