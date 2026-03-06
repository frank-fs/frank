namespace Frank.Datastar

open System.IO
open System.Text
open System.Text.Json
open System.Threading
open System.Threading.Tasks
open Microsoft.AspNetCore.Http

[<AutoOpen>]
module private ServerSentEventGeneratorHelpers =

    let defaultJsonOptions =
        let opts = JsonSerializerOptions()
        opts.PropertyNameCaseInsensitive <- true
        opts

    let inline writeScriptOpenTag (writer: System.IO.Pipelines.PipeWriter) (autoRemove: bool) (attributes: string[]) =
        writer
        |> match (autoRemove, attributes) with
           | true, [||] -> ServerSentEvent.sendDataBytesLine Bytes.DatalineElements Bytes.bOpenScriptAutoRemove
           | false, [||] -> ServerSentEvent.sendDataBytesLine Bytes.DatalineElements Bytes.bOpenScript
           | true, attributes ->
               ServerSentEvent.sendDataStringSeqLine
                   Bytes.DatalineElements
                   (seq {
                       "<script"
                       Consts.ScriptDataEffectRemove
                       yield! attributes
                       ">"
                   })
           | false, attributes ->
               ServerSentEvent.sendDataStringSeqLine
                   Bytes.DatalineElements
                   (seq {
                       "<script"
                       yield! attributes
                       ">"
                   })

type ServerSentEventGenerator =
    static member StartServerEventStreamAsync(httpResponse: HttpResponse, cancellationToken: CancellationToken) =
        let task =
            backgroundTask {
                // Ensure initialization occurs exactly once per request (FR-010/SC-006)
                let initKey = "Frank.Datastar.SseStreamInitialized"
                let isInitialized = httpResponse.HttpContext.Items.ContainsKey(initKey)

                if not isInitialized then
                    httpResponse.HttpContext.Items.[initKey] <- true
                    httpResponse.Headers.ContentType <- "text/event-stream"
                    httpResponse.Headers.CacheControl <- "no-cache"

                    if httpResponse.HttpContext.Request.Protocol = "HTTP/1.1" then
                        httpResponse.Headers.Connection <- "keep-alive"

                    do! httpResponse.StartAsync(cancellationToken)
                    let! _ = httpResponse.BodyWriter.FlushAsync(cancellationToken)
                    ()
            }

        task :> Task

    static member PatchElementsAsync
        (
            httpResponse: HttpResponse,
            elements: string,
            options: PatchElementsOptions,
            cancellationToken: CancellationToken
        ) =
        let writer = httpResponse.BodyWriter
        writer |> ServerSentEvent.sendEventType Bytes.EventTypePatchElements

        options.EventId
        |> ValueOption.iter (fun eventId -> writer |> ServerSentEvent.sendEventId eventId)

        if options.Retry <> Consts.DefaultSseRetryDuration then
            writer |> ServerSentEvent.sendRetry options.Retry

        options.Selector
        |> ValueOption.iter (fun selector ->
            writer |> ServerSentEvent.sendDataStringLine Bytes.DatalineSelector selector)

        if options.PatchMode <> Consts.DefaultElementPatchMode then
            writer
            |> ServerSentEvent.sendDataBytesLine
                Bytes.DatalineMode
                (options.PatchMode |> Bytes.ElementPatchMode.toBytes)

        if options.UseViewTransition <> Consts.DefaultElementsUseViewTransitions then
            writer
            |> ServerSentEvent.sendDataBytesLine
                Bytes.DatalineUseViewTransition
                (if options.UseViewTransition then
                     Bytes.bTrue
                 else
                     Bytes.bFalse)

        if options.Namespace <> Consts.DefaultPatchElementNamespace then
            writer
            |> ServerSentEvent.sendDataBytesLine
                Bytes.DatalineNamespace
                (options.Namespace |> Bytes.PatchElementNamespace.toBytes)

        for segment in String.splitLinesToSegments elements do
            writer |> ServerSentEvent.sendDataSegmentLine Bytes.DatalineElements segment

        writer |> ServerSentEvent.writeNewline

        writer.FlushAsync(cancellationToken).AsTask() :> Task

    static member PatchElementsAsync
        (
            httpResponse: HttpResponse,
            writer: System.IO.TextWriter -> Task,
            options: PatchElementsOptions,
            cancellationToken: CancellationToken
        ) =
        let bufWriter = httpResponse.BodyWriter
        bufWriter |> ServerSentEvent.sendEventType Bytes.EventTypePatchElements

        options.EventId
        |> ValueOption.iter (fun eventId -> bufWriter |> ServerSentEvent.sendEventId eventId)

        if options.Retry <> Consts.DefaultSseRetryDuration then
            bufWriter |> ServerSentEvent.sendRetry options.Retry

        options.Selector
        |> ValueOption.iter (fun selector ->
            bufWriter |> ServerSentEvent.sendDataStringLine Bytes.DatalineSelector selector)

        if options.PatchMode <> Consts.DefaultElementPatchMode then
            bufWriter
            |> ServerSentEvent.sendDataBytesLine
                Bytes.DatalineMode
                (options.PatchMode |> Bytes.ElementPatchMode.toBytes)

        if options.UseViewTransition <> Consts.DefaultElementsUseViewTransitions then
            bufWriter
            |> ServerSentEvent.sendDataBytesLine
                Bytes.DatalineUseViewTransition
                (if options.UseViewTransition then
                     Bytes.bTrue
                 else
                     Bytes.bFalse)

        if options.Namespace <> Consts.DefaultPatchElementNamespace then
            bufWriter
            |> ServerSentEvent.sendDataBytesLine
                Bytes.DatalineNamespace
                (options.Namespace |> Bytes.PatchElementNamespace.toBytes)

        task {
            use sseWriter =
                new SseDataLineWriter(bufWriter, Bytes.DatalineElements, cancellationToken)

            do! writer sseWriter
            sseWriter.Flush()
            bufWriter |> ServerSentEvent.writeNewline
            return! bufWriter.FlushAsync(cancellationToken).AsTask() :> Task
        }

    static member RemoveElementAsync
        (
            httpResponse: HttpResponse,
            selector: Selector,
            options: RemoveElementOptions,
            cancellationToken: CancellationToken
        ) =
        let writer = httpResponse.BodyWriter
        writer |> ServerSentEvent.sendEventType Bytes.EventTypePatchElements

        options.EventId
        |> ValueOption.iter (fun eventId -> writer |> ServerSentEvent.sendEventId eventId)

        if options.Retry <> Consts.DefaultSseRetryDuration then
            writer |> ServerSentEvent.sendRetry options.Retry

        writer
        |> ServerSentEvent.sendDataBytesLine
            Bytes.DatalineMode
            (ElementPatchMode.Remove |> Bytes.ElementPatchMode.toBytes)

        writer |> ServerSentEvent.sendDataStringLine Bytes.DatalineSelector selector

        if options.UseViewTransition <> Consts.DefaultElementsUseViewTransitions then
            writer
            |> ServerSentEvent.sendDataBytesLine
                Bytes.DatalineUseViewTransition
                (if options.UseViewTransition then
                     Bytes.bTrue
                 else
                     Bytes.bFalse)

        writer |> ServerSentEvent.writeNewline

        writer.FlushAsync(cancellationToken).AsTask() :> Task

    static member RemoveElementAsync
        (
            httpResponse: HttpResponse,
            writer: System.IO.TextWriter -> Task,
            options: RemoveElementOptions,
            cancellationToken: CancellationToken
        ) =
        let bufWriter = httpResponse.BodyWriter
        bufWriter |> ServerSentEvent.sendEventType Bytes.EventTypePatchElements

        options.EventId
        |> ValueOption.iter (fun eventId -> bufWriter |> ServerSentEvent.sendEventId eventId)

        if options.Retry <> Consts.DefaultSseRetryDuration then
            bufWriter |> ServerSentEvent.sendRetry options.Retry

        task {
            use sseWriter =
                new SseDataLineWriter(bufWriter, Bytes.DatalineSelector, cancellationToken)

            do! writer sseWriter
            sseWriter.Flush()

            bufWriter
            |> ServerSentEvent.sendDataBytesLine
                Bytes.DatalineMode
                (ElementPatchMode.Remove |> Bytes.ElementPatchMode.toBytes)

            if options.UseViewTransition <> Consts.DefaultElementsUseViewTransitions then
                bufWriter
                |> ServerSentEvent.sendDataBytesLine
                    Bytes.DatalineUseViewTransition
                    (if options.UseViewTransition then
                         Bytes.bTrue
                     else
                         Bytes.bFalse)

            bufWriter |> ServerSentEvent.writeNewline
            return! bufWriter.FlushAsync(cancellationToken).AsTask() :> Task
        }

    static member PatchSignalsAsync
        (
            httpResponse: HttpResponse,
            signals: Signals,
            options: PatchSignalsOptions,
            cancellationToken: CancellationToken
        ) =
        let writer = httpResponse.BodyWriter
        writer |> ServerSentEvent.sendEventType Bytes.EventTypePatchSignals

        options.EventId
        |> ValueOption.iter (fun eventId -> writer |> ServerSentEvent.sendEventId eventId)

        if options.Retry <> Consts.DefaultSseRetryDuration then
            writer |> ServerSentEvent.sendRetry options.Retry

        if options.OnlyIfMissing <> Consts.DefaultPatchSignalsOnlyIfMissing then
            writer
            |> ServerSentEvent.sendDataBytesLine
                Bytes.DatalineOnlyIfMissing
                (if options.OnlyIfMissing then Bytes.bTrue else Bytes.bFalse)

        for segment in String.splitLinesToSegments signals do
            writer |> ServerSentEvent.sendDataSegmentLine Bytes.DatalineSignals segment

        writer |> ServerSentEvent.writeNewline

        writer.FlushAsync(cancellationToken).AsTask() :> Task

    static member PatchSignalsAsync
        (
            httpResponse: HttpResponse,
            writer: System.IO.TextWriter -> Task,
            options: PatchSignalsOptions,
            cancellationToken: CancellationToken
        ) =
        let bufWriter = httpResponse.BodyWriter
        bufWriter |> ServerSentEvent.sendEventType Bytes.EventTypePatchSignals

        options.EventId
        |> ValueOption.iter (fun eventId -> bufWriter |> ServerSentEvent.sendEventId eventId)

        if options.Retry <> Consts.DefaultSseRetryDuration then
            bufWriter |> ServerSentEvent.sendRetry options.Retry

        if options.OnlyIfMissing <> Consts.DefaultPatchSignalsOnlyIfMissing then
            bufWriter
            |> ServerSentEvent.sendDataBytesLine
                Bytes.DatalineOnlyIfMissing
                (if options.OnlyIfMissing then Bytes.bTrue else Bytes.bFalse)

        task {
            use sseWriter =
                new SseDataLineWriter(bufWriter, Bytes.DatalineSignals, cancellationToken)

            do! writer sseWriter
            sseWriter.Flush()
            bufWriter |> ServerSentEvent.writeNewline
            return! bufWriter.FlushAsync(cancellationToken).AsTask() :> Task
        }

    static member ExecuteScriptAsync
        (httpResponse: HttpResponse, script: string, options: ExecuteScriptOptions, cancellationToken: CancellationToken) =
        let writer = httpResponse.BodyWriter
        writer |> ServerSentEvent.sendEventType Bytes.EventTypePatchElements

        options.EventId
        |> ValueOption.iter (fun eventId -> writer |> ServerSentEvent.sendEventId eventId)

        if options.Retry <> Consts.DefaultSseRetryDuration then
            writer |> ServerSentEvent.sendRetry options.Retry

        writer |> ServerSentEvent.sendDataBytesLine Bytes.DatalineSelector Bytes.bBody

        writer
        |> ServerSentEvent.sendDataBytesLine Bytes.DatalineMode Bytes.ElementPatchMode.bAppend

        // <script ...> with verbatim attributes written as-is
        writeScriptOpenTag writer options.AutoRemove options.Attributes

        // script
        for segment in String.splitLinesToSegments script do
            writer |> ServerSentEvent.sendDataSegmentLine Bytes.DatalineElements segment

        // </script>
        writer
        |> ServerSentEvent.sendDataBytesLine Bytes.DatalineElements Bytes.bCloseScript

        writer |> ServerSentEvent.writeNewline

        writer.FlushAsync(cancellationToken).AsTask() :> Task

    static member ExecuteScriptAsync
        (
            httpResponse: HttpResponse,
            writer: System.IO.TextWriter -> Task,
            options: ExecuteScriptOptions,
            cancellationToken: CancellationToken
        ) =
        let bufWriter = httpResponse.BodyWriter
        bufWriter |> ServerSentEvent.sendEventType Bytes.EventTypePatchElements

        options.EventId
        |> ValueOption.iter (fun eventId -> bufWriter |> ServerSentEvent.sendEventId eventId)

        if options.Retry <> Consts.DefaultSseRetryDuration then
            bufWriter |> ServerSentEvent.sendRetry options.Retry

        bufWriter
        |> ServerSentEvent.sendDataBytesLine Bytes.DatalineSelector Bytes.bBody

        bufWriter
        |> ServerSentEvent.sendDataBytesLine Bytes.DatalineMode Bytes.ElementPatchMode.bAppend

        // <script ...> with verbatim attributes written as-is
        writeScriptOpenTag bufWriter options.AutoRemove options.Attributes

        task {
            // script body
            use sseWriter =
                new SseDataLineWriter(bufWriter, Bytes.DatalineElements, cancellationToken)

            do! writer sseWriter
            sseWriter.Flush()

            // </script>
            bufWriter
            |> ServerSentEvent.sendDataBytesLine Bytes.DatalineElements Bytes.bCloseScript

            bufWriter |> ServerSentEvent.writeNewline
            return! bufWriter.FlushAsync(cancellationToken).AsTask() :> Task
        }

    // Stream-based overloads (Stream -> Task)
    static member PatchElementsAsync
        (
            httpResponse: HttpResponse,
            writer: System.IO.Stream -> Task,
            options: PatchElementsOptions,
            cancellationToken: CancellationToken
        ) =
        let bufWriter = httpResponse.BodyWriter
        bufWriter |> ServerSentEvent.sendEventType Bytes.EventTypePatchElements

        options.EventId
        |> ValueOption.iter (fun eventId -> bufWriter |> ServerSentEvent.sendEventId eventId)

        if options.Retry <> Consts.DefaultSseRetryDuration then
            bufWriter |> ServerSentEvent.sendRetry options.Retry

        options.Selector
        |> ValueOption.iter (fun selector ->
            bufWriter |> ServerSentEvent.sendDataStringLine Bytes.DatalineSelector selector)

        if options.PatchMode <> Consts.DefaultElementPatchMode then
            bufWriter
            |> ServerSentEvent.sendDataBytesLine
                Bytes.DatalineMode
                (options.PatchMode |> Bytes.ElementPatchMode.toBytes)

        if options.UseViewTransition <> Consts.DefaultElementsUseViewTransitions then
            bufWriter
            |> ServerSentEvent.sendDataBytesLine
                Bytes.DatalineUseViewTransition
                (if options.UseViewTransition then
                     Bytes.bTrue
                 else
                     Bytes.bFalse)

        if options.Namespace <> Consts.DefaultPatchElementNamespace then
            bufWriter
            |> ServerSentEvent.sendDataBytesLine
                Bytes.DatalineNamespace
                (options.Namespace |> Bytes.PatchElementNamespace.toBytes)

        task {
            use sseStream =
                new SseDataLineStream(bufWriter, Bytes.DatalineElements, cancellationToken)

            do! writer sseStream
            sseStream.Flush()
            bufWriter |> ServerSentEvent.writeNewline
            return! bufWriter.FlushAsync(cancellationToken).AsTask() :> Task
        }

    static member RemoveElementAsync
        (
            httpResponse: HttpResponse,
            writer: System.IO.Stream -> Task,
            options: RemoveElementOptions,
            cancellationToken: CancellationToken
        ) =
        let bufWriter = httpResponse.BodyWriter
        bufWriter |> ServerSentEvent.sendEventType Bytes.EventTypePatchElements

        options.EventId
        |> ValueOption.iter (fun eventId -> bufWriter |> ServerSentEvent.sendEventId eventId)

        if options.Retry <> Consts.DefaultSseRetryDuration then
            bufWriter |> ServerSentEvent.sendRetry options.Retry

        task {
            use sseStream =
                new SseDataLineStream(bufWriter, Bytes.DatalineSelector, cancellationToken)

            do! writer sseStream
            sseStream.Flush()

            bufWriter
            |> ServerSentEvent.sendDataBytesLine
                Bytes.DatalineMode
                (ElementPatchMode.Remove |> Bytes.ElementPatchMode.toBytes)

            if options.UseViewTransition <> Consts.DefaultElementsUseViewTransitions then
                bufWriter
                |> ServerSentEvent.sendDataBytesLine
                    Bytes.DatalineUseViewTransition
                    (if options.UseViewTransition then
                         Bytes.bTrue
                     else
                         Bytes.bFalse)

            bufWriter |> ServerSentEvent.writeNewline
            return! bufWriter.FlushAsync(cancellationToken).AsTask() :> Task
        }

    static member PatchSignalsAsync
        (
            httpResponse: HttpResponse,
            writer: System.IO.Stream -> Task,
            options: PatchSignalsOptions,
            cancellationToken: CancellationToken
        ) =
        let bufWriter = httpResponse.BodyWriter
        bufWriter |> ServerSentEvent.sendEventType Bytes.EventTypePatchSignals

        options.EventId
        |> ValueOption.iter (fun eventId -> bufWriter |> ServerSentEvent.sendEventId eventId)

        if options.Retry <> Consts.DefaultSseRetryDuration then
            bufWriter |> ServerSentEvent.sendRetry options.Retry

        if options.OnlyIfMissing <> Consts.DefaultPatchSignalsOnlyIfMissing then
            bufWriter
            |> ServerSentEvent.sendDataBytesLine
                Bytes.DatalineOnlyIfMissing
                (if options.OnlyIfMissing then Bytes.bTrue else Bytes.bFalse)

        task {
            use sseStream =
                new SseDataLineStream(bufWriter, Bytes.DatalineSignals, cancellationToken)

            do! writer sseStream
            sseStream.Flush()
            bufWriter |> ServerSentEvent.writeNewline
            return! bufWriter.FlushAsync(cancellationToken).AsTask() :> Task
        }

    static member ExecuteScriptAsync
        (
            httpResponse: HttpResponse,
            writer: System.IO.Stream -> Task,
            options: ExecuteScriptOptions,
            cancellationToken: CancellationToken
        ) =
        let bufWriter = httpResponse.BodyWriter
        bufWriter |> ServerSentEvent.sendEventType Bytes.EventTypePatchElements

        options.EventId
        |> ValueOption.iter (fun eventId -> bufWriter |> ServerSentEvent.sendEventId eventId)

        if options.Retry <> Consts.DefaultSseRetryDuration then
            bufWriter |> ServerSentEvent.sendRetry options.Retry

        bufWriter
        |> ServerSentEvent.sendDataBytesLine Bytes.DatalineSelector Bytes.bBody

        bufWriter
        |> ServerSentEvent.sendDataBytesLine Bytes.DatalineMode Bytes.ElementPatchMode.bAppend

        writeScriptOpenTag bufWriter options.AutoRemove options.Attributes

        task {
            use sseStream =
                new SseDataLineStream(bufWriter, Bytes.DatalineElements, cancellationToken)

            do! writer sseStream
            sseStream.Flush()

            bufWriter
            |> ServerSentEvent.sendDataBytesLine Bytes.DatalineElements Bytes.bCloseScript

            bufWriter |> ServerSentEvent.writeNewline
            return! bufWriter.FlushAsync(cancellationToken).AsTask() :> Task
        }

    static member ReadSignalsAsync(httpRequest: HttpRequest, cancellationToken: CancellationToken) =
        backgroundTask {
            if HttpMethods.IsGet(httpRequest.Method) then
                match httpRequest.Query.TryGetValue(Consts.DatastarKey) with
                | true, stringValues when stringValues.Count > 0 -> return stringValues[0]
                | _ -> return ""
            else
                try
                    use readResult = new StreamReader(httpRequest.Body)
                    return! readResult.ReadToEndAsync(cancellationToken)
                with
                | :? IOException -> return ""
                | :? JsonException -> return ""
        }

    static member ReadSignalsAsync<'T>
        (httpRequest: HttpRequest, jsonSerializerOptions: JsonSerializerOptions, cancellationToken: CancellationToken)
        =
        task {
            try
                if HttpMethods.IsGet(httpRequest.Method) then
                    match httpRequest.Query.TryGetValue(Consts.DatastarKey) with
                    | true, stringValues when stringValues.Count > 0 ->
                        return ValueSome(JsonSerializer.Deserialize<'T>(stringValues[0], jsonSerializerOptions))
                    | _ -> return ValueNone
                else
                    let! t =
                        JsonSerializer.DeserializeAsync<'T>(httpRequest.Body, jsonSerializerOptions, cancellationToken)

                    return (ValueSome t)
            with
            | :? IOException -> return ValueNone
            | :? JsonException -> return ValueNone
        }

    //
    // SHORT HAND METHODS
    //
    static member StartServerEventStreamAsync(httpResponse) =
        ServerSentEventGenerator.StartServerEventStreamAsync(httpResponse, httpResponse.HttpContext.RequestAborted)

    static member PatchElementsAsync(httpResponse: HttpResponse, elements: string, options: PatchElementsOptions) =
        ServerSentEventGenerator.PatchElementsAsync(
            httpResponse,
            elements,
            options,
            httpResponse.HttpContext.RequestAborted
        )

    static member PatchElementsAsync(httpResponse: HttpResponse, elements: string) =
        ServerSentEventGenerator.PatchElementsAsync(
            httpResponse,
            elements,
            PatchElementsOptions.Defaults,
            httpResponse.HttpContext.RequestAborted
        )

    static member RemoveElementAsync(httpResponse: HttpResponse, selector: Selector, options: RemoveElementOptions) =
        ServerSentEventGenerator.RemoveElementAsync(
            httpResponse,
            selector,
            options,
            httpResponse.HttpContext.RequestAborted
        )

    static member RemoveElementAsync(httpResponse: HttpResponse, selector: Selector) =
        ServerSentEventGenerator.RemoveElementAsync(
            httpResponse,
            selector,
            RemoveElementOptions.Defaults,
            httpResponse.HttpContext.RequestAborted
        )

    static member PatchSignalsAsync(httpResponse: HttpResponse, signals: Signals, options: PatchSignalsOptions) =
        ServerSentEventGenerator.PatchSignalsAsync(
            httpResponse,
            signals,
            options,
            httpResponse.HttpContext.RequestAborted
        )

    static member PatchSignalsAsync(httpResponse: HttpResponse, signals: Signals) =
        ServerSentEventGenerator.PatchSignalsAsync(
            httpResponse,
            signals,
            PatchSignalsOptions.Defaults,
            httpResponse.HttpContext.RequestAborted
        )

    static member ExecuteScriptAsync(httpResponse: HttpResponse, script: string, options: ExecuteScriptOptions) =
        ServerSentEventGenerator.ExecuteScriptAsync(
            httpResponse,
            script,
            options,
            httpResponse.HttpContext.RequestAborted
        )

    static member ExecuteScriptAsync(httpResponse: HttpResponse, script: string) =
        ServerSentEventGenerator.ExecuteScriptAsync(
            httpResponse,
            script,
            ExecuteScriptOptions.Defaults,
            httpResponse.HttpContext.RequestAborted
        )
    // Stream-based shorthand overloads
    static member PatchElementsAsync
        (httpResponse: HttpResponse, writer: System.IO.TextWriter -> Task, options: PatchElementsOptions)
        =
        ServerSentEventGenerator.PatchElementsAsync(
            httpResponse,
            writer,
            options,
            httpResponse.HttpContext.RequestAborted
        )

    static member PatchElementsAsync(httpResponse: HttpResponse, writer: System.IO.TextWriter -> Task) =
        ServerSentEventGenerator.PatchElementsAsync(
            httpResponse,
            writer,
            PatchElementsOptions.Defaults,
            httpResponse.HttpContext.RequestAborted
        )

    static member RemoveElementAsync
        (httpResponse: HttpResponse, writer: System.IO.TextWriter -> Task, options: RemoveElementOptions)
        =
        ServerSentEventGenerator.RemoveElementAsync(
            httpResponse,
            writer,
            options,
            httpResponse.HttpContext.RequestAborted
        )

    static member RemoveElementAsync(httpResponse: HttpResponse, writer: System.IO.TextWriter -> Task) =
        ServerSentEventGenerator.RemoveElementAsync(
            httpResponse,
            writer,
            RemoveElementOptions.Defaults,
            httpResponse.HttpContext.RequestAborted
        )

    static member PatchSignalsAsync
        (httpResponse: HttpResponse, writer: System.IO.TextWriter -> Task, options: PatchSignalsOptions)
        =
        ServerSentEventGenerator.PatchSignalsAsync(
            httpResponse,
            writer,
            options,
            httpResponse.HttpContext.RequestAborted
        )

    static member PatchSignalsAsync(httpResponse: HttpResponse, writer: System.IO.TextWriter -> Task) =
        ServerSentEventGenerator.PatchSignalsAsync(
            httpResponse,
            writer,
            PatchSignalsOptions.Defaults,
            httpResponse.HttpContext.RequestAborted
        )

    static member ExecuteScriptAsync
        (httpResponse: HttpResponse, writer: System.IO.TextWriter -> Task, options: ExecuteScriptOptions)
        =
        ServerSentEventGenerator.ExecuteScriptAsync(
            httpResponse,
            writer,
            options,
            httpResponse.HttpContext.RequestAborted
        )

    static member ExecuteScriptAsync(httpResponse: HttpResponse, writer: System.IO.TextWriter -> Task) =
        ServerSentEventGenerator.ExecuteScriptAsync(
            httpResponse,
            writer,
            ExecuteScriptOptions.Defaults,
            httpResponse.HttpContext.RequestAborted
        )
    // Stream-based (Stream -> Task) shorthand overloads
    static member PatchElementsAsync
        (httpResponse: HttpResponse, writer: System.IO.Stream -> Task, options: PatchElementsOptions)
        =
        ServerSentEventGenerator.PatchElementsAsync(
            httpResponse,
            writer,
            options,
            httpResponse.HttpContext.RequestAborted
        )

    static member PatchElementsAsync(httpResponse: HttpResponse, writer: System.IO.Stream -> Task) =
        ServerSentEventGenerator.PatchElementsAsync(
            httpResponse,
            writer,
            PatchElementsOptions.Defaults,
            httpResponse.HttpContext.RequestAborted
        )

    static member RemoveElementAsync
        (httpResponse: HttpResponse, writer: System.IO.Stream -> Task, options: RemoveElementOptions)
        =
        ServerSentEventGenerator.RemoveElementAsync(
            httpResponse,
            writer,
            options,
            httpResponse.HttpContext.RequestAborted
        )

    static member RemoveElementAsync(httpResponse: HttpResponse, writer: System.IO.Stream -> Task) =
        ServerSentEventGenerator.RemoveElementAsync(
            httpResponse,
            writer,
            RemoveElementOptions.Defaults,
            httpResponse.HttpContext.RequestAborted
        )

    static member PatchSignalsAsync
        (httpResponse: HttpResponse, writer: System.IO.Stream -> Task, options: PatchSignalsOptions)
        =
        ServerSentEventGenerator.PatchSignalsAsync(
            httpResponse,
            writer,
            options,
            httpResponse.HttpContext.RequestAborted
        )

    static member PatchSignalsAsync(httpResponse: HttpResponse, writer: System.IO.Stream -> Task) =
        ServerSentEventGenerator.PatchSignalsAsync(
            httpResponse,
            writer,
            PatchSignalsOptions.Defaults,
            httpResponse.HttpContext.RequestAborted
        )

    static member ExecuteScriptAsync
        (httpResponse: HttpResponse, writer: System.IO.Stream -> Task, options: ExecuteScriptOptions)
        =
        ServerSentEventGenerator.ExecuteScriptAsync(
            httpResponse,
            writer,
            options,
            httpResponse.HttpContext.RequestAborted
        )

    static member ExecuteScriptAsync(httpResponse: HttpResponse, writer: System.IO.Stream -> Task) =
        ServerSentEventGenerator.ExecuteScriptAsync(
            httpResponse,
            writer,
            ExecuteScriptOptions.Defaults,
            httpResponse.HttpContext.RequestAborted
        )

    static member ReadSignalsAsync(httpRequest) =
        ServerSentEventGenerator.ReadSignalsAsync(
            httpRequest,
            cancellationToken = httpRequest.HttpContext.RequestAborted
        )

    static member ReadSignalsAsync<'T>(httpRequest, jsonSerializerOptions) =
        ServerSentEventGenerator.ReadSignalsAsync<'T>(
            httpRequest,
            jsonSerializerOptions,
            httpRequest.HttpContext.RequestAborted
        )

    static member ReadSignalsAsync<'T>(httpRequest) =
        ServerSentEventGenerator.ReadSignalsAsync<'T>(
            httpRequest,
            defaultJsonOptions,
            httpRequest.HttpContext.RequestAborted
        )
