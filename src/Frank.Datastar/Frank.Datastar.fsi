namespace Frank.Datastar

open System.IO
open System.Text.Json
open System.Threading.Tasks
open Microsoft.AspNetCore.Http
open Frank.Builder

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
        member Datastar : spec:ResourceSpec * operation:(HttpContext -> Task<unit>) -> ResourceSpec

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
        member Datastar : spec:ResourceSpec * method:string * operation:(HttpContext -> Task<unit>) -> ResourceSpec

/// Helper functions for use inside the `datastar` handler.
/// These assume the SSE stream has already been started by the `datastar` operation.
/// Most functions are marked `inline`. tryReadSignals and tryReadSignalsWithOptions are not inline — they contain try/with which the F# compiler cannot inline.
module Datastar =

    /// Patch HTML elements. Use this as the primary pattern (hypermedia-first).
    val inline patchElements : html:string -> ctx:HttpContext -> Task

    /// Patch client-side signals. Use sparingly - prefer patchElements.
    val inline patchSignals : signals:string -> ctx:HttpContext -> Task

    /// Remove an element by CSS selector.
    val inline removeElement : selector:string -> ctx:HttpContext -> Task

    /// Execute JavaScript on the client. Use very sparingly.
    val inline executeScript : script:string -> ctx:HttpContext -> Task

    /// Read and deserialize signals from the request body.
    /// Returns ValueNone for invalid/missing JSON and logs the JsonException as a warning.
    /// Call ServerSentEventGenerator.ReadSignalsAsync<'T> directly to handle parse errors yourself.
    val tryReadSignals<'T> : ctx:HttpContext -> Task<voption<'T>>

    /// <summary>Patch HTML elements with custom options.</summary>
    /// <param name="options">Options controlling patch mode, selector, and view transitions.</param>
    /// <param name="html">The HTML content to patch into the DOM.</param>
    /// <param name="ctx">The HttpContext for the current request.</param>
    /// <example>
    /// let opts = { PatchElementsOptions.Defaults with PatchMode = ElementPatchMode.Inner }
    /// do! Datastar.patchElementsWithOptions opts "&lt;div&gt;Content&lt;/div&gt;" ctx
    /// </example>
    val inline patchElementsWithOptions : options:PatchElementsOptions -> html:string -> ctx:HttpContext -> Task

    /// <summary>Patch client-side signals with custom options.</summary>
    /// <param name="options">Options controlling whether to only set missing signals.</param>
    /// <param name="signals">JSON string containing signal values to patch.</param>
    /// <param name="ctx">The HttpContext for the current request.</param>
    /// <example>
    /// let opts = { PatchSignalsOptions.Defaults with OnlyIfMissing = true }
    /// do! Datastar.patchSignalsWithOptions opts """{"count": 0}""" ctx
    /// </example>
    val inline patchSignalsWithOptions : options:PatchSignalsOptions -> signals:string -> ctx:HttpContext -> Task

    /// <summary>Remove an element by CSS selector with custom options.</summary>
    /// <param name="options">Options controlling view transitions during removal.</param>
    /// <param name="selector">CSS selector for the element(s) to remove.</param>
    /// <param name="ctx">The HttpContext for the current request.</param>
    /// <example>
    /// let opts = { RemoveElementOptions.Defaults with UseViewTransition = true }
    /// do! Datastar.removeElementWithOptions opts "#old-element" ctx
    /// </example>
    val inline removeElementWithOptions : options:RemoveElementOptions -> selector:string -> ctx:HttpContext -> Task

    /// <summary>Execute JavaScript on the client with custom options.</summary>
    /// <param name="options">Options controlling auto-removal and attributes.</param>
    /// <param name="script">The JavaScript code to execute.</param>
    /// <param name="ctx">The HttpContext for the current request.</param>
    /// <example>
    /// let opts = { ExecuteScriptOptions.Defaults with AutoRemove = false }
    /// do! Datastar.executeScriptWithOptions opts "console.log('persistent')" ctx
    /// </example>
    val inline executeScriptWithOptions : options:ExecuteScriptOptions -> script:string -> ctx:HttpContext -> Task

    /// <summary>Read and deserialize signals with custom JSON serializer options.</summary>
    /// <typeparam name="T">The type to deserialize signals into.</typeparam>
    /// <param name="jsonOptions">Custom JsonSerializerOptions for deserialization.</param>
    /// <param name="ctx">The HttpContext for the current request.</param>
    /// <returns>ValueSome with deserialized signals, or ValueNone if parsing fails.</returns>
    /// <example>
    /// let jsonOpts = JsonSerializerOptions(PropertyNameCaseInsensitive = true)
    /// let! signals = Datastar.tryReadSignalsWithOptions&lt;MySignals&gt; jsonOpts ctx
    /// </example>
    val tryReadSignalsWithOptions<'T> : jsonOptions:JsonSerializerOptions -> ctx:HttpContext -> Task<voption<'T>>

    val inline streamPatchElements : writer:(TextWriter -> Task) -> ctx:HttpContext -> Task<unit>
    val inline streamPatchElementsWithOptions : options:PatchElementsOptions -> writer:(TextWriter -> Task) -> ctx:HttpContext -> Task<unit>
    val inline streamRemoveElement : writer:(TextWriter -> Task) -> ctx:HttpContext -> Task<unit>
    val inline streamRemoveElementWithOptions : options:RemoveElementOptions -> writer:(TextWriter -> Task) -> ctx:HttpContext -> Task<unit>
    val inline streamPatchSignals : writer:(TextWriter -> Task) -> ctx:HttpContext -> Task<unit>
    val inline streamPatchSignalsWithOptions : options:PatchSignalsOptions -> writer:(TextWriter -> Task) -> ctx:HttpContext -> Task<unit>
    val inline streamExecuteScript : writer:(TextWriter -> Task) -> ctx:HttpContext -> Task<unit>
    val inline streamExecuteScriptWithOptions : options:ExecuteScriptOptions -> writer:(TextWriter -> Task) -> ctx:HttpContext -> Task<unit>

    val inline streamPatchElementsToStream : writer:(Stream -> Task) -> ctx:HttpContext -> Task<unit>
    val inline streamPatchElementsToStreamWithOptions : options:PatchElementsOptions -> writer:(Stream -> Task) -> ctx:HttpContext -> Task<unit>
    val inline streamRemoveElementToStream : writer:(Stream -> Task) -> ctx:HttpContext -> Task<unit>
    val inline streamRemoveElementToStreamWithOptions : options:RemoveElementOptions -> writer:(Stream -> Task) -> ctx:HttpContext -> Task<unit>
    val inline streamPatchSignalsToStream : writer:(Stream -> Task) -> ctx:HttpContext -> Task<unit>
    val inline streamPatchSignalsToStreamWithOptions : options:PatchSignalsOptions -> writer:(Stream -> Task) -> ctx:HttpContext -> Task<unit>
    val inline streamExecuteScriptToStream : writer:(Stream -> Task) -> ctx:HttpContext -> Task<unit>
    val inline streamExecuteScriptToStreamWithOptions : options:ExecuteScriptOptions -> writer:(Stream -> Task) -> ctx:HttpContext -> Task<unit>
