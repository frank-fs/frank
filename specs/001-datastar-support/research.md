# Research: Datastar SSE Streaming Support

**Date**: 2025-01-25
**Feature**: 001-datastar-support

## Research Questions Resolved

### 1. How should sample applications host Frank resources?

**Decision**: Use Frank's `webHost` computation expression builder

**Rationale**:
- Frank's established pattern uses `webHost args { ... }` with `resource` custom operations
- All existing Frank samples (Sample, Frank.Oxpecker, Frank.Giraffe, Frank.Falco) use this pattern
- Consistent with Frank's resource-oriented design philosophy

**Correct Pattern**:
```fsharp
[<EntryPoint>]
let main args =
    webHost args {
        useDefaults
        resource home
        resource myDatastarResource
        // ... more resources
    }
    0
```

**Rejected Alternative**: Creating a `UseResource` extension for ASP.NET Core's minimal API `WebApplication` was considered but rejected as it deviates from Frank's established patterns.

### 2. Which Hox APIs should be used for HTML rendering?

**Decision**: Use `Render.asString` for the Task-based async API, and `.extension` syntax for attributes

**Rationale**: Hox 3.x uses F# naming conventions

**Key APIs**:
- `Hox.Rendering.Render.asString` - Render node to string (Task API)
- `Hox.Core.h` - Create HTML element
- `Hox.Core.fragment` - Create fragment of elements
- `Hox.Core.Text` - Text content

**Attributes**: Use `.extension` syntax, not `Attr("name", "value")`:
```fsharp
// Correct:
h("img", []).src(user.Avatar).alt(user.Name)

// Incorrect:
h("img", [Attr("src", user.Avatar); Attr("alt", user.Name)])
```

See: https://hox.tunaxor.me/guides/general-usage.html#attributes

### 3. How to handle F# string interpolation format specifiers?

**Decision**: Use explicit `.ToString("format")` instead of interpolation format specifiers

**Rationale**: F# has restrictions on format specifiers in interpolated strings within certain contexts (like inside list comprehensions)

**Example**:
```fsharp
// Instead of:
$"{DateTime.Now:HH:mm:ss}"  // May fail in some contexts

// Use:
DateTime.Now.ToString("HH:mm:ss")
```

### 4. What HTTP methods should Datastar operations support?

**Decision**: Any HTTP method (not just GET)

**Rationale**:
- Datastar uses `@microsoft/fetch-event-source` which supports any HTTP method
- Native SSE (EventSource) is GET-only, but Datastar doesn't use native SSE
- Current implementation hardcodes GET which violates FR-001 intent

**Change Required**: Update custom operations to accept HTTP method parameter to match StarFederation.Datastar.FSharp flexibility

### 5. How should client disconnection be detected?

**Decision**: Use `HttpContext.RequestAborted` cancellation token

**Rationale**: ASP.NET Core provides this token to detect when a client disconnects

**Implementation Pattern**:
```fsharp
let handler (ctx: HttpContext) = task {
    let token = ctx.RequestAborted
    while not token.IsCancellationRequested do
        // Send updates
        do! Task.Delay(100, token)
}
```

### 6. How should the library handle stream initialization?

**Decision**: Single stream start per request, managed by the custom operation

**Rationale**:
- SSE requires exactly one stream initialization
- Multiple start calls would corrupt the stream
- The `datastar` custom operation handles this automatically

**Pattern**: The extension method calls `StartServerEventStreamAsync` once, then invokes the user's handler

## Technology Decisions

### StarFederation.Datastar.FSharp Package

- **Version**: Latest stable (dynamically resolved via `Version="*"`)
- **Key Types**:
  - `ServerSentEventGenerator` - Static methods for SSE operations
  - `PatchElementsAsync` - Send HTML fragments
  - `PatchSignalsAsync` - Send signal updates
  - `RemoveElementAsync` - Remove DOM elements
  - `ExecuteScriptAsync` - Execute client JS
  - `ReadSignalsAsync<'T>` - Deserialize client signals

### Hox Package (for sample only)

- **Version**: 3.x
- **Purpose**: Demonstrate type-safe HTML rendering with Datastar
- **Not a dependency of Frank.Datastar** - only used in sample app

## Open Questions (for implementation)

1. ~~How to support HTTP methods other than GET?~~ - Resolved: Update API to accept method parameter
2. Performance benchmarks - can be deferred to post-release

## References

- [Datastar Documentation](https://data-star.dev/)
- [StarFederation.Datastar NuGet](https://www.nuget.org/packages/StarFederation.Datastar.FSharp)
- [Hox Documentation](https://github.com/AngelMunoz/Hox)
- [fetch-event-source](https://github.com/Azure/fetch-event-source)
