# Quickstart: Conditional Before-Routing Middleware

## Overview

This feature adds two new operations to the Frank `WebHostBuilder`:
- `plugBeforeRoutingWhen` - Apply middleware before routing when a condition is true
- `plugBeforeRoutingWhenNot` - Apply middleware before routing when a condition is false

## Usage Examples

### Conditional HTTPS Redirection (Production Only)

```fsharp
open Frank.Builder

let isProduction (app: IApplicationBuilder) =
    app.ApplicationServices
        .GetService<IWebHostEnvironment>()
        .IsProduction()

webHost args {
    plugBeforeRoutingWhenNot isProduction HttpsPolicyBuilderExtensions.UseHttpsRedirection

    resource home
}
```

### Conditional Static Files (Development Only)

```fsharp
open Frank.Builder

let isDevelopment (app: IApplicationBuilder) =
    app.ApplicationServices
        .GetService<IWebHostEnvironment>()
        .IsDevelopment()

webHost args {
    plugBeforeRoutingWhen isDevelopment StaticFileExtensions.UseStaticFiles

    resource api
}
```

### Multiple Conditional Middleware

```fsharp
open Frank.Builder

webHost args {
    // Only redirect to HTTPS in production
    plugBeforeRoutingWhenNot isDevelopment HttpsPolicyBuilderExtensions.UseHttpsRedirection

    // Only serve static files locally in development (CDN in production)
    plugBeforeRoutingWhen isDevelopment StaticFileExtensions.UseStaticFiles

    resource api
}
```

## API Reference

### plugBeforeRoutingWhen

```fsharp
plugBeforeRoutingWhen condition middleware
```

**Parameters**:
- `condition`: `IApplicationBuilder -> bool` - Function that returns true when middleware should be applied
- `middleware`: `IApplicationBuilder -> IApplicationBuilder` - The middleware to conditionally apply

**Behavior**: Applies `middleware` to the before-routing pipeline only when `condition` returns `true`.

### plugBeforeRoutingWhenNot

```fsharp
plugBeforeRoutingWhenNot condition middleware
```

**Parameters**:
- `condition`: `IApplicationBuilder -> bool` - Function that returns true when middleware should be skipped
- `middleware`: `IApplicationBuilder -> IApplicationBuilder` - The middleware to conditionally apply

**Behavior**: Applies `middleware` to the before-routing pipeline only when `condition` returns `false`.

## Pipeline Position

```
Request → plugBeforeRoutingWhen → plugBeforeRouting → UseRouting → plug/plugWhen → Endpoints → Response
```

The `plugBeforeRoutingWhen` operations execute in the same pipeline position as `plugBeforeRouting`, just with conditional application.

## Comparison with Existing Operations

| Operation | Pipeline Position | Conditional |
|-----------|-------------------|-------------|
| `plug` | After routing | No |
| `plugWhen` | After routing | Yes |
| `plugWhenNot` | After routing | Yes |
| `plugBeforeRouting` | Before routing | No |
| `plugBeforeRoutingWhen` | Before routing | Yes |
| `plugBeforeRoutingWhenNot` | Before routing | Yes |
