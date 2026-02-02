# Data Model: Browser Automation Test Suite

**Date**: 2026-01-28
**Feature**: 005-fix-sample-tests

## Overview

This test project doesn't create or persist data. It validates data displayed by sample applications through browser automation. The data model describes the test infrastructure entities and the domain entities being validated.

## Test Infrastructure Entities

### TestConfiguration

Runtime configuration for test execution.

| Field | Type | Source | Description |
|-------|------|--------|-------------|
| SampleName | string | `DATASTAR_SAMPLE` env var | Required sample to test (e.g., "Frank.Datastar.Basic") |
| BaseUrl | string | `DATASTAR_BASE_URL` env var | Default: "http://localhost:5000" |
| TimeoutMs | int | `DATASTAR_TIMEOUT_MS` env var | Default: 5000 |
| AvailableSamples | string list | Filesystem discovery | Discovered `Frank.Datastar.*` folders |

**Validation Rules**:
- `SampleName` must start with "Frank.Datastar."
- `SampleName` must exist in `AvailableSamples`
- `TimeoutMs` must be positive integer

### TestResult

Represents outcome of a single test case.

| Field | Type | Description |
|-------|------|-------------|
| TestName | string | Full test name (e.g., "ClickToEditTests.EditFormShowsCurrentValues") |
| Status | Pass \| Fail \| Skip | Test outcome |
| Duration | TimeSpan | Execution time |
| ErrorMessage | string option | Present when Status = Fail |
| Screenshot | string option | Path to failure screenshot (optional) |

## Domain Entities (Under Test)

These entities exist in the sample applications. Tests verify their behavior through the UI.

### Contact

Displayed in click-to-edit feature.

| Field | Type | Seed Value | Description |
|-------|------|------------|-------------|
| Id | int | 1 | Contact identifier |
| FirstName | string | "Joe" | First name (editable) |
| LastName | string | "Smith" | Last name (editable) |
| Email | string | "joe@smith.org" | Email address (editable) |

**UI Selectors** (expected in samples):
- View mode: `#contact-view`, `.contact-firstname`, `.contact-lastname`, `.contact-email`
- Edit button: `[data-action="edit"]` or similar
- Edit form: `#contact-edit`, `input[name="firstName"]`, `input[name="lastName"]`, `input[name="email"]`
- Save button: `[data-action="save"]` or `button[type="submit"]`

### Fruit

Displayed in searchable list.

| Field | Type | Seed Values | Description |
|-------|------|-------------|-------------|
| Name | string | "Apple", "Apricot", "Banana", "Cherry", "Date" | Fruit name |

**UI Selectors** (expected in samples):
- List container: `#fruits-list` or `[data-list="fruits"]`
- List items: `.fruit-item` or `li`
- Search input: `input[name="search"]` or `#fruit-search`

### User

Displayed in bulk update feature.

| Field | Type | Seed Values | Description |
|-------|------|-------------|-------------|
| Id | int | 1, 2, 3, 4 | User identifier |
| Name | string | Various | User display name |
| Status | Active \| Inactive | Mixed | Current status |

**UI Selectors** (expected in samples):
- Table: `#users-table`
- Row checkbox: `input[type="checkbox"][data-user-id]`
- Status indicator: `.user-status` or `[data-status]`
- Bulk activate button: `[data-action="bulk-activate"]`
- Bulk deactivate button: `[data-action="bulk-deactivate"]`

### Registration Form

Separate form for state isolation testing.

| Field | Type | Description |
|-------|------|-------------|
| FirstName | string | Registration first name input |
| LastName | string | Registration last name input |
| Email | string | Registration email input |

**UI Selectors** (expected in samples):
- Form: `#registration-form`
- Inputs: `input[name="regFirstName"]`, `input[name="regLastName"]`, `input[name="regEmail"]`

## Element Wait Patterns

### SSE Update Detection

Since SSE pushes HTML fragments that replace DOM content, tests must wait for content changes:

```fsharp
// Pattern: Wait for text content to match expected value
let waitForText (page: IPage) (selector: string) (expected: string) (timeoutMs: int) = task {
    do! page.WaitForFunctionAsync(
        sprintf "() => document.querySelector('%s')?.textContent?.trim() === '%s'" selector expected,
        PageWaitForFunctionOptions(Timeout = float timeoutMs)
    )
}

// Pattern: Wait for element to contain text (partial match)
let waitForTextContains (page: IPage) (selector: string) (substring: string) (timeoutMs: int) = task {
    do! page.WaitForFunctionAsync(
        sprintf "() => document.querySelector('%s')?.textContent?.includes('%s')" selector substring,
        PageWaitForFunctionOptions(Timeout = float timeoutMs)
    )
}

// Pattern: Wait for element to appear
let waitForVisible (page: IPage) (selector: string) (timeoutMs: int) = task {
    do! page.Locator(selector).WaitForAsync(
        LocatorWaitForOptions(State = WaitForSelectorState.Visible, Timeout = float timeoutMs)
    )
}

// Pattern: Wait for element to disappear
let waitForHidden (page: IPage) (selector: string) (timeoutMs: int) = task {
    do! page.Locator(selector).WaitForAsync(
        LocatorWaitForOptions(State = WaitForSelectorState.Hidden, Timeout = float timeoutMs)
    )
}
```

## State Transitions

### Click-to-Edit Flow

```
View Mode → [Click Edit] → Edit Mode → [Click Save] → View Mode (updated)
                              ↓
                        [Click Cancel]
                              ↓
                        View Mode (unchanged)
```

### Search Filter Flow

```
Full List → [Type Query] → Filtered List → [Clear Query] → Full List
```

### Bulk Update Flow

```
Initial State → [Select Items] → Items Selected → [Click Action] → Updated State
```

## Notes

- Actual UI selectors may vary between samples (Basic, Hox, Oxpecker)
- Tests should use resilient locators that work across implementations
- If selectors differ significantly, consider data-testid attributes in samples
