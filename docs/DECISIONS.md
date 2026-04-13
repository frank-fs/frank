# Frank Decision Log

Complete catalog of all design decisions extracted from specs, issues, and PRs (~400 decisions).
See [AUDIT.md](AUDIT.md) for the contradictions, evolution timeline, suspect findings, and dropped designs.

---

## v7.4.0 Algebra Design Decisions (from DESIGN_DECISIONS.md)

17 decisions resolved during v7.4.0 issue refinement. These are the authoritative design — the code needs to match them.

- D-DD1: **LCA is a parameter, not an algebra operation.** ComputeLCA is a pure query on StateHierarchy, computed externally. The algebra is a pure effect algebra (Exit, Enter, Fork, Sequence). Source: [#286](https://github.com/frank-fs/frank/issues/286) §1a.
- D-DD2: **Explicit Fork in algebra programs.** The CE auto-generates programs with Fork from transition declarations. DualAlgebra needs to see Fork for per-region obligations. Source: [#286](https://github.com/frank-fs/frank/issues/286) §1b. **NOT IMPLEMENTED** — Fork is no-op in code.
- D-DD3: **'r varies per interpreter (tagless final).** Programs are polymorphic: `TransitionAlgebra<'r> -> 'r`. Each interpreter chooses its own 'r. Source: [#286](https://github.com/frank-fs/frank/issues/286) §1c.
- D-DD4: **ActiveStateConfiguration is opaque.** Programs receive it from RestoreHistory and pass it through; never construct or query directly. Source: [#286](https://github.com/frank-fs/frank/issues/286) §2.
- D-DD5: **DualAlgebra replaces deriveWithHierarchy entirely.** Dual derivation IS a DualAlgebra interpreter. No wrapping layer. Source: [#288](https://github.com/frank-fs/frank/issues/288) §3. **NOT IMPLEMENTED** — Dual.fs is 740 lines of pre-algebra code.
- D-DD6: **onTransition does not exist.** Every transition declaration auto-generates its algebra program. Customization through interpreters, not hooks. Source: [#282](https://github.com/frank-fs/frank/issues/282) §4. **NOT IMPLEMENTED** — onTransition hooks still in code.
- D-DD7: **Single generated file per statechart.** One `OrderStatechart.Generated.fs` with types + programs. Source: [#283](https://github.com/frank-fs/frank/issues/283) §5.
- D-DD8: **childOf uses value binding.** `childOf parentResource` where parentResource is the let binding. Compiler-checked. Source: [#293](https://github.com/frank-fs/frank/issues/293) §6.
- D-DD9: **Two-path validation (build-time + startup).** SCXML-first: FCS analyzer. CE-first: startup validation. Both use same ValidationAlgebra. Source: [#296](https://github.com/frank-fs/frank/issues/296) §7.
- D-DD10: **Algebra types in Frank.Statecharts.Core.** No separate Abstractions package. One zero-dep foundation. Source: [#286](https://github.com/frank-fs/frank/issues/286) §8. Implemented.
- D-DD11: **Instance ID uses :: separator.** URL-encoded parameter values with `::` join. Source: [#293](https://github.com/frank-fs/frank/issues/293) §9.
- D-DD12: **RFC 9457 Problem Details for error responses.** ProblemDetails via IProblemDetailsService with TryAddSingleton. Source: [#294](https://github.com/frank-fs/frank/issues/294) §10. Implemented.
- D-DD13: **frank-cli distributed via existing dotnet tool.** Tool manifest, MSBuild targets run dotnet tool restore. Source: [#284](https://github.com/frank-fs/frank/issues/284) §11. Implemented.
- D-DD14: **frank init three-layer approach.** dotnet new + frank extract + frank scaffold, each independently useful. Source: [#155](https://github.com/frank-fs/frank/issues/155) §12.
- D-DD15: **Generated module naming conflicts are errors.** Fail early, caller refines inputs. Source: [#283](https://github.com/frank-fs/frank/issues/283) §13.
- D-DD16: **ALPS validator is semantic only.** Parser ensures structure; validator ensures meaning. Source: [#302](https://github.com/frank-fs/frank/issues/302) §14.
- D-DD17: **CollectorAlgebra in Core, reconstruction in CLI.** Pure interpreter in Core, reconstruction pipeline in Cli.Core. Source: [#290](https://github.com/frank-fs/frank/issues/290) §15.

---

## Spec-Kit Decisions (v7.0-v7.2)

54 decisions across 16 specs (4 skipped as pure sample app implementations).

### 001: Datastar SSE Streaming Support

- D-SK1: **No one-off convenience operations.** The library MUST NOT provide single-response helpers. Recent Datastar versions support regular HTTP responses for single-update interactions, so developers should use standard Frank `resource` handlers. The extension focuses exclusively on SSE streaming (0-n responses). Alternative rejected: one-off helpers would blur the boundary between standard HTTP and SSE.
- D-SK2: **Any HTTP method allowed for SSE streams.** Datastar uses `@microsoft/fetch-event-source` (fetch API-based), not native EventSource, so any HTTP method is valid for establishing SSE connections. Not just GET.
- D-SK3: **Separate extension package, not Frank core.** Frank.Datastar remains a separate extension library adapting StarFederation.Datastar to Frank's computation expressions. It adds no features beyond this adaptation.
- D-SK4: **Single stream start per request.** The `datastar` custom operation manages SSE stream lifecycle automatically -- calling `StartServerEventStreamAsync` once, then invoking the user's handler. Multiple start calls would corrupt the stream.
- D-SK5: **Standalone functions alongside CE integration.** The `Datastar` module exposes functions (`patchElements`, `patchSignals`, etc.) that can be called outside of Frank's `resource` CE with a raw `HttpContext`, enabling composition with other patterns.
- D-SK6: **Use `webHost` CE pattern for hosting.** Rejected alternative: creating a `UseResource` extension for ASP.NET Core's minimal API `WebApplication` -- deviates from Frank's established patterns.

### 002: Datastar Sample Application

- D-SK7: **RESTful resource semantics over RPC-style.** All resource URLs use nouns (`/contacts/{id}`) rather than verbs (`/getContact`, `/updateContact`). Query parameters filter collections; sub-resource URLs change representation (`/contacts/{id}/edit`), not query params like `?mode=edit`.
- D-SK8: **SSE connection model: one long-lived connection per page.** Opening multiple SSE connections per page exhausts browser connection limits. Design for a single SSE connection per page that serves as the channel for all UI updates. Fire-and-forget mutations return status codes; UI updates flow through the existing SSE connection.
- D-SK9: **Edit form as sub-resource URL.** Click-to-edit uses `GET /contacts/{id}/edit` for the edit form -- a separate representation of the same resource. Rejected: query parameter `?mode=edit` (query params should filter collections, not change representation type); POST for editing (POST creates new resources; PUT updates existing).
- D-SK10: **Validation as separate resource.** Form validation uses `POST /registrations/validate` separate from creation at `POST /registrations`. Rejected: conflating validation and creation on the same endpoint with a `?validate=true` parameter.

### 003: Datastar Hox Sample

*Skipped -- purely a sample app implementation spec with no library-level architectural decisions. View engine translation patterns only.*

### 004: Datastar Oxpecker Sample

*Skipped -- purely a sample app implementation spec. View engine syntax patterns only.*

### 005: Browser Automation Test Suite

- D-SK11: **Browser automation over curl for SSE testing.** Bash scripts using curl cannot properly evaluate streaming updates through SSE channels while other interactions occur. Playwright browser automation maintains SSE connections and verifies UI updates arrive correctly. Alternative rejected: curl-based test scripts.
- D-SK12: **`WaitForFunctionAsync` for SSE DOM verification.** `WaitForLoadStateAsync(NetworkIdle)` hangs indefinitely with SSE (persistent connection). `WaitForFunctionAsync` with custom JavaScript predicates allows checking for specific content. Alternative rejected: fixed `Task.Delay` sleeps (flaky), `WaitForSelectorAsync` (works for element existence but not content changes).
- D-SK13: **Environment variable for sample selection.** `DATASTAR_SAMPLE` environment variable over `.runsettings` only, command-line filter expressions, or test constructor parameters. Works across all platforms and allows fail-fast validation.
- D-SK14: **NUnit over other test runners for Playwright.** `Microsoft.Playwright.NUnit` chosen. Rejected: MSTest (less idiomatic for F#), xUnit (lacks `TestContext.Parameters`), core Playwright (requires manual browser lifecycle management).

### 006: Fix Datastar Basic Tests

- D-SK15: **Single global SSE channel per page.** Root cause of 8 failing tests was subscription/broadcast timing vulnerability in the 5-channel-per-resource architecture. Consolidate to one global channel. Rationale: eliminates timing races, respects browser connection limits (~6 for HTTP/1.1), matches Datastar best practices, simplifies code. Rejected: fixing timing with locks/semaphores (adds complexity, doesn't fix connection limits), SignalR (overkill), multiple SSE endpoints with shared state (still has connection limit issue).

### 007: Update Oxpecker Sample

*Skipped -- purely a sample app update spec to match a validated reference implementation. No library-level decisions.*

### 008: Update Hox Sample

*Skipped -- purely a sample app update spec. Hox rendering patterns only.*

### 009: ResourceBuilder Handler Guardrails

- D-SK16: **Compile-time detection via F# Analyzer, not runtime validation.** Use FSharp.Analyzers.SDK for compile-time duplicate handler detection. Runtime behavior (last handler wins/overwrites) remains as acceptable fallback. Rejected: runtime validation, MSBuild task (less integrated IDE experience), Roslyn analyzers (F# uses a different AST).
- D-SK17: **Warning severity, not error.** Duplicate handler detection reports warnings. Developers can promote to errors via `<TreatWarningsAsErrors>`. Rationale: code still compiles; user chooses enforcement level.
- D-SK18: **Separate NuGet package (`Frank.Analyzers`).** Users opt-in explicitly. Core Frank package remains lightweight. Mirrors the extension model (Frank.Auth, Frank.Datastar).
- D-SK19: **Untyped AST traversal with `SyntaxCollectorBase`.** Faster than typed AST (no type-checking required). CE custom operations are identifiable by name in the untyped AST. Rejected: typed AST (`TypedTreeCollectorBase`) -- more complex, slower, unnecessary.
- D-SK20: **Name-based HTTP method detection.** Detect custom operations by identifier name matching known operation names (`get`, `post`, `put`, etc. plus `datastar`). Rejected: symbol-based detection (requires type information), attribute-based detection.
- D-SK21: **Dual analyzer registration (Editor + CLI).** Both `[<EditorAnalyzer>]` and `[<CliAnalyzer>]` attributes with shared core logic. Ensures analyzer works in IDE real-time and CI/CD pipeline scenarios.

### 010: Datastar WithOptions Variants

- D-SK22: **Simple helpers + WithOptions variants (Approach B).** Keep simple helpers for the common case (defaults), add single `WithOptions` variant per function taking the full options record. Uses `{ *.Defaults with ... }` syntax. Rejected: individual functions per option (combinatorial explosion / API bloat), options-only functions (verbose for common case), dropping helpers entirely (loses HttpContext convenience).
- D-SK23: **Consistent signature pattern.** All `WithOptions` functions follow: `(options) -> (data) -> (ctx: HttpContext) -> Task`. Existing simple helpers remain unchanged (backward compatible).

### 011: Middleware Before Endpoints

- D-SK24: **Two-stage middleware pipeline.** `plugBeforeRouting` for middleware before `UseRouting()` (HttpsRedirection, StaticFiles, compression), `plug` for middleware between `UseRouting()` and `UseEndpoints()` (Authentication, Authorization, CORS). Rejected: single `plug` after UseRouting only (doesn't support pre-routing middleware), single `plug` before UseRouting only (breaks endpoint-aware middleware like auth/CORS), automatic middleware sorting (over-engineered).
- D-SK25: **`BeforeRoutingMiddleware` field on WebHostSpec.** Default is `id` function. Mirrors existing `Middleware` field pattern. Follows ASP.NET Core's documented middleware pipeline ordering.

### 012: Conditional Before-Routing Middleware

- D-SK26: **Mirror existing `plugWhen`/`plugWhenNot` pattern exactly.** Same condition signature (`IApplicationBuilder -> bool`), same middleware signature (`IApplicationBuilder -> IApplicationBuilder`), same composition model. Rejected: different condition signature (API inconsistency), lazy evaluation (current eager evaluation at startup matches existing pattern), fluent API (violates Constitution Principle II -- Idiomatic F#).

### 013: Frank.Auth Resource-Level Authorization

- D-SK27: **Generic endpoint metadata extensibility via `(EndpointBuilder -> unit) list`.** Added `Metadata` field to `ResourceSpec` with convention functions, not raw `obj list`. `Build()` switched from constructing `RouteEndpoint` directly to using `RouteEndpointBuilder`. Rationale: keeps public API type-safe (the `obj` boundary is confined to individual convention function implementations), future-proof (any extension can provide typed helpers without modifying Frank core), matches ASP.NET Core's `IEndpointConventionBuilder.Add(Action<EndpointBuilder>)` pattern. Rejected: `obj list` (breaks F# type safety conventions), typed metadata DU (every new metadata type requires modifying the DU), wrapper builder (breaks compositional model), post-processing `Resource.Endpoints` (impossible -- `EndpointMetadataCollection` is immutable after construction).
- D-SK28: **Authorization metadata as ASP.NET Core platform objects.** Translates `AuthRequirement` values into `AuthorizeAttribute` and `AuthorizationPolicy` objects that the authorization middleware already recognizes. No custom `IAuthorizationRequirement` types. Rejected: only `AuthorizeAttribute` (can't express inline claim requirements), custom requirement types (over-engineers it -- built-in `AuthorizationPolicyBuilder` methods suffice).
- D-SK29: **Separate library (Frank.Auth) with ResourceBuilder type extensions.** Authorization operations mix freely with handler operations in the same `resource { }` CE. Rejected: wrapper builder like `authResource` (breaks compositional model).
- D-SK30: **AND semantics across requirements, OR within claim values.** Multiple `requireClaim` operations on the same resource require all to pass (AND). Within a single `requireClaim` with multiple values, possessing any one suffices (OR). Matches ASP.NET Core's authorization policy composition model.
- D-SK31: **WebHostBuilder extensions for auth service registration.** `useAuthentication`, `useAuthorization`, `authorizationPolicy` -- thin wrappers around ASP.NET Core DI calls. Auth middleware positioned in `plug` (post-routing) because it needs endpoint metadata available only after routing.

### 014: Frank.Datastar Native SSE Implementation

- D-SK32: **Replace external StarFederation.Datastar dependency with purpose-built implementation.** Zero external NuGet dependencies beyond framework reference and Frank core project reference. Rationale: eliminates transitive dependency chain, enables direct control over buffer writing for Datastar-specific optimizations.
- D-SK33: **Write directly to `IBufferWriter<byte>` via `HttpResponse.BodyWriter`.** Uses pre-allocated byte arrays and inline functions. Rejected: `SseFormatter.WriteAsync` with `IAsyncEnumerable<SseItem<T>>` (Datastar's custom multi-line `data:` format doesn't map to `SseFormatter`'s single-data-field model), `TextWriter`/`StreamWriter` (char-to-byte encoding overhead), .NET 10 `SseFormatter` with custom formatter callback (still single-data-field model).
- D-SK34: **Do NOT use .NET 10's `System.Net.ServerSentEvents.SseFormatter` for writing.** Datastar requires multiple structured `data:` lines per event with different prefixes. The formatter's single-blob-per-event model doesn't map cleanly. Custom implementation works across net8.0/net9.0/net10.0.
- D-SK35: **Option types as `[<Struct>]` F# records with `static member Defaults`.** Struct records minimize allocation. `{ SomeOptions.Defaults with Field = value }` is idiomatic F#. Rejected: class-based types (heap allocation), builder pattern (verbose), single large options type (couples unrelated concerns).
- D-SK36: **`ExecuteScriptOptions.Attributes` as `string[]` written verbatim.** Changed from `KeyValuePair<string, string> list` to `string[]` to match ADR's `[]string` semantics. Each string is a complete, pre-formed attribute. Caller responsible for safe content and formatting.
- D-SK37: **`ConcurrentQueue<unit -> Task>` for thread-safe event ordering.** Same pattern as StarFederation.Datastar.FSharp for the instance-based `ServerSentEventGenerator`. Rejected: `SemaphoreSlim` (more overhead), `Channel<T>` (more complex), no queue (violates ADR requirement for concurrent-safe public generator).
- D-SK38: **Multi-target net8.0/net9.0/net10.0 restored.** Initially net10.0-only was attempted, then corrected. Implementation uses only APIs available across all three targets. Version 7.1.0 (minor bump, no breaking API changes).

### 015: Datastar Streaming HTML Generation

- D-SK39: **`TextWriter -> Task` as the streaming API surface.** View engines (Hox, Oxpecker) already support or can naturally add `TextWriter` output. Rejected: direct `IBufferWriter<byte>` callback (incompatible with view engines), `Stream` callback (less structured, no line-oriented API), `PipeWriter` callback (ASP.NET Core-specific, too low-level).
- D-SK40: **Custom `SseDataLineWriter` (TextWriter subclass) that auto-emits SSE `data:` lines on newlines.** Wraps `IBufferWriter<byte>` internally. Hides SSE line-splitting complexity from callers. Bridges char-level TextWriter API to Frank.Datastar's existing byte-level write pipeline.
- D-SK41: **`ArrayPool<char>.Shared.Rent(256)` for internal line buffer.** Effectively zero-allocation after warmup. Rejected: `StringBuilder` (allocates non-poolable internal `char[]`), `stackalloc` (can't be used in async contexts), fixed `char[256]` (can't grow). Net result: ~48 bytes per event vs 500-2000 bytes for string materialization.
- D-SK42: **`stream` prefix for module functions, method overloading for type members.** F# modules don't support overloading, so `Datastar.streamPatchElements`. `ServerSentEventGenerator` supports overloads by parameter type (`string` vs `TextWriter -> Task`). Rejected: separate method names on the type (doubles API surface names without benefit).
- D-SK43: **Async-only writer signature.** `TextWriter -> Task` only. Sync callers return `Task.CompletedTask` (cached singleton, zero allocation). Halves the API surface while supporting both sync and async scenarios via TextWriter's own sync/async methods.
- D-SK44: **Reframe "zero-allocation" to "minimal-allocation."** The `TextWriter` line buffer (~256 bytes rented from ArrayPool) is a necessary tradeoff for SSE compliance. The real win is eliminating full HTML string materialization.
- D-SK45: **Error propagation, not recovery.** If the writer callback throws mid-stream, let the exception propagate. Partial `data:` line bytes may already be committed to `IBufferWriter<byte>`; attempting recovery would produce malformed SSE. Rejected: flush a blank line (risky with partial bytes), catch and swallow (hides errors).
- D-SK46: **View engine streaming is external and incremental.** Frank.Datastar provides the `TextWriter -> Task` callbacks. Hox/Oxpecker add streaming at their own pace. String-based APIs remain for engines without streaming support. Frank.Datastar does NOT implement view engine adapter code.

### 016: OpenAPI Document Generation Support

- D-SK47: **Target net9.0/net10.0 only (not net8.0).** `Microsoft.AspNetCore.OpenApi` was introduced in .NET 9 with no backport. Rejected: net8.0 with a shim (re-implementing OpenAPI generation defeats the purpose), net8.0 with Swashbuckle fallback (deprecated, second code path).
- D-SK48: **All in Frank.OpenApi, core Frank unchanged.** Handler builder, handler definition type, ResourceBuilder overloads -- all live in `Frank.OpenApi` via type extensions. Follows the separation principle (Frank.Auth established the pattern). Rejected: modifying core Frank with OpenAPI concerns.
- D-SK49: **`useOpenApi` convenience on WebHostBuilder with optional config callback.** Wires up both `AddOpenApi()` (services) and `MapOpenApi()` (middleware) following the Frank.Auth pattern. Rejected: separate `addOpenApi`/`mapOpenApi` operations (over-engineering for always-paired setup).
- D-SK50: **Standard ASP.NET Core endpoint metadata types for discovery.** Uses `EndpointNameMetadata`, `ProducesResponseTypeMetadata`, `AcceptsMetadata`, `TagsMetadata`, `EndpointDescriptionMetadata` -- all read by the built-in `EndpointMetadataApiDescriptionProvider`. No custom `IApiDescriptionProvider` needed. Rejected: custom provider (duplicates built-in functionality), handler reflection for type inference (not feasible -- Frank handlers are `RequestDelegate`).
- D-SK51: **FSharp.Data.JsonSchema.OpenApi for F# type schema generation.** NuGet dependency on `FSharp.Data.JsonSchema.OpenApi` (3.0.0). Provides `FSharpSchemaTransformer` implementing `IOpenApiSchemaTransformer` for records, DUs, option types, collections, recursive types. Rejected: inline schema generation (duplicates type analysis), default `JsonSchemaExporter` only (doesn't understand DUs, option types, F#-specific patterns).
- D-SK52: **HandlerBuilder CE as metadata accumulation, not monadic.** `handle` is a `[<CustomOperation>]`, not `Bind`/`Return`. Produces `HandlerDefinition` records combining `RequestDelegate` with metadata lists. At registration time, metadata is converted to `EndpointBuilder -> unit` conventions via the existing extensibility point (D-SK27).
- D-SK53: **Auto-map `ResourceSpec.Name` to operationId via `IOpenApiOperationTransformer`.** Fallback when no explicit `HandlerDefinition.Name` is provided. Reads endpoint `DisplayName` and generates operationId. Bridges basic usage (just `resource` + `useOpenApi`) with rich metadata usage (handler builder). Lives in Frank.OpenApi, not core Frank. Rejected: adding `EndpointNameMetadata` directly in Frank core's `Build()` (couples core to OpenAPI concerns).
- D-SK54: **Conditional package references for net9.0 vs net10.0.** `Microsoft.OpenApi` v1.x (net9.0) vs v2.x (net10.0) have significantly different type systems. Frank.OpenApi doesn't manipulate `OpenApiSchema` directly (delegates to `FSharpSchemaTransformer`) but needs version-appropriate package references.

---

## Sound Kitty-Spec Decisions (v7.3.0)

76 decisions across 10 sound specs.

### ks-001: Semantic Resources Phase 1

- D-KS1: **Adopt dotNetRdf.Core 3.5.1** as the shared RDF triple model for both frank-cli and Frank.LinkedData. Justified despite transitive deps (AngleSharp, Newtonsoft.Json) because writing 3 RDF serializers from scratch would be larger/buggier.
- D-KS2: **Use FSharp.Compiler.Service (via Ionide.ProjInfo) for source analysis** -- untyped AST for route detection, typed AST for type structure. No compiled assembly required. Eliminated the two-pass build workflow.
- D-KS3: **Separate Frank.Cli.MSBuild NuGet package** with `.props`/`.targets` in `build/` and `buildTransitive/` for auto-embedding compiled artifacts from `obj/frank-cli/` as EmbeddedResource items.
- D-KS4: **Core extraction library (`Frank.Cli.Core`) + thin console entry point** -- isolates FCS dependency, makes extraction independently testable. Frank.Cli.Core does NOT depend on FSharp.Analyzers.SDK.
- D-KS5: **Schema.org + Hydra as default vocabularies** via `--vocabularies` parameter. Vocabulary alignment is a post-extraction enrichment step, keeping core extraction deterministic.

### ks-003: Statecharts Feasibility Research

- D-KS6: **Lossy-but-documented round-tripping is acceptable** -- code-to-spec is comprehensive (all 5 formats), spec-to-code is best-effort. No behavioral information may be lost in runtime code itself.
- D-KS7: **Deep CE integration via `statefulResource`** that auto-generates allowed methods/responses per state. Library-level composition rejected because it loses auto-generation benefit. (Overlaps DECISIONS.md #4 -- onTransition/transition declarations design)
- D-KS8: **MailboxProcessor as default IStateMachineStore implementation.** Actor serialization, negligible per-message overhead (~1-5us). Distributed backends out of scope for v7.3.0. (Overlaps DECISIONS.md #2 -- ActiveStateConfiguration as opaque type, and ks-010 D-KS30)
- D-KS9: **Both build-time (frank-cli) and runtime (middleware endpoints) generation** of spec artifacts.
- D-KS10: **Integrate statechart generation into frank-cli** rather than a separate tool, reusing existing assembly analysis and MSBuild integration. (Overlaps DECISIONS.md #11 -- frank-cli distribution)
- D-KS11: **ALPS limitations acknowledged** -- useful for semantic vocabulary but not a standalone statechart format. Cannot distinguish PUT from DELETE, `rt` is single-valued, no initial state concept, no native guards.
- D-KS12: **XState JSON and SCXML are independent output formats**, not interconvertible (XState v5 removed SCXML import/export).
- D-KS13: **smcat is the recommended human-authoring format** for spec-to-code direction. JavaScript-only parser; frank-cli needs either shell-out or custom F# parser.
- D-KS14: **wsd-gen F# fork is NOT a parser** -- it's a thin HTTP client. WSD parser must be written from scratch.
- D-KS15: **XState v5 guard evaluation confirms Frank's guard design** -- first-match-wins in registration order matches Frank's DD-03 pattern.
- D-KS16: **Frank's BlockReason model is intentionally richer than SCXML's `cond`** -- code-to-SCXML export is lossy for guard reasons (only names survive).
- D-KS17: **Complexity ceiling**: simple (90% feasible), moderate/Stripe (75%), complex/multi-entity (60%). Target simple-to-moderate as primary use case.

### ks-004: Frank.Statecharts Core Runtime

- D-KS18: **`StateMachine<'S,'E,'C>` record type** with DU-based states, pure transition functions, and named guards as the compile-time definition.
- D-KS19: **`TransitionResult<'S,'C>` DU** with `Transitioned`, `Blocked`, and `Invalid` cases as the transition outcome type.
- D-KS20: **`BlockReason` maps to HTTP status codes**: `NotAllowed`->403, `NotYourTurn`->409, `InvalidTransition`->400, `PreconditionFailed`->412, `Custom(code,msg)`->code.
- D-KS21: **Guards evaluated in registration order, first `Blocked` short-circuits.**
- D-KS22: **Middleware + endpoint metadata marker pattern** (matching Frank.LinkedData, Frank.Auth, Frank.OpenApi). `StateMachineMetadata` added to endpoints, middleware reads it.
- D-KS23: **BehaviorSubject semantics on store subscriptions** -- new subscribers immediately receive current state.
- D-KS24: **`onTransition` observable hooks** fire after successful transitions, not before and not on blocked requests. Error isolation: one throwing observer doesn't prevent others. (Note: ks-003 proposed onTransition; DECISIONS.md #4 later eliminated it in favor of auto-generated algebra programs)

### ks-005: SHACL Validation from F# Types

- D-KS25: **SHACL shape derivation via .NET reflection at startup**, not FCS at compile time. FCS would add ~30MB dep and require source files at runtime.
- D-KS26: **Use VDS.RDF.Shacl.ShapesGraph** from dotNetRdf for validation. ShapesGraph constructed once at startup, data graph per-request.
- D-KS27: **Static F#-to-XSD datatype mapping table with extension point** (`TypeMappingOverride: Type -> XsdDatatype option`). 15 types mapped. Option types -> `sh:minCount 0`, DUs without payloads -> `sh:in`, DUs with payloads -> `sh:or`.
- D-KS28: **Dual-path validation report serialization** -- semantic clients get native SHACL ValidationReport via Frank.LinkedData; standard clients get RFC 9457 Problem Details JSON with 422 status. (Overlaps DECISIONS.md #10 -- 409/403/404 response body format is also RFC 9457)
- D-KS29: **Middleware pattern for validation** (consistent with LinkedData, Auth). Pipeline ordering: useAuth -> useValidation -> handler dispatch.
- D-KS30: **Cycle detection with configurable depth limit** (default 5) for recursive/self-referential type handling during shape derivation.

### ks-007: WSD Lexer, Parser, AST

- D-KS31: **Hand-written recursive descent parser** (Option B) over FParsec or active patterns. Zero dependencies, full control over error recovery, better allocation profile for SC-007. WSD grammar is simple (~15 keywords, 4 arrow types, 7 grouping blocks).
- D-KS32: **Best-effort partial AST model** -- `ParseResult` always contains a `Diagram` (possibly partial), plus error and warning lists. Errors are hard failures; warnings allow graceful degradation.
- D-KS33: **Amundsen arrow semantics mapping**: solid = unsafe (POST/PUT/DELETE), dashed = safe/optional (GET), forward = request, deactivating = response. Parser assigns ArrowStyle/Direction but does NOT perform HTTP method mapping.
- D-KS34: **Guard extension syntax via `[guard: key=value]`** in `note over` annotations only. Square brackets chosen because they don't conflict with standard WSD. Parser does structural extraction only, not key validation.

### ks-008: Conditional Request ETags

- D-KS35: **SHA-256 truncated to 128 bits** (32 hex chars) for ETag hashing. `HashCode.Combine` disqualified (per-process randomized seeding). XxHash128 considered but rejected as not-yet-ubiquitous. `%A` formatting rejected as fragile across compiler versions.
- D-KS36: **Strong ETags only** in double-quoted format per RFC 9110 Section 8.8.3. Weak ETags deferred.
- D-KS37: **ETag middleware registered via `plug` (after routing, before handler execution)**. Runs before auth/statecharts so 304 responses skip unnecessary auth evaluation.
- D-KS38: **MailboxProcessor-backed ETag cache** with LRU eviction (default 10,000 entries). Chosen over ConcurrentDictionary for atomic read-compute-write and consistency with Frank.Statecharts pattern.
- D-KS39: **StatechartETagProvider subscribes to state transitions** and sends `InvalidateETag` messages to cache. For plain resources, cache invalidation after any successful mutation response.
- D-KS40: **Framework-wide opt-in** (not per-resource). Consistent with Frank.Provenance pattern.

### ks-010: Statecharts Production Readiness

- D-KS41: **`FSharpValue.PreComputeUnionTagReader`** for state key extraction -- precomputed delegate reads DU tag integer, O(1) lookup into case name array. Immune to `ToString()` overrides, backward compatible for simple cases.
- D-KS42: **Actor-serialized concurrency model with UNCHANGED `IStateMachineStore` interface** -- no version tokens, no compare-and-swap, no `VersionConflict` case. Actor serializes all reads/writes. (Overlaps DECISIONS.md #2 -- ActiveStateConfiguration as opaque, managed by interpreter)
- D-KS43: **Guard DU with `AccessControl` and `EventValidation` cases** -- `AccessControl` has no event parameter (type-safe by construction), `EventValidation` receives actual event post-handler. Single `Guards` list on `StateMachine`, middleware pattern-matches to separate phases. Eliminates `Unchecked.defaultof<'E>`.
- D-KS44: **SQLite store as actor-wrapped persistence** -- `MailboxProcessor` serializes all reads/writes internally. Lazy rehydration (load on first access, not eager). Single SQLite connection (actor is single-threaded). Composite primary key (`instance_id`, `state_type`). JSON serialization via configurable `System.Text.Json`. Separate package `Frank.Statecharts.Sqlite`.
- D-KS45: **MailboxProcessor backpressure documented as known limitation**, not implemented in V1. Kestrel connection limits serve as implicit backpressure.
- D-KS46: **Accept `JsonSerializerOptions` as parameter** for SQLite store; users configure `FSharp.SystemTextJson` themselves. No hard dependency.

### ks-015: RDF SPARQL Validation

- D-KS47: **Use `LeviathanQueryProcessor` with `InMemoryDataset`** for SPARQL query execution in tests (dotNetRdf.Core already a dependency). No external SPARQL endpoints needed.
- D-KS48: **Use `GraphDiff` from dotNetRdf** for cross-format graph isomorphism checks (handles blank node renaming).
- D-KS49: **TestHost pattern** from existing Frank.LinkedData.Tests for RDF content negotiation testing. Combined LinkedData + Provenance middleware.
- D-KS50: **Named graph conventions for provenance** -- resource-scoped named graph URIs, following Frank.Provenance conventions. Test-only and documentation feature, no runtime library code.

### ks-017: WSD Generator and Cross-Format Validator

- D-KS51: **Use `StateHandlerMap` and `InitialStateKey` as primary data source** for WSD generation. Reflection on boxed `Machine` only for guards and state metadata. Do NOT attempt to reverse-engineer the `Transition` closure.
- D-KS52: **WSD output is a state-capability diagram** (HTTP methods per state), not a full transition graph. Each state emits a message per HTTP method handler to a synthetic "Resource" participant.
- D-KS53: **Guard annotations use wildcard values**: `[guard: role=*]` since guard predicates are opaque. Machine-wide guards placed as `note over <initialState>`.
- D-KS54: **Generator returns `Result<Diagram, GeneratorError>`** for structured error handling of unrecognized boxed types.

### ks-019: OPTIONS and Link Header Discovery

- D-KS55: **Use `EndpointDataSource` from DI** to enumerate sibling endpoints by matching `RoutePattern.RawText` for OPTIONS handler. Only on OPTIONS requests (infrequent).
- D-KS56: **Link headers formatted as `<URI>; rel="describedby"; type="media/type"`** per RFC 8288. Separate headers per media type (not comma-separated).
- D-KS57: **CORS preflight detection via `Access-Control-Request-Method` header** on OPTIONS requests. Discovery middleware passes through if present.
- D-KS58: **Explicit OPTIONS handler detection** via `HttpMethodMetadata` containing "OPTIONS". Explicit handlers take precedence over implicit discovery.
- D-KS59: **`[<Struct>] DiscoveryMediaType = { MediaType; Rel }`** in Frank core `Builder.fs`. Zero allocation in endpoint metadata. No Option types; `Rel` always has a value.
- D-KS60: **Three WebHostBuilder operations**: `useOptionsDiscovery`, `useLinkHeaders`, and `useDiscovery` (convenience for both). (Overlaps the `useX`/`useXWith` naming convention in CLAUDE.md)

### ks-025: Validation Pipeline Wiring

- D-KS61: **Uniform parser interface `string -> ParseResult`** for all formats (WSD, smcat, SCXML, ALPS). No mapper step post-migration.
- D-KS62: **Pipeline module (`Pipeline.validateSources`) in `Frank.Statecharts.Validation` namespace** -- public, callable by frank-cli. Accepts `(FormatTag * string) list`, returns `PipelineResult` with per-format parse diagnostics and unified `ValidationReport`.
- D-KS63: **Graceful parse failure handling** -- best-effort `ParseResult.Document` always populated (AST contract), validation runs on partial results. Parse errors surfaced in result, not thrown.
- D-KS64: **Duplicate format rejection and unsupported format reporting** as `PipelineError` DU cases, not exceptions. Duplicate check before parsing; unsupported format continues processing remaining formats.

---

## GitHub Issue Spec Decisions (v7.4.0)

46 decisions across 8 issue specs.

### ks-033: Role Definition Schema

- D-GH1: **Separate `IRoleFeature` interface, non-generic** -- roles are `Set<string>`, no dependency on statechart type parameters. Content negotiation and ALPS middleware can read roles without statechart dependency.
- D-GH2: **`Roles: Set<string>` field + `HasRole` member method** added to both `AccessControlContext` and `EventValidationContext`. Preserves structural equality, enables future projection operator reasoning.
- D-GH3: **`Roles: RoleInfo list` on `ExtractedStatechart`** in `Frank.Resources.Model`. Roles only apply to stateful resources, alongside existing `GuardNames` for cohesion.
- D-GH4: **`RoleDefinition` lives in `Types.fs`** in Frank.Statecharts, alongside `Guard` and `AccessControlContext`. Same behavioral type family.
- D-GH5: **`RoleInfo` is portable and zero-dependency** -- name + optional description. Hierarchy-neutral by design (FR-008).
- D-GH6: **Per-resource role declarations, not global registry.** Predicate is source of truth; name is label for tooling/projection/cross-validation.

### gh-257: Tagless Final Interpreter

- D-GH7: **Tagless-final encoding over free monad** for the interpreter abstraction. F# has first-class records of functions but no HKTs. Records compose trivially. Code generation is simpler (string template emitting algebra calls, not continuation-threaded DU trees). (Overlaps DECISIONS.md #1a-1c -- TransitionAlgebra shape)
- D-GH8: **Six-operation vocabulary** extracted from `HierarchicalRuntime.transition`: ComputeLCA, Exit, Enter, Fork, RecordHistory, RestoreHistory. These are the instruction set. (Overlaps DECISIONS.md #1a -- LCA as parameter not algebra op)
- D-GH9: **Four interpreter types**: RuntimeAlgebra (mutates ActiveStateConfiguration), TraceAlgebra (collects ExitedStates/EnteredStates), DualAlgebra (computes client obligations, closes AND-state gap), ValidationAlgebra (dry-run guard evaluation without mutation).
- D-GH10: **Interpreter stays pure and synchronous** -- async concerns (store access, actor serialization) remain in middleware. Per-request isolation for interpreter state, shared immutable `StateHierarchy`. (Overlaps DECISIONS.md #2 -- ActiveStateConfiguration opaque)
- D-GH11: **Roles constrain the concurrency surface** via MPST projections. Per-region ownership is explicit. Turn-taking enforced by protocol (guards), not interpreter. AND-state tensor product composed by role structure.
- D-GH12: **Two orthogonal concerns**: interpreter solves interpretation plurality; actor+roles solve concurrent access. Interpreter never manages concurrency; actor never needs to know which interpreter is used.

### gh-286: TransitionAlgebra + RuntimeInterpreter

- D-GH13: **`TransitionOp` DU** (Exited, Entered, HistoryRecorded, HistoryRestored) and **`TransitionStep` tree** (Leaf, Seq, Par) as fundamental runtime types preserving Harel AND-state parallelism. NO companion module. NO flatten functions. (Overlaps DECISIONS.md #1b -- Fork explicit in algebra)
- D-GH14: **`enterState` returns `(ActiveStateConfiguration * TransitionStep)` tuple** -- Enter stops at AND composites, Fork does the region entry producing `Par` nodes. No parallel function, no duplication.
- D-GH15: **`HierarchicalTransitionResult` drops flat fields** -- only `Configuration`, `Steps: TransitionStep`, `HistoryRecord`. `TransitionEvent` likewise drops flat `ExitedStates`/`EnteredStates`, gains `Steps: TransitionStep`.
- D-GH16: **ALL tests must assert tree shape** -- zero flat list assertions. Flat lists are explicitly how the no-op Fork survived undetected.
- D-GH17: **Five banned anti-patterns**: no `flatten` functions, no `enteredStates` extractors, no flat `string list` fields on result types, no helper returning `string list` from tree input, no keeping flat fields "for production consumers."
- D-GH18: **`transition` delegates to `TransitionProgram.fromTransition` + `runProgram`** -- eliminates duplicated 70-line implementation. (Overlaps DECISIONS.md #4 -- onTransition eliminated)

### gh-285: Statechart Analyzers

- D-GH19: **FRANK1XX diagnostic code range** for statechart-specific rules, distinguishing from existing FRANK0XX. Five new rules: FRANK101 (direct store injection in child), FRANK102 (nonexistent parent reference), FRANK103 (dual ownership), FRANK104 (route parameter mismatch), FRANK105 (raw string event name).
- D-GH20: **Cross-resource symbol resolution** via `FSharpCheckProjectResults` for FRANK101 and FRANK102. Extends existing single-resource CE operation traversal.
- D-GH21: **Generated type convention**: `*.Generated` module with `[<RequireQualifiedAccess>]` DU as the detection contract for FRANK105. (Overlaps DECISIONS.md #5, #13 -- generated file naming conventions)
- D-GH22: **Zero Frank runtime dependencies** on the analyzer package -- only FSharp.Analyzers.SDK (and transitive FCS). Uses `TryGetFullName` exclusively, never `FullName` (FCS API footgun).
- D-GH23: **Analyzer validates against SCXML-first path at build time** and CE-first path validates at startup, sharing same `ValidationAlgebra` interpreter and rules. (Overlaps DECISIONS.md #7 -- analyzer invocation strategy)

### gh-283: SCXML Codegen

- D-GH24: **`frank-cli extract --format fsharp` emits a single `<Name>.Generated.fs` file** into `obj/`. Replaces `model.bin` binary blob with compile-time-checked code. (Overlaps DECISIONS.md #5 -- one file per statechart)
- D-GH25: **V1 SCXML feature scope explicitly defined**: `<state>`, `<parallel>`, `<final>`, `<transition>`, `<history>`, `<initial>` are in scope. `<datamodel>`, `<script>`, `<assign>`, `<invoke>`, `<send>`, `<onentry>`, `<onexit>` deferred to v2. `onentry`/`onexit` are SCXML side-effect actions, distinct from algebra's Exit/Enter.
- D-GH26: **Generated transition programs are polymorphic functions `TransitionAlgebra<'r> -> 'r`**. LCA computed at generation time and baked in as parameter. (Overlaps DECISIONS.md #1a, #1c)
- D-GH27: **Module naming convention**: `OrderStatechart.Generated` (filename PascalCased + `.Generated` suffix). `[<RequireQualifiedAccess>]` on all DU types. This is the contract for FRANK105 analyzer detection. (Overlaps DECISIONS.md #13)
- D-GH28: **Generated code depends only on `Frank.Statecharts.Core`** -- no runtime Frank.Statecharts dependency. (Overlaps DECISIONS.md #8 -- merged abstractions into Core)
- D-GH29: **Unsupported SCXML features produce warnings, not failures** -- generator continues with supported elements.
- D-GH30: **Generated code never checked into source control** -- lives in `obj/`, regenerated on build when SCXML changes.

### gh-273: Child Resources

- D-GH31: **`childOf` CE operation** links child resource to parent's state machine. Serves both SCXML-first (scaffolded) and CE-first (hand-authored) paths. (Overlaps DECISIONS.md #6 -- childOf reference mechanism as value binding)
- D-GH32: **Instance ID resolution via shared route parameter names** between parent and child. Validated at startup (and by FRANK104 analyzer).
- D-GH33: **Error model for child resource operations**: parent region inactive -> 409, event invalid for region state -> 409, role not authorized -> 403, parent instance not found -> 404. (Overlaps DECISIONS.md #10 -- RFC 9457 Problem Details)
- D-GH34: **`StateMachineContext` capability boundary**: child handlers get `Send(event)`, `CurrentState`, `RegionState`, `Affordances`. Explicitly NO access to `IStatechartsStore`, mutation outside own region scope, or parent's full `ActiveStateConfiguration`.
- D-GH35: **Startup validation**: fails when childOf references nonexistent parent or when route parameters are mismatched.

### gh-252: Discovery Surface

- D-GH36: **Complete discovery requires 5 capabilities**: entry point catalog (JSON Home), ALPS profile serving, Link headers on every successful response, link-driven navigation (no hardcoded URLs), and OPTIONS support.
- D-GH37: **model.bin generated from source via frank CLI pipeline** (`source -> frank extract -> frank compile -> model.bin -> embedded resource -> useAffordances auto-loads`). No hand-built AffordanceMap. (Overlaps DECISIONS.md #11 -- frank-cli distribution)
- D-GH38: **Naive client navigation as the thesis test** -- a client with only the base URL must navigate the full lifecycle by following links alone.

### gh-251: Role-Based Affordance Projection

- D-GH39: **Transitions must use `RestrictedTo` constraints** matching intended roles, not `Unrestricted`. `Projection.projectAll` computes per-role views.
- D-GH40: **Role projection must be observable as different HTTP responses** -- different Allow headers and different Link headers for different roles hitting the same endpoint in the same state.
- D-GH41: **Restricted transitions are enforced at the guard level**, not just projected. 403/409 for out-of-role transition attempts.
- D-GH42: **Basic authentication required** (even header-based for samples) so role predicates can resolve to different roles.

### gh-254: HTTP Protocol Compliance

- D-GH43: **405 MUST always include Allow header** per RFC 9110 Section 15.5.6 -- both the "no handlers at all" path and the "handlers exist but method doesn't match" path.
- D-GH44: **202 responses must include `Content-Location`** pointing to the resource URI (RFC 9110 Section 15.3.3) so clients can discover where the resource is after a transition.
- D-GH45: **Handlers must participate in content negotiation** per RFC 9110 Section 12 -- Accept header honored, Content-Type set correctly, 406 Not Acceptable for unsupported media types. The "pit of success" must produce handlers that negotiate, not bypass.
- D-GH46: **Allow header must be consistent** between regular responses and OPTIONS responses, reflecting hierarchy-aware method resolution.

---

## Suspect Kitty-Spec Decisions (v7.3.0 -- flat-semantics audit)

66 decisions across 11 specs. Each flagged [SUSPECT] or [SOUND] based on flat-FSM indicator analysis.

### ks-006: PROV-O State Change Tracking

- D-SUS1: TransitionEvent captures PreviousState/NewState as flat values, not tree paths [SUSPECT: State changes are modeled as flat A-to-B pairs. In a hierarchical statechart, entering a state involves entering ancestor states and potentially child/initial states. The `PreviousState`/`NewState` fields capture a single state each, not the entry/exit state sets that a Harel statechart produces.]
- D-SUS2: ProvenanceRecord contains a single pre-transition entity and single post-transition entity (FR-004) [SUSPECT: A hierarchical transition can exit multiple states and enter multiple states simultaneously. Recording a single "pre" and "post" entity assumes flat FSM semantics where exactly one state is active at a time.]
- D-SUS3: Provenance subscribes to onTransition hooks which fire per successful state transition (FR-001) [SUSPECT: Assumes one transition = one state change. In Harel statecharts with parallel regions, a single event can trigger transitions in multiple orthogonal regions simultaneously. The spec does not account for compound transitions.]
- D-SUS4: ProvenanceActivity captures a single HTTP method and triggering event (FR-006) [SOUND: HTTP request is inherently singular; this is a reasonable simplification at the HTTP layer.]
- D-SUS5: IProvenanceStore with MailboxProcessor-backed serialized writes [SOUND: Infrastructure pattern, orthogonal to statechart semantics.]
- D-SUS6: Content negotiation via custom Accept media types on resource URI [SOUND: REST design pattern, no statechart semantics involved.]

### ks-011: ALPS Parser and Generator

- D-SUS7: ALPS descriptors mapped to StateMachineMetadata states and transitions [SOUND: ALPS is inherently flat -- it has no workflow ordering or hierarchy. The spec correctly documents this as a known limitation (D-006).]
- D-SUS8: Mapper produces "states and transitions without ordering constraints" when ALPS lacks workflow ordering [SOUND: Correctly acknowledges ALPS' limitations rather than inventing hierarchy.]
- D-SUS9: ALPS-specific parse types (AlpsDocument, Descriptor) with separate mapper to shared AST [SOUND: Clean separation of format-specific concerns.]
- D-SUS10: Result type for error handling, forward-compatible parsing [SOUND: Standard pattern.]
- D-SUS11: JSON/XML parsers share the same AST types [SOUND: Eliminates duplication.]
- D-SUS12: Roundtrip consistency tests preserve ALPS-expressible information only [SOUND: Honest about format limitations.]

### ks-013: smcat Parser and Generator

- D-SUS13: Parser maps to flat StateMachineMetadata with state names, transitions, guards, initial state [SUSPECT: "The mapper produces a StateMachine<'State, 'Event, 'Context>-compatible representation" -- StateMachine is the flat generic type. smcat supports composite states with `{ }` blocks (FR-005), but the mapper flattens this to a flat state machine representation.]
- D-SUS14: Pseudo-state detection by naming convention (FR-004) including Fork/Join recognized by `]` prefix [SUSPECT: Fork/Join pseudo-states are detected syntactically but the spec says nothing about what happens with them during mapping. They're just another StateType enum value. In a real Harel statechart, fork/join are operational constructs that activate/deactivate parallel regions.]
- D-SUS15: Composite states parse recursively into nested SmcatDocument within SmcatState (FR-005) [SOUND: The parser correctly captures hierarchy.]
- D-SUS16: StateType DU includes ForkJoin as a case [SUSPECT: ForkJoin is listed as a state type classification for detection purposes, but there is no discussion of what Fork/Join means operationally. It's treated as a naming-convention marker, not as a mechanism for activating parallel regions.]
- D-SUS17: Generator output: "metadata with no transitions produces only state declarations" [SOUND: Generator behavior, format-neutral.]
- D-SUS18: Mapper extracts "state names, initial state, final states, transition topology" into a flat representation [SUSPECT: The mapper output is described as a flat topology even though the parser captured composite state hierarchy. The hierarchy is parsed but then flattened during mapping.]

### ks-018: SCXML Parser and Generator

- D-SUS19: Parser captures `<parallel>` as compound state concept; `<history>` and `<invoke>` as "non-functional annotations preserved for LLM context" [SUSPECT: `<parallel>` is described as mapping to "a compound state concept" but the spec says history and invoke are "non-functional" -- meaning they are parsed but have no runtime meaning. In SCXML, parallel is a critical operational construct, not just a "compound state concept." The language "non-functional annotations" signals that the runtime won't act on these.]
- D-SUS20: Parser maps all SCXML elements to the shared AST (FR-001-FR-015) including hierarchy [SOUND: The parser correctly captures SCXML's hierarchical structure including compound states, parallel regions, history.]
- D-SUS21: Multi-target transitions split into one TransitionEdge per target (FR-007) [SUSPECT: SCXML multi-target transitions are a single atomic transition to multiple states simultaneously (entering parallel regions). Splitting into separate edges loses the atomicity -- it converts a compound transition into multiple independent transitions, which is flat-FSM thinking.]
- D-SUS22: Data model entries as name/expression pairs (FR-008) [SOUND: SCXML data model is simple key-value, correctly captured.]
- D-SUS23: Generator produces nested `<state>` elements for compound state hierarchies (FR-022) [SOUND: Hierarchy preserved in generation direction.]
- D-SUS24: Eventless transitions and internal transitions correctly modeled [SOUND: These are Harel-compatible concepts properly captured.]
- D-SUS25: `<history>` parsed as structured but "non-functional" AST nodes [SUSPECT: History is an essential Harel statechart concept. Calling it "non-functional" means the runtime ignores it, which is flat-FSM thinking -- a flat FSM has no hierarchy to remember.]

### ks-020: Shared Statechart AST

- D-SUS26: StatechartDocument uses an ordered list of StatechartElement nodes (FR-001, FR-016) [SOUND: The element list is an ordered container -- hierarchy is expressed through StateNode.Children, not through the top-level list ordering.]
- D-SUS27: StateNode has optional Children for hierarchy (FR-002) [SOUND: Hierarchical nesting is modeled via recursive StateNode.Children.]
- D-SUS28: StateKind DU includes Parallel, ShallowHistory, DeepHistory, Choice, ForkJoin (FR-005) [SOUND: All Harel statechart state types are represented in the type system.]
- D-SUS29: TransitionEdge has source, optional target, event, guard, action, parameters (FR-003) [SUSPECT: A transition in a Harel statechart can cross hierarchy levels (exiting/entering multiple states). TransitionEdge models this as simple source-to-target, which is fine for the AST, but there's no field for the "transition scope" or "least common ancestor" that determines which states are exited/entered. This is an AST limitation but may be acceptable for a parse-level representation.]
- D-SUS30: Annotations as typed DU cases per format (FR-006) [SOUND: Clean design for format-specific metadata.]
- D-SUS31: Partial population via Option types (FR-013) [SOUND: Correctly handles formats with different expressive capabilities.]
- D-SUS32: WSD migration: WSD participants become StateNodes, Messages become TransitionEdges [SOUND: WSD is inherently sequential/flat; correct mapping.]
- D-SUS33: GroupBlock with branches for alt/opt/loop/par control flow (FR-015) [SOUND: Groups captured as structural elements. Note: `par` (parallel) is modeled as a group kind, which is WSD-specific parallel notation, not Harel parallel regions. This is correct for the WSD-originated concept.]
- D-SUS34: No concept of "active state configuration" in the AST [SOUND: The AST is a static parse artifact, not a runtime state. Active configuration belongs in the runtime, not the AST.]

### ks-021: Cross-Format Validator

- D-SUS35: Validation compares state names, event names, and transition targets across formats [SOUND: Cross-format name matching is format-agnostic and works regardless of flat/hierarchical semantics.]
- D-SUS36: Self-consistency checks include "all transition targets reference states that exist" (US1) [SUSPECT: In a hierarchical statechart, transition targets can reference states at any level of the hierarchy. A flat validation that checks "does state X exist in the top-level state list" would miss states nested inside composite states. The spec doesn't clarify whether the check traverses the hierarchy.]
- D-SUS37: ValidationRule is a pure function accepting format-tagged artifacts [SOUND: Clean functional design, semantics-agnostic.]
- D-SUS38: Case-sensitive comparison for identifiers (FR-014) [SOUND: Correct strictness for cross-format validation.]
- D-SUS39: Pairwise cross-format checks, not higher-order [SOUND: Simplifies validation without loss of correctness.]
- D-SUS40: Pluggable rule registration from format modules [SOUND: Extensible architecture.]

### ks-022: smcat Shared AST Migration

- D-SUS41: Parser maps SmcatState to StateNode with Children for composite states [SOUND: Hierarchy preserved through StateNode.Children.]
- D-SUS42: StateType 1:1 maps to StateKind including ForkJoin [SOUND: All state types correctly mapped.]
- D-SUS43: Generator produces initial-to-first-state and state-to-final transitions [SOUND: Idiomatic smcat constructs.]
- D-SUS44: Generator stores guard info as NoteElement with plain text, not format-specific annotations [SUSPECT: Guards are stored as opaque text notes rather than structured guard data. This loses the guard's semantic connection to a specific transition, making it harder for a hierarchical runtime to evaluate guards in the correct scope.]
- D-SUS45: Generator uses StateMachineMetadata.StateHandlerMap to extract states, creates self-transition TransitionElements for HTTP methods [SUSPECT: This directly uses the flat StateHandlerMap (which maps state keys to HTTP handlers). It creates self-transitions per HTTP method rather than modeling actual state-to-state transitions. This is explicitly flat-FSM behavior -- the generator sees states as isolated nodes with HTTP capabilities, not as participants in hierarchical transitions.]
- D-SUS46: Round-trip property: parse -> serialize -> reparse produces structurally equal StatechartDocument [SOUND: Round-trip testing is format-level, not semantics-level.]
- D-SUS47: Serializer ignores NoteElement, GroupElement, DirectiveElement [SOUND: smcat format limitation, correctly handled.]

### ks-023: ALPS Shared AST Migration

- D-SUS48: Parser absorbs mapper heuristics for classifying descriptors as states vs data [SOUND: ALPS classification is inherently flat -- ALPS has no hierarchy. Correct treatment.]
- D-SUS49: AlpsMeta extended with documentation, links, data descriptors, version for full roundtrip [SOUND: Format-specific annotation preservation.]
- D-SUS50: Generator reconstructs descriptor hierarchy by grouping transitions by source state [SOUND: ALPS descriptor nesting is vocabulary nesting, not state hierarchy.]
- D-SUS51: Generator-side deduplication for shared transitions [SOUND: ALPS-specific optimization, no hierarchy implications.]
- D-SUS52: Two-pass parser approach: JSON -> intermediate -> StatechartDocument [SOUND: Clean architecture.]
- D-SUS53: Transition type defaults to `unsafe` when no annotation present [SOUND: ALPS-specific default.]

### ks-024: SCXML Shared AST Migration

- D-SUS54: Parser maps `<parallel>` to StateNode with Kind = Parallel and child states [SOUND: Correct hierarchical representation.]
- D-SUS55: Multi-target transitions: Target set to first target, full list in ScxmlMultiTarget annotation (FR-007) [SUSPECT: Same issue as KS-018. The TransitionEdge.Target field holds only the first target. The full target list is relegated to an annotation. This means any code that reads TransitionEdge.Target without checking annotations sees only one target -- flat-FSM behavior where transitions have exactly one target.]
- D-SUS56: History default transitions stored in ScxmlHistory annotation payload, NOT as separate TransitionElements (D2) [SUSPECT: This is a pragmatic decision but it means history default transitions are invisible to code that processes TransitionElements. History is being treated as annotation metadata rather than as an operational construct. A Harel-compliant runtime needs to know about history defaults as first-class transitions.]
- D-SUS57: State-scoped data entries flattened into StatechartDocument.DataEntries (D3) [SUSPECT: SCXML allows data models scoped to specific states. Flattening loses the scope information. In a hierarchical statechart, data scoping to states is semantically meaningful -- variables may only be visible within certain state regions.]
- D-SUS58: Non-SCXML StateKind handling: Choice/ForkJoin/Terminate silently skipped by generator (D4) [SUSPECT: ForkJoin is silently skipped by the SCXML generator. In SCXML, fork/join semantics are expressed through `<parallel>` elements. Skipping ForkJoin means the generator cannot produce the SCXML equivalent of fork/join -- suggesting ForkJoin is treated as a dead classification rather than an operational concept.]
- D-SUS59: ScxmlMeta extended with TransitionType, MultiTarget, DatamodelType, Binding, Initial [SOUND: Thorough annotation coverage for roundtripping.]
- D-SUS60: ScxmlInvoke extended with optional invoke type and id [SOUND: Preserves SCXML invoke semantics.]
- D-SUS61: Parser correctly maps compound states via StateNode.Children [SOUND: Hierarchy preserved in parse direction.]

### ks-026: CLI Statechart Commands

- D-SUS62: Extract command reads StateMachineMetadata from endpoint metadata [SUSPECT: StateMachineMetadata contains StateHandlerMap (state -> HTTP methods), initial state key, guard names. This is the flat-FSM runtime metadata. There is no hierarchy, no composite states, no parallel regions in what gets extracted. The entire CLI pipeline is built on flat metadata.]
- D-SUS63: "Transition targets between different states are NOT directly available from StateMachineMetadata" (Assumptions) [SUSPECT: This is an explicit acknowledgment that the metadata is flat. The spec says generated artifacts "represent state-capability views (what HTTP methods are available in each state), not full transition graphs." This is definitively flat-FSM semantics.]
- D-SUS64: All format generators consume StatechartDocument produced by Wsd.Generator.generate from flat metadata [SUSPECT: The generation pipeline is: flat StateMachineMetadata -> Wsd.Generator -> StatechartDocument -> format serializer. The Wsd.Generator produces a flat StatechartDocument (no hierarchy, no composite states) because its input is flat. All five format outputs are therefore flat regardless of the format's ability to express hierarchy.]
- D-SUS65: Validate command compares spec files against "code truth" derived from Wsd.Generator (D-006) [SUSPECT: The "code truth" is a flat artifact because it comes from flat metadata. Validation compares hierarchical spec files (SCXML, smcat) against a flat code-derived artifact. This means validation would flag hierarchy in spec files as "extra" compared to the flat code truth.]
- D-SUS66: XState v5 serializer/deserializer [SOUND: Format support addition.]
- D-SUS67: Format detection from file extensions [SOUND: Infrastructure concern.]
- D-SUS68: Assembly loading via host-based approach with isolated AssemblyLoadContext [SOUND: Infrastructure concern.]
- D-SUS69: "Implement flat-state mapping first. Compound states can be added later." (Risk register) [SUSPECT: Explicitly acknowledges flat-state-first approach for XState, deferring compound states. Confirms the pattern of flat-FSM-first design.]

### ks-030: Cross-Format Validation Pipeline and AST Merge

- D-SUS70: Merge priority ordering: SCXML > XState > smcat > WSD for structure, ALPS annotations-only (FR-002) [SOUND: Correctly identifies SCXML as the most structurally authoritative format.]
- D-SUS71: Merge accumulates annotations from all formats on matching nodes [SOUND: Annotation accumulation is hierarchy-neutral.]
- D-SUS72: State matching by Identifier string (exact match) in merge (FR-011) [SUSPECT: In a hierarchical statechart, the same identifier could appear at different levels of the hierarchy (e.g., "Active" inside "Playing" vs "Active" at the top level). Flat identifier matching doesn't account for hierarchical scope -- it would incorrectly merge two different states that happen to share a name at different hierarchy levels.]
- D-SUS73: Transition matching by (Source, Target, Event) triple (FR-011) [SUSPECT: Same hierarchical scope issue. In a Harel statechart, the same (Source, Target, Event) triple could have different meaning at different hierarchy levels. Flat triple matching ignores the transition's scope/LCA.]
- D-SUS74: Merge uses left fold with format priority [SOUND: Clean functional design.]
- D-SUS75: Near-match detection with Jaro-Winkler string distance [SOUND: String similarity is hierarchy-agnostic.]
- D-SUS76: SCXML > smcat for structural conflicts [SOUND: SCXML is the richer hierarchical format.]
- D-SUS77: "ALPS MUST NOT override structural fields" [SOUND: ALPS correctly limited to annotations.]
- D-SUS78: Validate-before-merge pattern [SOUND: Safety pattern, semantics-agnostic.]
- D-SUS79: States present in one format but not others included in merged document (FR-004) [SOUND: Union semantics for merge.]

### Suspect Section Summary

**Most suspect specs (flat-FSM assumptions are load-bearing):**
1. **KS-026 (CLI Statechart Commands)** -- The entire CLI pipeline is built on flat StateMachineMetadata. Generated artifacts are flat. Validation compares against flat code truth.
2. **KS-006 (PROV-O State Change Tracking)** -- TransitionEvent models single PreviousState/NewState, not entry/exit state sets. ProvenanceRecord captures single entities per transition.
3. **KS-022 (smcat Shared AST Migration)** -- Generator uses flat StateHandlerMap, creates self-transitions per HTTP method, stores guards as unstructured notes.

**Moderately suspect specs (hierarchy captured but made second-class):**
4. **KS-024 (SCXML Shared AST Migration)** -- Multi-target transitions lose atomicity, history defaults in annotations, data scope flattened, ForkJoin silently skipped.
5. **KS-018 (SCXML Parser and Generator)** -- History/invoke called "non-functional annotations," multi-target transitions split.
6. **KS-013 (smcat Parser and Generator)** -- Composite states parsed but mapper flattens to StateMachine<> generic type.
7. **KS-030 (Cross-Format Validation Pipeline)** -- State and transition matching use flat identifiers without hierarchical scope.

**Sound specs (no flat-FSM assumptions):**
8. **KS-020 (Shared Statechart AST)** -- AST correctly models hierarchy through StateNode.Children and StateKind.
9. **KS-011 (ALPS Parser and Generator)** -- ALPS is inherently flat; spec correctly treats it that way.
10. **KS-023 (ALPS Shared AST Migration)** -- No hierarchy implications; ALPS-specific concerns only.
11. **KS-021 (Cross-Format Validator)** -- Mostly sound, with one potential issue around hierarchy traversal.

---

## PR Decisions

151 decisions across 48 PRs (14 skipped as mechanical/no architectural decisions).

### PR #311: feat: add AST role metadata and CompositeKind (#307)

- D-PR1: `SenderRole`/`ReceiverRole` on `TransitionEdge` are protocol-layer metadata (from WSD/Scribble), not statechart-intrinsic. Documented per Harel review. This is a semantic distinction: the AST carries data from protocol formalisms that the statechart engine itself doesn't need.
- D-PR2: `CompositeKind` DU (`XOR`/`AND`) added to `StateContainment` with a reflection-based DU sync test to catch divergence between the zero-dep `Frank.Resources.Model` and `Frank.Statecharts` copies. Syme finding.
- D-PR3: `writePresent` (omit-on-absent) vs `writeOptional` (null-on-absent) JSON semantics for new fields. New fields that most documents won't populate use `writePresent`.
- D-PR4: `Option.orElse` as the default pattern for option-field merging in `mergeTransition`. Replaces 9 verbose match blocks. Strict evaluation acceptable since both sides are field reads.
- D-PR5: Legacy flat `states[]`/`transitions[]` JSON arrays removed, replaced by hierarchical `elements[]`. v7.3.0 format was deprecated.

### PR #281: feat: MayPoll observation rels + ALPS fragment URI link relations (#271)

- D-PR6: Roles with no transitions from a state (MayPoll) get `rel="monitor"` (IANA RFC 5765) GET link relations instead of zero affordances. Observation guidance for passive roles.
- D-PR7: Transition rels use ALPS profile fragment URIs (`{profileUrl}#{EventName}`) instead of bare kebab-case strings, satisfying RFC 8288 S2.1.2 requirement that link relations be valid URIs.

### PR #279: feat: multi-role users see union of affordances (#268)

- D-PR8: Multi-role users see the **union** of all matching roles' methods and link relations, not first-match. Changed from `Seq.tryPick` (first alphabetical) to `Seq.choose` + merge. Single-role fast path avoids list allocation.
- D-PR9: `Methods` field added to `PreComputedAffordance` as structured data, eliminating fragile `Split(", ")` parsing of Allow header strings for merge operations.

### PR #277: feat: safe/unsafe/idempotent method mapping for transitions (#269)

- D-PR10: `TransitionSafety` DU (`Safe | Unsafe | Idempotent`) replaces hardcoded `Method = "POST"` for all transitions. Three CE operations: `transition` (Unsafe/POST, default), `safeTransition` (Safe/GET), `idempotentTransition` (Idempotent/PUT). Models the ALPS three-valued safety taxonomy per Don Syme.
- D-PR11: HEAD always included in Allow headers per RFC 9110 S9.3.2 across all code paths. Fielding finding.

### PR #276: fix: toKebabCase keeps acronym runs together (#270)

- D-PR12: `toKebabCase` treats consecutive uppercase runs as single words with a general algorithm (no special-casing of known acronyms). `ATest` -> `atest` (short runs stay together), `getHTMLParser` -> `get-html-parser` (long runs split before last uppercase that starts new word).

### PR #274: feat: role-based affordance projection works end-to-end (#251)

- D-PR13: `transition` CE operation declares MPST-projected transitions with `RoleConstraint` (`Unrestricted | RestrictedTo`). Architectural principle: headers come from affordance middleware, enforcement via guard mechanism, sample handlers never inspect roles.
- D-PR14: **Closed-world semantics**: undeclared transitions blocked when roles + transitions are declared. If you declare any roles, everything not explicitly declared is forbidden.
- D-PR15: `AffordanceMiddleware` uses `OnStarting` callback to defer header injection until state/roles resolved by downstream middleware. Gated on `RouteEndpoint` check to avoid closure allocation on every request.
- D-PR16: `BlockReason.Forbidden` (renamed from `NotAllowed`) with all block variants producing RFC 9457 problem+json responses.
- D-PR17: `Vary: Authorization` header added for role-scoped and profile-overlay responses.

### PR #267: fix: enterState must include all ancestors up to root (#265)

- D-PR18: `addAncestors` helper walks `ParentMap` to root at the top of both `enterState` and `enterWithHistory`. Root must always be in active configuration. Harel semantics invariant.

### PR #266: feat: HTTP protocol compliance (#254)

- D-PR19: Allow header computed before handler dispatch to cover both "no handlers for state" and "method-mismatch" branches. Mandatory on 405 per RFC 9110 S15.5.6, useful for HATEOAS on 200/202.
- D-PR20: `Content-Location` set on 202 responses after successful transitions per RFC 9110 S15.3.3.
- D-PR21: 406 returned for unsupported Accept types. `ReturnHttpNotAcceptable = true` already default in `WebHostSpec.Empty`.

### PR #259: feat: make hierarchy operational (#250)

- D-PR22: **Breaking store redesign**: `IStateMachineStore<'S,'C>` renamed to `IStatechartsStore<'S,'C>` with `Load`/`Save` on unified `InstanceSnapshot<'S,'C>` (bundles State, Context, HierarchyConfig, HistoryRecord). `InstanceSnapshot.ofPair` convenience for flat resources.
- D-PR23: `ExecuteTransition` calls `HierarchicalRuntime.transition` after flat machine, persisting config + history. `TransitionEvent` carries `ExitedStates`/`EnteredStates` for observers.
- D-PR24: LCA history recording: `transition` records history for the LCA when it's a composite, enabling shallow history for intra-composite transitions.
- D-PR25: **AND-state model (Harel formalism)**: Sub-XOR regions within AND composites. Completed regions stay active in their final sub-state (not removed from config).
- D-PR26: Pre-computed `DescendantMap` in `StateHierarchy.build` for O(1) descendant lookups. @7sharp9 performance finding.

### PR #258: feat: cross-state circular wait detection (#253)

- D-PR27: Cross-state edges in `buildRoleDependencyGraph` enable genuine circular wait detection. Previous intra-state-only edges made cycles structurally impossible (graph was a DAG by construction).
- D-PR28: `buildRoleTransitionTargets` uses role projections (not raw statechart) for consistency with obligation classification. Harel finding.
- D-PR29: DFS path uses O(1) cons instead of O(n) append. All 4 experts agreed.
- D-PR30: Documented formalism bounds: flat-FSM caveat (Harel), involution asymmetry and cooperative liveness assumption (Wadler), guard over-approximation (Wadler). [SUSPECT: The "flat-FSM caveat" documentation acknowledges the analysis operates on a flat FSM, which may be masking hierarchy-aware analysis gaps.]

### PR #256: feat: preserve ALPS ext href on typed extension cases

- D-PR31: All 4 typed `AlpsMeta` DU cases (`AlpsRole`, `AlpsGuardExt`, `AlpsDuality`, `AlpsAvailableInStates`) now carry `href: string option` to preserve round-trip fidelity for ALPS `ext` attributes.
- D-PR32: `GuardHref` added to `TransitionEdge` as a first-class AST field (guards promoted from annotation to structure).

### PR #248: feat: wire hierarchy runtime to public API

- D-PR33: `HierarchyBridge.fromDocument` walks AST recursively, maps `StateKind.Parallel` to `AND`, children-with-ids to `XOR`. Bridge between parsed documents and the hierarchy runtime.
- D-PR34: `stateMetadataMap` key changed from `string s` to `StateKeyExtractor.keyOf s` to fix key mismatch.
- D-PR35: Deep history XOR enforcement: `enterWithHistory` Deep case uses recursive `restoreSubtree` calling `enterState` top-down, enforcing XOR exclusivity throughout restore.

### PR #247: fix: close algebraic proof gaps -- involution, composition, circular wait

- D-PR36: `deriveReverse` now re-derives `RaceConditions`, `CircularWaits`, `ProtocolSinks` from reversed state instead of passthrough. Fixes involution proof gap. (Overlaps D-003 DualAlgebra)
- D-PR37: DFS `visited` set updated on cycle detection in `detectCircularWaits` to prevent duplicate cycle reporting.
- D-PR38: Circular waits in flat FSM are structurally impossible because edges are `(waiter, S) -> (actor, S)` within the same state. [SUSPECT: This documents that the flat FSM analysis framework cannot detect genuine cross-state circular waits by construction. The fix (PR #258) added cross-state edges, but this finding reveals the original implementation was fundamentally limited by flat FSM semantics.]

### PR #246: docs+feat: MPST formalism bounds and AND-state guard

- D-PR39: AND-state dual derivation gap surfaced as `DeriveResult.Warnings` when AND-state hierarchy detected. Documented explicitly as a formalism bound.
- D-PR40: Sequential duality approximation documented: per-(role, state) snapshot is an explicit design choice, not a bug.
- D-PR41: No protocol composition operator (no tensor product/parallel composition/cut rule) documented as future work.
- D-PR42: `RoleConstraint` comment corrected from "broadcast" to "shared-input" with MPST terminology.

### PR #241: fix: embedded resource pipeline + CI NU5100 suppression

- D-PR43: Dynamic `EmbedFrankSemanticDefinitions` target with `BeforeTargets="SplitResourcesByCulture"` instead of static `ItemGroup` or `BeforeTargets="CoreCompile"`. F# SDK resource pipeline requires hooking before `SplitResourcesByCulture` for items to flow through the standard pipeline.

### PR #240: fix: MSBuild integration OOM -- response file + lightweight checker

- D-PR44: JSON response file approach replaces spawning 2 `dotnet msbuild` subprocesses. MSBuild target writes resolved project options to JSON; `frank compile --project-options-file` reads it. Eliminates OOM from subprocess chains.
- D-PR45: Lightweight `FSharpChecker` (lazy, `keepAssemblyContents=false`, `projectCacheSize=1`) for extraction path.
- D-PR46: Sample projects removed from `Frank.sln` because they were triggering OOM via MSBuild targets.

### PR #238: fix: Wave 4 expert review -- Link header URIs, middleware allocation, hierarchy perf

- D-PR47: Pre-compute full URI in `DualProfileEntry.LinkHeaderValue` at startup for zero-alloc request path. Fielding critical finding: Link headers must be valid URIs per RFC 8288.
- D-PR48: Explicit `Seq.sort` before `Seq.tryPick` for deterministic (non-random) role selection. Fielding finding.

### PR #237: fix: resolve cross-file stateMachine bindings in frank extract

- D-PR49: Fallback binding search via `AssemblySignature.Entities` when `stateMachine` CE is in a separate file from `statefulResource`. FCS lifetime documented on `LoadedProject` and `parseSourceFile`.

### PR #235: feat: dual derivation enhancements -- involution, method safety, race detection

- D-PR50: `deriveReverse` flips `MustSelect` <-> `MayPoll` per session type duality. FsCheck property test validates involution. (Overlaps D-003 DualAlgebra)
- D-PR51: Method safety integration: Unsafe self-loops (POST) classified as `MustSelect`, safe (GET) as `MayPoll`.
- D-PR52: Race detection only flags overlapping descriptors. Non-overlapping = valid interleaving.
- D-PR53: `MayObserve` removed per Harel+Wadler+Fielding consensus: final states with self-loops classified as `MayPoll`, not a separate category.
- D-PR54: Hierarchy-aware dual via `deriveWithHierarchy` for composite state livelock suppression.

### PR #234: feat: hierarchy-aware livelock detection and closed-world projection

- D-PR55: `StateContainment` lightweight hierarchy type for analysis, separate from full `StateHierarchy`. Bridge function `StateHierarchy.toContainment`. (Overlaps D-008 Abstractions merged into Core)
- D-PR56: Livelock detection composite-state-aware: parent self-loop is not livelock when children progress. `hasProgressingDescendant` checks reachable descendants.
- D-PR57: Closed-world projection uses fixed-point iteration for containment-aware reachability. Implicit initial-child entry states not pruned.

### PR #233: feat: dual conformance checking for provenance traces

- D-PR58: Three conformance checks for provenance: obligation fulfillment (MustSelect states must see advancing action), sequence well-typedness (trace follows valid dual FSM path), cut consistency (cross-service transitions from correct states).

### PR #232: feat: serve client dual via Prefer: return=dual content negotiation

- D-PR59: `Prefer: return=dual` HTTP header triggers serving client dual (duality-annotated ALPS profile) instead of plain projected profile. Content negotiation via Prefer header, not Accept.

### PR #227: feat: session type dual derivation engine

- D-PR60: Core dual derivation algorithm: `(statechart, projections) -> Map<(role, state), DualAnnotation list>`. One-directional (server -> client), explicitly not full session-type duality with involution. Honestly documented. (Overlaps D-003 DualAlgebra)
- D-PR61: `ClientObligation` DU: `MustSelect | MayPoll | SessionComplete`. Three-valued taxonomy of what the client must/can/cannot do.
- D-PR62: `ChoiceGroupId` groups co-occurring MustSelect descriptors per Wadler's external choice. Wadler review finding.
- D-PR63: "Deadlock" renamed to `ProtocolSinks`: non-final states with no advancing transitions across all roles. Fielding finding: "deadlock" was misleading.
- D-PR64: Reachability filtering applied to annotations and protocol sinks. Harel finding: annotations on unreachable states are noise.
- D-PR65: Transition deduplication: grouped by `(Source, Event)` to avoid duplicate annotations. Harel finding.

### PR #223: feat: session type foundation -- ALPS duality vocabulary + algebraic composition

- D-PR66: `TransitionResult.apply` (applicative) as primary abstraction, `.bind` (Kleisli) as secondary. User preference: applicative over monad. (Overlaps D-001 TransitionAlgebra shape)
- D-PR67: `GuardResult.identity`, `compose`, `alternative` -- guard monoid with FsCheck property tests for all laws.
- D-PR68: Runtime dogfooding: `StatefulResourceBuilder` uses `List.fold GuardResult.compose GuardResult.identity` instead of manual `List.tryPick`.
- D-PR69: Bifunctor, applicative, monoid, monad laws all verified via FsCheck property tests. Algebraic claims require property-based tests.

### PR #222: feat: HTTP-dereferenceable shape URIs for ALPS def

- D-PR70: ALPS `def` values are dereferenceable HTTP URLs (not `urn:frank:shape:` URIs). Agents can follow their nose from ALPS profile to SHACL shapes.
- D-PR71: Per-resource shape endpoints at `/shapes/{slug}` with content negotiation (`text/turtle`, `application/ld+json`, `application/rdf+xml`).
- D-PR72: Backward compatibility: no baseUri configured falls back to URN scheme.

### PR #221: feat: hierarchical statechart runtime

- D-PR73: Opt-in hierarchy via `StateMachineMetadata.Hierarchy: StateHierarchy option`. When `None`, flat FSM dispatch unchanged. Zero breaking changes.
- D-PR74: `StateKind.Composite` added to AST. `ActiveStateConfiguration = Set<StateId>` for AND-state independent regions.
- D-PR75: LCA entry/exit ordering follows SCXML specification.
- D-PR76: History pseudo-states: shallow + deep with default targets.

### PR #220: feat: closed-world projection with totality check

- D-PR77: Closed-world projection is **purely additive**: when `Unrestricted` transitions are converted to `RestrictedTo [all roles]`, resulting per-role projections are identical to open-world projections. Closed-world catches missing assignments without changing runtime behavior.
- D-PR78: Closed-world viable as **opt-in `--strict` flag**, should NOT become default in near term. Adds friction to "start simple" workflow but useful for mature resources.
- D-PR79: `projectAllStrict` returns `Result<Map<string, ExtractedStatechart>, ProjectionError list>` -- rejects `Unrestricted` (must be explicitly assigned) and `RestrictedTo []` (dead transitions).

### PR #219: feat: typed sub-DUs for AlpsRole and AlpsDuality

- D-PR80: `AlpsRoleKind` DU (`ProjectedRole | ProtocolState`) and `AlpsDualityKind` DU (`ClientObligation | AdvancesProtocol | DualOf | CutPoint`) replace string `id` fields. Consumers get exhaustive pattern matching instead of string comparison.

### PR #218: feat: add livelock detection to ProjectionValidator

- D-PR81: Livelock defined as non-final state where ALL outgoing transitions across ALL role projections are self-loops. System active but cannot progress. Severity = Warning, not Error.

### PR #215: feat: TicTacToe sample auto-load + profile endpoints

- D-PR82: `useAffordances` (zero-arg auto-load from embedded `model.bin`) replaces explicit `useAffordancesWith gameAffordanceMap`. Sample proves library works through auto-load path.

### PR #214: feat: emit protocolState ext in ALPS profiles

- D-PR83: `protocolState` ext element emitted alongside `availableInStates` on state-scoped transition descriptors. Uses canonical URI from `Classification.ProtocolStateExtId`.

### PR #213: feat: ALPS extension IDs use namespaced HTTPS URIs

- D-PR84: 8 ALPS extension literal constants changed from bare names (`"guard"`, `"projectedRole"`) to `https://frank-fs.github.io/alps-ext/{name}`. Backward compatibility: `classifyExtension` matches both old bare names and new URIs.

### PR #212: feat: entry-point designation for JSON Home document

- D-PR85: `EntryPointMetadata` is a reference type (record, not struct) because `EndpointMetadataCollection.GetMetadata<T>()` has a `class` constraint.
- D-PR86: When any endpoints carry `EntryPointMetadata`, only those routes appear in JSON Home document; otherwise all non-internal routes appear (backward compat fallback).
- D-PR87: `JsonHomeProjectionResult` wraps `JsonHomeInput` + `UsedFallback` flag, keeping the projection pure. Warning logged when fallback-all behavior is used.

### PR #211: feat: projection validator -- guard consistency + SHACL cross-ref

- D-PR88: `checkShapeReference` standalone (not wired into `validateProjection`) because it requires external `ShapeCache` + URI inputs that the projection validator doesn't have.

### PR #202: fix: v7.3.1 discovery bugfixes -- expert findings, simplify, review

- D-PR89: OPTIONS returns 204 with RFC-compliant headers. Link header URI templates resolved at request time. TemplateMatcher cached immutably per-request for thread safety.
- D-PR90: Unauthenticated users see role-agnostic links only.
- D-PR91: `~` fragment separator for collision avoidance in generated identifiers.
- D-PR92: Route constraints stripped for RFC 6570 compliance in URI templates.

### PR #195: Clean up extraction pipeline: loadOrExtract cache + I/O separation

- D-PR93: `LoadResult` named record replaces opaque `(UnifiedResource list * bool)` tuple for extraction results. Named fields over tuples.
- D-PR94: Pure `applySpecTransitions` extracted from `enrichWithSpecTransitions`, separating file I/O from transformation logic. Tests use in-memory documents.
- D-PR95: Spec files (`.wsd`, `.smcat`, `.scxml`, `.alps.*`) included in cache source hash. Previously spec file changes didn't invalidate the cache.

### PR #194: refactor: type design improvements and deduplication

- D-PR96: `FoundStatefulResource` changed from 4-tuple to `SyntaxStatefulResource` record so future fields don't break match sites.
- D-PR97: `ProjectedArtifact` refined: `Slug` -> `ProfileSlug`, added `ResourceSlug`, `IsGlobalOverride: bool` replaced with `ArtifactKind` DU (`RoleProfile of roleName | GlobalOverride`).

### PR #192: feat: add --base-uri option to project and generate commands

- D-PR98: Default base URI is `http://example.org` (no trailing slash). Trailing slash caused double-slash in generated ALPS URIs. All three unified commands share the same default.

### PR #191: fix: replace bare catch-all with TryParseList in LinkedData content negotiation

- D-PR99: Malformed Accept headers handled via `TryParseList` (no-throw API) with pragmatic passthrough. RFC 9110 S12.5.1 allows ignoring unparseable Accept. Eliminates exception-driven control flow on untrusted input.

### PR #190: Wire matchDocToResource into production path

- D-PR100: Four-layer decomposition for document matching: `statesOverlap` -> `matchesResource` -> `matchDocToResource` -> `enrichWithSpecTransitions`. Pre-computes `documentStateNames` once per doc for O(M) not O(N*M).

### PR #182: feat: add frank project command with role projection

- D-PR101: `filterCapabilitiesByTransitions` removes unsafe HTTP capabilities from states where a role has no non-self-loop transition. Projection based on transition presence, not explicit configuration.
- D-PR102: Spec file co-extraction discovers `specs/{slug}.*` files and merges transitions into `ExtractedStatechart`. Convention-over-configuration file layout.

### PR #180: feat: add progress analysis -- deadlock and starvation detection

- D-PR103: **Deadlock**: non-final state where NO role has an advancing + live transition. **Starvation**: role permanently excluded on ALL forward paths from a reachable state. Only strong starvation reported; weak starvation (turn-taking) is normal.
- D-PR104: Forward BFS uses all live global transitions (Harel correction); dead transitions excluded from adjacency (Harel soundness fix).
- D-PR105: **Read-only role**: role with zero advancing+live transitions in any state. Info severity, not a problem.

### PR #179: feat: ALPS def for role-aware SHACL shape references

- D-PR106: `def` emitted only on `unsafe`/`idempotent` descriptors, not `safe` (GET has no request body). Shapes are message type definitions that disappear automatically when projection removes the transition.

### PR #178: feat: projected profile middleware for role-aware ALPS Link headers

- D-PR107: Profile selection via Link headers (hypermedia-driven), NOT Accept-based body content negotiation. The ALPS profile is a separate resource at its own URL; middleware only changes which URL the Link header points to. Expert panel unanimously validated.
- D-PR108: Pre-computed all profile URLs at startup via `RoleProfileOverlay` for zero per-request string allocation.

### PR #177: feat: add projection consistency validator with 4 MPST checks

- D-PR109: Standalone `ProjectionCheckResult` type rather than reusing `ValidationRule` -- different input types require different result containers.
- D-PR110: Completeness check uses pruned chart because projections are built from pruned chart.
- D-PR111: Deadlock is **global** (any role can advance), not per-role -- correct for HTTP where state is server-side. Per-role progress is separate (#108).

### PR #173: feat: post-hoc session conformance checking

- D-PR112: **Existential conformance**: a transition is valid if ANY acting role's projection includes it (MPST capability union semantics).
- D-PR113: Three distinct violation types: `TransitionNotInProjection` (role exists, transition missing), `RoleNotInProjection` (unknown role), `NoActingRoles` (no roles recorded). Precise diagnostics over catch-all failures.

### PR #172: feat: projection operator for per-role ALPS profiles

- D-PR114: One ALPS profile per role (not per state). ALPS = vocabulary document; `availableInStates` handles state scoping. Expert-validated.
- D-PR115: `RoleConstraint` DU on `TransitionSpec` (pre-resolved). MPST: each interaction names its participants; "parse, don't validate."
- D-PR116: Uniform projection (no safe descriptor special case). Guards are sole arbiter; HTTP safety does not equal access scope.
- D-PR117: `pruneUnreachableStates >> filterTransitionsByRole` ordering. Multi-party protocols: state reachability is global.
- D-PR118: `Transitions = []` in F# source extractor -- runtime closures can't be statically analyzed. Spec files provide real transition data. Honestly documented.

### PR #170: fix: adopt withServer callback pattern for proper test host disposal

- D-PR119: `withServer` callback pattern with `try/finally` disposal replaces factory functions that returned `HttpClient` (losing host references). `withTestHost` shared helper eliminates near-duplicate server builders.

### PR #169: feat: typed ALPS extension vocabulary

- D-PR120: 4 typed `AlpsMeta` DU cases for known ALPS `ext` elements, with `classifyExtension` function and `[<Literal>]` constants. Unknown extensions fall back to `AlpsExtension` (backward compatible).

### PR #168: feat: auto-infer ResourceSpec.Name from route template

- D-PR121: `ResourceSpec.Name` changed from `string` (null sentinel) to `string option` (`None` = infer, `Some` = explicit). Eliminates a latent null.
- D-PR122: Name inference: `/users` -> "Users", `/users/{id}` -> "User" (singularization), `/admin/users` -> "Admin Users" (multi-segment). Inferred names flow into endpoint `DisplayName` and auto-generate OpenAPI `operationId`.

### PR #164: Fix bare catch-all in StartupProjection with logged warning

- D-PR123: `UseAffordances` CE operation defers model.bin loading to middleware pipeline time where `ILoggerFactory` is available via DI. Restructured to avoid needing logger at CE evaluation time.

### PR #160: feat: add role definition schema for statefulResource

- D-PR124: Two-tier type design: portable `RoleInfo` in zero-dep `Frank.Resources.Model`, platform `RoleDefinition` in `Frank.Statecharts`. Harel: future-proof for hierarchical runtime.
- D-PR125: Separate `IRoleFeature` typed feature interface (not merged into `IStatechartFeature`). Fowler: one feature per concern.
- D-PR126: Guard contexts carry `Roles: Set<string>` + `HasRole` member. Wadler: value semantics; Harel: inspectable for projection.
- D-PR127: Duplicate role names rejected at startup with descriptive error.

### PR #156: Discovery polish: useDiscovery as pit-of-success default

- D-PR128: `useDiscovery` = all-three default (OPTIONS + Link + JSON Home). `useDiscoveryHeaders` = partial. Delegates via `UseJsonHome |> UseDiscoveryHeaders` with no duplication.
- D-PR129: `useAffordances` (no-arg auto-load) and `useAffordancesWith` (explicit map) naming convention. `useX`/`useXWith` naming pattern established.

### PR #151: feat: JSON Home document generation for Frank.Discovery

- D-PR130: Strict Accept matching for JSON Home: only `application/json-home`, not `*/*` or `application/json`. Separate media type for machine discovery.
- D-PR131: `JsonHomeMiddleware` positioned via `BeforeRoutingMiddleware` to avoid route conflicts. Lazy first-request computation via `Lazy<T>`.
- D-PR132: `JsonHomeMetadata` type in Frank core as DI-contributed metadata from extension packages (ALPS, OpenAPI) without compile-time coupling.

### PR #144: Replace HttpContext.Items with typed IStatechartFeature

- D-PR133: Two-level interface: `IStatechartFeature` (non-generic, for AffordanceMiddleware) + `IStatechartFeature<'S,'C>` (generic, eliminates boxing for guards/transitions). Dual registration on `HttpContext.Features` follows standard ASP.NET Core pattern.
- D-PR134: Events stay in `HttpContext.Items` (`StateMachineContext.setEvent`/`tryGetEvent`). Only state/context moved to typed features.

### PR #145: Fix MSBuild EmbeddedResource for F# SDK, consolidate MSBuild packages

- D-PR135: Static `ItemGroup Condition="Exists(...)"` replaced dynamic target approach. F# SDK collects resources before targets run. **Note**: This was later reversed in PR #241 back to a dynamic target, but with `BeforeTargets="SplitResourcesByCulture"` instead of `CoreCompile`. The MSBuild embedding strategy evolved through 3 iterations.

### PR #116: v7.3.0: Statecharts spec pipeline

- D-PR136: Shared `StatechartDocument` AST with format-specific annotations. All parsers (WSD, ALPS, SCXML, smcat) target the same AST. Temporary mappers as migration strategy.
- D-PR137: Cross-format validator: self-consistency rules + cross-format agreement rules (state names, event names, casing).

### PR #109: feat: Semantic Resources Phase 3

- D-PR138: SHACL shape derivation from F# types with dual-path violation reporting (Problem Details JSON + RDF).
- D-PR139: PROV-O provenance via `TransitionObserver` with agent type discrimination.
- D-PR140: `MailboxProcessor`-based LRU cache for ETag generation from statechart state.

### PR #96: Frank.Statecharts: Core runtime library

- D-PR141: `StateMachine<'State, 'Event, 'Context>` typed state machine with pure transition functions, named guards, and `InitialContext`.
- D-PR142: `GuardContext` includes `ClaimsPrincipal`, current state, event, and application context for state-aware authorization.
- D-PR143: `IStateMachineStore<'S,'C>` storage abstraction with in-memory `MailboxProcessor` default.

### PR #89: Use WebApplicationBuilder for net10.0 targets

- D-PR144: `#if NET10_0_OR_GREATER` compiler directive switches to `WebApplication.CreateBuilder()`. `CreateSlimBuilder()` when `useDefaults = false`. `WebHostSpec` type unchanged across all targets.

### PR #83: feat: Semantic Resources Phase 1

- D-PR145: Constitution principles VI (resource disposal), VII (no silent exception swallowing), VIII (no duplicated logic) established from Phase 1 code review findings.
- D-PR146: Full semantic pipeline: extract -> clarify -> validate -> compile -> build -> serve. Zero behavioral changes to existing Frank resources.

### PR #72: feat: Frank.Datastar with optimized SSE support

- D-PR147: Direct `IBufferWriter<byte>` writes with pre-allocated byte arrays. Zero-allocation string splitting via `StringTokenizer`. No intermediate strings.
- D-PR148: Breaking change from KeyValuePair list to string array for `ExecuteScriptOptions.Attributes` -- verbatim writing without HTML encoding allocations.

### PR #74: Add OpenAPI document generation specification

- D-PR149: `ResourceSpec.Name` auto-maps to OpenAPI `operationId`. Integration with ASP.NET Core's built-in `AddOpenApi`/`MapOpenApi` and `FSharp.Data.JsonSchema.OpenApi`.

### PR #85: Cross-library consistency fixes

- D-PR150: Narrow `ReadSignalsAsync` catch from `with _ ->` to `IOException` and `JsonException` only. Let unexpected exceptions propagate. Hoist per-call `JsonSerializerOptions` to static field.

### PR #73: feat: stream-based SSE overloads for zero-allocation HTML generation

- D-PR151: `TextWriter -> Task` and `Stream -> Task` overloads for all SSE event operations. `SseDataLineWriter` (char-based, ArrayPool buffered) and `SseDataLineStream` (byte-based, newline scanning). True zero-copy paths for view engine integration.

---

## Cross-Reference: Overlaps with DECISIONS.md

| Source Decision | Overlaps DECISIONS.md |
|---|---|
| ks-003 D-KS7 (deep CE integration) | #4 (onTransition eliminated, transition declarations) |
| ks-003 D-KS8 (MailboxProcessor default store) | #2 (ActiveStateConfiguration opaque) |
| ks-003 D-KS10 (integrate into frank-cli) | #11 (frank-cli distribution) |
| ks-005 D-KS28 (RFC 9457 Problem Details) | #10 (409/403/404 response body format) |
| ks-010 D-KS42 (actor-serialized, unchanged interface) | #2 (ActiveStateConfiguration opaque) |
| gh-257 D-GH7 (tagless final over free monad) | #1a-1c (TransitionAlgebra shape) |
| gh-257 D-GH8 (six-operation vocabulary) | #1a (LCA as parameter) |
| gh-257 D-GH10 (interpreter pure, async in middleware) | #2 (ActiveStateConfiguration opaque) |
| gh-286 D-GH13 (TransitionStep tree, no flatten) | #1b (Fork explicit in algebra) |
| gh-286 D-GH18 (transition delegates to algebra) | #4 (onTransition eliminated) |
| gh-285 D-GH21 (Generated module convention) | #5, #13 (codegen file structure, naming conflicts) |
| gh-285 D-GH23 (analyzer invocation strategy) | #7 (how analyzers invoke programs) |
| gh-283 D-GH24 (single Generated.fs file) | #5 (codegen file structure) |
| gh-283 D-GH26 (polymorphic transition programs) | #1a, #1c (LCA parameter, 'r varies) |
| gh-283 D-GH27 (module naming convention) | #13 (naming conflicts) |
| gh-283 D-GH28 (Core-only dependency) | #8 (merged into Core) |
| gh-273 D-GH31 (childOf CE operation) | #6 (childOf reference mechanism) |
| gh-273 D-GH33 (error model) | #10 (RFC 9457 Problem Details) |
| gh-252 D-GH37 (model.bin via CLI pipeline) | #11 (frank-cli distribution) |
| D-PR16 (BlockReason.Forbidden with problem+json) | #10 (RFC 9457 Problem Details) |
| D-PR36, D-PR50, D-PR60 (dual derivation evolution) | #3 (DualAlgebra) |
| D-PR55 (StateContainment lightweight hierarchy) | #8 (Abstractions merged into Core) |
| D-PR66 (applicative as primary abstraction) | #1 (TransitionAlgebra shape) |

---

## Decision Counts

| Section | Count |
|---|---|
| Spec-Kit Decisions (v7.0-v7.2) | 54 |
| Sound Kitty-Spec Decisions (v7.3.0) | 76 |
| GitHub Issue Spec Decisions (v7.4.0) | 46 |
| Suspect Kitty-Spec Decisions (v7.3.0) | 79 (including summary) |
| PR Decisions | 151 |
| **Total** | **406** |
