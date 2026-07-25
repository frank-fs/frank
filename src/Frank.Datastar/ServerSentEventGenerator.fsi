namespace Frank.Datastar

open System.IO
open System.Text.Json
open System.Threading
open System.Threading.Tasks
open Microsoft.AspNetCore.Http

[<Class>]
type ServerSentEventGenerator =
    /// <summary>
    /// Initializes the SSE response stream: sets <c>Content-Type: text/event-stream</c>,
    /// <c>Cache-Control: no-cache</c>, and (HTTP/1.1 only) <c>Connection: keep-alive</c>,
    /// then flushes to the client. Idempotent per request — only the first call takes effect.
    /// </summary>
    /// <remarks>
    /// Thread safety: <see cref="System.IO.Pipelines.PipeWriter"/> is not thread-safe.
    /// Do not write to the same SSE stream from parallel tasks. The <c>datastar</c> CE
    /// operation and <c>Datastar.*</c> helpers enforce sequential writes implicitly via
    /// <c>task { }</c> linearization.
    /// </remarks>
    static member StartServerEventStreamAsync : httpResponse:HttpResponse * cancellationToken:CancellationToken -> Task

    static member PatchElementsAsync : httpResponse:HttpResponse * elements:string * options:PatchElementsOptions * cancellationToken:CancellationToken -> Task
    static member PatchElementsAsync : httpResponse:HttpResponse * writer:(TextWriter -> Task) * options:PatchElementsOptions * cancellationToken:CancellationToken -> Task<unit>

    static member RemoveElementAsync : httpResponse:HttpResponse * selector:Selector * options:RemoveElementOptions * cancellationToken:CancellationToken -> Task
    static member RemoveElementAsync : httpResponse:HttpResponse * writer:(TextWriter -> Task) * options:RemoveElementOptions * cancellationToken:CancellationToken -> Task<unit>

    static member PatchSignalsAsync : httpResponse:HttpResponse * signals:Signals * options:PatchSignalsOptions * cancellationToken:CancellationToken -> Task
    static member PatchSignalsAsync : httpResponse:HttpResponse * writer:(TextWriter -> Task) * options:PatchSignalsOptions * cancellationToken:CancellationToken -> Task<unit>

    static member ExecuteScriptAsync : httpResponse:HttpResponse * script:string * options:ExecuteScriptOptions * cancellationToken:CancellationToken -> Task
    static member ExecuteScriptAsync : httpResponse:HttpResponse * writer:(TextWriter -> Task) * options:ExecuteScriptOptions * cancellationToken:CancellationToken -> Task<unit>

    // Stream-based overloads (Stream -> Task)
    static member PatchElementsAsync : httpResponse:HttpResponse * writer:(Stream -> Task) * options:PatchElementsOptions * cancellationToken:CancellationToken -> Task<unit>
    static member RemoveElementAsync : httpResponse:HttpResponse * writer:(Stream -> Task) * options:RemoveElementOptions * cancellationToken:CancellationToken -> Task<unit>
    static member PatchSignalsAsync : httpResponse:HttpResponse * writer:(Stream -> Task) * options:PatchSignalsOptions * cancellationToken:CancellationToken -> Task<unit>
    static member ExecuteScriptAsync : httpResponse:HttpResponse * writer:(Stream -> Task) * options:ExecuteScriptOptions * cancellationToken:CancellationToken -> Task<unit>

    // backgroundTask vs task: both are equivalent on ASP.NET Core's threadpool sync context.
    // The asymmetry is intentional: the string overload predates the typed overload.
    static member ReadSignalsAsync : httpRequest:HttpRequest * cancellationToken:CancellationToken -> Task<string>
    static member ReadSignalsAsync<'T> : httpRequest:HttpRequest * jsonSerializerOptions:JsonSerializerOptions * cancellationToken:CancellationToken -> Task<'T voption>

    //
    // SHORT HAND METHODS
    //
    static member StartServerEventStreamAsync : httpResponse:HttpResponse -> Task
    static member PatchElementsAsync : httpResponse:HttpResponse * elements:string * options:PatchElementsOptions -> Task
    static member PatchElementsAsync : httpResponse:HttpResponse * elements:string -> Task
    static member RemoveElementAsync : httpResponse:HttpResponse * selector:Selector * options:RemoveElementOptions -> Task
    static member RemoveElementAsync : httpResponse:HttpResponse * selector:Selector -> Task
    static member PatchSignalsAsync : httpResponse:HttpResponse * signals:Signals * options:PatchSignalsOptions -> Task
    static member PatchSignalsAsync : httpResponse:HttpResponse * signals:Signals -> Task
    static member ExecuteScriptAsync : httpResponse:HttpResponse * script:string * options:ExecuteScriptOptions -> Task
    static member ExecuteScriptAsync : httpResponse:HttpResponse * script:string -> Task
    // Stream-based shorthand overloads
    static member PatchElementsAsync : httpResponse:HttpResponse * writer:(TextWriter -> Task) * options:PatchElementsOptions -> Task<unit>
    static member PatchElementsAsync : httpResponse:HttpResponse * writer:(TextWriter -> Task) -> Task<unit>
    static member RemoveElementAsync : httpResponse:HttpResponse * writer:(TextWriter -> Task) * options:RemoveElementOptions -> Task<unit>
    static member RemoveElementAsync : httpResponse:HttpResponse * writer:(TextWriter -> Task) -> Task<unit>
    static member PatchSignalsAsync : httpResponse:HttpResponse * writer:(TextWriter -> Task) * options:PatchSignalsOptions -> Task<unit>
    static member PatchSignalsAsync : httpResponse:HttpResponse * writer:(TextWriter -> Task) -> Task<unit>
    static member ExecuteScriptAsync : httpResponse:HttpResponse * writer:(TextWriter -> Task) * options:ExecuteScriptOptions -> Task<unit>
    static member ExecuteScriptAsync : httpResponse:HttpResponse * writer:(TextWriter -> Task) -> Task<unit>
    // Stream-based (Stream -> Task) shorthand overloads
    static member PatchElementsAsync : httpResponse:HttpResponse * writer:(Stream -> Task) * options:PatchElementsOptions -> Task<unit>
    static member PatchElementsAsync : httpResponse:HttpResponse * writer:(Stream -> Task) -> Task<unit>
    static member RemoveElementAsync : httpResponse:HttpResponse * writer:(Stream -> Task) * options:RemoveElementOptions -> Task<unit>
    static member RemoveElementAsync : httpResponse:HttpResponse * writer:(Stream -> Task) -> Task<unit>
    static member PatchSignalsAsync : httpResponse:HttpResponse * writer:(Stream -> Task) * options:PatchSignalsOptions -> Task<unit>
    static member PatchSignalsAsync : httpResponse:HttpResponse * writer:(Stream -> Task) -> Task<unit>
    static member ExecuteScriptAsync : httpResponse:HttpResponse * writer:(Stream -> Task) * options:ExecuteScriptOptions -> Task<unit>
    static member ExecuteScriptAsync : httpResponse:HttpResponse * writer:(Stream -> Task) -> Task<unit>
    static member ReadSignalsAsync : httpRequest:HttpRequest -> Task<string>
    static member ReadSignalsAsync<'T> : httpRequest:HttpRequest * jsonSerializerOptions:JsonSerializerOptions -> Task<'T voption>
    static member ReadSignalsAsync<'T> : httpRequest:HttpRequest -> Task<'T voption>
