---
source: specs/011-middleware-before-endpoints
type: plan
---

# Implementation Plan: Middleware Before Endpoints

**Branch**: `011-middleware-before-endpoints` | **Date**: 2026-02-04 | **Spec**: [spec.md](spec.md)
**Input**: Feature specification from `/specs/011-middleware-before-endpoints/spec.md`

## Summary

Fix the middleware ordering bug in `WebHostBuilder.Run` method and add `plugBeforeRouting` operation for pre-routing middleware. The correct ASP.NET Core pipeline order is: plugBeforeRouting → `UseRouting()` → plug → `UseEndpoints()`.

**Changes:**
1. Fix `plug` to apply middleware between `UseRouting()` and `UseEndpoints()`
2. Add `BeforeRoutingMiddleware` field to `WebHostSpec` record
3. Add `plugBeforeRouting` custom operation for pre-routing middleware (HttpsRedirection, StaticFiles, compression)
4. Update README documentation
5. Increment minor version (reset patch to 0)

## Technical Context

**Language/Version**: F# 8.0+ targeting .NET 8.0/9.0/10.0 (multi-targeting)
**Primary Dependencies**: ASP.NET Core (Microsoft.AspNetCore.*)
**Storage**: N/A
**Testing**: Sample applications serve as integration tests (per Development Workflow in constitution)
**Target Platform**: Cross-platform (.NET)
**Project Type**: Library
**Performance Goals**: Performance parity with Giraffe, Falco, and raw ASP.NET Core (per Constitution Principle V)
**Constraints**: Additive API change; existing `plug` behavior changes to correct position
**Scale/Scope**: Single file modification + README + version bump

## Constitution Check

*GATE: Must pass before Phase 0 research. Re-check after Phase 1 design.*

| Principle | Status | Notes |
|-----------|--------|-------|
| I. Resource-Oriented Design | ✅ PASS | No impact on resource API; middleware is infrastructure |
| II. Idiomatic F# | ✅ PASS | Using F# pipeline operator and computation expression patterns |
| III. Library, Not Framework | ✅ PASS | Still using standard ASP.NET Core middleware; no opinions added |
| IV. ASP.NET Core Native | ✅ PASS | Fix aligns with ASP.NET Core's documented middleware pipeline requirements |
| V. Performance Parity | ✅ PASS | No performance impact; enables optimal middleware positioning |

**Technical Standards Check**:
- Target Framework: ✅ .NET 8.0+ multi-targeting
- F# Version: ✅ F# 8.0+
- Dependencies: ✅ No new dependencies
- Testing: ✅ Sample apps will validate the fix

**All gates pass. Proceeding with implementation.**

## Project Structure

### Documentation (this feature)

```text
specs/011-middleware-before-endpoints/
├── spec.md              # Feature specification
├── plan.md              # This file
├── research.md          # Phase 0 output
├── checklists/
│   └── requirements.md  # Specification quality checklist
└── tasks.md             # Phase 2 output (/speckit.tasks command)
```

### Source Code (repository root)

```text
src/
├── Directory.Build.props    # Version to increment
├── Frank/
│   └── Builder.fs           # File to modify
├── Frank.Datastar/
└── Frank.Analyzers/

README.md                    # Documentation to update

sample/                      # Integration tests via sample applications
├── Sample/                  # Uses plug (ResponseCaching, Compression, StaticFiles)
├── Frank.Falco/             # Uses plug (HttpsRedirection, StaticFiles)
├── Frank.Giraffe/           # Uses plug (HttpsRedirection, StaticFiles)
├── Frank.Oxpecker/          # Uses plug (HttpsRedirection)
├── Frank.Datastar.Basic/    # Uses plug (DefaultFiles, StaticFiles)
├── Frank.Datastar.Hox/      # Uses plug (DefaultFiles, StaticFiles)
├── Frank.Datastar.Oxpecker/ # Uses plug (DefaultFiles, StaticFiles)
└── Frank.Datastar.Tests/    # Playwright tests for Datastar samples
```

**Structure Decision**: Existing library structure. Files to modify:
- `src/Frank/Builder.fs` - Core implementation
- `src/Directory.Build.props` - Version increment
- `README.md` - Documentation

## Complexity Tracking

> No violations to justify. This is a targeted enhancement.

## Implementation Details

### WebHostSpec Changes

```fsharp
// Add BeforeRoutingMiddleware field
type WebHostSpec =
    { Host: (IWebHostBuilder -> IWebHostBuilder)
      BeforeRoutingMiddleware: (IApplicationBuilder -> IApplicationBuilder)  // NEW
      Middleware: (IApplicationBuilder -> IApplicationBuilder)
      Endpoints: Endpoint[]
      Services: (IServiceCollection -> IServiceCollection)
      UseDefaults: bool }
    static member Empty =
        { Host=id
          BeforeRoutingMiddleware=id  // NEW: default to identity
          Middleware=id
          Endpoints=[||]
          Services=(fun services ->
            services.AddMvcCore(fun options -> options.ReturnHttpNotAcceptable <- true) |> ignore
            services)
          UseDefaults=false }
```

### WebHostBuilder.Run Changes

```fsharp
// Current (bug):
.Configure(fun app ->
    app.UseRouting()
       .UseEndpoints(fun endpoints ->
           let dataSource = ResourceEndpointDataSource(spec.Endpoints)
           endpoints.DataSources.Add(dataSource))
    |> spec.Middleware
    |> ignore)

// Fixed with plugBeforeRouting support:
.Configure(fun app ->
    app
    |> spec.BeforeRoutingMiddleware  // NEW: Pre-routing middleware
    |> fun app -> app.UseRouting()
    |> spec.Middleware               // FIX: Post-routing, pre-endpoint middleware
    |> fun app ->
        app.UseEndpoints(fun endpoints ->
            let dataSource = ResourceEndpointDataSource(spec.Endpoints)
            endpoints.DataSources.Add(dataSource))
    |> ignore)
```

### New plugBeforeRouting Operation

```fsharp
[<CustomOperation("plugBeforeRouting")>]
member __.PlugBeforeRouting(spec, f) =
    { spec with BeforeRoutingMiddleware = spec.BeforeRoutingMiddleware >> f }
```

### Pipeline Visualization

```
Request
   │
   ▼
┌─────────────────────────────────────┐
│  plugBeforeRouting middleware       │  ← HttpsRedirection, StaticFiles,
│  (spec.BeforeRoutingMiddleware)     │    ResponseCompression, etc.
└─────────────────────────────────────┘
   │
   ▼
┌─────────────────────────────────────┐
│  UseRouting()                       │  ← Route matching
└─────────────────────────────────────┘
   │
   ▼
┌─────────────────────────────────────┐
│  plug middleware                    │  ← Authentication, Authorization,
│  (spec.Middleware)                  │    CORS, custom middleware
└─────────────────────────────────────┘
   │
   ▼
┌─────────────────────────────────────┐
│  UseEndpoints()                     │  ← Execute matched endpoint
└─────────────────────────────────────┘
   │
   ▼
Response
```

## Verification Plan

1. Build all projects: `dotnet build`
2. Run sample applications manually to verify middleware works
3. Run Playwright tests for Datastar samples: `dotnet test sample/Frank.Datastar.Tests/`

## Risk Assessment

| Risk | Likelihood | Impact | Mitigation |
|------|------------|--------|------------|
| Breaking existing apps | Low | Medium | Existing `plug` usage moves to correct position |
| API addition confusion | Low | Low | Clear documentation distinguishes plugBeforeRouting vs plug |
| Performance regression | Very Low | Low | Same operations, correct order |

## Dependencies

None. This is a self-contained enhancement with no external dependencies.
