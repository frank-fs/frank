namespace Frank.Datastar

open System.Text.Json
open System.Threading.Tasks
open Microsoft.AspNetCore.Http
open Frank.Builder
open StarFederation.Datastar.FSharp

/// Extensions to Frank's ResourceBuilder for Datastar SSE operations.
///
/// IMPORTANT: The SSE stream should be started ONCE per request. Use the `datastar`
/// custom operation to execute multiple Datastar operations with proper stream management.
[<AutoOpen>]
module DatastarExtensions =

    type ResourceBuilder with

        /// Execute Datastar operations with automatic SSE stream management.
        /// The stream is started once, then your operations are executed.
        /// This is the RECOMMENDED way to use Datastar with Frank.
        ///
        /// Example:
        /// ```fsharp
        /// resource "/updates" {
        ///     datastar (fun ctx -> task {
        ///         // Stream starts automatically here
        ///         do! ServerSentEventGenerator.PatchElementsAsync(ctx.Response, "<div>1</div>")
        ///         do! ServerSentEventGenerator.PatchSignalsAsync(ctx.Response, "{\"count\": 5}")
        ///         // Multiple operations on the same stream
        ///     })
        /// }
        /// ```
        [<CustomOperation("datastar")>]
        member _.Datastar(spec: ResourceSpec, operation: HttpContext -> Task<unit>) : ResourceSpec =
            let handler (ctx: HttpContext) =
                task {
                    // Start the SSE stream ONCE
                    do! ServerSentEventGenerator.StartServerEventStreamAsync(ctx.Response)
                    // Execute user's operations
                    do! operation ctx
                }

            ResourceBuilder.AddHandler(HttpMethods.Get, spec, handler)

        /// Convenience operation for simple single-patch scenarios.
        /// For multiple operations, use the `datastar` operation instead.
        ///
        /// Example:
        /// ```fsharp
        /// resource "/simple" {
        ///     patchElements "<div>Hello</div>"
        /// }
        /// ```
        [<CustomOperation("patchElements")>]
        member _.PatchElements(spec: ResourceSpec, html: string) : ResourceSpec =
            let handler (ctx: HttpContext) =
                task {
                    do! ServerSentEventGenerator.StartServerEventStreamAsync(ctx.Response)
                    do! ServerSentEventGenerator.PatchElementsAsync(ctx.Response, html)
                }

            ResourceBuilder.AddHandler(HttpMethods.Get, spec, handler)

        /// Patch HTML elements using a function that receives the HttpContext.
        [<CustomOperation("patchElements")>]
        member _.PatchElements(spec: ResourceSpec, htmlFn: HttpContext -> string) : ResourceSpec =
            let handler (ctx: HttpContext) =
                task {
                    do! ServerSentEventGenerator.StartServerEventStreamAsync(ctx.Response)
                    let html = htmlFn ctx
                    do! ServerSentEventGenerator.PatchElementsAsync(ctx.Response, html)
                }

            ResourceBuilder.AddHandler(HttpMethods.Get, spec, handler)

        /// Patch HTML elements using an async function.
        [<CustomOperation("patchElements")>]
        member _.PatchElements(spec: ResourceSpec, htmlTask: HttpContext -> Task<string>) : ResourceSpec =
            let handler (ctx: HttpContext) =
                task {
                    do! ServerSentEventGenerator.StartServerEventStreamAsync(ctx.Response)
                    let! html = htmlTask ctx
                    do! ServerSentEventGenerator.PatchElementsAsync(ctx.Response, html)
                }

            ResourceBuilder.AddHandler(HttpMethods.Get, spec, handler)

        /// Remove an element from the DOM by its CSS selector.
        [<CustomOperation("removeElement")>]
        member _.RemoveElement(spec: ResourceSpec, selector: string) : ResourceSpec =
            let handler (ctx: HttpContext) =
                task {
                    do! ServerSentEventGenerator.StartServerEventStreamAsync(ctx.Response)
                    do! ServerSentEventGenerator.RemoveElementAsync(ctx.Response, selector)
                }

            ResourceBuilder.AddHandler(HttpMethods.Get, spec, handler)

        /// Patch client-side signals (ephemeral UI state).
        /// Use sparingly - prefer patchElements (hypermedia-first).
        [<CustomOperation("patchSignals")>]
        member _.PatchSignals(spec: ResourceSpec, signals: string) : ResourceSpec =
            let handler (ctx: HttpContext) =
                task {
                    do! ServerSentEventGenerator.StartServerEventStreamAsync(ctx.Response)
                    do! ServerSentEventGenerator.PatchSignalsAsync(ctx.Response, signals)
                }

            ResourceBuilder.AddHandler(HttpMethods.Get, spec, handler)

        /// Patch client-side signals using a context-aware function.
        [<CustomOperation("patchSignals")>]
        member _.PatchSignals(spec: ResourceSpec, signalsFn: HttpContext -> string) : ResourceSpec =
            let handler (ctx: HttpContext) =
                task {
                    do! ServerSentEventGenerator.StartServerEventStreamAsync(ctx.Response)
                    let signals = signalsFn ctx
                    do! ServerSentEventGenerator.PatchSignalsAsync(ctx.Response, signals)
                }

            ResourceBuilder.AddHandler(HttpMethods.Get, spec, handler)

        /// Execute JavaScript on the client.
        /// Use very sparingly - prefer server-driven HTML updates.
        [<CustomOperation("executeScript")>]
        member _.ExecuteScript(spec: ResourceSpec, script: string) : ResourceSpec =
            let handler (ctx: HttpContext) =
                task {
                    do! ServerSentEventGenerator.StartServerEventStreamAsync(ctx.Response)
                    do! ServerSentEventGenerator.ExecuteScriptAsync(ctx.Response, script)
                }

            ResourceBuilder.AddHandler(HttpMethods.Get, spec, handler)

        /// Read signals sent from the client and process them with a handler function.
        [<CustomOperation("readSignals")>]
        member _.ReadSignals<'T>
            (spec: ResourceSpec, handlerFn: HttpContext -> voption<'T> -> Task<unit>)
            : ResourceSpec =
            let handler (ctx: HttpContext) =
                task {
                    do! ServerSentEventGenerator.StartServerEventStreamAsync(ctx.Response)
                    let! signals = ServerSentEventGenerator.ReadSignalsAsync<'T>(ctx.Request)
                    do! handlerFn ctx signals
                }

            ResourceBuilder.AddHandler(HttpMethods.Get, spec, handler)

        /// Transform signals: read from client, transform on server, send back.
        [<CustomOperation("transformSignals")>]
        member _.TransformSignals<'TIn, 'TOut>
            (spec: ResourceSpec, transformer: HttpContext -> 'TIn -> Task<'TOut>)
            : ResourceSpec =
            let handler (ctx: HttpContext) =
                task {
                    do! ServerSentEventGenerator.StartServerEventStreamAsync(ctx.Response)
                    let! signals = ServerSentEventGenerator.ReadSignalsAsync<'TIn>(ctx.Request)

                    match signals with
                    | ValueSome input ->
                        let! output = transformer ctx input
                        let json = JsonSerializer.Serialize(output)
                        do! ServerSentEventGenerator.PatchSignalsAsync(ctx.Response, json)
                    | ValueNone ->
                        do!
                            ServerSentEventGenerator.PatchSignalsAsync(
                                ctx.Response,
                                """{"error": "Invalid signal format"}"""
                            )
                }

            ResourceBuilder.AddHandler(HttpMethods.Get, spec, handler)

/// Helper functions for working with Datastar outside of computation expressions
module Datastar =

    /// Start an SSE stream and execute multiple Datastar operations.
    /// Use this when not using Frank's resource computation expression.
    let stream (operations: (HttpContext -> Task<unit>) list) (ctx: HttpContext) =
        task {
            do! ServerSentEventGenerator.StartServerEventStreamAsync(ctx.Response)

            for operation in operations do
                do! operation ctx
        }

    /// Patch HTML elements (assumes stream already started).
    let patchElements (html: string) (ctx: HttpContext) =
        ServerSentEventGenerator.PatchElementsAsync(ctx.Response, html)

    /// Patch signals (assumes stream already started).
    let patchSignals (signals: string) (ctx: HttpContext) =
        ServerSentEventGenerator.PatchSignalsAsync(ctx.Response, signals)

    /// Remove an element (assumes stream already started).
    let removeElement (selector: string) (ctx: HttpContext) =
        ServerSentEventGenerator.RemoveElementAsync(ctx.Response, selector)

    /// Execute JavaScript (assumes stream already started).
    let executeScript (script: string) (ctx: HttpContext) =
        ServerSentEventGenerator.ExecuteScriptAsync(ctx.Response, script)

    /// Read signals from the request.
    let tryReadSignals<'T> (ctx: HttpContext) : Task<voption<'T>> =
        ServerSentEventGenerator.ReadSignalsAsync<'T>(ctx.Request)
