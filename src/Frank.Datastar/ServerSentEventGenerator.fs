namespace Frank.Datastar

open System.IO
open System.Text
open System.Text.Json
open System.Threading
open System.Threading.Tasks
open Microsoft.AspNetCore.Http

type ServerSentEventGenerator =
    static member StartServerEventStreamAsync(httpResponse:HttpResponse, cancellationToken:CancellationToken) =
        let task = backgroundTask {
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

    static member PatchElementsAsync(httpResponse:HttpResponse, elements:string, options:PatchElementsOptions, cancellationToken:CancellationToken) =
        let writer = httpResponse.BodyWriter
        writer |> ServerSentEvent.sendEventType Bytes.EventTypePatchElements
        options.EventId |> ValueOption.iter (fun eventId -> writer |> ServerSentEvent.sendEventId eventId)
        if options.Retry <> Consts.DefaultSseRetryDuration then writer |> ServerSentEvent.sendRetry options.Retry

        options.Selector |> ValueOption.iter (fun selector -> writer |> ServerSentEvent.sendDataStringLine Bytes.DatalineSelector selector)

        if options.PatchMode <> Consts.DefaultElementPatchMode then
            writer |> ServerSentEvent.sendDataBytesLine Bytes.DatalineMode (options.PatchMode |> Bytes.ElementPatchMode.toBytes)

        if options.UseViewTransition <> Consts.DefaultElementsUseViewTransitions then
            writer |> ServerSentEvent.sendDataBytesLine Bytes.DatalineUseViewTransition (if options.UseViewTransition then Bytes.bTrue else Bytes.bFalse)

        if options.Namespace <> Consts.DefaultPatchElementNamespace then
            writer |> ServerSentEvent.sendDataBytesLine Bytes.DatalineNamespace (options.Namespace |> Bytes.PatchElementNamespace.toBytes)

        for segment in String.splitLinesToSegments elements do
            writer |> ServerSentEvent.sendDataSegmentLine Bytes.DatalineElements segment

        writer |> ServerSentEvent.writeNewline

        writer.FlushAsync(cancellationToken).AsTask() :> Task

    static member RemoveElementAsync(httpResponse:HttpResponse, selector:Selector, options:RemoveElementOptions, cancellationToken:CancellationToken) =
        let writer = httpResponse.BodyWriter
        writer |> ServerSentEvent.sendEventType Bytes.EventTypePatchElements
        options.EventId |> ValueOption.iter (fun eventId -> writer |> ServerSentEvent.sendEventId eventId)
        if options.Retry <> Consts.DefaultSseRetryDuration then writer |> ServerSentEvent.sendRetry options.Retry

        writer |> ServerSentEvent.sendDataBytesLine Bytes.DatalineMode (ElementPatchMode.Remove |> Bytes.ElementPatchMode.toBytes)

        writer |> ServerSentEvent.sendDataStringLine Bytes.DatalineSelector selector

        if options.UseViewTransition <> Consts.DefaultElementsUseViewTransitions then
            writer |> ServerSentEvent.sendDataBytesLine Bytes.DatalineUseViewTransition (if options.UseViewTransition then Bytes.bTrue else Bytes.bFalse)

        writer |> ServerSentEvent.writeNewline

        writer.FlushAsync(cancellationToken).AsTask() :> Task

    static member PatchSignalsAsync(httpResponse:HttpResponse, signals:Signals, options:PatchSignalsOptions, cancellationToken:CancellationToken) =
        let writer = httpResponse.BodyWriter
        writer |> ServerSentEvent.sendEventType Bytes.EventTypePatchSignals
        options.EventId |> ValueOption.iter (fun eventId -> writer |> ServerSentEvent.sendEventId eventId)
        if options.Retry <> Consts.DefaultSseRetryDuration then writer |> ServerSentEvent.sendRetry options.Retry

        if options.OnlyIfMissing <> Consts.DefaultPatchSignalsOnlyIfMissing then
            writer |> ServerSentEvent.sendDataBytesLine Bytes.DatalineOnlyIfMissing (if options.OnlyIfMissing then Bytes.bTrue else Bytes.bFalse)

        for segment in String.splitLinesToSegments signals do
            writer |> ServerSentEvent.sendDataSegmentLine Bytes.DatalineSignals segment

        writer |> ServerSentEvent.writeNewline

        writer.FlushAsync(cancellationToken).AsTask() :> Task

    static member ExecuteScriptAsync(httpResponse:HttpResponse, script:string, options:ExecuteScriptOptions, cancellationToken:CancellationToken) =
        let writer = httpResponse.BodyWriter
        writer |> ServerSentEvent.sendEventType Bytes.EventTypePatchElements
        options.EventId |> ValueOption.iter (fun eventId -> writer |> ServerSentEvent.sendEventId eventId)
        if options.Retry <> Consts.DefaultSseRetryDuration then writer |> ServerSentEvent.sendRetry options.Retry

        writer |> ServerSentEvent.sendDataBytesLine Bytes.DatalineSelector Bytes.bBody

        writer |> ServerSentEvent.sendDataBytesLine Bytes.DatalineMode Bytes.ElementPatchMode.bAppend

        // <script ...> with verbatim attributes written as-is
        writer
        |> match (options.AutoRemove, options.Attributes) with
           | true, [||] -> ServerSentEvent.sendDataBytesLine Bytes.DatalineElements Bytes.bOpenScriptAutoRemove
           | false, [||] -> ServerSentEvent.sendDataBytesLine Bytes.DatalineElements Bytes.bOpenScript
           | true, attributes ->
               ServerSentEvent.sendDataStringSeqLine Bytes.DatalineElements
                   (seq { "<script"; Consts.ScriptDataEffectRemove; yield! attributes; ">" })
           | false, attributes ->
               ServerSentEvent.sendDataStringSeqLine Bytes.DatalineElements
                   (seq { "<script"; yield! attributes; ">" })

        // script
        for segment in String.splitLinesToSegments script do
            writer |> ServerSentEvent.sendDataSegmentLine Bytes.DatalineElements segment

        // </script>
        writer |> ServerSentEvent.sendDataBytesLine Bytes.DatalineElements Bytes.bCloseScript

        writer |> ServerSentEvent.writeNewline

        writer.FlushAsync(cancellationToken).AsTask() :> Task

    static member ReadSignalsAsync(httpRequest:HttpRequest, cancellationToken:CancellationToken) =
        backgroundTask {
            match httpRequest.Method with
            | "GET" ->
                match httpRequest.Query.TryGetValue(Consts.DatastarKey) with
                | true, stringValues when stringValues.Count > 0 -> return stringValues[0]
                | _ -> return ""
            | _ ->
                try
                    use readResult = new StreamReader(httpRequest.Body)
                    return! readResult.ReadToEndAsync(cancellationToken)
                with _ ->
                    return ""
        }

    static member ReadSignalsAsync<'T>(httpRequest:HttpRequest, jsonSerializerOptions:JsonSerializerOptions, cancellationToken:CancellationToken) =
        task {
            try
                match httpRequest.Method with
                | "GET" ->
                    match httpRequest.Query.TryGetValue(Consts.DatastarKey) with
                    | true, stringValues when stringValues.Count > 0 ->
                        return ValueSome (JsonSerializer.Deserialize<'T>(stringValues[0], jsonSerializerOptions))
                    | _ ->
                        return ValueNone
                | _ ->
                    let! t = JsonSerializer.DeserializeAsync<'T>(httpRequest.Body, jsonSerializerOptions, cancellationToken)
                    return (ValueSome t)
            with _ -> return ValueNone
        }

    //
    // SHORT HAND METHODS
    //
    static member StartServerEventStreamAsync(httpResponse) =
        ServerSentEventGenerator.StartServerEventStreamAsync(httpResponse, httpResponse.HttpContext.RequestAborted)
    static member PatchElementsAsync(httpResponse, elements, options) =
        ServerSentEventGenerator.PatchElementsAsync(httpResponse, elements, options, httpResponse.HttpContext.RequestAborted)
    static member PatchElementsAsync(httpResponse, elements) =
        ServerSentEventGenerator.PatchElementsAsync(httpResponse, elements, PatchElementsOptions.Defaults, httpResponse.HttpContext.RequestAborted)
    static member RemoveElementAsync(httpResponse, selector, options) =
        ServerSentEventGenerator.RemoveElementAsync(httpResponse, selector, options, httpResponse.HttpContext.RequestAborted)
    static member RemoveElementAsync(httpResponse, selector) =
        ServerSentEventGenerator.RemoveElementAsync(httpResponse, selector, RemoveElementOptions.Defaults, httpResponse.HttpContext.RequestAborted)
    static member PatchSignalsAsync(httpResponse, signals, options) =
        ServerSentEventGenerator.PatchSignalsAsync(httpResponse, signals, options, httpResponse.HttpContext.RequestAborted)
    static member PatchSignalsAsync(httpResponse, signals) =
        ServerSentEventGenerator.PatchSignalsAsync(httpResponse, signals, PatchSignalsOptions.Defaults, httpResponse.HttpContext.RequestAborted)
    static member ExecuteScriptAsync(httpResponse, script, options) =
        ServerSentEventGenerator.ExecuteScriptAsync(httpResponse, script, options, httpResponse.HttpContext.RequestAborted)
    static member ExecuteScriptAsync(httpResponse, script) =
        ServerSentEventGenerator.ExecuteScriptAsync(httpResponse, script, ExecuteScriptOptions.Defaults, httpResponse.HttpContext.RequestAborted)
    static member ReadSignalsAsync(httpRequest) =
        ServerSentEventGenerator.ReadSignalsAsync(httpRequest, cancellationToken = httpRequest.HttpContext.RequestAborted)
    static member ReadSignalsAsync<'T>(httpRequest, jsonSerializerOptions) =
        ServerSentEventGenerator.ReadSignalsAsync<'T>(httpRequest, jsonSerializerOptions, httpRequest.HttpContext.RequestAborted)
    static member ReadSignalsAsync<'T>(httpRequest) =
        let defaultJsonOptions = JsonSerializerOptions()
        defaultJsonOptions.PropertyNameCaseInsensitive <- true
        ServerSentEventGenerator.ReadSignalsAsync<'T>(httpRequest, defaultJsonOptions, httpRequest.HttpContext.RequestAborted)
