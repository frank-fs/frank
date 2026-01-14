module DatastarTests

open System
open System.IO
open System.Text
open System.Threading.Tasks
open Microsoft.AspNetCore.Http
open Expecto
open Frank.Builder
open Frank.Datastar

/// Helper to create a mock HttpContext with in-memory response stream
let createMockContext () =
    let context = DefaultHttpContext()
    let responseStream = new MemoryStream()
    context.Response.Body <- responseStream
    context.Response.Headers.Append("Content-Type", "text/plain")
    context

/// Helper to read the response body as a string
let getResponseBody (context: HttpContext) =
    context.Response.Body.Position <- 0L
    use reader = new StreamReader(context.Response.Body)
    reader.ReadToEnd()

/// Helper to create a mock HttpRequest with a body
let setRequestBody (context: HttpContext) (body: string) =
    let requestStream = new MemoryStream(Encoding.UTF8.GetBytes(body))
    context.Request.Body <- requestStream
    context.Request.ContentType <- "application/json"

[<Tests>]
let datastarTests =
    testList "Datastar ResourceBuilder Extensions" [
        
        testCase "Datastar() starts SSE stream and executes multiple operations" <| fun () ->
            // Arrange
            let context = createMockContext()
            let mutable patchCalled = false
            let mutable signalsCalled = false
            
            let resource = 
                ResourceBuilder("/test") {
                    datastar (fun ctx -> task {
                        // Execute multiple Datastar operations
                        do! StarFederation.Datastar.FSharp.ServerSentEventGenerator.PatchElementsAsync(ctx.Response, "<div>Test</div>")
                        patchCalled <- true
                        do! StarFederation.Datastar.FSharp.ServerSentEventGenerator.PatchSignalsAsync(ctx.Response, """{"count": 5}""")
                        signalsCalled <- true
                    })
                }
            
            // Act
            let endpoint = resource.Endpoints.[0] :?> Microsoft.AspNetCore.Routing.RouteEndpoint
            endpoint.RequestDelegate.Invoke(context).Wait()
            
            // Assert
            let responseBody = getResponseBody context
            Expect.isTrue patchCalled "PatchElements should be called"
            Expect.isTrue signalsCalled "PatchSignals should be called"
            Expect.stringContains responseBody "event: datastar-patch-elements" "Response should contain patch-elements event"
            Expect.stringContains responseBody "event: datastar-patch-signals" "Response should contain patch-signals event"
            Expect.stringContains responseBody "<div>Test</div>" "Response should contain HTML content"
            Expect.stringContains responseBody "\"count\": 5" "Response should contain signal data"
        
        testCase "PatchElements() with string sends expected HTML in SSE stream" <| fun () ->
            // Arrange
            let context = createMockContext()
            let htmlContent = "<div id='test'>Hello World</div>"
            
            let resource = 
                ResourceBuilder("/test") {
                    patchElements htmlContent
                }
            
            // Act
            let endpoint = resource.Endpoints.[0] :?> Microsoft.AspNetCore.Routing.RouteEndpoint
            endpoint.RequestDelegate.Invoke(context).Wait()
            
            // Assert
            let responseBody = getResponseBody context
            Expect.stringContains responseBody "event: datastar-patch-elements" "Response should contain patch-elements event"
            Expect.stringContains responseBody htmlContent "Response should contain the HTML content"
        
        testCase "PatchElements() with sync function sends expected HTML in SSE stream" <| fun () ->
            // Arrange
            let context = createMockContext()
            let htmlFunction (ctx: HttpContext) = 
                $"<div>Context Path: {ctx.Request.Path}</div>"
            
            context.Request.Path <- PathString("/test-path")
            
            let resource = 
                ResourceBuilder("/test") {
                    patchElements htmlFunction
                }
            
            // Act
            let endpoint = resource.Endpoints.[0] :?> Microsoft.AspNetCore.Routing.RouteEndpoint
            endpoint.RequestDelegate.Invoke(context).Wait()
            
            // Assert
            let responseBody = getResponseBody context
            Expect.stringContains responseBody "event: datastar-patch-elements" "Response should contain patch-elements event"
            Expect.stringContains responseBody "Context Path: /test-path" "Response should contain dynamically generated HTML"
        
        testCase "PatchElements() with async function sends expected HTML in SSE stream" <| fun () ->
            // Arrange
            let context = createMockContext()
            let asyncHtmlFunction (ctx: HttpContext) = 
                task {
                    do! Task.Delay(10) // Simulate async work
                    return $"<div>Async Content from {ctx.Request.Path}</div>"
                }
            
            context.Request.Path <- PathString("/async-test")
            
            let resource = 
                ResourceBuilder("/test") {
                    patchElements asyncHtmlFunction
                }
            
            // Act
            let endpoint = resource.Endpoints.[0] :?> Microsoft.AspNetCore.Routing.RouteEndpoint
            endpoint.RequestDelegate.Invoke(context).Wait()
            
            // Assert
            let responseBody = getResponseBody context
            Expect.stringContains responseBody "event: datastar-patch-elements" "Response should contain patch-elements event"
            Expect.stringContains responseBody "Async Content from /async-test" "Response should contain async-generated HTML"
        
        testCase "RemoveElement() sends correct remove element command for CSS selector" <| fun () ->
            // Arrange
            let context = createMockContext()
            let selector = "#obsolete-element"
            
            let resource = 
                ResourceBuilder("/test") {
                    removeElement selector
                }
            
            // Act
            let endpoint = resource.Endpoints.[0] :?> Microsoft.AspNetCore.Routing.RouteEndpoint
            endpoint.RequestDelegate.Invoke(context).Wait()
            
            // Assert
            let responseBody = getResponseBody context
            Expect.stringContains responseBody "event: datastar-patch-elements" "Response should contain patch-elements event"
            Expect.stringContains responseBody "mode remove" "Response should contain remove mode"
            Expect.stringContains responseBody selector "Response should contain the CSS selector"
        
        testCase "PatchSignals() with string sends expected signal JSON in SSE stream" <| fun () ->
            // Arrange
            let context = createMockContext()
            let signalsJson = """{"counter": 42, "message": "test"}"""
            
            let resource = 
                ResourceBuilder("/test") {
                    patchSignals signalsJson
                }
            
            // Act
            let endpoint = resource.Endpoints.[0] :?> Microsoft.AspNetCore.Routing.RouteEndpoint
            endpoint.RequestDelegate.Invoke(context).Wait()
            
            // Assert
            let responseBody = getResponseBody context
            Expect.stringContains responseBody "event: datastar-patch-signals" "Response should contain patch-signals event"
            Expect.stringContains responseBody "\"counter\": 42" "Response should contain counter signal"
            Expect.stringContains responseBody "\"message\": \"test\"" "Response should contain message signal"
        
        testCase "PatchSignals() with function sends expected signal JSON in SSE stream" <| fun () ->
            // Arrange
            let context = createMockContext()
            let signalsFunction (ctx: HttpContext) = 
                let path = ctx.Request.Path.Value
                $"""{{\"path\": \"{path}\", \"timestamp\": \"{DateTime.UtcNow:o}\"}}"""
            
            context.Request.Path <- PathString("/signal-test")
            
            let resource = 
                ResourceBuilder("/test") {
                    patchSignals signalsFunction
                }
            
            // Act
            let endpoint = resource.Endpoints.[0] :?> Microsoft.AspNetCore.Routing.RouteEndpoint
            endpoint.RequestDelegate.Invoke(context).Wait()
            
            // Assert
            let responseBody = getResponseBody context
            Expect.stringContains responseBody "event: datastar-patch-signals" "Response should contain patch-signals event"
            Expect.stringContains responseBody "/signal-test" "Response should contain path signal"
            Expect.stringContains responseBody "timestamp" "Response should contain timestamp signal"
        
        testCase "ReadSignals() correctly reads signals from client request and forwards to handler" <| fun () ->
            // Arrange
            let context = createMockContext()
            let requestSignals = """{"userId": 123, "action": "click"}"""
            setRequestBody context requestSignals
            
            let mutable capturedSignals: voption<{| userId: int; action: string |}> = ValueNone
            let mutable handlerCalled = false
            
            let resource = 
                ResourceBuilder("/test") {
                    readSignals (fun ctx signals -> task {
                        capturedSignals <- signals
                        handlerCalled <- true
                        
                        // Optionally send a response back
                        match signals with
                        | ValueSome data ->
                            do! StarFederation.Datastar.FSharp.ServerSentEventGenerator.PatchSignalsAsync(
                                ctx.Response, 
                                $"""{{\"received\": true, \"userId\": {data.userId}}}""")
                        | ValueNone ->
                            do! StarFederation.Datastar.FSharp.ServerSentEventGenerator.PatchSignalsAsync(
                                ctx.Response, 
                                """{"error": "No signals received"}""")
                    })
                }
            
            // Act
            let endpoint = resource.Endpoints.[0] :?> Microsoft.AspNetCore.Routing.RouteEndpoint
            endpoint.RequestDelegate.Invoke(context).Wait()
            
            // Assert
            Expect.isTrue handlerCalled "Handler function should be called"
            Expect.isTrue capturedSignals.IsSome "Signals should be successfully deserialized"
            
            match capturedSignals with
            | ValueSome signals ->
                Expect.equal signals.userId 123 "UserId should match"
                Expect.equal signals.action "click" "Action should match"
            | ValueNone ->
                failtest "Signals should have been captured"
            
            let responseBody = getResponseBody context
            Expect.stringContains responseBody "event: datastar-patch-signals" "Response should contain patch-signals event"
            Expect.stringContains responseBody "received" "Response should confirm receipt"
            Expect.stringContains responseBody "123" "Response should include userId"
        
        testCase "ReadSignals() handles invalid JSON gracefully" <| fun () ->
            // Arrange
            let context = createMockContext()
            let invalidJson = """{"invalid": json"""
            setRequestBody context invalidJson
            
            let mutable capturedSignals: voption<{| userId: int |}> = ValueSome {| userId = -1 |}
            
            let resource = 
                ResourceBuilder("/test") {
                    readSignals (fun ctx signals -> task {
                        capturedSignals <- signals
                    })
                }
            
            // Act
            let endpoint = resource.Endpoints.[0] :?> Microsoft.AspNetCore.Routing.RouteEndpoint
            endpoint.RequestDelegate.Invoke(context).Wait()
            
            // Assert
            Expect.isTrue capturedSignals.IsNone "Invalid JSON should result in ValueNone"
    ]
