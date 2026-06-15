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
- **Generated `.fs` must precede its consumers in `@(Compile)`**: F# compile order = item order. A `BeforeCompile`/`CoreCompile` target that `<Compile Include>`s a generated file appends it LAST → "value/namespace not defined" where a later-but-static file (e.g. `Program.fs`) uses it. Fix: in the target, `<Compile Remove="Program.fs"/>` then add the generated file, then re-add `Program.fs` — forcing the generated module ahead of its consumer on both initial and incremental builds. Split generate (incremental, `Inputs`/`Outputs` on the lock) from inject-and-reorder (always runs) so the ordering holds when the generate step is skipped.
- **Rebuilding an MSBuild task DLL needs `dotnet build-server shutdown`**: MSBuild caches task assemblies in-process (the persistent build server / VBCSCompiler nodes) across builds. After rebuilding a task project (e.g. `Frank.Cli.MSBuild`), the *old* cached task DLL keeps running until the build server is shut down — symptom: code changes to a task appear to have no effect, or stale `MSBuildLoadContext` errors. Run `dotnet build-server shutdown` before re-building a consumer that uses the just-rebuilt task. Clean CI builds (fresh process) are unaffected.
- **Vocabulary evaluation is FCS typed-AST reconstruction, not FSI execution**: `VocabularyEvaluator.evalRegistry` does NOT run the vocabulary CE. It typechecks the source files with `FSharpChecker.ParseAndCheckProject` (`keepAssemblyContents=true`), walks the typed tree of the registry binding, reads literal args, and resolves each `typeof`/`typedefof` to its `FSharpEntity.TryFullName`. Type identity therefore comes from the compiler, never reflection — and there is no `AssemblyLoadContext` boundary to cross (no `FsiEvaluationSession`, no serialize/deserialize bridge). Consequences: vocabulary args must be literals (a computed IRI → `Error`); a `typeof` of a type abbreviation or constructed generic → `Error` (use `typedefof<T<_>>` for generic definitions).
- **If you ever host in-process FSI in an MSBuild task**: the task runs in an isolated `MSBuildLoadContext`; an `FsiEvaluationSession` `#r`-ing the project's copy loads a *different* identity of the same type → `:?>` casts across the boundary throw "type A cannot be cast to type B (same type, different context)". Do NOT return strongly-typed values across the boundary — serialize to a string inside FSI and deserialize in the caller. (Frank no longer hosts FSI in a task; kept as a caution.)
