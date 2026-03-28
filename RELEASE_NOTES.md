### New in 7.3.1

Patch release: state-aware OPTIONS, role-filtered Link rels, and ALPS profile completeness.

**Discovery and Entry Points**

- **Entry-point designation** for JSON Home — `entryPoint` CE operation on both `ResourceBuilder` and `StatefulResourceBuilder` filters the home document to designated resources only (fallback to all with logged warning)
- **ALPS extension IDs** use HTTPS dereferenceable URIs (`https://frank-fs.github.io/alps-ext/{name}`) with backward-compatible parsing of bare names
- **`protocolState` ext element** emitted in generated ALPS profiles alongside `availableInStates`

**Validation and Conformance**

- **Conformance sequence validation** — `checkSequenceConformance` verifies transition ordering from initial state, not just per-transition role checks
- **Guard consistency check** — new `ProjectionCheckKind.GuardConsistency` compares ALPS guard annotations against SCXML guard definitions
- **SHACL shape cross-reference** — new `ProjectionCheckKind.ShapeReference` validates ALPS `def` URIs against `ShapeCache` entries
- **CLI filter fix** — `--check-projection` now includes guard-only statecharts (previously required roles)

**TicTacToe Sample**

- **Auto-load affordances** — replaced `useAffordancesWith` with `useAffordances` (loads from embedded `model.bin`)
- **Projected profile serving** — added `useProjectedProfiles` for role-specific ALPS profile Link headers
- **Entry-point designated** — games resource marked with `entryPoint`

**Code Quality**

- Removed 8 dead `StatechartError` DU cases from abandoned assembly extraction feature
- Added orphaned `Wsd/LexerTests.fs` to fsproj (453 lines of tests that were never compiled)
- Deleted 5 stale test file copies left from restructure

### New in 7.3.0

Self-describing, protocol-aware applications — legible to both human developers and machine agents, with formal multi-party session guarantees enforced at the HTTP boundary.

**Multi-Party Session Protocol Enforcement**

- **Role definitions** on stateful resources with typed guard projections
- **Projection operator** derives per-role ALPS profiles from the global statechart
- **Projected profile middleware** serves role-aware ALPS `Link` headers at zero per-request cost
- **Progress analysis** detects deadlock and starvation at build time
- **Projection consistency validator** with 4 MPST checks (safety, completeness, role independence, liveness)
- **Post-hoc session conformance checking** via `Frank.Provenance`
- **Role-aware SHACL shape references** for projected content negotiation and validation
- **`frank project` command** generates per-role ALPS profiles with `--base-uri` support

**Bidirectional Spec Pipeline**

- **Shared statechart AST** (`Frank.Statecharts.Core`) — unified type model for all format parsers
- **Cross-format validator** with Jaro-Winkler near-match detection across WSD, ALPS, SCXML, smcat, XState
- **Typed ALPS extension vocabulary** for role, guard, duality, and classification metadata
- **End-to-end extraction pipeline** with `loadOrExtract` caching and pure/impure separation
- **`frank extract`**, **`compile`**, **`validate`**, **`generate`**, **`diff`** CLI commands

**Discovery and JSON Home**

- **JSON Home document generation** at `GET /` via strict content negotiation
- **`useDiscovery`** as pit-of-success default — bundles OPTIONS, Link headers, and JSON Home
- **`useAffordances`** auto-loads pre-computed affordance map from embedded assembly resource
- **Combinatorial integration tests** for discovery middleware composition

**Frank.LinkedData — Semantic RDF Content Negotiation**

- **New library** for automatic RDF content negotiation (JSON-LD, Turtle, RDF/XML)
- **OWL ontology integration** projects JSON responses to RDF graphs using ontology-derived predicate URIs
- **`useLinkedData`** / **`useLinkedDataWith`** WebHostBuilder extensions

**Frank.Cli.MSBuild — Build-Time Artifact Embedding**

- **Content-only NuGet package** auto-embeds compiled semantic artifacts and affordance maps as assembly resources
- Works automatically via `buildTransitive/` targets

**Additional Improvements**

- **`IStatechartFeature`** typed feature replaces `HttpContext.Items` string conventions
- **Auto-infer `ResourceSpec.Name`** from route template
- **`frank` CLI** renamed from `frank-cli`; LLM-ready hierarchical help system
- **FRANK001 analyzer** extended to cover `datastar` operations
- **Constitution VII compliance** — all bare `with _ ->` catches replaced with logged helpers
- **Type design improvements** — `FoundStatefulResource` record carrier, `ArtifactKind` DU, `RoleProjectionResult` naming

### New in 7.2.0 (Released 2026-02-10)

**Frank.OpenApi - Native OpenAPI Document Generation Support**

- **New Library:** Frank.OpenApi extension library for declarative OpenAPI metadata
- **HandlerBuilder CE:** Computation expression for defining handlers with embedded OpenAPI metadata:
  - `name` — operationId for the endpoint
  - `summary` / `description` — operation documentation
  - `tags` — endpoint categorization
  - `produces typeof<T> statusCode [contentTypes]` — response types with optional content negotiation
  - `producesEmpty statusCode` — empty responses (204, 404, etc.)
  - `accepts typeof<T> [contentTypes]` — request types with optional content negotiation
  - `handle` — supports Task, Task<'a>, Async<unit>, Async<'a>
- **ResourceBuilder Extensions:** All HTTP method operations (`get`, `post`, `put`, `delete`, `patch`, `head`, `options`) accept HandlerDefinition
- **F# Type Schemas:** Automatic JSON Schema generation for F# types via FSharpSchemaTransformer:
  - Records with required and optional fields
  - Discriminated unions with anyOf/oneOf
  - Collections (list, Set, Map)
  - Option types as nullable
- **WebHostBuilder Integration:** `useOpenApi` operation to enable OpenAPI document generation at `/openapi/v1.json`
- **Content Negotiation:** Full support for multiple content types (application/json, application/xml, etc.)
- **No Breaking Changes:** Per-handler metadata via method-specific conventions — fully backward compatible
- **Multi-Targeting:** Supports .NET 10.0 (LTS)
- **Core Fix:** Added MethodInfo to endpoint metadata for OpenAPI discovery (required by ASP.NET Core's EndpointMetadataApiDescriptionProvider)

**Example Usage:**
```fsharp
handler {
    name "createProduct"
    summary "Create a new product"
    tags [ "Products"; "Admin" ]
    produces typeof<Product> 201
    accepts typeof<CreateProductRequest>
    handle (fun ctx -> async { return! createProduct ctx })
}
```

### New in 7.1.0 (Released 2026-02-07)

**Frank.Datastar - Native SSE Implementation & Stream-Based HTML Generation**

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
- **Stream-Based Overloads:** Added stream-based SSE operations for zero-allocation HTML rendering:
  - All SSE operations now have stream-based overloads accepting `TextWriter -> Task` writer functions
  - `streamPatchElements`, `streamPatchSignals`, `streamRemoveElement`, `streamExecuteScript` module functions
  - Eliminates full HTML string materialization — 50%+ allocation reduction in high-throughput scenarios (1000+ events/sec)
  - Compatible with view engines supporting `TextWriter` output (e.g., Hox `Render.toTextWriter`)
  - String-based API remains unchanged for backward compatibility
  - Internal `SseDataLineWriter` handles SSE line-splitting transparently

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
