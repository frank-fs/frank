# Quickstart: Frank.Datastar Native SSE

## Prerequisites

- .NET 10 SDK
- Frank 7.0.0+ (project reference)

## Installation

Add a project reference to Frank.Datastar (no additional NuGet packages needed):

```xml
<ProjectReference Include="path/to/Frank.Datastar.fsproj" />
```

Or, once published:

```xml
<PackageReference Include="Frank.Datastar" Version="8.*" />
```

## Basic Usage

The API is identical to Frank.Datastar 7.x:

```fsharp
open Frank
open Frank.Builder
open Frank.Datastar

let app = webHost {
    resource "/updates" {
        name "Updates"
        datastar (fun ctx -> task {
            do! Datastar.patchElements "<div id='status'>Loading...</div>" ctx
            do! Task.Delay(500)
            do! Datastar.patchElements "<div id='status'>Complete!</div>" ctx
        })
    }
}
```

## Reading Signals

```fsharp
type FormData = { name: string; email: string }

resource "/submit" {
    name "Submit"
    datastar HttpMethods.Post (fun ctx -> task {
        match! Datastar.tryReadSignals<FormData> ctx with
        | ValueSome data ->
            do! Datastar.patchElements $"<div id='result'>Hello, {data.name}!</div>" ctx
        | ValueNone ->
            do! Datastar.patchElements "<div id='result'>Invalid input</div>" ctx
    })
}
```

## Using Options

```fsharp
// Patch with inner mode
let opts = { PatchElementsOptions.Defaults with PatchMode = ElementPatchMode.Inner }
do! Datastar.patchElementsWithOptions opts "<span>Updated content</span>" ctx

// Execute script with custom attributes
let scriptOpts = { ExecuteScriptOptions.Defaults with
                     Attributes = [| "type=\"module\"" |] }
do! Datastar.executeScriptWithOptions scriptOpts "console.log('hello')" ctx
```

## Advanced: Direct ServerSentEventGenerator Access

For advanced scenarios outside the `datastar` custom operation:

```fsharp
open Frank.Datastar

let handler (ctx: HttpContext) = task {
    do! ServerSentEventGenerator.StartServerEventStreamAsync(ctx.Response)
    do! ServerSentEventGenerator.PatchElementsAsync(ctx.Response, "<div id='x'>Hello</div>")
    do! ServerSentEventGenerator.PatchSignalsAsync(ctx.Response, """{"count": 1}""")
}
```

## What Changed from 7.x

- **No external dependencies**: StarFederation.Datastar.FSharp is no longer required
- **net10.0 only**: Consumers on .NET 8/9 should use Frank core + StarFederation.Datastar.FSharp directly
- **New `Attributes` field**: `ExecuteScriptOptions` now includes `Attributes: string[]` for script tag attributes
- **Public `ServerSentEventGenerator`**: Advanced users can access the SSE generator directly
- **Same API**: All existing `Datastar.*` module functions and `datastar` custom operation work unchanged
