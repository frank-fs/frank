### New in 7.3.2 (Released 2026-07-21)

**Seven New Packages — Semantic Discovery for Frank Applications**

v7.3.2 introduces a full semantic-discovery pipeline: declare a vocabulary once, let a CLI score your domain types against it and record reviewed decisions in a lock file, and let a build-time MSBuild target regenerate the modules that drive discovery, content negotiation, validation, and provenance at runtime. Every JSON-LD term, ALPS descriptor, SHACL shape, and PROV-O class served at runtime traces back to that reviewed lock file — nothing is hand-authored or guessed at request time. All seven packages target **net10.0 only** (per [#401](https://github.com/frank-fs/frank/issues/401)), except `Frank.Semantic.Core`, which is deliberately kept multi-target — see the README's Packages table for why. See the README's Packages/Installation section for current publish status.

**Frank.Semantic & Frank.Semantic.Core — Vocabulary Declaration and Convention Matching**

- `vocabulary { }` computation expression and `VocabularyRegistry` types for declaring prefixes, `using`, `seeAlso`, `equivalentClass`, `provClass`, and pattern constraints (#314).
- Vocabulary fetching and caching with hash-drift detection, so a vocabulary that changes upstream is caught rather than silently trusted (#315).
- Jaro-Winkler convention-matching engine that scores F# record/DU fields against vocabulary terms and proposes mappings (#316).
- Lock-file I/O and schema versioning (`.frank/semantic-mappings.lock.json`) in the new `Frank.Semantic.Core` package, plus self-contained vocabulary provenance and an integrity checksum recorded in the lock file itself (#317, #370).
- Progressive enhancement: an `excluded` status tier, a `finalize` step, and a convention-confidence baseline tier so authors can defer or explicitly opt a type out of vocabulary matching (#372).
- A design-time warning fires when a referenced vocabulary is unpublished or undereferenceable (#377); Frank.Analyzers gained a companion compile-time analyzer warning for the same condition (#378).
- Non-owned vocabulary prefixes (e.g. a Wikidata `seeAlso` target) are emitted as relative IRIs only when the app actually owns that prefix — external prefixes stay absolute (#396).
- `ResolvedModel` is now built once per build rather than once per codegen task (#386); generated `iri`/`clrType` accessors are members rather than bare module-level functions, avoiding an `open` naming clash (#387).
- Outbound linked data — `owl:equivalentClass`/`rdfs:seeAlso` links to external vocabularies including Wikidata — confirmed end-to-end: the CLI makes no runtime LLM calls (`frank semantic clarify` emits a schema-versioned contract for offline resolution; `accept` merges the result back in), and the TicTacToe capstone lock file carries 16 confirmed mappings, 15 to schema.org (#137).

**Frank.Cli & Frank.Cli.Core — Semantic Discovery Tooling**

- FCS-based extraction of `TypeInfo` records from an F# project, with no runtime reflection (#318).
- `frank semantic extract` — the end-to-end pipeline: typecheck the project, discover every type referenced from `vocabulary { }`/`resource { }`, score it against the declared vocabularies, and write the lock file (#319).
- `frank semantic clarify` — a schema-versioned JSON contract for LLM-assisted resolution of proposed/unresolved mappings (#320).
- `frank semantic accept`, `refresh`, and `status` (including a `--by-package` breakdown) to merge resolutions, re-score changed types, and report lock-file health (#321, #371).
- The MSBuild target (`Frank.Cli.MSBuild`) reads the lock file, gates the build on unresolved mappings, and conditionally regenerates the four generated modules described below (#322).
- `Frank.Cli.MSBuild.Tests`' subprocess `dotnet build` calls are now bounded, removing a source of solution-wide test timeout flakes (#402).

**Generated modules consumed by the four runtime packages**

- Four MSBuild-generated F# modules, each derived from the reviewed lock file: `GeneratedLinkedData.fs` (JSON-LD `@context` + `IGraph` triples, #323), `GeneratedValidation.fs` (a SHACL `ShapesGraph` built from vocabulary IRIs, #324), `GeneratedProvenance.fs` (PROV-O class mappings, #325), and `GeneratedDiscovery.fs` (ALPS descriptors + Link headers, #326).

**Frank.Discovery — JSON Home, ALPS, OPTIONS/Allow, Link rel=describedby**

- Fresh package consuming `GeneratedDiscovery`: serves JSON Home directories, ALPS+JSON profiles, `OPTIONS`/`Allow`, and `Link rel=describedby` headers (#327).
- ALPS action-descriptor `Type`/`Rt` is reconciled against the resource's real registered HTTP method rather than trusted blindly from codegen (#397); OPTIONS/ALPS serving-time correctness — origin-relative `href` resolution, `rel=type` scoped to the matched resource, a correct `Allow` header (#398).
- HTTP-method correlation reads Frank's own composed `Endpoint[]` directly rather than depending on `Microsoft.AspNetCore.OpenApi`/ApiExplorer, which was evaluated (#400) and ultimately dropped (#411); ALPS `Type` is now declared directly via the `resource` CE's `relation` operation instead of being derived through three layers of indirection (#410).
- JSON Home's `href`/`href-vars` are now resolved against the live request origin, matching the fix ALPS already had (#416); a build-time assertion catches an unresolved JSON Home template variable before the app ever serves it (#379).
- Route templates are parsed once per endpoint at cache-build time instead of once per `OPTIONS` request (#421); a `DiscoveryEmitter` regression that served a full ALPS descriptor for a type with zero backing route — the phantom `MoveLog`/`ItemList` resource — is fixed (#418).
- Origin-keyed JSON Home/ALPS resolution caches are bounded against unbounded growth from client-controlled `Host` headers (#405).

**Frank.LinkedData — Content Negotiation for RDF Graphs**

- Fresh package consuming `GeneratedLinkedData`: serves JSON-LD, Turtle, and RDF/XML via content negotiation, with the served `@context` referencing external vocabularies (e.g. `https://schema.org`) rather than inlining every term (#329).
- The served `@context` is verified to actually expand against live schema.org, with the live-network check made deterministic rather than flaky (#394, #380).
- POSTing a resource's own served `@context` back to it is no longer rejected with a spurious 400 (#414); a duplicated `Vary: Accept` header — both LinkedData and Provenance were appending it — is deduplicated (#381); per-origin SHACL shapes graphs and static-graph JSON-LD compaction are memoized instead of rebuilt per request (#382, shared with Frank.Validation).
- Origin-keyed caches are bounded against the same Host-header cache-flood vector as Frank.Discovery (#405).

**Frank.Validation — SHACL Request Validation**

- Fresh package consuming `GeneratedValidation`: validates request bodies against a `ShapesGraph` built from the vocabulary lock, with the prior reflection-based validation removed entirely (#328).
- Request bodies are now stream-parsed instead of fully buffered before validation, with a BenchmarkDotNet suite added covering the 422-response path (#373).
- Per-origin SHACL shapes graphs are memoized instead of rebuilt per request, sharing the fix with Frank.LinkedData's JSON-LD compaction memoization (#382).

**Frank.Provenance — Request-Level W3C PROV-O**

- Fresh package consuming `GeneratedProvenance`: runs in a standalone mode (no dependency on LinkedData or Validation) and records per-request `prov:Activity`/`prov:Entity`/`prov:Agent` triples (#330).
- Provenance lineage: dereferenceable activity IRIs and a `prov:wasDerivedFrom` version chain, meeting the 5-star linked-data bar (#391).
- Constitution rule 7 (no silent exception swallowing) backlog closed out: the remaining bare `catch` in the provenance/CLI path was tightened to a specific exception type — `grep 'with _ ->' src` now returns zero (#374, #375).
- A duplicated `Vary: Accept` header, also present in Frank.LinkedData, is deduplicated here too (#381).

**Cross-package hardening and the TicTacToe-v732 capstone**

- Per-package HTTP integration tests for Validation, LinkedData, Provenance, and Discovery (#331), plus a composition test proving the same domain field resolves to the same IRI across all four runtime packages (#332).
- `sample/TicTacToe-v732` capstone: a naive HTTP client navigates the entire application via discovery alone — JSON Home, ALPS, and generated hypermedia — with no hardcoded routes (#333).
- Negative tests confirm the pipeline fails correctly on a swapped vocabulary, an unresolved-mapping build gate, and lock-file hash drift (#334).
- E2E tests assert against real parsed RDF graphs instead of string-matching serialized output (#388); the CLI's `status` output is confirmed the single source of truth on vocabulary dereferenceability, rather than duplicating that logic elsewhere (#389).
- Comprehensive `.fsi` signature files were added across all seven packages, with codegen/interop plumbing (serializers, generated-config resolvers) marked `internal` and exposed only via `InternalsVisibleTo` to the packages and test projects that genuinely need it (#392).
- A pre-release readiness-gate sweep (#405, #414, #416, #418, #420, #421) fixed a security cache-growth gap, two request-origin resolution bugs, the phantom-resource descriptor noted above, an unbounded route-template re-parse, and a structurally broken "naive client learns real-world identity via seeAlso" thesis that had been masked by a self-admitted vacuous E2E test.
- `docs/SEMANTIC-DISCOVERY-WALKTHROUGH.md`: a literally-run, pit-of-success walkthrough of the whole pipeline against the TicTacToe-v732 sample, with every command and every pasted output actually executed (#335).
- `Microsoft.OpenApi` bumped to 2.9.0, resolving a known-vulnerability (NU1903) build failure that had been blocking the full-solution build (#383).

### New in 7.2.1 (Released 2026-06-21)

**Frank.Datastar - Datastar v1 ADR Compliance**

- **`viewTransitionSelector` support:** `PatchElementsOptions.ViewTransition` is now a discriminated union (`NoViewTransition | ViewTransition of selector: string voption`). Set `ViewTransition(ValueSome "#my-el")` to emit both `useViewTransition true` and `viewTransitionSelector #my-el` in the SSE frame; `ViewTransition(ValueNone)` emits `useViewTransition true` without a selector. Aligned with the StarFederation.Datastar reference SDK.
- **DELETE signal routing:** `ReadSignalsAsync` and `ReadSignalsAsync<'T>` now read signals from the `datastar` query parameter for DELETE requests, matching the behaviour already in place for GET. Previously DELETE fell through to body parsing, returning empty signals.
- **`JsonException` surfacing:** `ReadSignalsAsync<'T>` now lets `JsonException` propagate instead of swallowing it. The `tryReadSignals` and `tryReadSignalsWithOptions` convenience wrappers catch `JsonException` and return `ValueNone`, logging a warning via `ILoggerFactory`. Call `ReadSignalsAsync<'T>` directly to handle parse errors yourself.
- **Thread-safety documentation:** `StartServerEventStreamAsync` now carries an XML doc note that `PipeWriter` is not thread-safe — writes to the same SSE stream must be serialised, which the `datastar` CE operation enforces implicitly via `task { }` linearisation.
- **Samples updated to Datastar JS v1.0.2:** All three Datastar sample apps (Basic, Hox, Oxpecker) now load the stable `v1.0.2` client script from the CDN (was `v1.0.0-RC.7`).

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
