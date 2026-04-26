---
source: specs/005-fix-sample-tests
status: complete
type: spec
---

# Feature Specification: Browser Automation Test Suite for Frank.Datastar Samples

**Feature Branch**: `005-fix-sample-tests`
**Created**: 2026-01-27
**Updated**: 2026-01-28
**Status**: Draft

## Clarifications

### Session 2026-01-27

- Q: Should test scripts only detect bugs or also fix the underlying sample bugs? → A: Only detect and report bugs (tests fail when bugs are present); fixing sample bugs is out of scope
- Q: How should tests handle data state between test runs? → A: Tests are sequential and stateful, representing a single user; each test builds on state from previous tests. Samples intentionally don't support concurrent users (single SSE channel). Tests verify known behavior, not complex scenarios.

### Session 2026-01-28

- Q: Why replace bash scripts with browser automation? → A: Bash scripts using curl cannot properly evaluate streaming updates through SSE channels while other interactions occur. A real browser can maintain SSE connections and verify that UI updates arrive correctly in response to user actions, which is the core behavior Datastar applications need to demonstrate.
- Q: What technology should the test project use? → A: F# with Playwright, structured as a .NET test project that can be run with `dotnet test`
- Q: Where should the test project be located? → A: In the `sample/` directory alongside the other sample projects
- Q: How should the target sample be specified? → A: Via a required parameter matching `Frank.Datastar.*` pattern, validated against existing folder paths in `sample/`
- Q: How should the test project determine the target URL for a sample? → A: Default to localhost:5000 for all samples; tests run one sample at a time, and samples use port 5000 via launchSettings.json
- Q: What should the default timeout be for waiting on SSE updates? → A: 5 seconds

**Input**: User description: "Update the specification to not update the test.sh script(s) but instead create a browser automation app that can target any of the Frank.Datastar sample applications. The bash script is not set up to properly evaluate the streaming updates through the SSE channel as other interactions take place."

## User Scenarios & Testing *(mandatory)*

### User Story 1 - Validate Click-to-Edit SSE Updates in Browser (Priority: P1)

As a developer running the automated tests, I want the browser automation to verify that clicking edit on a contact triggers an SSE update that renders the edit form with current values, and that saving triggers another SSE update showing the persisted values, so that I can validate the core Datastar SSE-driven UI update pattern.

**Why this priority**: Click-to-edit demonstrates the fundamental Datastar pattern: user action → server response → SSE push → UI update. If this doesn't work, the entire Datastar integration is broken.

**Independent Test**: Can be fully tested by launching a browser, navigating to the sample app, observing the contact display, clicking edit, verifying the form appears with current values via SSE, modifying values, saving, and verifying the updated display arrives via SSE.

**Acceptance Scenarios**:

1. **Given** a contact exists with first name "Joe", **When** the user clicks the edit button, **Then** an edit form appears (via SSE update) containing the value "Joe" in the first name field
2. **Given** the edit form is displayed with current values, **When** the user changes first name to "Updated" and clicks save, **Then** the display updates (via SSE) to show "Updated" as the first name
3. **Given** a contact was edited and saved, **When** the page is refreshed, **Then** the persisted value "Updated" is displayed (proving data was saved, not just displayed)

---

### User Story 2 - Validate Search Filtering with Concurrent SSE Stream (Priority: P1)

As a developer running the automated tests, I want the browser automation to verify that typing a search query triggers SSE updates that filter the displayed list to matching items, while maintaining the SSE connection throughout, so that I can validate that search filtering works with Datastar's reactive update model.

**Why this priority**: Search filtering is a common pattern that requires the SSE connection to remain active while processing user input and pushing filtered results. This validates that the SSE stream handles multiple updates correctly.

**Independent Test**: Can be fully tested by navigating to the fruits list, verifying all fruits are displayed, typing a search query, and verifying the list updates via SSE to show only matching items.

**Acceptance Scenarios**:

1. **Given** a fruits list contains "Apple", "Apricot", and "Banana", **When** the user types "ap" in the search field, **Then** the list updates (via SSE) to show "Apple" and "Apricot" but not "Banana"
2. **Given** a filtered list is displayed, **When** the user clears the search field, **Then** the full unfiltered list is restored (via SSE)
3. **Given** a fruits list is displayed, **When** the user searches for a term with no matches, **Then** an empty list or "no results" indicator is displayed (not an error)

---

### User Story 3 - Validate Bulk Update Operations with SSE Feedback (Priority: P1)

As a developer running the automated tests, I want the browser automation to verify that selecting multiple users and triggering a bulk status change results in SSE updates showing the changed statuses, so that I can validate bulk operations work correctly with Datastar.

**Why this priority**: Bulk operations demonstrate that the server correctly processes multi-item updates and pushes the results to the client via SSE. This is critical for validating data integrity in batch operations.

**Independent Test**: Can be fully tested by navigating to the users list, selecting multiple users via checkboxes, clicking a bulk action button, and verifying each selected user's status updates via SSE.

**Acceptance Scenarios**:

1. **Given** users exist with mixed active/inactive statuses, **When** the user selects two inactive users and clicks "Activate Selected", **Then** those users' statuses update to "Active" (via SSE) while unselected users remain unchanged
2. **Given** users have been bulk-activated, **When** the page is refreshed, **Then** the activated users still show as "Active" (proving changes were persisted)
3. **Given** no users are selected, **When** the user clicks a bulk action button, **Then** no changes occur (graceful handling of empty selection)

---

### User Story 4 - Validate State Isolation Between Features (Priority: P2)

As a developer running the automated tests, I want the browser automation to verify that entering data in one form (registration) does not affect data displayed in another feature (click-to-edit), so that I can catch state leakage bugs between independent UI components.

**Why this priority**: State isolation ensures independent features don't accidentally share data. This is less critical than core features working, but important for overall application correctness.

**Independent Test**: Can be fully tested by entering values in the registration form, then navigating to click-to-edit and verifying those values don't appear in the contact form.

**Acceptance Scenarios**:

1. **Given** the registration form and contact display are both accessible, **When** the user enters "test@isolation.com" in the registration email field, **Then** the contact display does not show "test@isolation.com"
2. **Given** a contact has email "joe@smith.org", **When** the user interacts with the registration form using different values, **Then** the contact's email remains "joe@smith.org"

---

### User Story 5 - Target Any Frank.Datastar Sample via Parameter (Priority: P1)

As a developer, I want to specify which Frank.Datastar sample to test by providing a parameter like `Frank.Datastar.Basic`, and have the test project validate this against existing sample folders, so that I can run tests against any current or future Datastar sample without modifying the test code.

**Why this priority**: This is the primary interface for running tests. Without proper parameter handling, tests cannot be executed at all.

**Independent Test**: Can be fully tested by running the test project with various valid and invalid sample names and observing the behavior.

**Acceptance Scenarios**:

1. **Given** the test project is run with parameter `Frank.Datastar.Basic`, **When** the folder `sample/Frank.Datastar.Basic` exists, **Then** tests execute against that sample
2. **Given** the test project is run with parameter `Frank.Datastar.Hox`, **When** the folder `sample/Frank.Datastar.Hox` exists, **Then** tests execute against that sample
3. **Given** the test project is run with parameter `Frank.Datastar.NewSample`, **When** the folder `sample/Frank.Datastar.NewSample` does not exist, **Then** an error message lists all available `Frank.Datastar.*` samples and exits with non-zero status
4. **Given** the test project is run without any parameter, **When** no sample name is provided, **Then** a help message explains the required parameter format and lists available samples
5. **Given** the test project is run with parameter `Frank.Giraffe` (not matching `Frank.Datastar.*`), **When** the parameter doesn't start with `Frank.Datastar`, **Then** an error message explains the parameter must match the `Frank.Datastar.*` pattern

---

### User Story 6 - Clear Test Results and Failure Reporting (Priority: P2)

As a developer, I want the browser automation to produce clear, detailed test results showing which tests passed and failed, with diagnostic information for failures, so that I can quickly identify and investigate broken behavior.

**Why this priority**: Good test output accelerates debugging. Without clear failure messages, developers waste time investigating what went wrong.

**Independent Test**: Can be fully tested by running tests that include intentional failures and verifying the output explains what failed and why.

**Acceptance Scenarios**:

1. **Given** all tests pass, **When** the test run completes, **Then** a summary shows all tests passed with a count
2. **Given** a test fails, **When** the test run completes, **Then** the failure message explains what was expected vs. what was observed
3. **Given** multiple tests fail, **When** the test run completes, **Then** all failures are listed with individual diagnostics, and the application exits with a non-zero status code

---

### Edge Cases

- What happens when the target application server is not running? → Test reports a clear connection error indicating the sample server needs to be started first
- What happens when an SSE connection is interrupted? → Test detects the disconnection and reports it as a failure with timeout information
- What happens when the UI takes longer than expected to update? → Tests use reasonable timeouts (configurable) and report timeout failures with the expected vs. actual state
- What happens when running tests against an application with no seed data? → Tests detect missing prerequisites and fail with descriptive messages indicating what seed data is required
- What happens when the `sample/` directory structure changes? → Tests dynamically discover available `Frank.Datastar.*` folders at runtime, adapting to new samples automatically

## Requirements *(mandatory)*

### Functional Requirements

#### Project Structure

- **FR-001**: Test project MUST be an F# .NET test project using Playwright for browser automation
- **FR-002**: Test project MUST be located in `sample/Frank.Datastar.Tests/` alongside other sample projects
- **FR-003**: Test project MUST be runnable via standard `dotnet test` command

#### Parameter Handling

- **FR-004**: Test project MUST require a sample name parameter to specify which sample to test
- **FR-005**: Test project MUST validate that the parameter starts with `Frank.Datastar`
- **FR-006**: Test project MUST validate that a matching folder exists in `sample/` for the specified sample name
- **FR-007**: Test project MUST display a help message listing available `Frank.Datastar.*` samples when no parameter is provided
- **FR-008**: Test project MUST display an error with available samples when an invalid sample name is provided

#### Test Execution

- **FR-009**: Test project MUST target `http://localhost:5000` as the default base URL for all sample applications
- **FR-010**: Test project MUST maintain an active SSE connection while performing user interactions to verify SSE-pushed updates
- **FR-011**: Test project MUST verify that click-to-edit displays current values in the edit form (received via SSE)
- **FR-012**: Test project MUST verify that saving edits results in updated values appearing in the display (via SSE)
- **FR-013**: Test project MUST verify that search filtering updates the list with matching results (via SSE)
- **FR-014**: Test project MUST verify that bulk operations update selected items' statuses (via SSE)
- **FR-015**: Test project MUST verify that different forms maintain state isolation
- **FR-016**: Test project MUST wait for SSE updates with a default timeout of 5 seconds (configurable)

#### Output and Reporting

- **FR-017**: Test project MUST produce clear pass/fail output with diagnostic information for failures
- **FR-018**: Test project MUST exit with non-zero status code if any test fails
- **FR-019**: Test project MUST report which sample was tested in the output

### Key Entities

- **Test Project**: An F# Playwright test project (`sample/Frank.Datastar.Tests/`) that runs browser automation tests
- **Sample Name Parameter**: A required string parameter matching pattern `Frank.Datastar.*` that identifies which sample to test
- **Target Sample**: One of the Frank.Datastar sample applications (Basic, Hox, Oxpecker, or future additions) located in `sample/`
- **SSE Connection**: A persistent connection that receives server-pushed UI updates; tests must verify updates arrive through this channel
- **Test Result**: The outcome of a single test case including pass/fail status and diagnostic information
- **Contact**: An entity with first name, last name, and email that supports click-to-edit operations
- **Fruit List**: A searchable list used for search filtering validation
- **User**: An entity with active/inactive status used for bulk update validation

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: Tests correctly identify when SSE updates do not arrive (detecting the curl-based test limitation that prompted this change)
- **SC-002**: Tests detect all previously-identified bugs: click-to-edit not showing current values, search clearing the list, bulk updates not modifying data
- **SC-003**: The same test suite runs successfully against all three sample applications (Basic, Hox, Oxpecker) by changing only the sample name parameter
- **SC-004**: Test suite completes in under 60 seconds per sample application
- **SC-005**: Test failures include enough diagnostic information for a developer to understand what went wrong without re-running manually
- **SC-006**: Running the test suite 5 times consecutively produces consistent results (no flaky failures due to timing)
- **SC-007**: Running `dotnet test` without parameters displays a helpful message listing all available `Frank.Datastar.*` samples
- **SC-008**: Adding a new `Frank.Datastar.NewEngine` sample folder makes it immediately available as a test target without code changes

## Assumptions

- Playwright supports F# through its .NET bindings
- Sample applications have seed data pre-loaded (at least one contact with known values, fruits list, users with known statuses)
- The target sample application server is started separately before running tests
- SSE connections can be observed through Playwright's browser automation capabilities
- Tests run sequentially (not in parallel) to avoid SSE channel conflicts since samples are single-user
- The test project will reference sample folder paths relative to its location in `sample/`
- All sample applications run on port 5000 (configured via launchSettings.json); only one sample runs at a time
- Tests always target `http://localhost:5000` - the user ensures the correct sample is running before executing tests

## Out of Scope

- Fixing bugs in the sample applications (Basic, Hox, Oxpecker) - tests only detect and report issues
- Modifying the sample application source code
- Adding new features to the sample applications
- Testing concurrent user scenarios (samples are explicitly single-user)
- Performance/load testing beyond basic timeout verification
- Maintaining the existing bash test.sh scripts (these will be superseded by the browser automation approach)
- Automatic server startup (the sample server must be running before tests execute)

## Research

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
