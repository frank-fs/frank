### New in 7.1.0 (Released 2026-02-07)

**Frank.Datastar - Native SSE Implementation**

- **Performance:** Replaced StarFederation.Datastar.FSharp dependency with native SSE implementation using `IBufferWriter<byte>` for zero-copy buffer writing
- **Zero External Dependencies:** Frank.Datastar now has no external NuGet dependencies beyond framework references and Frank core
- **Multi-Targeting Restored:** Supports .NET 8.0, 9.0, and 10.0 (`net8.0;net9.0;net10.0`)
- **API Compatibility:** Zero breaking changes — seamless upgrade from 7.0.x with identical public API surface
- **Performance Optimizations:**
  - Pre-allocated byte arrays for SSE field prefixes (no runtime UTF-8 encoding)
  - Zero-allocation string segmentation via `StringTokenizer` for multi-line payloads
  - Direct buffer writing without intermediate copies
  - Per-event flushing for immediate delivery
- **ADR Compliance:** Full conformance to Datastar SDK ADR specification for SSE message format
- **Added:** `Attributes` field to `ExecuteScriptOptions` for custom script tag attributes (additive, non-breaking)
- **Public API:** `ServerSentEventGenerator` now public for advanced SSE event construction

### New in 7.0.0 (Released 2026-02-05)

- **Breaking:** Added `Metadata` field to `ResourceSpec` and `AddMetadata` to `ResourceBuilder` for composable endpoint metadata conventions
- Added `plugBeforeRoutingWhen` for conditional middleware before routing when condition is true
- Added `plugBeforeRoutingWhenNot` for conditional middleware before routing when condition is false
- Added **Frank.Auth** library for resource-level authorization:
  - `requireAuth` — require authenticated user
  - `requireClaim` — require a specific claim type and value(s)
  - `requireRole` — require a specific role
  - `requirePolicy` — require a named authorization policy
  - `useAuthentication` / `useAuthorization` — configure auth services and middleware on the web host
  - `authorizationPolicy` — define named authorization policies on the web host

### New in 6.5.0 (Released 2026-02-04)

- Fixed middleware pipeline ordering: `plug` middleware now runs after `UseRouting` and before `UseEndpoints`
- Added `plugBeforeRouting` for middleware that must run before routing (e.g., StaticFiles, HttpsRedirection)
- Added middleware ordering tests

### New in 6.4.1 (Released 2026-02-04)

- Add Frank.Analyzers to assist with validating resource definitions
- Added additional Frank.Datastar helpers to use more StarFederation.Datastar options

### New in 6.4.0 (Released 2026-02-02)

- Updated to target net8.0, net9.0, and net10.0
- Add Frank.Datastar
- Updated samples and added samples for Frank.Datastar

### New in 6.3.0 (Released 2025-03-14)

- Updated to target net8.0 and net9.0
- Updated examples

### New in 6.2.0 (Released 2020-11-18)

- Updated samples

### New in 6.1.0 (Released 2020-06-11)

- Encapsulate `IHostBuilder` and expose option to use web builder defaults with `useDefaults`.
- Server application can now be simply a standard console application. See [samples](https://github.com/frank-fs/frank/tree/master/sample).

### New in 6.0.0 (Released 2020-06-02)

- Update to .NET Core 3.1
- Use Endpoint Routing
- Pave the way for built-in generation of Open API spec

### New in 5.0.0 (Released 2019-01-05)

- Starting over based on ASP.NET Core Routing and Hosting
- New MIT license
- Computation expression for configuring IWebHostBuilder
- Computation expression for specifying HTTP resources
- Sample using simple ASP.NET Core web application
- Sample using standard Giraffe template web application

### New in 4.0.0 - (Released 2018/03/27)

- Update to .NETStandard 2.0 and .NET 4.6.1
- Now more easily used with Azure Functions or ASP.NET Core

### New in 3.1.1 - (Released 2014/12/07)

- Use FSharp.Core from NuGet

### New in 3.1.0 - (Released 2014/10/13)

- Remove dependency on F#x
- Signatures remain equivalent, but some type aliases have been removed.

### New in 3.0.19 - (Released 2014/10/13)

- Merge all implementations into one file and add .fsi signature

### New in 3.0.18 - (Released 2014/10/12)

- Use Paket for package management
- FSharp.Core 4.3.1.0
- NOTE: Jumped to 3.0.18 due to bad build script configuration

### New in 3.0.0 - (Released 2014/05/24)

- Updated dependencies to Web API 2.1 and .NET 4.5

### New in 2.0.3 - (Released 2014/02/07)

- Add SourceLink to link to GitHub sources (courtesy Cameron Taggart).

### New in 2.0.2 - (Released 2014/01/26)

- Remove FSharp.Core.3 as a package dependency.

### New in 2.0.0 - (Released 2014/01/07)

- Generate documentation with every release
- Fix a minor bug in routing (leading '/' was not stripped)
- Reference FSharp.Core.3 NuGet package
- Release assembly rather than current source packages:
- FSharp.Net.Http
- FSharp.Web.Http
- Frank
- Adopt the FSharp.ProjectScaffold structure

### New in 1.1.1 - (Released 2014/01/01)

- Correct spacing and specify additional types in HttpContent extensions.

### New in 1.1.0 - (Released 2014/01/01)

- Remove descriptor-based implementation.

### New in 1.0.2 - (Released 2013/12/10)

- Restore Frank dependency on FSharp.Web.Http. Otherwise, devs will have to create their own routing mechanisms. A better solution is on its way.

### New in 1.0.1 - (Released 2013/12/10)

- Change Web API dependency to Microsoft.AspNet.WebApi.Core.

### New in 1.0.0 - (Released 2013/12/10)

- First official release.
- Use an Option type for empty content.
