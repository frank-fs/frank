# Implementation Plan: Browser Automation Test Suite for Frank.Datastar Samples

**Branch**: `005-fix-sample-tests` | **Date**: 2026-01-28 | **Spec**: [spec.md](./spec.md)
**Input**: Feature specification from `/specs/005-fix-sample-tests/spec.md`

## Summary

Create an F# Playwright test project (`sample/Frank.Datastar.Tests/`) that validates SSE-driven UI updates in Frank.Datastar sample applications. The test project accepts a sample name parameter (e.g., `Frank.Datastar.Basic`), validates it against existing sample folders, and runs browser automation tests that verify click-to-edit, search filtering, bulk updates, and state isolation behaviors by observing actual DOM changes pushed through SSE connections.

## Technical Context

**Language/Version**: F# 8.0+ targeting .NET 10.0 (matching sample projects)
**Primary Dependencies**: Microsoft.Playwright.NUnit (1.57.0+), NUnit (3.x/4.x)
**Storage**: N/A (tests read from sample applications' in-memory stores)
**Testing**: NUnit with Playwright integration via `dotnet test`
**Target Platform**: Cross-platform (Windows, macOS, Linux) via .NET
**Project Type**: Single test project in `sample/` directory
**Performance Goals**: Complete all tests in under 60 seconds per sample
**Constraints**: 5-second default timeout for SSE updates; sequential test execution (no parallelism)
**Scale/Scope**: ~15-20 test cases covering 6 user stories across 3 sample applications

## Constitution Check

*GATE: Must pass before Phase 0 research. Re-check after Phase 1 design.*

| Principle | Status | Notes |
|-----------|--------|-------|
| I. Resource-Oriented Design | ✅ N/A | Test project doesn't add resources to Frank |
| II. Idiomatic F# | ✅ Pass | F# test project using task expressions |
| III. Library, Not Framework | ✅ Pass | Test project is standalone, doesn't modify Frank |
| IV. ASP.NET Core Native | ✅ N/A | Test project is external to Frank core |
| V. Performance Parity | ✅ N/A | No performance impact on Frank itself |

**Gate Result**: PASS - No violations. This is a test/tooling project that doesn't modify Frank core.

## Project Structure

### Documentation (this feature)

```text
specs/005-fix-sample-tests/
├── spec.md              # Feature specification
├── plan.md              # This file
├── research.md          # Phase 0 research findings
├── data-model.md        # Phase 1 data model
├── quickstart.md        # Phase 1 quickstart guide
└── tasks.md             # Phase 2 task breakdown (via /speckit.tasks)
```

### Source Code (repository root)

```text
sample/
├── Frank.Datastar.Basic/       # Existing sample
├── Frank.Datastar.Hox/         # Existing sample
├── Frank.Datastar.Oxpecker/    # Existing sample
└── Frank.Datastar.Tests/       # NEW: Browser automation test project
    ├── Frank.Datastar.Tests.fsproj
    ├── TestConfiguration.fs     # Sample discovery, URL config, timeout settings
    ├── TestHelpers.fs           # Playwright helpers, SSE wait utilities
    ├── ClickToEditTests.fs      # US1: Click-to-edit validation
    ├── SearchFilterTests.fs     # US2: Search filtering validation
    ├── BulkUpdateTests.fs       # US3: Bulk update validation
    ├── StateIsolationTests.fs   # US4: State isolation validation
    └── test.runsettings         # Test run configuration
```

**Structure Decision**: Single test project alongside existing samples. Tests are organized by user story/feature area. Configuration and helpers are separated for reusability.

## Complexity Tracking

No constitution violations to justify.

## Implementation Phases

### Phase 0: Research (Completed)

Research findings documented in [research.md](./research.md).

### Phase 1: Design & Contracts

#### Test Project Configuration

The test project uses environment variables and `.runsettings` for configuration:

```xml
<!-- test.runsettings -->
<RunSettings>
  <RunConfiguration>
    <EnvironmentVariables>
      <DATASTAR_SAMPLE><!-- Required: e.g., Frank.Datastar.Basic --></DATASTAR_SAMPLE>
      <DATASTAR_BASE_URL>http://localhost:5000</DATASTAR_BASE_URL>
      <DATASTAR_TIMEOUT_MS>5000</DATASTAR_TIMEOUT_MS>
    </EnvironmentVariables>
  </RunConfiguration>
</RunSettings>
```

#### Sample Discovery Logic

```fsharp
// Discovers available Frank.Datastar.* samples at runtime
let discoverSamples (sampleRoot: string) : string list =
    Directory.GetDirectories(sampleRoot)
    |> Array.filter (fun d -> Path.GetFileName(d).StartsWith("Frank.Datastar."))
    |> Array.filter (fun d -> Path.GetFileName(d) <> "Frank.Datastar.Tests")
    |> Array.map Path.GetFileName
    |> Array.toList
```

#### SSE Wait Pattern

Critical for validating SSE-pushed UI updates:

```fsharp
// Wait for element content to change via SSE
let waitForSseUpdate (page: IPage) (selector: string) (expectedContent: string) = task {
    do! page.WaitForFunctionAsync(
        sprintf "() => document.querySelector('%s')?.textContent?.includes('%s')" selector expectedContent,
        PageWaitForFunctionOptions(Timeout = float timeoutMs)
    )
}

// Wait for element to appear via SSE
let waitForElementVisible (page: IPage) (selector: string) = task {
    do! page.Locator(selector).WaitForAsync(
        LocatorWaitForOptions(State = WaitForSelectorState.Visible, Timeout = float timeoutMs)
    )
}
```

### Phase 2: Implementation Tasks

See [tasks.md](./tasks.md) (generated via `/speckit.tasks`).

## Test Execution Commands

```bash
# Run tests against a specific sample (sample server must be running)
DATASTAR_SAMPLE=Frank.Datastar.Basic dotnet test sample/Frank.Datastar.Tests/

# Run with custom timeout
DATASTAR_SAMPLE=Frank.Datastar.Hox DATASTAR_TIMEOUT_MS=10000 dotnet test sample/Frank.Datastar.Tests/

# Run with .runsettings file
dotnet test sample/Frank.Datastar.Tests/ --settings sample/Frank.Datastar.Tests/test.runsettings
```

## Key Design Decisions

1. **NUnit over xUnit**: NUnit's `TestContext.Parameters` integrates well with `.runsettings` for passing the sample name parameter.

2. **Environment variables for sample selection**: The `DATASTAR_SAMPLE` environment variable is required; tests fail fast with helpful message if missing.

3. **WaitForFunctionAsync for SSE**: Cannot use `WaitForLoadStateAsync(NetworkIdle)` because SSE connections keep the network active. Instead, wait for specific DOM changes.

4. **Sequential test execution**: Tests run sequentially (NUnit default) to avoid SSE channel conflicts since samples are single-user.

5. **No automatic server startup**: Tests assume the sample server is already running on localhost:5000. This keeps the test project simple and allows developers to run the server in debug mode.

## Risk Mitigation

| Risk | Mitigation |
|------|------------|
| Playwright browser install required | Document `pwsh playwright.ps1 install` in quickstart |
| SSE timing flakiness | Use WaitForFunctionAsync with 5-second timeout; configurable |
| Different samples have different bugs | Tests detect bugs, don't assume correctness |
| Sample server not running | Clear error message with instructions |

## Post-Implementation Verification

After implementation, verify:
- [ ] `dotnet test` without `DATASTAR_SAMPLE` shows help with available samples
- [ ] Tests pass against Frank.Datastar.Basic (or fail detecting known bugs)
- [ ] Tests run against all three samples with same codebase
- [ ] 5 consecutive runs produce consistent results
- [ ] Test failures include diagnostic information
