# frank Development Guidelines

Auto-generated from all feature plans. Last updated: 2026-01-25

## Active Technologies
- F# 8.0+ targeting .NET 8.0/9.0/10.0 (multi-targeting) + ASP.NET Core (Microsoft.AspNetCore.*) (011-middleware-before-endpoints)
- F# 8.0+ targeting .NET 8.0/9.0/10.0 (multi-targeting, matching Frank core) + Frank 6.5.0+ (core, modified), Microsoft.AspNetCore.Authorization (framework reference) (013-frank-auth)
- N/A (no persistence — metadata is compile-time/startup-time configuration) (013-frank-auth)
- F# 8.0+ targeting .NET 10.0 (single target, down from multi-target) + Frank 7.0.0 (project reference), Microsoft.AspNetCore.App (framework reference), Microsoft.Extensions.Primitives (for StringTokenizer, included in framework) (014-datastar-native-sse)
- N/A (no persistence) (014-datastar-native-sse)
- F# 8.0+ targeting .NET 8.0, 9.0, and 10.0 (multi-targeting: `net8.0;net9.0;net10.0`) (014-datastar-native-sse)
- N/A (stateless SSE event streaming) (014-datastar-native-sse)
- F# 8.0+ targeting .NET 8.0, 9.0, and 10.0 + ASP.NET Core (HttpResponse, IBufferWriter, PipeWriter), System.IO (TextWriter), System.Buffers (ArrayPool) (015-datastar-streaming-html)
- F# 8.0+ targeting .NET 9.0 and .NET 10.0 (multi-targeting) + Frank 7.1.0 (project reference), FSharp.Data.JsonSchema.OpenApi 3.0.0 (NuGet), Microsoft.AspNetCore.OpenApi (9.0.x / 10.0.x conditional), Microsoft.AspNetCore.App (framework reference) (016-openapi)

- F# 8.0+ targeting .NET 8.0/9.0/10.0 (multi-targeting) + Frank 6.x, StarFederation.Datastar.FSharp (latest), ASP.NET Core (002-datastar-sample)
- In-memory (Dictionary, ResizeArray) - demo purposes only (002-datastar-sample)
- F# 8.0+ targeting .NET 10.0 + Frank 6.x, Hox 3.x, StarFederation.Datastar.FSharp (via Frank.Datastar project reference) (003-datastar-hox-sample)
- F# 8.0+ targeting .NET 10.0 + Frank 6.x, Oxpecker.ViewEngine 2.x, StarFederation.Datastar.FSharp (via Frank.Datastar project reference) (004-datastar-oxpecker-sample)
- Bash (POSIX-compatible shell scripting) + curl (HTTP client), grep/sed (text parsing), standard Unix tools (005-fix-sample-tests)
- N/A (tests read from sample applications' in-memory stores) (005-fix-sample-tests)
- F# 8.0+ targeting .NET 10.0 (matching sample projects) + Microsoft.Playwright.NUnit (1.57.0+), NUnit (3.x/4.x) (005-fix-sample-tests)
- F# 8.0+ targeting .NET 10.0 + Frank 6.x, StarFederation.Datastar.FSharp (via Frank.Datastar project reference), ASP.NET Core (006-fix-datastar-basic-tests)
- F# 8.0+ targeting .NET 8.0/9.0/10.0 (multi-targeting) + Frank 6.x, StarFederation.Datastar.FSharp, ASP.NET Core (010-datastar-patch-mode)
- F# 8.0+ targeting .NET 8.0/9.0/10.0 (multi-targeting, matching Frank) + FSharp.Analyzers.SDK (0.35.0+), FSharp.Compiler.Service (bundled with SDK) (009-resourcebuilder-handler-guardrails)
- N/A (static analysis tool, no persistence) (009-resourcebuilder-handler-guardrails)

- F# 8.0+ targeting .NET 8.0/9.0/10.0 (multi-targeting) + Frank 6.x, StarFederation.Datastar.FSharp (latest) (001-datastar-support)

- F# 8.0+ targeting .NET 8.0/9.0/10.0 (multi-targeting, matching Frank core) + Frank 6.x (project reference), Microsoft.AspNetCore.App (framework reference), Microsoft.Data.Sqlite (for SQLite project only), FSharp.Reflection (in FSharp.Core) (010-statecharts-production-readiness)
- SQLite via Microsoft.Data.Sqlite (new `Frank.Statecharts.Sqlite` project); in-memory MailboxProcessor (existing, unchanged) (010-statecharts-production-readiness)
## Project Structure

```text
src/
tests/
```

## Commands

# Add commands for F# 8.0+ targeting .NET 8.0/9.0/10.0 (multi-targeting)

## Code Style

F# 8.0+ targeting .NET 8.0/9.0/10.0 (multi-targeting): Follow standard conventions

## Recent Changes
- 010-statecharts-production-readiness: Added F# 8.0+ targeting .NET 8.0/9.0/10.0 (multi-targeting, matching Frank core) + Frank 6.x (project reference), Microsoft.AspNetCore.App (framework reference), Microsoft.Data.Sqlite (for SQLite project only), FSharp.Reflection (in FSharp.Core)
- 016-openapi: Added F# 8.0+ targeting .NET 9.0 and .NET 10.0 (multi-targeting) + Frank 7.1.0 (project reference), FSharp.Data.JsonSchema.OpenApi 3.0.0 (NuGet), Microsoft.AspNetCore.OpenApi (9.0.x / 10.0.x conditional), Microsoft.AspNetCore.App (framework reference)
- 015-datastar-streaming-html: Added F# 8.0+ targeting .NET 8.0, 9.0, and 10.0 + ASP.NET Core (HttpResponse, IBufferWriter, PipeWriter), System.IO (TextWriter), System.Buffers (ArrayPool)
<!-- MANUAL ADDITIONS START -->
<!-- MANUAL ADDITIONS END -->
