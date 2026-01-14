# Frank.Datastar Tests

Unit tests for the Frank.Datastar library using [Expecto](https://github.com/haf/expecto).

## Test Coverage

This test suite covers the following `ResourceBuilder` extensions:

1. **Datastar()** - Verifies SSE stream starts correctly and executes multiple Datastar operations
2. **PatchElements()** - Tests all three overloads:
   - String overload
   - Synchronous function overload (`HttpContext -> string`)
   - Asynchronous function overload (`HttpContext -> Task<string>`)
3. **RemoveElement()** - Validates correct remove element command with CSS selector
4. **PatchSignals()** - Tests both overloads:
   - String overload
   - Function overload (`HttpContext -> string`)
5. **ReadSignals()** - Verifies signal deserialization from client request and handler invocation
   - Includes test for invalid JSON handling

## Running Tests

### From the test project directory:
```bash
dotnet run --project Frank.Datastar.Tests.fsproj
```

### From the solution root:
```bash
dotnet run --project test\Frank.Datastar.Tests\Frank.Datastar.Tests.fsproj
```

### Build only:
```bash
dotnet build test\Frank.Datastar.Tests\Frank.Datastar.Tests.fsproj
```

## Test Framework

These tests use **Expecto**, an F#-first testing framework that provides:
- Strong typing
- Excellent F# support
- Composable test suites
- Good performance

Note: Use `dotnet run` instead of `dotnet test` since Expecto tests are executable console applications.

## Test Structure

Each test follows the Arrange-Act-Assert pattern:

```fsharp
testCase "Test name" <| fun () ->
    // Arrange - set up test context
    let context = createMockContext()
    
    // Act - execute the operation
    let resource = ResourceBuilder("/test") { operation }
    endpoint.RequestDelegate.Invoke(context).Wait()
    
    // Assert - verify expectations
    let responseBody = getResponseBody context
    Expect.stringContains responseBody "expected" "message"
```

## Helper Functions

- `createMockContext()` - Creates a mock `HttpContext` with in-memory response stream
- `getResponseBody(context)` - Reads the response body as a string
- `setRequestBody(context, body)` - Sets the request body for testing input signals
