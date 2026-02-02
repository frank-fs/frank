# Quickstart: Update Oxpecker Sample

**Feature**: 007-update-oxpecker-sample
**Date**: 2026-02-02

## Prerequisites

- .NET 10.0 SDK
- Playwright browsers installed (for tests)

## Running the Sample

```bash
# Start the Oxpecker sample server
dotnet run --project sample/Frank.Datastar.Oxpecker/
```

Server runs at http://localhost:5000

## Running Tests

```bash
# Start the server (in background)
dotnet run --project sample/Frank.Datastar.Oxpecker/ &
sleep 3

# Run tests against Oxpecker sample
DATASTAR_SAMPLE=Frank.Datastar.Oxpecker dotnet test sample/Frank.Datastar.Tests/

# Stop the server
pkill -f "Frank.Datastar.Oxpecker"
```

### One-liner

```bash
dotnet run --project sample/Frank.Datastar.Oxpecker/ & sleep 3 && DATASTAR_SAMPLE=Frank.Datastar.Oxpecker dotnet test sample/Frank.Datastar.Tests/; pkill -f "Frank.Datastar.Oxpecker"
```

### Headed Mode (Visual Browser)

```bash
HEADED=1 DATASTAR_SAMPLE=Frank.Datastar.Oxpecker dotnet test sample/Frank.Datastar.Tests/
```

## Key Files

| File | Purpose |
|------|---------|
| `sample/Frank.Datastar.Oxpecker/Program.fs` | Main application code |
| `sample/Frank.Datastar.Oxpecker/wwwroot/index.html` | Static HTML page |
| `sample/Frank.Datastar.Oxpecker/Frank.Datastar.Oxpecker.fsproj` | Project file |
| `sample/Frank.Datastar.Tests/` | Playwright test suite |

## Development Workflow

1. Make changes to `Program.fs`
2. Restart the server (`Ctrl+C` then `dotnet run`)
3. Run tests to verify behavior matches Basic sample
4. Repeat until all tests pass

## Comparison with Basic Sample

The Oxpecker sample should behave identically to the Basic sample. To verify:

```bash
# Test Basic sample
dotnet run --project sample/Frank.Datastar.Basic/ & sleep 3 && DATASTAR_SAMPLE=Frank.Datastar.Basic dotnet test sample/Frank.Datastar.Tests/; pkill -f "Frank.Datastar.Basic"

# Test Oxpecker sample
dotnet run --project sample/Frank.Datastar.Oxpecker/ & sleep 3 && DATASTAR_SAMPLE=Frank.Datastar.Oxpecker dotnet test sample/Frank.Datastar.Tests/; pkill -f "Frank.Datastar.Oxpecker"
```

Both should pass all tests.

## Debugging Tips

1. **Tests timeout**: Server not running or wrong port
2. **SSE not working**: Check `data-init="@get('/sse')"` on body element
3. **HTML mismatch**: Compare generated HTML with Basic sample using browser dev tools
4. **Signals not sent**: Check `data-bind:` attributes match Basic sample
