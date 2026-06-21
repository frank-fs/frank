# Datastar ADR Compliance Fixes — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Close four ADR compliance gaps in `Frank.Datastar`: missing `viewTransitionSelector`, `DELETE` signals routing, `JsonException` propagation, and thread-safety documentation.

**Architecture:** Gaps 1–3 are surgical changes to `Consts.fs`, `Types.fs`, `ServerSentEventGenerator.fs`, and `Frank.Datastar.fs`. All follow TDD: write a failing test, run to confirm failure, implement the fix, run to confirm green. Gap 4 is documentation-only.

**Tech Stack:** F#, ASP.NET Core, Expecto, `dotnet test`

---

## Context: Official SDK Comparison (`StarFederation.Datastar.FSharp`)

The official .NET SDK at `~/Code/datastar-dotnet/src/fsharp/` was compared against Frank's implementation. Key findings that shape this plan:

- **Gap 1 (`viewTransitionSelector`):** Confirmed. Official SDK has `ViewTransitionSelector: string voption` in `PatchElementsOptions` and `Bytes.DatalineViewTransitionSelector`. Critically, **`RemoveElementOptions` in the official SDK does NOT include `ViewTransitionSelector`** — we align with this, adding the field to `PatchElementsOptions` only.
- **Gap 2 (DELETE):** Confirmed. Official SDK branches on `method = "GET" || method = "DELETE"` in both `ReadSignalsAsync` overloads.
- **Gap 3 (`JsonException`):** The official SDK **also swallows all exceptions** with `with _ -> return ValueNone`. This deviates from the ADR but reflects the official .NET reference. This plan fixes it per the strict ADR reading ("Must return error for invalid JSON") by propagating `JsonException` from `ReadSignalsAsync<'T>` and catching it in the `tryReadSignals`/`tryReadSignalsWithOptions` convenience wrappers (whose `try` prefix already signals this semantics). If you prefer to match the official SDK instead, skip Task 3.
- **Gap 4 (thread safety):** Official static methods also have no guards — only the DI-based instance API uses `lock`. Documentation is the right fix for Frank's static API.
- **`ExecuteScriptOptions.Attributes` type mismatch:** Official SDK uses `KeyValuePair<string, string> list` (with `HttpUtility.HtmlEncode`); Frank uses `string[]` (verbatim). **Not fixed in this plan** — it's a deliberate design choice in Frank's API. Note if you consider replacing Frank's SSE core with the official SDK.

**Frank cannot be replaced wholesale by the official SDK**: Frank's `datastar` CE operation (`ResourceBuilder` extension), stream-based overloads (`TextWriter -> Task`, `Stream -> Task`), and `AffordanceHelper.fs` are not in the official SDK. The stream-based overloads are Frank's unique contribution and may be worth contributing upstream.

---

## File Map

| File | Change |
|------|--------|
| `src/Frank.Datastar/Consts.fs` | Add `DatalineViewTransitionSelector` byte constant after `DatalineUseViewTransition` |
| `src/Frank.Datastar/Types.fs` | Add `ViewTransitionSelector: string voption` field + default to `PatchElementsOptions` |
| `src/Frank.Datastar/ServerSentEventGenerator.fs` | Emit `viewTransitionSelector` in all 3 `PatchElementsAsync` overloads; fix `IsDelete` in both `ReadSignalsAsync` overloads; remove `JsonException` catch from `ReadSignalsAsync<'T>`; add thread-safety XML doc |
| `src/Frank.Datastar/Frank.Datastar.fs` | Wrap `tryReadSignals` and `tryReadSignalsWithOptions` in `task { try ... with :? JsonException -> return ValueNone }` |
| `test/Frank.Datastar.Tests/DatastarTests.fs` | Add `[<Tests>] let adrComplianceTests` test list with cases for all four gaps |

---

## Task 1: `viewTransitionSelector` — types, constant, emission

**Files:**
- Modify: `test/Frank.Datastar.Tests/DatastarTests.fs` (add failing tests)
- Modify: `src/Frank.Datastar/Consts.fs` (add byte constant)
- Modify: `src/Frank.Datastar/Types.fs` (add field)
- Modify: `src/Frank.Datastar/ServerSentEventGenerator.fs` (emit in 3 overloads)

---

- [ ] **Step 1: Write failing tests**

Add a new `[<Tests>]` value at the bottom of `test/Frank.Datastar.Tests/DatastarTests.fs`, before the final newline:

```fsharp
[<Tests>]
let adrComplianceTests =
    testList "ADR Compliance" [

        testCase "ADR-Gap1: PatchElementsAsync emits viewTransitionSelector when UseViewTransition is true" <| fun () ->
            let context = createMockContext()
            let html = "<div id='target'>Content</div>"
            let opts = { PatchElementsOptions.Defaults with UseViewTransition = true; ViewTransitionSelector = ValueSome "#main" }
            task {
                do! ServerSentEventGenerator.StartServerEventStreamAsync(context.Response)
                do! ServerSentEventGenerator.PatchElementsAsync(context.Response, html, opts)
            } |> (fun t -> t.Wait())
            let responseBody = getResponseBody context
            Expect.stringContains responseBody "data: useViewTransition true" "Should emit useViewTransition"
            Expect.stringContains responseBody "data: viewTransitionSelector #main" "Should emit viewTransitionSelector"

        testCase "ADR-Gap1: PatchElementsAsync omits viewTransitionSelector when UseViewTransition is false" <| fun () ->
            let context = createMockContext()
            let opts = { PatchElementsOptions.Defaults with UseViewTransition = false; ViewTransitionSelector = ValueSome "#main" }
            task {
                do! ServerSentEventGenerator.StartServerEventStreamAsync(context.Response)
                do! ServerSentEventGenerator.PatchElementsAsync(context.Response, "<div>x</div>", opts)
            } |> (fun t -> t.Wait())
            let responseBody = getResponseBody context
            Expect.isFalse (responseBody.Contains("viewTransitionSelector")) "Should NOT emit viewTransitionSelector when UseViewTransition is false"

        testCase "ADR-Gap1: streamPatchElementsWithOptions emits viewTransitionSelector" <| fun () ->
            let context = createMockContext()
            let opts = { PatchElementsOptions.Defaults with UseViewTransition = true; ViewTransitionSelector = ValueSome "#hero" }
            task {
                do! ServerSentEventGenerator.StartServerEventStreamAsync(context.Response)
                do! Datastar.streamPatchElementsWithOptions opts (fun tw ->
                    tw.Write("<div id='target'>Content</div>")
                    Tasks.Task.CompletedTask) context
            } |> (fun t -> t.Wait())
            let responseBody = getResponseBody context
            Expect.stringContains responseBody "data: viewTransitionSelector #hero" "TextWriter overload should emit viewTransitionSelector"

        testCase "ADR-Gap1: streamPatchElementsToStreamWithOptions emits viewTransitionSelector" <| fun () ->
            let context = createMockContext()
            let opts = { PatchElementsOptions.Defaults with UseViewTransition = true; ViewTransitionSelector = ValueSome "#hero" }
            let htmlBytes = System.Text.Encoding.UTF8.GetBytes("<div id='target'>Content</div>")
            task {
                do! ServerSentEventGenerator.StartServerEventStreamAsync(context.Response)
                do! Datastar.streamPatchElementsToStreamWithOptions opts (fun stream ->
                    stream.Write(htmlBytes, 0, htmlBytes.Length)
                    Tasks.Task.CompletedTask) context
            } |> (fun t -> t.Wait())
            let responseBody = getResponseBody context
            Expect.stringContains responseBody "data: viewTransitionSelector #hero" "Stream overload should emit viewTransitionSelector"

    ]
```

- [ ] **Step 2: Run tests — confirm compile error** (`ViewTransitionSelector` field does not exist yet)

```
dotnet build test/Frank.Datastar.Tests/Frank.Datastar.Tests.fsproj
```

Expected: build fails with `error FS0039: The record label 'ViewTransitionSelector' is not defined`

- [ ] **Step 3: Add `DatalineViewTransitionSelector` byte constant to `src/Frank.Datastar/Consts.fs`**

In the `Bytes` module, after `DatalineUseViewTransition`:

```fsharp
    let DatalineUseViewTransition = "useViewTransition"B
    let DatalineViewTransitionSelector = "viewTransitionSelector"B
    let DatalineNamespace = "namespace"B
```

- [ ] **Step 4: Add `ViewTransitionSelector` field to `PatchElementsOptions` in `src/Frank.Datastar/Types.fs`**

Replace the existing `PatchElementsOptions` type:

```fsharp
[<Struct>]
type PatchElementsOptions =
    { Selector: Selector voption
      PatchMode: ElementPatchMode
      UseViewTransition: bool
      ViewTransitionSelector: string voption
      Namespace: PatchElementNamespace
      EventId: string voption
      Retry: TimeSpan }

    static member Defaults =
        { Selector = ValueNone
          PatchMode = Consts.DefaultElementPatchMode
          UseViewTransition = Consts.DefaultElementsUseViewTransitions
          ViewTransitionSelector = ValueNone
          Namespace = Consts.DefaultPatchElementNamespace
          EventId = ValueNone
          Retry = Consts.DefaultSseRetryDuration }
```

- [ ] **Step 5: Build and run tests — confirm they compile but fail** (field exists, but nothing is emitted yet)

```
dotnet build test/Frank.Datastar.Tests/Frank.Datastar.Tests.fsproj && dotnet test test/Frank.Datastar.Tests/ --filter "ADR-Gap1"
```

Expected: tests run and fail with `Expected string to contain 'data: viewTransitionSelector #main'`

- [ ] **Step 6: Add `viewTransitionSelector` emission to the string `PatchElementsAsync` overload in `src/Frank.Datastar/ServerSentEventGenerator.fs`**

In the full `PatchElementsAsync(httpResponse, elements: string, options, cancellationToken)` overload (~line 90), after the `UseViewTransition` check and before the `Namespace` check:

```fsharp
        if options.UseViewTransition <> Consts.DefaultElementsUseViewTransitions then
            writer
            |> ServerSentEvent.sendDataBytesLine
                Bytes.DatalineUseViewTransition
                (if options.UseViewTransition then
                     Bytes.bTrue
                 else
                     Bytes.bFalse)

        if options.UseViewTransition then
            options.ViewTransitionSelector
            |> ValueOption.iter (fun sel ->
                writer |> ServerSentEvent.sendDataStringLine Bytes.DatalineViewTransitionSelector sel)

        if options.Namespace <> Consts.DefaultPatchElementNamespace then
```

- [ ] **Step 7: Add the same emission block to the `TextWriter -> Task` `PatchElementsAsync` overload** (~line 140, same pattern — after `DatalineUseViewTransition` write, before `DatalineNamespace` write):

```fsharp
        if options.UseViewTransition <> Consts.DefaultElementsUseViewTransitions then
            bufWriter
            |> ServerSentEvent.sendDataBytesLine
                Bytes.DatalineUseViewTransition
                (if options.UseViewTransition then
                     Bytes.bTrue
                 else
                     Bytes.bFalse)

        if options.UseViewTransition then
            options.ViewTransitionSelector
            |> ValueOption.iter (fun sel ->
                bufWriter |> ServerSentEvent.sendDataStringLine Bytes.DatalineViewTransitionSelector sel)

        if options.Namespace <> Consts.DefaultPatchElementNamespace then
```

- [ ] **Step 8: Add the same emission block to the `Stream -> Task` `PatchElementsAsync` overload** (~line 405, same pattern):

```fsharp
        if options.UseViewTransition <> Consts.DefaultElementsUseViewTransitions then
            bufWriter
            |> ServerSentEvent.sendDataBytesLine
                Bytes.DatalineUseViewTransition
                (if options.UseViewTransition then
                     Bytes.bTrue
                 else
                     Bytes.bFalse)

        if options.UseViewTransition then
            options.ViewTransitionSelector
            |> ValueOption.iter (fun sel ->
                bufWriter |> ServerSentEvent.sendDataStringLine Bytes.DatalineViewTransitionSelector sel)

        if options.Namespace <> Consts.DefaultPatchElementNamespace then
```

- [ ] **Step 9: Run Gap1 tests and full suite — confirm green**

```
dotnet build Frank.sln && dotnet test test/Frank.Datastar.Tests/ --filter "ADR-Gap1"
dotnet test test/Frank.Datastar.Tests/
```

Expected: all Gap1 tests pass; no pre-existing tests regressed

- [ ] **Step 10: Commit**

```
git add src/Frank.Datastar/Consts.fs src/Frank.Datastar/Types.fs src/Frank.Datastar/ServerSentEventGenerator.fs test/Frank.Datastar.Tests/DatastarTests.fs
git commit -m "fix(datastar): add viewTransitionSelector support per ADR spec

PatchElementsOptions gains ViewTransitionSelector: string voption (aligned
with StarFederation.Datastar.FSharp). Emitted only when UseViewTransition
= true and a value is provided, across all three PatchElementsAsync
overloads (string, TextWriter, Stream).

RemoveElementOptions intentionally omitted — official SDK also omits it."
```

---

## Task 2: Fix `DELETE` routing in `ReadSignalsAsync`

ADR: `DELETE` requests send signals as a `datastar` query parameter (same as `GET`), not in the request body.

**Files:**
- Modify: `test/Frank.Datastar.Tests/DatastarTests.fs` (add failing tests to `adrComplianceTests`)
- Modify: `src/Frank.Datastar/ServerSentEventGenerator.fs` (both `ReadSignalsAsync` overloads)

---

- [ ] **Step 1: Add failing tests to `adrComplianceTests` in `DatastarTests.fs`**

Inside the `adrComplianceTests` `testList`, after the Gap1 cases:

```fsharp
        testCase "ADR-Gap2: ReadSignalsAsync with DELETE reads from query parameter" <| fun () ->
            let context = createMockContext()
            context.Request.Method <- HttpMethods.Delete
            let signals = """{"id":42}"""
            context.Request.QueryString <- QueryString($"?datastar={Uri.EscapeDataString(signals)}")
            let result =
                ServerSentEventGenerator.ReadSignalsAsync(context.Request)
                    .GetAwaiter().GetResult()
            Expect.equal result signals "DELETE should read 'datastar' query param, not body"

        testCase "ADR-Gap2: ReadSignalsAsync<T> with DELETE reads from query parameter" <| fun () ->
            let context = createMockContext()
            context.Request.Method <- HttpMethods.Delete
            let signals = """{"id":42}"""
            context.Request.QueryString <- QueryString($"?datastar={Uri.EscapeDataString(signals)}")
            let result =
                ServerSentEventGenerator.ReadSignalsAsync<{| id: int |}>(context.Request)
                    .GetAwaiter().GetResult()
            Expect.isTrue result.IsSome "DELETE with query param should parse signals"
            Expect.equal result.Value.id 42 "Should correctly parse 'id' field"
```

- [ ] **Step 2: Run to confirm failure**

```
dotnet test test/Frank.Datastar.Tests/ --filter "ADR-Gap2"
```

Expected: both tests fail — DELETE falls through to body parsing, body is empty, returns `""` / `ValueNone`

- [ ] **Step 3: Fix string `ReadSignalsAsync` in `src/Frank.Datastar/ServerSentEventGenerator.fs`**

Change line ~538:
```fsharp
            if HttpMethods.IsGet(httpRequest.Method) then
```
to:
```fsharp
            if HttpMethods.IsGet(httpRequest.Method) || HttpMethods.IsDelete(httpRequest.Method) then
```

- [ ] **Step 4: Fix generic `ReadSignalsAsync<'T>` in the same file**

Change line ~554:
```fsharp
            if HttpMethods.IsGet(httpRequest.Method) then
```
to:
```fsharp
            if HttpMethods.IsGet(httpRequest.Method) || HttpMethods.IsDelete(httpRequest.Method) then
```

- [ ] **Step 5: Run Gap2 tests and full suite — confirm green**

```
dotnet test test/Frank.Datastar.Tests/ --filter "ADR-Gap2"
dotnet test test/Frank.Datastar.Tests/
```

Expected: both Gap2 tests pass; no regressions

- [ ] **Step 6: Commit**

```
git add src/Frank.Datastar/ServerSentEventGenerator.fs test/Frank.Datastar.Tests/DatastarTests.fs
git commit -m "fix(datastar): route DELETE signals from query param per ADR spec

ReadSignalsAsync (string and generic overloads) now treat DELETE like GET:
signals are read from the 'datastar' query parameter rather than the
request body. Aligns with StarFederation.Datastar.FSharp reference impl."
```

---

## Task 3: Propagate `JsonException` from `ReadSignalsAsync<'T>`

ADR: "Must return/throw errors per language conventions" for invalid JSON. The `ServerSentEventGenerator.ReadSignalsAsync<'T>` method must propagate `JsonException` rather than silently returning `ValueNone`. The `Datastar.tryReadSignals`/`tryReadSignalsWithOptions` convenience wrappers retain their `try`-based semantics by catching `JsonException` at the wrapper layer.

> **Note:** The official `StarFederation.Datastar.FSharp` SDK also swallows all exceptions (`with _ -> return ValueNone`), which deviates from the ADR. This plan follows the strict ADR reading. If you prefer to align with the official SDK instead, **skip this task entirely**.

**Files:**
- Modify: `test/Frank.Datastar.Tests/DatastarTests.fs` (add failing test to `adrComplianceTests`)
- Modify: `src/Frank.Datastar/ServerSentEventGenerator.fs` (remove `JsonException` catch; remove dead catch from string overload)
- Modify: `src/Frank.Datastar/Frank.Datastar.fs` (wrap `tryReadSignals` and `tryReadSignalsWithOptions`)

---

- [ ] **Step 1: Add failing test to `adrComplianceTests` in `DatastarTests.fs`**

```fsharp
        testCase "ADR-Gap3: ReadSignalsAsync<T> propagates JsonException for invalid POST body" <| fun () ->
            let context = createMockContext()
            setRequestBody context """{"unclosed": """
            context.Request.Method <- HttpMethods.Post

            let mutable propagated = false
            try
                ServerSentEventGenerator.ReadSignalsAsync<{| id: int |}>(context.Request).Wait()
            with
            | :? AggregateException as ae when (ae.InnerException :? JsonException) ->
                propagated <- true

            Expect.isTrue propagated "ReadSignalsAsync<T> must propagate JsonException for invalid JSON (ADR §ReadSignals)"

        testCase "ADR-Gap3: tryReadSignals returns ValueNone for POST with invalid JSON" <| fun () ->
            // Verifies the tryReadSignals wrapper catches JsonException at the convenience layer
            let context = createMockContext()
            setRequestBody context """{"unclosed": """
            context.Request.Method <- HttpMethods.Post

            let mutable result: voption<{| id: int |}> = ValueSome {| id = -1 |}
            let resource =
                ResourceBuilder("/test") {
                    datastar HttpMethods.Post (fun ctx -> task {
                        let! signals = Datastar.tryReadSignals<{| id: int |}> ctx
                        result <- signals
                    })
                }
            let endpoint = resource.Endpoints.[0] :?> Microsoft.AspNetCore.Routing.RouteEndpoint
            endpoint.RequestDelegate.Invoke(context).Wait()

            Expect.isTrue result.IsNone "tryReadSignals should return ValueNone for invalid JSON (try-semantics)"
```

- [ ] **Step 2: Run to confirm failure**

```
dotnet test test/Frank.Datastar.Tests/ --filter "ADR-Gap3"
```

Expected: first test fails (exception is swallowed, `propagated` stays `false`); second test passes (returns `ValueNone` already via the GET path — existing behavior)

- [ ] **Step 3: Fix `ReadSignalsAsync<'T>` in `src/Frank.Datastar/ServerSentEventGenerator.fs`**

Remove `| :? JsonException -> return ValueNone` from the `with` block, keeping only `IOException`:

```fsharp
    static member ReadSignalsAsync<'T>
        (httpRequest: HttpRequest, jsonSerializerOptions: JsonSerializerOptions, cancellationToken: CancellationToken)
        =
        task {
            try
                if HttpMethods.IsGet(httpRequest.Method) || HttpMethods.IsDelete(httpRequest.Method) then
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
        }
```

- [ ] **Step 4: Remove dead `JsonException` catch from string `ReadSignalsAsync`**

The `| :? JsonException ->` catch in the string overload is unreachable (the method only reads the body as a string, never parses JSON). Remove it:

```fsharp
    static member ReadSignalsAsync(httpRequest: HttpRequest, cancellationToken: CancellationToken) =
        backgroundTask {
            if HttpMethods.IsGet(httpRequest.Method) || HttpMethods.IsDelete(httpRequest.Method) then
                match httpRequest.Query.TryGetValue(Consts.DatastarKey) with
                | true, stringValues when stringValues.Count > 0 -> return stringValues[0]
                | _ -> return ""
            else
                try
                    use readResult = new StreamReader(httpRequest.Body)
                    return! readResult.ReadToEndAsync(cancellationToken)
                with
                | :? IOException -> return ""
        }
```

- [ ] **Step 5: Wrap `tryReadSignals` in `src/Frank.Datastar/Frank.Datastar.fs`**

Replace (~line 91–92):
```fsharp
    let inline tryReadSignals<'T> (ctx: HttpContext) : Task<voption<'T>> =
        ServerSentEventGenerator.ReadSignalsAsync<'T>(ctx.Request)
```
with:
```fsharp
    let inline tryReadSignals<'T> (ctx: HttpContext) : Task<voption<'T>> =
        task {
            try
                return! ServerSentEventGenerator.ReadSignalsAsync<'T>(ctx.Request)
            with
            | :? JsonException -> return ValueNone
        }
```

- [ ] **Step 6: Wrap `tryReadSignalsWithOptions` in `src/Frank.Datastar/Frank.Datastar.fs`**

Replace (~line 151–155):
```fsharp
    let inline tryReadSignalsWithOptions<'T>
        (jsonOptions: JsonSerializerOptions)
        (ctx: HttpContext)
        : Task<voption<'T>> =
        ServerSentEventGenerator.ReadSignalsAsync<'T>(ctx.Request, jsonOptions)
```
with:
```fsharp
    let inline tryReadSignalsWithOptions<'T>
        (jsonOptions: JsonSerializerOptions)
        (ctx: HttpContext)
        : Task<voption<'T>> =
        task {
            try
                return! ServerSentEventGenerator.ReadSignalsAsync<'T>(ctx.Request, jsonOptions)
            with
            | :? JsonException -> return ValueNone
        }
```

- [ ] **Step 7: Run Gap3 tests and full suite — confirm green**

```
dotnet test test/Frank.Datastar.Tests/ --filter "ADR-Gap3"
dotnet test test/Frank.Datastar.Tests/
```

Expected: both Gap3 tests pass; existing `US2: tryReadSignals returns ValueNone for malformed JSON` still passes (that test uses GET without a query param, which returns `ValueNone` before any JSON parsing — unchanged behavior)

> **FS3511 watch:** If the Release build (CI) fails with `FS3511` on `tryReadSignals` or `tryReadSignalsWithOptions`, extract the try/with into a private non-inline helper:
> ```fsharp
> let private readSignalsSafe<'T> (t: Task<voption<'T>>) =
>     task {
>         try
>             return! t
>         with
>         | :? JsonException -> return ValueNone
>     }
> let inline tryReadSignals<'T> (ctx: HttpContext) : Task<voption<'T>> =
>     readSignalsSafe (ServerSentEventGenerator.ReadSignalsAsync<'T>(ctx.Request))
> ```

- [ ] **Step 8: Commit**

```
git add src/Frank.Datastar/ServerSentEventGenerator.fs src/Frank.Datastar/Frank.Datastar.fs test/Frank.Datastar.Tests/DatastarTests.fs
git commit -m "fix(datastar): propagate JsonException from ReadSignalsAsync<T> per ADR

ADR §ReadSignals: 'Must return error for invalid JSON.'
ReadSignalsAsync<T> now lets JsonException propagate (IOException still
caught). The tryReadSignals/tryReadSignalsWithOptions convenience wrappers
catch JsonException and return ValueNone, preserving their try-semantics.

Note: StarFederation.Datastar.FSharp swallows all exceptions; this
implementation follows the strict ADR reading instead."
```

---

## Task 4: Document the thread-safety contract

The ADR says "Should ensure ordered delivery (e.g., mutex in Go)." In ASP.NET Core, each request handler runs on a single logical thread — `PipeWriter` is **not** thread-safe. The static API has no guards (same as the official SDK's static methods). The right fix is documentation.

**Files:**
- Modify: `src/Frank.Datastar/ServerSentEventGenerator.fs` (add XML doc to `StartServerEventStreamAsync`)

---

- [ ] **Step 1: Add XML doc comment to `StartServerEventStreamAsync` in `src/Frank.Datastar/ServerSentEventGenerator.fs`**

Replace the existing declaration (~line 42):
```fsharp
type ServerSentEventGenerator =
    static member StartServerEventStreamAsync(httpResponse: HttpResponse, cancellationToken: CancellationToken) =
```
with:
```fsharp
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
    static member StartServerEventStreamAsync(httpResponse: HttpResponse, cancellationToken: CancellationToken) =
```

- [ ] **Step 2: Build to confirm no errors**

```
dotnet build Frank.sln
```

- [ ] **Step 3: Commit**

```
git add src/Frank.Datastar/ServerSentEventGenerator.fs
git commit -m "docs(datastar): document PipeWriter single-writer contract on StartServerEventStreamAsync

ADR §Construction: 'Should ensure ordered delivery.' Documents that
PipeWriter is not thread-safe and callers must serialize writes, matching
the behavior of the official StarFederation.Datastar.FSharp static API."
```

---

## Final Verification

- [ ] **Run the full test suite and build**

```
dotnet build Frank.sln && dotnet test test/Frank.Datastar.Tests/
```

Expected: all tests green

- [ ] **Run format check**

```
dotnet fantomas --check src/Frank.Datastar/
```

Fix any formatting issues, then commit if needed.

---

## Self-Review

**Spec coverage:**
- Gap 1 (`viewTransitionSelector`): Task 1 — field in `PatchElementsOptions`, byte constant, emission in 3 overloads ✅
- Gap 2 (DELETE): Task 2 — `IsDelete` branch in both `ReadSignalsAsync` overloads ✅
- Gap 3 (`JsonException`): Task 3 — propagation from `ReadSignalsAsync<'T>`, catch in `tryReadSignals` wrappers ✅
- Gap 4 (thread safety): Task 4 — XML doc on `StartServerEventStreamAsync` ✅
- `AffordanceHelper.fs` and stream overloads: explicitly preserved, not touched ✅

**Placeholder scan:** None found. All steps contain exact code or exact commands.

**Type consistency:**
- `ViewTransitionSelector: string voption` — added to `PatchElementsOptions` in Task 1 Step 4, referenced in tests (Step 1) and emission (Steps 6–8)
- `Bytes.DatalineViewTransitionSelector` — added in Task 1 Step 3, used in emission steps
- `HttpMethods.IsDelete` — used consistently in Tasks 2 and 3
- `tryReadSignals` / `tryReadSignalsWithOptions` — updated in Task 3 Steps 5–6, tested in Task 3 Step 1
