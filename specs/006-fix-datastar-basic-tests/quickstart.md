# Quickstart: Running Frank.Datastar.Basic Tests

**Feature**: 006-fix-datastar-basic-tests
**Date**: 2026-02-01

## Prerequisites

- .NET 10.0 SDK
- Playwright browsers installed (`pwsh bin/Debug/net10.0/playwright.ps1 install` or equivalent)

## Running Tests

### Step 1: Start the Sample Server

In one terminal:

```bash
dotnet run --project sample/Frank.Datastar.Basic/
```

The server starts on `http://localhost:5000`.

### Step 2: Run the Tests

In another terminal:

```bash
DATASTAR_SAMPLE=Frank.Datastar.Basic dotnet test sample/Frank.Datastar.Tests/
```

### Optional: Headed Mode (Watch Tests Run)

```bash
DATASTAR_SAMPLE=Frank.Datastar.Basic HEADED=1 dotnet test sample/Frank.Datastar.Tests/
```

### Optional: Custom Timeout

```bash
DATASTAR_SAMPLE=Frank.Datastar.Basic DATASTAR_TIMEOUT_MS=15000 dotnet test sample/Frank.Datastar.Tests/
```

## Environment Variables

| Variable | Default | Description |
|----------|---------|-------------|
| `DATASTAR_SAMPLE` | (required) | Sample name, e.g., `Frank.Datastar.Basic` |
| `DATASTAR_BASE_URL` | `http://localhost:5000` | Base URL of running sample |
| `DATASTAR_TIMEOUT_MS` | `5000` | Timeout for SSE updates in milliseconds |
| `HEADED` | `false` | Set to `1` or `true` to watch browser |

## Current Test Status

### Before Fix (8 failing)

```
Failed!  - Failed: 8, Passed: 12, Skipped: 0, Total: 20
```

**Failing tests:**
- `BulkUpdateTests`: 4 failures (bulk activate/deactivate not working)
- `SearchFilterTests`: 2 failures (search results not updating)
- `ClickToEditTests`: 2 failures (cancel/persistence not working)

### After Fix (target: 20 passing)

```
Passed!  - Failed: 0, Passed: 20, Skipped: 0, Total: 20
```

## Test Categories

| Test File | Tests | Description |
|-----------|-------|-------------|
| `ConfigurationTests.fs` | 6 | Environment configuration loading |
| `StateIsolationTests.fs` | 3 | Data isolation between sections |
| `ClickToEditTests.fs` | 4 | Contact edit/save/cancel flows |
| `SearchFilterTests.fs` | 4 | Fruit list search filtering |
| `BulkUpdateTests.fs` | 6 | User status bulk operations |

## Troubleshooting

### "Cannot connect to http://localhost:5000"

The sample server isn't running. Start it first:

```bash
dotnet run --project sample/Frank.Datastar.Basic/
```

### "DATASTAR_SAMPLE environment variable is required"

You must specify which sample to test:

```bash
DATASTAR_SAMPLE=Frank.Datastar.Basic dotnet test sample/Frank.Datastar.Tests/
```

### Timeout errors on SSE updates

Increase the timeout:

```bash
DATASTAR_SAMPLE=Frank.Datastar.Basic DATASTAR_TIMEOUT_MS=15000 dotnet test sample/Frank.Datastar.Tests/
```

### Tests pass locally but fail intermittently

This is the symptom of the multi-channel timing issue this feature is fixing. After implementing the single-channel architecture, tests should be deterministic.
