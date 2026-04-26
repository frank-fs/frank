# Frank.Discovery

OPTIONS, Link headers, JSON Home, and other affordance-discovery middlewares.

## Gotchas

- **Link headers must be URIs per RFC 8288.** Pre-computed Link headers with route template params (`{gameId}`) need runtime resolution against `ctx.Request.RouteValues`. Use a `HasTemplateLinks` flag to skip resolution for non-parameterized resources (zero-alloc fast path).
- **`StringValues` overload on `Headers.Append`.** `sprintf` returns `string`, but `IHeaderDictionary.Append` expects `StringValues`. Use an intermediate `let` binding: `let linkValue = sprintf "..." in ctx.Response.Headers.Append("Link", linkValue)`.
- **`TemplateMatcher` is not thread-safe.** Cache immutable `RouteTemplate` objects (via `TemplateParser.Parse`); create `TemplateMatcher` per-request. Sharing cached matchers across concurrent requests causes subtle data races.
- **`GetMetadata<T>()` requires reference types.** `EndpointMetadataCollection.GetMetadata<T>()` has a `class` constraint. Endpoint metadata marker types must be records, not `[<Struct>]` types.
- **`Response.OnStarting` for deferred header injection.** When middleware needs data set by later middleware (e.g., AffordanceMiddleware needs state/roles from StateMachineMiddleware), register an `OnStarting` callback. The callback fires just before the response is sent — after all middleware has completed. Pattern: `ctx.Response.OnStarting(Func<Task>(fun () -> ... Task.CompletedTask))`. Gate on `RouteEndpoint` check to avoid closure allocation on every request.
- **`AddSingleton` vs `TryAddSingleton`.** `AddSingleton` always registers (last-wins for same type). `TryAddSingleton` is first-wins (no-op if already registered). Use `TryAddSingleton` for auto-load defaults, `AddSingleton` for explicit overrides.
- **`Allow` is a content header.** Use `resp.Content.Headers.Allow` not `resp.Headers.Contains("Allow")`. The latter throws `Misused header name` at runtime.
- **CE delegation for bundles.** `spec |> this.UseJsonHome |> this.UseDiscoveryHeaders` — compose CE operations via member calls without inline duplication. Each writes to a different `WebHostSpec` field. Precedent: `Frank.Provenance`.
- **`useX`/`useXWith` naming convention.** Zero-arg auto-load operations use `useX`; explicit-arg overloads use `useXWith`. Established by: `useValidation`/`useValidationWith`, `useLinkedData`/`useLinkedDataWith`, `useAffordances`/`useAffordancesWith`. Do not attempt same-name CE overloading at different arities.
