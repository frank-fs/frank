# Quickstart: Frank.Analyzers

**Date**: 2026-02-03
**Feature**: 009-resourcebuilder-handler-guardrails

## What It Does

`Frank.Analyzers` detects common mistakes in Frank resource definitions at compile-time:

- **FRANK001**: Duplicate HTTP method handlers in a single resource

## Installation

### 1. Add the Analyzer Package

```bash
dotnet add package Frank.Analyzers
```

Or add to your `.fsproj`:

```xml
<PackageReference Include="Frank.Analyzers" Version="1.*" />
```

### 2. IDE Support

**VS Code (Ionide)**: Analyzers work automatically once the package is referenced.

**Visual Studio**: Analyzers work automatically once the package is referenced.

**JetBrains Rider**: Analyzers work automatically once the package is referenced.

### 3. CI/CD Support

Install the `fsharp-analyzers` CLI tool:

```bash
dotnet tool install fsharp-analyzers
```

Run analyzers in your build script:

```bash
dotnet fsharp-analyzers --project MyProject.fsproj --analyzers-path ~/.nuget/packages/frank.analyzers/1.0.0/lib/net8.0
```

## Example

### Problem Code

```fsharp
open Frank.Builder

let contactApi =
    resource "/contacts/{id}" {
        name "Contact"
        get (fun ctx -> task {
            // Return contact
            return! ctx.Response.WriteAsJsonAsync({ Id = 1; Name = "John" })
        })
        get (fun ctx -> task {  // FRANK001: Duplicate GET handler
            // This overwrites the first handler!
            return! ctx.Response.WriteAsJsonAsync({ Id = 1; Name = "Jane" })
        })
    }
```

### Warning Message

```
FRANK001: HTTP method 'GET' handler is already defined for this resource at line 6. Only one handler per HTTP method is allowed.
```

### Fixed Code

```fsharp
open Frank.Builder

let contactApi =
    resource "/contacts/{id}" {
        name "Contact"
        get (fun ctx -> task {
            return! ctx.Response.WriteAsJsonAsync({ Id = 1; Name = "John" })
        })
        // Removed duplicate GET handler
    }
```

## Covered Methods

The analyzer detects duplicates for all 9 HTTP methods:

| Method | Operation |
|--------|-----------|
| GET | `get` |
| POST | `post` |
| PUT | `put` |
| DELETE | `delete` |
| PATCH | `patch` |
| HEAD | `head` |
| OPTIONS | `options` |
| CONNECT | `connect` |
| TRACE | `trace` |

## Datastar Integration

If you use `Frank.Datastar`, the analyzer also detects conflicts between `datastar` and explicit HTTP method handlers:

```fsharp
resource "/events" {
    datastar (fun ctx -> task {  // Registers GET by default
        // SSE handler
    })
    get (fun ctx -> task {  // FRANK001: Duplicate GET handler
        // This conflicts with datastar!
    })
}
```

## Configuration

### Treat as Error

To fail compilation on duplicate handlers, add to your `.fsproj`:

```xml
<PropertyGroup>
    <WarningsAsErrors>FRANK001</WarningsAsErrors>
</PropertyGroup>
```

### Suppress Warning

To suppress the warning for a specific case (not recommended):

```fsharp
#nowarn "FRANK001"
```

## Troubleshooting

### Analyzer Not Running

1. Ensure the package is referenced correctly
2. Rebuild the project
3. Check IDE analyzer settings are enabled

### CI/CD Not Detecting Issues

1. Ensure `fsharp-analyzers` tool is installed
2. Verify `--analyzers-path` points to the correct location
3. Check the tool output for errors

## More Information

- [Frank Documentation](https://github.com/frank-fs/frank)
- [GitHub Issue #59](https://github.com/frank-fs/frank/issues/59) - Original feature request
- [FSharp.Analyzers.SDK](https://ionide.io/FSharp.Analyzers.SDK/) - Analyzer framework
