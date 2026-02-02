# Quickstart: Frank.Datastar.Hox Sample

**Feature**: 008-update-hox-sample
**Date**: 2026-02-02

## Prerequisites

- .NET 10.0 SDK
- Playwright browsers (for testing)

## Build

```bash
dotnet build sample/Frank.Datastar.Hox/
```

## Run

```bash
dotnet run --project sample/Frank.Datastar.Hox/
```

The server starts at http://localhost:5000

## Test

**Important**: The sample server must be running before executing tests.

### Manual Testing

1. Start the server (in background or separate terminal):
   ```bash
   dotnet run --project sample/Frank.Datastar.Hox/ &
   ```

2. Wait for startup:
   ```bash
   sleep 3
   ```

3. Run tests:
   ```bash
   DATASTAR_SAMPLE=Frank.Datastar.Hox dotnet test sample/Frank.Datastar.Tests/
   ```

4. Stop server:
   ```bash
   pkill -f "Frank.Datastar.Hox"
   ```

### One-Liner

```bash
dotnet run --project sample/Frank.Datastar.Hox/ & sleep 3 && DATASTAR_SAMPLE=Frank.Datastar.Hox dotnet test sample/Frank.Datastar.Tests/; pkill -f "Frank.Datastar.Hox"
```

### Headed Mode (Debug)

To see browser interactions:

```bash
HEADED=1 DATASTAR_SAMPLE=Frank.Datastar.Hox dotnet test sample/Frank.Datastar.Tests/
```

## Environment Variables

| Variable | Default | Description |
|----------|---------|-------------|
| `DATASTAR_SAMPLE` | (required) | Must be `Frank.Datastar.Hox` |
| `DATASTAR_BASE_URL` | `http://localhost:5000` | Server URL |
| `DATASTAR_TIMEOUT_MS` | `5000` | SSE update timeout |
| `HEADED` | `false` | Show browser (`1` or `true`) |

## Project Structure

```
sample/Frank.Datastar.Hox/
├── Program.fs                    # Main application
├── Frank.Datastar.Hox.fsproj     # Project file
└── wwwroot/
    └── index.html                # Client-side code (same as Basic)
```

## Verify Implementation

After updating, verify:

1. **Build succeeds**: `dotnet build sample/Frank.Datastar.Hox/`
2. **Server starts**: Browse to http://localhost:5000
3. **All tests pass**: Run the test one-liner above
4. **Manual verification**: Test each demo section in browser
   - Click-to-Edit: Load Contact → Edit → Save/Cancel
   - Search: Load Fruits → Type filter → Clear
   - Delete: Load Items → Delete rows
   - Bulk Update: Load Users → Select checkboxes → Activate/Deactivate
   - Form Validation: Show Form → Enter data → Submit

## Troubleshooting

| Issue | Solution |
|-------|----------|
| Tests timeout waiting for elements | Server not running - start it first |
| Port 5000 in use | Kill existing process: `pkill -f Frank.Datastar` |
| Playwright not found | Run `pwsh bin/Debug/net10.0/playwright.ps1 install` |
| Build fails | Check .NET 10.0 SDK: `dotnet --version` |
