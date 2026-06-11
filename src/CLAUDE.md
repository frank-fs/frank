# Frank Source Libraries

ASP.NET Core and MSBuild gotchas specific to `src/`. Load alongside root `CLAUDE.md`.

## ASP.NET Core Gotchas

- **`Allow` is a content header**: Use `resp.Content.Headers.Allow` not `resp.Headers.Contains("Allow")` — the latter throws `Misused header name` at runtime.
- **`StringValues` on `Headers.Append`**: `sprintf` returns `string`; use an intermediate `let` binding before passing to `IHeaderDictionary.Append`.
- **`TemplateMatcher` is not thread-safe**: Cache immutable `RouteTemplate` objects (via `TemplateParser.Parse`); create `TemplateMatcher` per-request.
- **`GetMetadata<T>()` requires reference types**: `EndpointMetadataCollection.GetMetadata<T>()` has a `class` constraint — marker types must be records, not `[<Struct>]`.
- **`AddSingleton` vs `TryAddSingleton`**: `AddSingleton` is last-wins; `TryAddSingleton` is first-wins. Use `TryAddSingleton` for auto-load defaults, `AddSingleton` for explicit overrides.
- **`Response.OnStarting` for deferred header injection**: Register an `OnStarting` callback when middleware needs data set by later middleware. Gate on `RouteEndpoint` check to avoid closure allocation on every request.
- **Link headers must be URIs (RFC 8288), not URI templates**: Pre-computed Link headers with route params need runtime resolution against `ctx.Request.RouteValues` before emission.

## MSBuild and Tooling

- **`.props` vs `.targets` evaluation timing**: Properties in `.props` that reference SDK-computed values (`IntermediateOutputPath`, `TargetFramework`) resolve empty — SDK sets them after `.props` import. Use a `Target` with inner `PropertyGroup` for true late-binding.
- **NuGet tool cache serves stale binaries**: Clear the global cache first: `rm -rf ~/.nuget/packages/<tool-name>` before `dotnet tool install`. `dotnet clean` + `dotnet pack` alone don't invalidate it.
