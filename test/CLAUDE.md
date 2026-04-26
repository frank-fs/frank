# Frank Tests

Test projects for Frank libraries. Target net10.0 only.

## Conventions

- **Expecto + ASP.NET TestHost.** Use `testTask` for async, `testCase` for pure.
- **`Frank.Tests` is NOT in `Frank.sln`** — test it separately: `DOTNET_SYSTEM_GLOBALIZATION_INVARIANT=1 dotnet test test/Frank.Tests/`.

## Gotchas

- **Type annotations needed on `let!` bindings in task CEs.** F# can't infer the result type: `let! (resp: HttpResponseMessage) = client.SendAsync(req)`.
- **`use` in task CEs requires `IAsyncDisposable`.** `IHost`/`IDisposable`-only types need `let` not `use` in `task { }`.
- **`ResourceEndpointDataSource` is internal.** Tests create their own `EndpointDataSource` subclass (see `TestEndpointDataSource.fs`).
- **Testing `WebHostSpec` directly.** `WebHostBuilder.Run()` blocks (starts the app). To test, build the spec manually: `ceBuilder.Yield() |> fun s -> ceBuilder.UseJsonHome(s) |> fun s -> ceBuilder.Resource(s, res)`.
- **Handler overloads.** Wrap lambdas in `RequestDelegate(fun ctx -> ...)` to resolve `ResourceBuilder.Get` overload ambiguity.
- **`IWebHostBuilder.Configure`.** Requires `open Microsoft.AspNetCore.Hosting` — it's an extension method, not on the interface directly.
- **Reflection-based DU sync tests for intentionally-duplicated types.** When a DU is duplicated across zero-dep assemblies (e.g., `CompositeKind`), add a test using `FSharpType.GetUnionCases` to assert case count equality. Catches divergence at test time since there's no compile-time coupling between the two definitions.
