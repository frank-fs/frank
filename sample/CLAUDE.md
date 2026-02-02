# Frank Sample Applications

## Running Tests

**IMPORTANT**: The sample server MUST be started before running tests. Tests do NOT start the server automatically.

### Step-by-step Instructions

1. **Start the sample server first** (in a background process or separate terminal):
   ```bash
   dotnet run --project sample/Frank.Datastar.Basic/ &
   ```

2. **Wait for the server to start** (approximately 2-3 seconds):
   ```bash
   sleep 3
   ```

3. **Run the tests**:
   ```bash
   DATASTAR_SAMPLE=Frank.Datastar.Basic dotnet test sample/Frank.Datastar.Tests/
   ```

4. **Stop the server when done**:
   ```bash
   pkill -f "Frank.Datastar.Basic"
   ```

### One-liner Command

```bash
dotnet run --project sample/Frank.Datastar.Basic/ & sleep 3 && DATASTAR_SAMPLE=Frank.Datastar.Basic dotnet test sample/Frank.Datastar.Tests/; pkill -f "Frank.Datastar.Basic"
```

### Available Sample Applications

| Sample | Port | Description |
|--------|------|-------------|
| Frank.Datastar.Basic | 5000 | Basic Frank + Datastar patterns |
| Frank.Datastar.Hox | 5000 | Frank + Datastar with Hox view engine |
| Frank.Datastar.Oxpecker | 5000 | Frank + Datastar with Oxpecker.ViewEngine |

### Environment Variables

| Variable | Default | Description |
|----------|---------|-------------|
| `DATASTAR_SAMPLE` | (required) | Which sample to test (e.g., `Frank.Datastar.Basic`) |
| `DATASTAR_BASE_URL` | `http://localhost:5000` | Base URL of running sample |
| `DATASTAR_TIMEOUT_MS` | `5000` | Timeout for SSE updates in milliseconds |
| `HEADED` | `false` | Set to `1` or `true` to run browser in headed mode |

### Debugging Tips

1. If tests timeout waiting for elements, the server is probably not running
2. Use `HEADED=1` to see the browser interactions visually
3. Server must be restarted between test runs if you modify the sample code
