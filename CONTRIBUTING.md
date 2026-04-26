# Contributing to Frank

## Setup

After cloning, restore local tools and enable the pre-commit hook:

```bash
dotnet tool restore
git config core.hooksPath hooks
```

This installs [Fantomas](https://fsprojects.github.io/fantomas/) and enables a pre-commit hook that checks F# formatting. If a commit is blocked, format the staged files with:

```bash
git diff --cached --name-only --diff-filter=ACM | grep '\.fs$' | xargs dotnet fantomas
```

## Building and Testing

```bash
# Build
dotnet build Frank.sln

# Test (excludes sample apps)
dotnet test Frank.sln --filter "FullyQualifiedName!~Sample"

# Test a single project
dotnet test test/Frank.Discovery.Tests/

# Check formatting
dotnet fantomas --check src/

# Frank.Tests is not in Frank.sln — test it separately
dotnet test test/Frank.Tests/
```

On nix-darwin, prefix commands with `DOTNET_SYSTEM_GLOBALIZATION_INVARIANT=1` to work around an ICU mismatch.

Run build and tests before submitting a PR. Some sample apps also have `test-e2e.sh` scripts that test the library through a running app:

```bash
find sample/ -name "test-e2e.sh"
```

Run these if your change could affect sample behavior.

## Project Structure

```
src/      Library projects (multi-target net8.0/net9.0/net10.0)
test/     Test projects (net10.0 only)
sample/   Sample apps (TicTacToe, Datastar variants)
hooks/    Git hooks (Fantomas pre-commit)
docs/     Design documents
```

Key assemblies:

| Assembly | Purpose |
|---|---|
| `Frank` | Core CE builders (`resource`, `webHost`), metadata types |
| `Frank.Discovery` | OPTIONS, Link headers, JSON Home middlewares |
| `Frank.Statecharts` | Parser/generator suite, runtime, affordances |
| `Frank.Statecharts.Core` | Zero-dep AST types (`Frank.Statecharts.Ast`) |
| `Frank.Resources.Model` | Zero-dep resource model, affordance map |

**fsproj compile order matters.** F# requires types to be defined before use. Add new files in dependency order within `.fsproj`.

## Design Principles

These are non-negotiable. Changes that violate them won't be merged.

1. **Resource-Oriented Design.** Resources are the primary abstraction, not URL patterns with handlers. The `resource` CE is the central API. Hypermedia over static specs.
2. **Idiomatic F#.** CEs for config, DUs for choices, `Option` over null, pipeline-friendly signatures, declarative over imperative.
3. **Library, Not Framework.** No view engine, no ORM, no auth system. Compose with ASP.NET Core; don't replace it.
4. **ASP.NET Core Native.** Expose `HttpContext` directly. Don't hide the platform behind abstractions.
5. **Performance Parity.** No runtime overhead vs raw ASP.NET Core routing. Avoid allocations in hot paths. Benchmark perf-sensitive changes.
6. **Resource Disposal Discipline.** All `IDisposable` values must use `use` bindings. No exceptions.
7. **No Silent Exception Swallowing.** Middleware and request code must log via `ILogger`. No bare `with _ ->` catch-alls.
8. **No Duplicated Logic.** The same function appearing in two or more modules means it should be extracted to a shared module before merging. Copy-paste is a review blocker.

There is intentionally no simplified `frank.get "/path" handler` API. The computation expression is the design.

## Code Quality

Frank adapts Gerard Holzmann's "Power of Ten" rules from NASA/JPL. These apply to all code — library, test, and sample.

9. **Keep it linear.** Max two levels of nesting. Flatten with early returns, pipeline operators, or extracted functions.
10. **Bound every loop.** Every loop, retry, poll, and recursive call needs an explicit maximum. Define what happens when the cap is hit.
11. **One function, one job.** Describable in one sentence without "and." Hard limit: 60 lines. Long functions mean the decomposition hasn't happened yet.
12. **State your assumptions.** Preconditions and invariants belong in code — `invalidArg`, `invalidOp`, `assert`, or `failwith` — not comments. At least one precondition check per public function at system boundaries.
13. **Narrow your state.** No module-level `mutable`. Pass dependencies explicitly. Data lives as close to its use as possible.
14. **Surface your side effects.** I/O, mutations, and network calls must be obvious at the call site. Separate pure computation from side-effectful orchestration.
15. **One layer of indirection.** If tracing a call requires navigating more than one layer of dynamic dispatch or callback, simplify. Prefer linear composition.

## F# Conventions

A few patterns that come up repeatedly:

- **CE builders are the API.** `ResourceBuilder` and `WebHostBuilder` — the CE ceremony is intentional. Don't suggest flattening it.
- **Explicit types on `let!` bindings.** F# needs them in task CEs: `let! (resp: HttpResponseMessage) = client.SendAsync(req)`.
- **Handler overloads.** Wrap lambdas in `RequestDelegate(fun ctx -> ...)` to resolve `ResourceBuilder.Get` overload ambiguity.
- **`use` in task CEs requires `IAsyncDisposable`.** `IHost`/`IDisposable` types need `let` not `use` inside `task { }`.
- **`Allow` is a content header.** Use `resp.Content.Headers.Allow`, not `resp.Headers.Contains("Allow")` — the latter throws `Misused header name` at runtime.
- **`useX`/`useXWith` naming.** Zero-arg auto-load operations use `useX`; explicit-arg overloads use `useXWith`. Don't try same-name CE overloading at different arities.
- **`Result` over `Option` for diagnostics.** When `None` would discard useful error context, return `Result<'T, string>` so callers can surface warnings instead of silently dropping them.
- **Tests use Expecto + ASP.NET TestHost.** Use `testTask` for async, `testCase` for pure. `ResourceEndpointDataSource` is internal — tests subclass `EndpointDataSource` directly.

## Reporting Issues

GitHub will show a template chooser when you open a new issue. Pick the right one:

- **Bug report** — something is broken or behaves incorrectly. Provide a reproduction, the actual and expected behavior, and your environment.
- **Feature request** — a new capability or behaviour change. State a *thesis*, describe the problem, propose a solution, and write falsifiable acceptance tests.

**Acceptance tests must be falsifiable.** An acceptance test is falsifiable if a wrong implementation produces a failing result. HTTP request/response pairs work well: the `curl -v` output shows both headers and body, so the test can't be faked by returning the wrong Content-Type or status code. Include at least one negative test when possible.

When a feature request depends on other open issues, list them explicitly. Don't close a dependent issue until all of its requirements are met.

## Git Workflow

Frank uses trunk-based development. Small, targeted changes (config, docs, single-file fixes) go directly to master. Multi-commit features or risky experiments use short-lived branches — by convention these live in `.claude/worktrees/<name>` (gitignored) and are merged fast-forward into master once the verification sequence passes.

PRs should include `Closes #XX` in the body to auto-close related issues.

Before opening a PR:

1. Run the full build and test suite.
2. Run `dotnet fantomas --check src/` and fix any formatting issues.
3. Make sure each commit compiles and tests pass — don't leave broken intermediate states.

**PR descriptions** should enumerate the requirements from any linked issue with a status for each: implemented, blocked, or deferred with rationale. "Closes #X" without per-requirement accounting is insufficient for non-trivial changes.

Don't close issues that have unfulfilled dependencies. Either split the issue or add a comment listing what's still blocked.

## Design Documents

The `docs/` directory has background on the project's ideas and decisions:

- [DECISIONS.md](docs/DECISIONS.md) — ~400 design decisions with rationale
- [STATECHARTS.md](docs/STATECHARTS.md) — Hierarchical statechart support, guards, test coverage
- [SEMANTIC-RESOURCES.md](docs/SEMANTIC-RESOURCES.md) — Agent-legible applications and self-describing app architecture
- [SPEC-PIPELINE.md](docs/SPEC-PIPELINE.md) — Bidirectional design spec pipeline (WSD, SCXML, ALPS)
- [COMPARISON.md](docs/COMPARISON.md) — How Frank.Statecharts compares to Webmachine and Freya
