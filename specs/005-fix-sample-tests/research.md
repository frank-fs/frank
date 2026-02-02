# Research: Browser Automation Test Suite for Frank.Datastar Samples

**Date**: 2026-01-28
**Feature**: 005-fix-sample-tests

## Research Questions

### 1. Playwright NuGet Package for F#/.NET

**Decision**: Use `Microsoft.Playwright.NUnit` package

**Rationale**:
- Official Microsoft package with NUnit integration
- Current version: 1.57.0
- Supports .NET Standard 2.0 (works with .NET 8.0+, .NET 10.0)
- Provides `PageTest` base class and test lifecycle management

**Alternatives considered**:
- `Microsoft.Playwright` (core only) - Requires manual browser lifecycle management
- `Microsoft.Playwright.MSTest` - MSTest is less idiomatic for F# projects
- `Microsoft.Playwright.Xunit` - xUnit lacks `TestContext.Parameters` for runsettings integration

### 2. F# Integration Pattern for Playwright

**Decision**: Use F# `task { }` computation expressions

**Rationale**:
- Playwright APIs return `Task<T>` and `ValueTask<T>`
- F# task expressions provide natural interop without `|> Async.AwaitTask` overhead
- Pattern: `let!` for awaiting, `do!` for side effects, `use!` for disposables

**Code pattern**:
```fsharp
[<Test>]
member this.TestExample() = task {
    use! page = context.NewPageAsync()
    do! page.GotoAsync("http://localhost:5000")
    let! content = page.Locator(".data").TextContentAsync()
    Assert.That(content, Is.EqualTo("Expected"))
}
```

**Alternatives considered**:
- `async { }` with `|> Async.AwaitTask` - More verbose, unnecessary conversion overhead
- Direct `.Result` blocking - Defeats async purpose, can cause deadlocks

### 3. Waiting for SSE-Pushed DOM Updates

**Decision**: Use `WaitForFunctionAsync` with DOM query predicates

**Rationale**:
- `WaitForLoadStateAsync(NetworkIdle)` hangs indefinitely with SSE (persistent connection)
- `WaitForFunctionAsync` allows custom JavaScript predicates
- Can check for specific content, element visibility, or DOM mutations
- Supports configurable timeout (default 5 seconds per spec)

**Code pattern**:
```fsharp
// Wait for specific content to appear via SSE
do! page.WaitForFunctionAsync(
    "() => document.querySelector('.contact-name')?.textContent === 'Updated'",
    PageWaitForFunctionOptions(Timeout = 5000.0)
)

// Wait for element to exist
do! page.WaitForFunctionAsync(
    "selector => !!document.querySelector(selector)",
    "#edit-form"
)
```

**Alternatives considered**:
- `WaitForLoadStateAsync(NetworkIdle)` - Hangs with SSE
- Fixed `Task.Delay` sleeps - Flaky, either too slow or too fast
- `WaitForSelectorAsync` - Works for element existence but not content changes

### 4. Passing Sample Name Parameter to Tests

**Decision**: Use `DATASTAR_SAMPLE` environment variable

**Rationale**:
- Simple to set via command line: `DATASTAR_SAMPLE=Frank.Datastar.Basic dotnet test`
- Works across all platforms (Windows, macOS, Linux)
- Can also be set via `.runsettings` file for IDE integration
- Allows fail-fast validation in test setup

**Code pattern**:
```fsharp
let sampleName =
    Environment.GetEnvironmentVariable("DATASTAR_SAMPLE")
    |> Option.ofObj
    |> Option.defaultWith (fun () ->
        failwith "DATASTAR_SAMPLE environment variable required. Available samples: ...")
```

**Alternatives considered**:
- Command-line filter expression (`--filter`) - Can't pass arbitrary data
- `.runsettings` only - Less convenient for quick command-line runs
- Test constructor parameters - NUnit doesn't support this pattern well

### 5. Test Project Structure

**Decision**: Single test project with feature-based file organization

**Rationale**:
- Matches user story organization from spec
- Easy to run all tests or filter by feature
- Shared configuration and helpers in separate files
- Follows existing sample project naming convention

**File structure**:
```
Frank.Datastar.Tests/
├── Frank.Datastar.Tests.fsproj
├── TestConfiguration.fs     # Runs first (F# compilation order)
├── TestHelpers.fs           # Common utilities
├── ClickToEditTests.fs      # US1
├── SearchFilterTests.fs     # US2
├── BulkUpdateTests.fs       # US3
├── StateIsolationTests.fs   # US4
└── test.runsettings
```

**Alternatives considered**:
- Separate test projects per sample - Unnecessary complexity, same tests for all
- Single file with all tests - Hard to navigate, doesn't scale
- Tests in existing sample projects - Violates separation of concerns

### 6. Browser Lifecycle Management

**Decision**: Use NUnit's `[<OneTimeSetUp>]` and `[<SetUp>]` for browser/page lifecycle

**Rationale**:
- Browser launch is expensive (~1 second), do once per test class
- Each test gets fresh page for isolation
- Context shared within test class for efficiency
- Proper cleanup via `[<TearDown>]` and `[<OneTimeTearDown>]`

**Code pattern**:
```fsharp
[<TestFixture>]
type ClickToEditTests() =
    let mutable playwright: IPlaywright = null
    let mutable browser: IBrowser = null
    let mutable context: IBrowserContext = null
    let mutable page: IPage = null

    [<OneTimeSetUp>]
    member this.SetupBrowser() = task {
        playwright <- Playwright.CreateAsync() |> Async.AwaitTask |> Async.RunSynchronously
        let! b = playwright.Chromium.LaunchAsync()
        browser <- b
    }

    [<SetUp>]
    member this.SetupPage() = task {
        let! ctx = browser.NewContextAsync()
        context <- ctx
        let! p = ctx.NewPageAsync()
        page <- p
        do! page.GotoAsync(baseUrl)
    }

    [<TearDown>]
    member this.TeardownPage() = task {
        do! page.CloseAsync()
        do! context.CloseAsync()
    }

    [<OneTimeTearDown>]
    member this.TeardownBrowser() =
        browser.CloseAsync() |> Async.AwaitTask |> Async.RunSynchronously
        playwright.Dispose()
```

**Alternatives considered**:
- New browser per test - Too slow
- Single page for all tests - State bleeds between tests
- Playwright's `PageTest` base class - C#-centric, less idiomatic for F#

## Summary

All research questions resolved. Key findings:
1. Use `Microsoft.Playwright.NUnit` package
2. Use F# `task { }` expressions for async operations
3. Use `WaitForFunctionAsync` for SSE content verification (not NetworkIdle)
4. Use `DATASTAR_SAMPLE` environment variable for sample selection
5. Feature-based file organization matching user stories
6. Browser shared per test class, fresh page per test
