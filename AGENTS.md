# Frank — Agent Guidelines

Rules for AI coding assistants in this repository. Applies to every model and tool. Read fully before changing code.

## Primary Guiding Rules

### Karpathy Guidelines — how to behave

1. **Think before coding.** State assumptions and tradeoffs explicitly. When multiple interpretations exist, present them — never pick silently. If something is unclear, stop and ask. Push back when a simpler approach exists.
2. **Simplicity first.** Minimum code that solves the problem, nothing speculative. No features beyond the ask, no abstractions for single use, no unrequested flexibility, no error handling for impossible cases. If 200 lines can be 50, rewrite it.
3. **Surgical changes.** Touch only what the request requires. Don't "improve" adjacent code, comments, or formatting. Remove only what your change orphaned. Mention unrelated dead code — don't delete it.
4. **Goal-driven execution.** Define verifiable success criteria before starting ("add validation" → "write tests that fail, then make them pass"). Multi-step tasks: state a plan with a check per step; loop until verified.

### NASA JPL Power of Ten (Holzmann) — how to write code

Safety-critical discipline, from Gerard Holzmann's JPL rules.

1. **Simple control flow.** No `goto`, `setjmp`/`longjmp`, or recursion.
2. **Bound every loop.** Fixed, provable upper bound, with defined behavior at the bound.
3. **No dynamic allocation after initialization.** Prefer static/stack; no hot-path allocation.
4. **Small functions.** At most one page (~60 lines), a single well-defined job, few parameters.
5. **Data-centric design.** Each data structure is owned by exactly one function, accessed only via accessors.
6. **Real-time guarantees.** Distinct initialization and run phases; no runtime reconfiguration.
7. **Annotate assumptions.** Assertions in code, with side-effect-free predicates; revisit when assumptions change.
8. **Few threads, deterministic synchronization.**
9. **Hide state.** Encapsulate; no shared or module-level mutable state.
10. **Verify.** Unit-test every function; instrument for coverage.

## Repository

**Frank** — F# web framework: HATEOAS + statecharts + semantic discovery. Multi-targets .NET 8.0/9.0/10.0.

- `src/Frank.*` — framework packages (`Frank`, `Alps`, `Analyzers`, `Auth`, `Datastar`, `JsonHome`, `OpenApi`, `Provenance`, `Rdf`, `Validation`)
- `sample/` — runnable sample applications
- `test/Frank.*.Tests/` — test suites (one per package; not in `Frank.sln`)

### Commands

```bash
dotnet restore Frank.sln
dotnet build Frank.sln -c Release --no-restore
dotnet test test/<Pkg>.Tests        # run each suite; full list in .github/workflows/ci.yml
dotnet fantomas --check             # on changed src/ and test/ files, before commit
```

Verify build + tests before reporting work complete.

### F# Conventions

- `Resource` CE is the central API — never substitute a path shorthand.
- Idiomatic F#: CEs for configuration, DUs for choices, `Option` over null, pipelines, declarative over imperative.
- Library, not framework: no view engine, ORM, or auth system. Compose with ASP.NET Core; expose `HttpContext`, don't hide the platform.
- Zero runtime overhead vs raw ASP.NET Core — no hot-path allocations.
- `IDisposable` values bind with `use`; `IAsyncDisposable`-only types with `let`.
- Preconditions via `invalidArg`/`invalidOp`/`failwith` — no silent returns or swallowed exceptions. `Result` over `Option` when `None` discards error context.
- Middleware logs via `ILogger`; no bare catch-alls.
- A function found in 2+ modules is extracted to a shared module before merge — copy-paste blocks review.
- Every `.fs` gets a matching `.fsi` signature file directly above it in `<Compile>` order (mark cross-file members `internal`, not `private`). Signature mismatches only surface at compile time — verify with a real build across every targeted TFM.
- `.fsproj` compile order matters: types must be defined before use.
- New `src/Frank.*` packages include a package `README.md` and a runnable `sample/` app in the same plan; `hooks/check-new-package-deliverables.sh` flags missing ones.

### Workflow

- Never work on master directly. Branch → build + test + format → merge `--ff-only`.
- Run the `discipline` skill (Holzmann Power of Ten review) on changed code before committing.
- Never push unverified output; agent output may be partial.
- No issues/PRs/external actions while discussing — wait for explicit go-ahead.
- Never close issues with unfulfilled requirements.

## Context Files

- `sample/CLAUDE.md` — sample server lifecycle and Playwright test setup
- `docs/superpowers/{specs,plans}/` — design specs and feature plans