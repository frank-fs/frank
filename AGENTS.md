# Frank — Agent Guidelines

Rules for AI coding assistants working in this repo. Applies to all models (Claude, Gemini, Copilot, etc.).

## Project Overview

F# web framework. HATEOAS + statecharts + semantic discovery. Multi-target: net8.0/net9.0/net10.0.

## Commands

```bash
# Build
DOTNET_SYSTEM_GLOBALIZATION_INVARIANT=1 dotnet build Frank.sln

# Test (excludes samples)
DOTNET_SYSTEM_GLOBALIZATION_INVARIANT=1 dotnet test Frank.sln --filter "FullyQualifiedName!~Sample"

# Frank.Tests (NOT in Frank.sln)
DOTNET_SYSTEM_GLOBALIZATION_INVARIANT=1 dotnet test test/Frank.Tests/

# Format check
dotnet fantomas --check src/
```

Always run build + tests before reporting work complete.

## Non-Negotiable Rules

### Constitution

1. Resources are the primary abstraction — not URL patterns. The `resource` CE is the central API.
2. Idiomatic F#: CEs for config, DUs for choices, `Option` over null, pipelines, declarative over imperative.
3. Library not framework: no view engine, ORM, or auth system. Compose with ASP.NET Core.
4. Expose `HttpContext` directly. Don't hide the platform.
5. No runtime overhead vs raw ASP.NET Core. Avoid hot-path allocations.
6. All `IDisposable` MUST use `use` bindings.
7. Middleware MUST log via `ILogger`. No bare catch-alls.
8. Same function in 2+ modules → extract before merge. Copy-paste is a review blocker.

### Holzmann Rules (Power of Ten, rules 9–15)

9. **Keep It Linear.** Max two nesting levels. Flatten with early returns or extracted functions.
10. **Bound Every Loop.** Every loop, retry, poll, and recursion needs an explicit cap with defined cap-hit behavior.
11. **One Function, One Job.** Describable without "and." Hard limit: 60 lines.
12. **State Your Assumptions.** Preconditions in code (`invalidArg`/`assert`/`failwith`), not comments.
13. **Narrow Your State.** No module-level mutable. Pass dependencies explicitly.
14. **Surface Your Side Effects.** I/O and mutations obvious at the call site. Pure and effectful code separate.
15. **One Layer of Indirection.** Max one layer of dynamic dispatch. Linear composition over decoded elegance.

### Karpathy Guidelines

- **Think before coding.** State assumptions. Surface tradeoffs. Ask when unclear — don't pick silently between interpretations.
- **Simplicity first.** Minimum code that solves the problem. No speculative features, abstractions, or error handling for impossible scenarios.
- **Surgical changes.** Touch only what the request requires. Don't improve adjacent code. Mention unrelated dead code — don't delete it.
- **Goal-driven execution.** Define verifiable success criteria before starting. Multi-step: state the plan with per-step verification.

## Workflow

- Never work on master directly. Create a branch.
- All implementation work: branch → build+test+format → merge `--ff-only` into master.
- Never close issues with unfulfilled requirements.
- Never create issues, PRs, or external actions while discussing — wait for explicit go-ahead.
- Never push without verifying output — agent output may be partial.

## F# Patterns

- **CE builders are the API.** Never suggest a simplified `frank.get "/path"` alternative.
- **fsproj compile order matters.** Types must be defined before use.
- **`Result` over `Option`** when `None` would discard useful error context.
- **`invalidArg`/`invalidOp`/`failwith`** for preconditions — not silent returns or exceptions swallowed by catch-alls.
- **`use` vs `let` in `task {}`**: `IDisposable`-only types (not `IAsyncDisposable`) need `let`, not `use`.

## Sub-directory Context

- `src/CLAUDE.md` — ASP.NET Core + MSBuild gotchas
- `test/CLAUDE.md` — Expecto + TestHost patterns
- `sample/CLAUDE.md` — Sample server lifecycle, Datastar env vars
