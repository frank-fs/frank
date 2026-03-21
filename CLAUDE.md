# Frank

F# web framework proving that HATEOAS, statecharts, and semantic discovery compose into a pit of success for hypermedia APIs.

## Commands

```bash
# Build
dotnet build Frank.sln

# Test (excludes sample apps)
dotnet test Frank.sln --filter "FullyQualifiedName!~Sample"

# Test single project
dotnet test test/Frank.Discovery.Tests/

# Format check (Fantomas 7.0.5)
dotnet fantomas --check src/

# Frank.Tests is NOT in Frank.sln — test it separately
dotnet test test/Frank.Tests/
```

Always run `dotnet build Frank.sln` and `dotnet test` before claiming work is complete.

## Project Structure

- `src/` — Library projects, multi-target net8.0/net9.0/net10.0
- `test/` — Test projects, target net10.0 only
- `sample/` — Sample apps (TicTacToe, Datastar variants)
- `.worktrees/` — Git worktrees for feature branches (gitignored)
- `hooks/` — Git hooks (Fantomas pre-commit, Entire CLI)

16 projects. Key assemblies:
- `Frank` — Core CE builders (`resource`, `webHost`), metadata types
- `Frank.Discovery` — OPTIONS, Link headers, JSON Home middlewares
- `Frank.Statecharts` — Parser/generator suite, runtime, affordances
- `Frank.Statecharts.Core` — Zero-dep AST types (`Frank.Statecharts.Ast`)
- `Frank.Resources.Model` — Zero-dep resource model, affordance map

## Constitution (non-negotiable)

These principles govern all code changes. See `.kittify/memory/constitution.md` for full rationale.

1. **Resource-Oriented Design.** Resources are the primary abstraction, not URL patterns with handlers. The `resource` CE is the central API. Hypermedia over static specs.
2. **Idiomatic F#.** CEs for config, DUs for choices, Option over null, pipeline-friendly signatures, declarative over imperative.
3. **Library, Not Framework.** No view engine, no ORM, no auth system. Compose with ASP.NET Core, don't replace it.
4. **ASP.NET Core Native.** Expose `HttpContext` directly. Don't hide the platform behind abstractions.
5. **Performance Parity.** No runtime overhead vs raw ASP.NET Core routing. Avoid allocations in hot paths. Benchmark perf-sensitive changes.
6. **Resource Disposal Discipline.** All `IDisposable` values MUST use `use` bindings. No exceptions.
7. **No Silent Exception Swallowing.** Middleware/request code MUST log via `ILogger`. No bare `with _ ->` catch-alls.
8. **No Duplicated Logic.** Same function in 2+ modules → extract to shared module before merge. Copy-paste is a review blocker.

## F# Patterns

- **CE builders**: `ResourceBuilder`, `WebHostBuilder` — the CE ceremony IS the pit of success. Never suggest simplifying it.
- **Extensions**: `[<AutoOpen>] module` + `type X with [<CustomOperation>]`
- **Tests**: Expecto + ASP.NET TestHost. Use `testTask` for async, `testCase` for pure.
- **Type annotations needed**: `let! (resp: HttpResponseMessage) = client.SendAsync(req)` — F# needs explicit types on `let!` bindings in task CEs.
- **Handler overloads**: Wrap lambdas in `RequestDelegate(fun ctx -> ...)` to resolve `ResourceBuilder.Get` overload ambiguity.
- **`use` in task CEs**: Requires `IAsyncDisposable`. `IHost`/`IDisposable` types need `let` not `use` in `task { }`.
- **`ResourceEndpointDataSource` is internal**: Tests create their own `EndpointDataSource` subclass.
- **`IWebHostBuilder.Configure`**: Requires `open Microsoft.AspNetCore.Hosting` — it's an extension method, not on the interface directly.
- **Testing `WebHostSpec`**: `WebHostBuilder.Run()` blocks (starts the app). To test, build the spec manually: `ceBuilder.Yield() |> fun s -> ceBuilder.UseJsonHome(s) |> fun s -> ceBuilder.Resource(s, res)`.
- **fsproj compile order matters**: Types must be defined before use. Add new files in dependency order.

## Workflow Rules

### Git workflow
- **Keep master clean by working on a worktree within `.worktrees/`.** Every change — features, fixes, docs, config — follows: create worktree → work on branch → push → create PR → merge on approval. This keeps master free of in-progress commits (spec-kitty status, partial work) so parallel branches can always use local master as a clean base.
- **PRs must include `Closes #XX`** in the body to auto-close related issues (when applicable).
- **Never merge without explicit approval.** Merging to master is a destructive op — always ask first.

### Planning and communication
- **Always surface questions.** Never auto-answer planning/discovery questions from subagents. Present to user with recommendation.
- **Report autonomous decisions.** Maintain a running decisions table (Decision, Rationale, Impact) when making choices without explicit user confirmation.
- **Use reviewer-informed questions.** Draw on the expert panel perspectives when asking clarifying questions. "What do my experts recommend?" = consult the reviewer panel.

### Implementation
- **Always use spec-kitty commands** for pipeline work. Never bypass by generating artifacts manually.
- **Never blame pre-existing issues.** Surface, investigate, and file issues. Never dismiss problems as "not my change."
- **Portable concept filter.** When scoping work, ask: "Is this a portable concept or an F#-specific detail?" Portable concepts (statechart semantics, ALPS discovery, affordance projection) get full investment. F#-specific details (CE syntax, DU encoding, .NET middleware) are implemented but not over-invested.
- **No lightweight API.** Never suggest a simplified `frank.get "/path" handler` alternative. The CE is the design. On-ramp is solved by docs/examples.
- **Prefer skills over commands.** Use `.claude/skills/` (portable across repos via plugins) rather than `.claude/commands/` (repo-local only). Skills support YAML frontmatter, model selection, and isolation modes.

### Strategy (Phase 1: demonstrate the thesis)
- Prove the ideas work: naive-client demo, generated-artifact reference app, blog series
- Don't optimize prematurely (that's Phase 2)
- Don't abstract for portability prematurely (that's Phase 3)
- The "is this portable?" filter avoids dead ends but doesn't drive new work

## Recurring Skills

Run these at the suggested cadence to maintain quality and capture learning.

| Skill | When to run | What it does |
|-------|-------------|--------------|
| `/context-dump` | Start of session, after a break, or "where are we?" | Aggregates GitHub issues, PRs, milestones, recent activity into a briefing |
| `/retrospective` | End of every session or major task | Mines session for CLAUDE.md rules, skill candidates, agent candidates |
| `/simplify` | After completing a feature (post-commit, pre-PR) | Parallel agents review changed code for reuse, quality, efficiency |
| `/techdebt` | Weekly or before cleanup sprints | Scans code + GitHub for tech debt, categorizes by priority, proposes fixes |
| `/expert-review` | Before merging any PR or "what do my experts think?" | Dispatches 2-4 expert agents in parallel for multi-perspective review |
| `/spec-kitty.status` | Start of session when resuming pipeline work | Kanban board showing work package progress |

**Not recurring but important to remember:**
- `/spec-kitty.specify` → `/spec-kitty.plan` → `/spec-kitty.tasks` — full pipeline for new features
- `superpowers:brainstorming` — before any creative/design work
- `superpowers:systematic-debugging` — before proposing fixes for any bug

## Expert Panel

"My experts" = 10 reviewer agents in `~/.claude/agents/expert-*.md`. Dispatch via `/expert-review`.

| Tier | Experts | Focus |
|------|---------|-------|
| 1 (highest gap) | Fielding, Darrel Miller | HATEOAS, API discovery, HTTP standards |
| 2 (active) | Fowler, @7sharp9 | ASP.NET Core, F# performance |
| 3 (long-term) | Harel, Seemann, Don Syme | Statecharts, purity, F# API design |
| 4 (domain) | Amundsen, Wlaschin, Claude-agent | ALPS, F# DX, agentic consumption |

Dispatch 2-4 per review based on change type. Don't dispatch all 10.
