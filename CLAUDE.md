# Frank

F# web framework proving that HATEOAS, statecharts, and semantic discovery compose into a pit of success for hypermedia APIs.

## Commands

```bash
# Build (DOTNET_SYSTEM_GLOBALIZATION_INVARIANT=1 required on nix-darwin due to ICU mismatch)
DOTNET_SYSTEM_GLOBALIZATION_INVARIANT=1 dotnet build Frank.sln

# Test (excludes sample apps)
DOTNET_SYSTEM_GLOBALIZATION_INVARIANT=1 dotnet test Frank.sln --filter "FullyQualifiedName!~Sample"

# Test single project
DOTNET_SYSTEM_GLOBALIZATION_INVARIANT=1 dotnet test test/Frank.Discovery.Tests/

# Format check (Fantomas 7.0.5)
dotnet fantomas --check src/

# Frank.Tests is NOT in Frank.sln — test it separately
DOTNET_SYSTEM_GLOBALIZATION_INVARIANT=1 dotnet test test/Frank.Tests/
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

These principles govern all code changes.

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
- **`Allow` is a content header**: Use `resp.Content.Headers.Allow` not `resp.Headers.Contains("Allow")`. The latter throws `Misused header name` at runtime.
- **CE delegation for bundles**: `spec |> this.UseJsonHome |> this.UseDiscoveryHeaders` — compose CE operations via member calls without inline duplication. Each writes to a different `WebHostSpec` field. Precedent: `Frank.Provenance`.
- **`useX`/`useXWith` naming convention**: Zero-arg auto-load operations use `useX`; explicit-arg overloads use `useXWith`. Established by: `useValidation`/`useValidationWith`, `useLinkedData`/`useLinkedDataWith`, `useAffordances`/`useAffordancesWith`. Do not attempt same-name CE overloading at different arities.
- **Integer division in threshold checks**: `count / 2` truncates (3/2=1), making thresholds more permissive than intended. Use multiplication instead: `overlap * 2 >= total` gives honest "at least half" semantics without rounding issues.
- **Result over Option for diagnostics**: When a `None` return would discard useful error context (parse failures, file not found, unsupported format), return `Result<'T, string>` instead. Lets callers aggregate and surface warnings rather than silently dropping them.
- **Transition state collection**: When extracting state names from `TransitionElement`, collect both `edge.Source` and `edge.Target`. Transition-only formats (smcat) don't create `StateDecl` for target-only states — they'd be invisible if you only collect sources.
- **`StringValues` overload on `Headers.Append`**: `sprintf` returns `string`, but `IHeaderDictionary.Append` expects `StringValues`. Use an intermediate `let` binding: `let linkValue = sprintf "..." in ctx.Response.Headers.Append("Link", linkValue)`.
- **`TemplateMatcher` is not thread-safe**: Cache immutable `RouteTemplate` objects (via `TemplateParser.Parse`); create `TemplateMatcher` per-request. Sharing cached matchers across concurrent requests causes subtle data races.
- **`AddSingleton` vs `TryAddSingleton`**: `AddSingleton` always registers (last-wins for same type). `TryAddSingleton` is first-wins (no-op if already registered). Use `TryAddSingleton` for auto-load defaults, `AddSingleton` for explicit overrides.
- **Link headers must be URIs per RFC 8288**: Pre-computed Link headers with route template params (`{gameId}`) need runtime resolution against `ctx.Request.RouteValues`. Use a `HasTemplateLinks` flag to skip resolution for non-parameterized resources (zero-alloc fast path).
- **`_.Member` shorthand can break type inference with `Set.ofList`**: `List.map _.AbsoluteUri |> Set.ofList` may fail with "Uri does not support comparison" because the compiler doesn't resolve the intermediate `string` type. Use explicit lambdas: `List.map (fun (u: Uri) -> u.AbsoluteUri) |> Set.ofList`.
- **`GetMetadata<T>()` requires reference types**: `EndpointMetadataCollection.GetMetadata<T>()` has a `class` constraint. Endpoint metadata marker types must be records, not `[<Struct>]` types.
- **NuGet tool cache serves stale binaries**: When reinstalling local dotnet tools from `nupkg/`, clear the global cache first: `rm -rf ~/.nuget/packages/<tool-name>` before `dotnet tool install`. `dotnet clean` + `dotnet pack` alone don't invalidate the cache.
- **Parallel worktree agents cause OOM on 16GB machines**: Limit to 3 concurrent agents spawning `dotnet build`. Each build + NuGet restore can consume 2-4GB. Stagger builds or use `--no-restore` after the first successful build.

## Workflow Rules

### Git workflow
- **Keep master clean by working on a worktree within `.worktrees/`.** Every change — features, fixes, docs, config — follows: create worktree → work on branch → push → create PR → merge on approval. This keeps master free of in-progress commits so parallel branches can always use local master as a clean base.
- **PRs must include `Closes #XX`** in the body to auto-close related issues (when applicable).
- **Never merge without explicit approval.** Merging to master is a destructive op — always ask first.
- **Parallel worktree merge ordering.** When parallel worktrees touch the same file in different regions, merge the branch with the fewest changes to shared files first. The branch with the most shared-file changes merges last to minimize rebase churn.
- **Always use `isolation: "worktree"` for implementation/fix agents.** Non-isolated agents lack Bash/Read permissions even when pointed at accessible paths. For fix agents on existing branches, include `git fetch origin <branch> && git checkout <branch>` in the prompt. The agent's `EnterWorktree` tool handles worktree creation.
- **Worktree agent Bash commands must be standalone.** Compound commands (`cd /path && dotnet build`) don't match pre-approved `Bash(dotnet:*)` patterns. Instruct agents to run `cd /path/to/worktree` as a separate Bash call first (working directory persists), then run standalone commands (`dotnet build Frank.sln`). Also `mkdir -p nupkg` in each worktree before building.
- **Verify fix agent output before pushing.** Always build+test fix agent output in a clean worktree before pushing. Agent output may be partial due to rate limits or permission failures — never assume it's complete.
- **Verification sequence before PRs.** Before creating a PR, run the full verification sequence: build → test → fantomas → `/verification-before-completion` → `/simplify` → `/expert-review`. Address findings before opening the PR.
- **Section-by-section audit before closing.** When an issue has multiple sections (e.g., "Operational MPST" + "Wadler/dual"), verify every requirement in every section before closing. Don't close an issue because one section is done.
- **PRs must enumerate all issue requirements with status.** For each requirement in the linked issue, the PR body must state: implemented, blocked by #X, or deferred with rationale. "Closes #X" without per-requirement accounting is insufficient.
- **Never close issues with unfulfilled-dependency requirements.** If an issue has requirements that depend on open issues (#124, #125, etc.), leave the issue open. Either split it (done-now vs blocked) or add a comment listing what's blocked. The user decides when to close.

### Planning and communication
- **Always surface questions.** Never auto-answer planning/discovery questions from subagents. Present to user with recommendation.
- **Report autonomous decisions.** Maintain a running decisions table (Decision, Rationale, Impact) when making choices without explicit user confirmation.
- **Use reviewer-informed questions.** Draw on the expert panel perspectives when asking clarifying questions. "What do my experts recommend?" = consult the reviewer panel.

### Implementation
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

**Not recurring but important to remember:**
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
