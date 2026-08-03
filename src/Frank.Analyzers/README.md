# Frank.Analyzers

[![NuGet Version](https://img.shields.io/nuget/v/Frank.Analyzers)](https://www.nuget.org/packages/Frank.Analyzers/)

Compile-time static analysis to catch common mistakes in [Frank](https://www.nuget.org/packages/Frank/) applications.

## Installation

```bash
dotnet add package Frank.Analyzers
```

## Available Analyzers

### FRANK001: Duplicate HTTP Handler Detection

Detects when multiple handlers for the same HTTP method are defined on a single resource. Only the last handler would be used at runtime, so this is almost always a mistake.

```fsharp
// This will produce a warning:
resource "/example" {
    name "Example"
    get (fun ctx -> ctx.Response.WriteAsync("First"))   // Warning: FRANK001
    get (fun ctx -> ctx.Response.WriteAsync("Second"))  // This one takes effect
}
```

### FRANK002: Duplicate Accepts Media Type

Detects when the same media type is registered more than once inside a single `negotiate { }` block. Only the first registration can ever be selected — later ones for the same media type are unreachable — so this is almost always a mistake.

```fsharp
// This will produce a warning:
resource "/test" {
    get (negotiate {
        accepts "application/json" jsonHandler
        accepts "application/json" anotherJsonHandler  // Warning: FRANK002 (unreachable)
    })
}
```

## IDE Integration

Frank.Analyzers works with:
- **Ionide** (VS Code)
- **Visual Studio** with F# support
- **JetBrains Rider**

Warnings appear inline as you type, helping catch issues before you even compile. It runs via [FSharp.Analyzers.SDK](https://github.com/ionide/FSharp.Analyzers.SDK) — no changes to your build are required beyond adding the package reference.

## Related Packages

Analyzes code written against [`Frank`](https://www.nuget.org/packages/Frank/).

See the [project repository](https://github.com/frank-fs/frank) for the complete guide and sample applications.

## License

[MIT](https://github.com/frank-fs/frank/blob/master/LICENSE)
