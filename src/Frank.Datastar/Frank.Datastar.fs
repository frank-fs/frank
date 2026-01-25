namespace Frank.Datastar

open System.Threading.Tasks
open Microsoft.AspNetCore.Http
open Frank.Builder
open StarFederation.Datastar.FSharp

/// Extensions to Frank's ResourceBuilder for Datastar SSE operations.
///
/// Per FR-005, this module provides ONLY the `datastar` custom operation.
/// One-off convenience operations are explicitly forbidden - use standard Frank
/// resource handlers for single-response interactions.
[<AutoOpen>]
module DatastarExtensions =

    type ResourceBuilder with

        /// Execute Datastar operations with automatic SSE stream management.
        /// The stream is started once, then your operations are executed.
        /// Use Datastar.* helper functions inside the handler for SSE events.
        ///
        /// Example:
        /// ```fsharp
        /// resource "/updates" {
        ///     name "Updates"
        ///     datastar (fun ctx -> task {
        ///         // Stream starts automatically
        ///         do! Datastar.patchElements "<div id='status'>Loading...</div>" ctx
        ///         do! Task.Delay(500)
        ///         do! Datastar.patchElements "<div id='status'>Complete!</div>" ctx
        ///     })
        /// }
        /// ```
        /// Execute Datastar operations with automatic SSE stream management (defaults to GET).
        [<CustomOperation("datastar")>]
        member _.Datastar(spec: ResourceSpec, operation: HttpContext -> Task<unit>) : ResourceSpec =
            let handler (ctx: HttpContext) =
                task {
                    do! ServerSentEventGenerator.StartServerEventStreamAsync(ctx.Response)
                    do! operation ctx
                }

            ResourceBuilder.AddHandler(HttpMethods.Get, spec, handler)

        /// Execute Datastar operations with automatic SSE stream management using specified HTTP method.
        ///
        /// Example:
        /// ```fsharp
        /// resource "/submit" {
        ///     name "Submit"
        ///     datastar HttpMethods.Post (fun ctx -> task {
        ///         let! signals = Datastar.tryReadSignals<FormData> ctx
        ///         // Process signals and send updates...
        ///     })
        /// }
        /// ```
        [<CustomOperation("datastar")>]
        member _.Datastar(spec: ResourceSpec, method: string, operation: HttpContext -> Task<unit>) : ResourceSpec =
            let handler (ctx: HttpContext) =
                task {
                    do! ServerSentEventGenerator.StartServerEventStreamAsync(ctx.Response)
                    do! operation ctx
                }

            ResourceBuilder.AddHandler(method, spec, handler)

/// Helper functions for use inside the `datastar` handler.
/// These assume the SSE stream has already been started by the `datastar` operation.
module Datastar =

    /// Patch HTML elements. Use this as the primary pattern (hypermedia-first).
    let patchElements (html: string) (ctx: HttpContext) =
        ServerSentEventGenerator.PatchElementsAsync(ctx.Response, html)

    /// Patch client-side signals. Use sparingly - prefer patchElements.
    let patchSignals (signals: string) (ctx: HttpContext) =
        ServerSentEventGenerator.PatchSignalsAsync(ctx.Response, signals)

    /// Remove an element by CSS selector.
    let removeElement (selector: string) (ctx: HttpContext) =
        ServerSentEventGenerator.RemoveElementAsync(ctx.Response, selector)

    /// Execute JavaScript on the client. Use very sparingly.
    let executeScript (script: string) (ctx: HttpContext) =
        ServerSentEventGenerator.ExecuteScriptAsync(ctx.Response, script)

    /// Read and deserialize signals from the request body.
    /// Returns ValueNone for invalid/missing JSON.
    let tryReadSignals<'T> (ctx: HttpContext) : Task<voption<'T>> =
        ServerSentEventGenerator.ReadSignalsAsync<'T>(ctx.Request)
