# Quickstart: Frank.Datastar Browser Automation Tests

## Prerequisites

- .NET 10.0 SDK
- PowerShell (for Playwright browser installation)

## Setup

### 1. Install Playwright Browsers

After building the test project for the first time:

```bash
cd sample/Frank.Datastar.Tests
dotnet build
pwsh bin/Debug/net10.0/playwright.ps1 install
```

This installs Chromium, Firefox, and WebKit browsers used by Playwright.

### 2. Start a Sample Application

In a separate terminal, start the sample you want to test:

```bash
# Option 1: Basic sample
dotnet run --project sample/Frank.Datastar.Basic

# Option 2: Hox sample
dotnet run --project sample/Frank.Datastar.Hox

# Option 3: Oxpecker sample
dotnet run --project sample/Frank.Datastar.Oxpecker
```

The application will start on http://localhost:5000.

## Running Tests

### Basic Usage

```bash
# Specify which sample you're testing
DATASTAR_SAMPLE=Frank.Datastar.Basic dotnet test sample/Frank.Datastar.Tests/
```

**Note**: Some tests may fail - this is expected behavior. The test suite is designed to detect bugs in the sample applications. Test failures indicate bugs in the samples that need to be fixed, not issues with the test suite itself.

### Windows (PowerShell)

```powershell
$env:DATASTAR_SAMPLE="Frank.Datastar.Basic"
dotnet test sample/Frank.Datastar.Tests/
```

### Windows (Command Prompt)

```cmd
set DATASTAR_SAMPLE=Frank.Datastar.Basic
dotnet test sample/Frank.Datastar.Tests/
```

## Configuration Options

| Environment Variable | Default | Description |
|---------------------|---------|-------------|
| `DATASTAR_SAMPLE` | (required) | Sample name, e.g., `Frank.Datastar.Basic` |
| `DATASTAR_BASE_URL` | `http://localhost:5000` | Base URL of running sample |
| `DATASTAR_TIMEOUT_MS` | `5000` | Timeout for SSE updates in milliseconds |

### Custom Timeout

If tests are flaky due to slow updates:

```bash
DATASTAR_SAMPLE=Frank.Datastar.Hox DATASTAR_TIMEOUT_MS=10000 dotnet test sample/Frank.Datastar.Tests/
```

## Common Issues

### "DATASTAR_SAMPLE environment variable required"

You forgot to specify which sample to test. Set the environment variable:

```bash
DATASTAR_SAMPLE=Frank.Datastar.Basic dotnet test sample/Frank.Datastar.Tests/
```

### "Sample 'Frank.Datastar.Foo' not found"

The specified sample doesn't exist. Check available samples:

```bash
ls sample/ | grep Frank.Datastar
```

### "Connection refused" or timeout on page load

The sample server isn't running. Start it in a separate terminal:

```bash
dotnet run --project sample/Frank.Datastar.Basic
```

### Tests pass locally but fail in CI

Ensure Playwright browsers are installed in CI:

```yaml
# GitHub Actions example
- name: Install Playwright
  run: pwsh sample/Frank.Datastar.Tests/bin/Debug/net10.0/playwright.ps1 install
```

## Test Categories

Run specific test categories:

```bash
# Click-to-edit tests only
DATASTAR_SAMPLE=Frank.Datastar.Basic dotnet test sample/Frank.Datastar.Tests/ --filter "FullyQualifiedName~ClickToEdit"

# Search filter tests only
DATASTAR_SAMPLE=Frank.Datastar.Basic dotnet test sample/Frank.Datastar.Tests/ --filter "FullyQualifiedName~SearchFilter"

# Bulk update tests only
DATASTAR_SAMPLE=Frank.Datastar.Basic dotnet test sample/Frank.Datastar.Tests/ --filter "FullyQualifiedName~BulkUpdate"
```

## Test All Samples

To test all samples sequentially:

```bash
#!/bin/bash
for sample in Frank.Datastar.Basic Frank.Datastar.Hox Frank.Datastar.Oxpecker; do
    echo "=== Testing $sample ==="
    # Note: You need to restart the server for each sample
    DATASTAR_SAMPLE=$sample dotnet test sample/Frank.Datastar.Tests/
done
```

## Debugging Failed Tests

### Run in headed mode (visible browser)

Set the `HEADED` environment variable:

```bash
DATASTAR_SAMPLE=Frank.Datastar.Basic HEADED=1 dotnet test sample/Frank.Datastar.Tests/
```

### Verbose output

```bash
DATASTAR_SAMPLE=Frank.Datastar.Basic dotnet test sample/Frank.Datastar.Tests/ -v detailed
```

### Single test

```bash
DATASTAR_SAMPLE=Frank.Datastar.Basic dotnet test sample/Frank.Datastar.Tests/ --filter "TestName=EditFormShowsCurrentValues"
```
