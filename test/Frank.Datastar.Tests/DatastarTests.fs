module DatastarTests

open System
open System.IO
open System.Text
open System.Threading
open System.Threading.Tasks
open Microsoft.AspNetCore.Http
open Expecto
open Frank.Builder
open Frank.Datastar
open StarFederation.Datastar.FSharp

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

        // ==========================================
        // User Story 1: Stream Multiple Updates
        // ==========================================

        testCase "US1: datastar operation starts SSE stream and executes multiple operations" <| fun () ->
            // Arrange
            let context = createMockContext()
            let mutable patchCount = 0

            let resource =
                ResourceBuilder("/test") {
                    datastar (fun ctx -> task {
                        do! Datastar.patchElements "<div>First</div>" ctx
                        patchCount <- patchCount + 1
                        do! Datastar.patchElements "<div>Second</div>" ctx
                        patchCount <- patchCount + 1
                    })
                }

            // Act
            let endpoint = resource.Endpoints.[0] :?> Microsoft.AspNetCore.Routing.RouteEndpoint
            endpoint.RequestDelegate.Invoke(context).Wait()

            // Assert
            let responseBody = getResponseBody context
            Expect.equal patchCount 2 "Should have patched twice"
            Expect.stringContains responseBody "event: datastar-patch-elements" "Response should contain patch-elements event"
            Expect.stringContains responseBody "<div>First</div>" "Response should contain first HTML"
            Expect.stringContains responseBody "<div>Second</div>" "Response should contain second HTML"

        testCase "US1: Multi-event streaming with 10 progressive updates" <| fun () ->
            // Arrange (T018)
            let context = createMockContext()
            let mutable updatesSent = 0

            let resource =
                ResourceBuilder("/progress") {
                    datastar (fun ctx -> task {
                        for i in 1..10 do
                            let html = $"""<div id="progress">{i * 10}%%</div>"""
                            do! Datastar.patchElements html ctx
                            updatesSent <- updatesSent + 1
                            // Note: In real scenarios there would be delays
                    })
                }

            // Act
            let endpoint = resource.Endpoints.[0] :?> Microsoft.AspNetCore.Routing.RouteEndpoint
            endpoint.RequestDelegate.Invoke(context).Wait()

            // Assert
            let responseBody = getResponseBody context
            Expect.equal updatesSent 10 "Should send 10 updates"
            Expect.stringContains responseBody "10%" "Should contain first update"
            Expect.stringContains responseBody "100%" "Should contain last update"

        testCase "US1: Client disconnection detected via cancellation token" <| fun () ->
            // Arrange (T020)
            let context = createMockContext()
            let cts = new CancellationTokenSource()
            // Set the RequestAborted token on the context
            context.RequestAborted <- cts.Token

            let mutable loopIterations = 0
            let mutable cancellationDetected = false

            let resource =
                ResourceBuilder("/stream") {
                    datastar (fun ctx -> task {
                        // Simulate a streaming loop that checks for cancellation
                        while not ctx.RequestAborted.IsCancellationRequested && loopIterations < 100 do
                            do! Datastar.patchElements $"<div>Update {loopIterations}</div>" ctx
                            loopIterations <- loopIterations + 1

                            // Cancel after 3 iterations to simulate client disconnect
                            if loopIterations = 3 then
                                cts.Cancel()

                        cancellationDetected <- ctx.RequestAborted.IsCancellationRequested
                    })
                }

            // Act
            let endpoint = resource.Endpoints.[0] :?> Microsoft.AspNetCore.Routing.RouteEndpoint
            endpoint.RequestDelegate.Invoke(context).Wait()

            // Assert
            Expect.equal loopIterations 3 "Should stop after 3 iterations when cancelled"
            Expect.isTrue cancellationDetected "Should detect cancellation via RequestAborted"

        testCase "US1: Single stream start per request (SC-002)" <| fun () ->
            // Arrange (T019)
            let context = createMockContext()

            let resource =
                ResourceBuilder("/test") {
                    datastar (fun ctx -> task {
                        // Stream is started ONCE by the datastar operation
                        // Multiple patches should work without re-starting
                        do! Datastar.patchElements "<div>1</div>" ctx
                        do! Datastar.patchElements "<div>2</div>" ctx
                        do! Datastar.patchElements "<div>3</div>" ctx
                    })
                }

            // Act
            let endpoint = resource.Endpoints.[0] :?> Microsoft.AspNetCore.Routing.RouteEndpoint
            endpoint.RequestDelegate.Invoke(context).Wait()

            // Assert - verify content-type is set once (SSE characteristic)
            let responseBody = getResponseBody context
            // Count occurrences of the SSE stream initialization markers
            let eventCount =
                responseBody.Split("event: datastar-patch-elements").Length - 1
            Expect.equal eventCount 3 "Should have exactly 3 events"

        testCase "US1: datastar with POST method" <| fun () ->
            // Arrange (T014a)
            let context = createMockContext()
            context.Request.Method <- HttpMethods.Post

            let resource =
                ResourceBuilder("/submit") {
                    datastar HttpMethods.Post (fun ctx -> task {
                        do! Datastar.patchElements "<div id='result'>Submitted!</div>" ctx
                    })
                }

            // Act
            let endpoint = resource.Endpoints.[0] :?> Microsoft.AspNetCore.Routing.RouteEndpoint

            // Verify the endpoint is registered for POST
            let httpMethodMetadata =
                endpoint.Metadata
                |> Seq.tryPick (fun m ->
                    match m with
                    | :? Microsoft.AspNetCore.Routing.HttpMethodMetadata as meta -> Some meta
                    | _ -> None)

            Expect.isSome httpMethodMetadata "Should have HTTP method metadata"
            let methods = httpMethodMetadata.Value.HttpMethods |> Seq.toList
            Expect.contains methods "POST" "Should be registered for POST"

            // Execute the handler
            endpoint.RequestDelegate.Invoke(context).Wait()

            // Assert
            let responseBody = getResponseBody context
            Expect.stringContains responseBody "Submitted!" "Response should contain HTML"

        // ==========================================
        // User Story 2: Read Client Signals
        // ==========================================

        testCase "US2: tryReadSignals correctly deserializes valid JSON" <| fun () ->
            // Arrange (T024)
            let context = createMockContext()
            let requestSignals = """{"userId": 123, "action": "click"}"""
            setRequestBody context requestSignals

            let mutable capturedSignals: voption<{| userId: int; action: string |}> = ValueNone

            let resource =
                ResourceBuilder("/test") {
                    datastar (fun ctx -> task {
                        let! signals = Datastar.tryReadSignals<{| userId: int; action: string |}> ctx
                        capturedSignals <- signals

                        match signals with
                        | ValueSome data ->
                            do! Datastar.patchElements $"<div>User: {data.userId}</div>" ctx
                        | ValueNone ->
                            do! Datastar.patchElements "<div>No data</div>" ctx
                    })
                }

            // Act
            let endpoint = resource.Endpoints.[0] :?> Microsoft.AspNetCore.Routing.RouteEndpoint
            endpoint.RequestDelegate.Invoke(context).Wait()

            // Assert
            Expect.isTrue capturedSignals.IsSome "Signals should be deserialized"
            match capturedSignals with
            | ValueSome signals ->
                Expect.equal signals.userId 123 "UserId should match"
                Expect.equal signals.action "click" "Action should match"
            | ValueNone ->
                failtest "Signals should have been captured"

            let responseBody = getResponseBody context
            Expect.stringContains responseBody "User: 123" "Response should show user"

        testCase "US2: tryReadSignals returns ValueNone for malformed JSON" <| fun () ->
            // Arrange (T025)
            let context = createMockContext()
            let invalidJson = """{"invalid": json"""
            setRequestBody context invalidJson

            let mutable capturedSignals: voption<{| userId: int |}> = ValueSome {| userId = -1 |}

            let resource =
                ResourceBuilder("/test") {
                    datastar (fun ctx -> task {
                        let! signals = Datastar.tryReadSignals<{| userId: int |}> ctx
                        capturedSignals <- signals
                    })
                }

            // Act
            let endpoint = resource.Endpoints.[0] :?> Microsoft.AspNetCore.Routing.RouteEndpoint
            endpoint.RequestDelegate.Invoke(context).Wait()

            // Assert (SC-005: Malformed JSON returns ValueNone)
            Expect.isTrue capturedSignals.IsNone "Invalid JSON should result in ValueNone"

        // ==========================================
        // User Story 3: Standalone Helper Functions
        // ==========================================

        testCase "US3: Datastar.patchElements sends HTML fragment" <| fun () ->
            // Arrange (T028)
            let context = createMockContext()
            let html = "<div id='test'>Hello World</div>"

            let resource =
                ResourceBuilder("/test") {
                    datastar (fun ctx -> task {
                        do! Datastar.patchElements html ctx
                    })
                }

            // Act
            let endpoint = resource.Endpoints.[0] :?> Microsoft.AspNetCore.Routing.RouteEndpoint
            endpoint.RequestDelegate.Invoke(context).Wait()

            // Assert
            let responseBody = getResponseBody context
            Expect.stringContains responseBody "event: datastar-patch-elements" "Should have patch-elements event"
            Expect.stringContains responseBody html "Should contain HTML"

        testCase "US3: Datastar.patchSignals sends signal JSON" <| fun () ->
            // Arrange (T029)
            let context = createMockContext()
            let signals = """{"count": 42}"""

            let resource =
                ResourceBuilder("/test") {
                    datastar (fun ctx -> task {
                        do! Datastar.patchSignals signals ctx
                    })
                }

            // Act
            let endpoint = resource.Endpoints.[0] :?> Microsoft.AspNetCore.Routing.RouteEndpoint
            endpoint.RequestDelegate.Invoke(context).Wait()

            // Assert
            let responseBody = getResponseBody context
            Expect.stringContains responseBody "event: datastar-patch-signals" "Should have patch-signals event"
            Expect.stringContains responseBody "\"count\": 42" "Should contain signal data"

        testCase "US3: Datastar.removeElement sends remove command" <| fun () ->
            // Arrange (T030)
            let context = createMockContext()
            let selector = "#obsolete-element"

            let resource =
                ResourceBuilder("/test") {
                    datastar (fun ctx -> task {
                        do! Datastar.removeElement selector ctx
                    })
                }

            // Act
            let endpoint = resource.Endpoints.[0] :?> Microsoft.AspNetCore.Routing.RouteEndpoint
            endpoint.RequestDelegate.Invoke(context).Wait()

            // Assert
            let responseBody = getResponseBody context
            Expect.stringContains responseBody "event: datastar-patch-elements" "Should have patch-elements event"
            Expect.stringContains responseBody "mode remove" "Should have remove mode"
            Expect.stringContains responseBody selector "Should contain selector"

        testCase "US3: Datastar.executeScript sends script" <| fun () ->
            // Arrange (T031)
            let context = createMockContext()
            let script = "console.log('hello')"

            let resource =
                ResourceBuilder("/test") {
                    datastar (fun ctx -> task {
                        do! Datastar.executeScript script ctx
                    })
                }

            // Act
            let endpoint = resource.Endpoints.[0] :?> Microsoft.AspNetCore.Routing.RouteEndpoint
            endpoint.RequestDelegate.Invoke(context).Wait()

            // Assert - StarFederation.Datastar wraps scripts in patch-elements with script tags
            let responseBody = getResponseBody context
            Expect.stringContains responseBody "event: datastar-patch-elements" "Should have patch-elements event"
            Expect.stringContains responseBody "<script" "Should contain script tag"
            Expect.stringContains responseBody script "Should contain script content"

        // ==========================================
        // API Compliance
        // ==========================================

        testCase "FR-005: Only datastar custom operation exists on ResourceBuilder" <| fun () ->
            // T007: Verify only datastar remains
            // This is a compile-time verification - if this test compiles,
            // it means the removed operations don't exist

            let resource =
                ResourceBuilder("/test") {
                    datastar (fun ctx -> task {
                        do! Datastar.patchElements "<div>Test</div>" ctx
                    })
                }

            Expect.equal resource.Endpoints.Length 1 "Should have one endpoint"
    ]
