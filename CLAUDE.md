# Frank

F# web framework proving that HATEOAS, statecharts, and semantic discovery compose into a pit of success for hypermedia APIs.

## Commands

```bash
# Build (DOTNET_SYSTEM_GLOBALIZATION_INVARIANT=1 required on nix-darwin due to ICU mismatch)
DOTNET_SYSTEM_GLOBALIZATION_INVARIANT=1 dotnet build Frank.sln

# Test (excludes sample apps)
DOTNET_SYSTEM_GLOBALIZATION_INVARIANT=1 dotnet test Frank.sln --filter "FullyQualifiedName!~Sample"

# Frank.Tests is NOT in Frank.sln — test separately
DOTNET_SYSTEM_GLOBALIZATION_INVARIANT=1 dotnet test test/Frank.Tests/

# Format check (Fantomas 7.0.5)
dotnet fantomas --check src/
```

Always run `dotnet build Frank.sln` and `dotnet test` before claiming work is complete.
**Sample E2E tests:** Check `find sample/ -name "test-e2e.sh"` and run when working on issues that affect sample behavior.

## Project Structure

- `src/` — Library projects, multi-target net8.0/net9.0/net10.0
- `test/` — net10.0 only; `Frank.Tests` NOT in `Frank.sln`
- `sample/` — Sample apps (Frank, Falco/Giraffe/Oxpecker variants, OpenApi, Datastar)
- `.claude/worktrees/` — Git worktrees for feature branches (gitignored)
- `hooks/` — Git hooks (Fantomas pre-commit, Entrie CLI)

Five shipping packages: `Frank`, `Frank.Auth`, `Frank.OpenApi`, `Frank.Datastar`, `Frank.Analyzers`.
v7.3.2 in-scope packages (`Frank.Semantic`, `Frank.Validation`, `Frank.LinkedData`, `Frank.Provenance`, `Frank.Discovery`, `Frank.Cli`) don't exist yet — created fresh per spec.

## Constitution (non-negotiable)

1. **Resource-Oriented Design.** Resources are the primary abstraction. `resource` CE is the central API. Hypermedia over static specs.
2. **Idiomatic F#.** CEs for config, DUs for choices, Option over null, pipeline-friendly, declarative over imperative.
3. **Library, Not Framework.** No view engine, ORM, or auth system. Compose with ASP.NET Core.
4. **ASP.NET Core Native.** Expose `HttpContext` directly. Don't hide the platform.
5. **Performance Parity.** No runtime overhead vs raw ASP.NET Core routing. Avoid allocations in hot paths.
6. **Resource Disposal Discipline.** All `IDisposable` values MUST use `use` bindings.
7. **No Silent Exception Swallowing.** Middleware MUST log via `ILogger`. No bare `with _ ->`.
8. **No Duplicated Logic.** Same function in 2+ modules → extract before merge.

## Code Discipline

### Holzmann Rules (9–15)

AI-generated code routinely violates these. Push back when it does.

9. **Keep It Linear.** Max two nesting levels. Flatten with early returns, pipelines, or extracted functions.
10. **Bound Every Loop.** Every loop, retry, poll, recursion needs an explicit cap with defined cap-hit behavior.
11. **One Function, One Job.** Describable without "and." Hard limit: 60 lines.
12. **State Your Assumptions.** Preconditions in code (`invalidArg`/`invalidOp`/`assert`/`failwith`) — not comments.
13. **Narrow Your State.** No module-level `mutable`. Pass dependencies explicitly.
14. **Surface Your Side Effects.** I/O and mutations obvious at the call site. Separate pure from effectful.
15. **One Layer of Indirection.** Max one layer of dynamic dispatch. Linear composition over decoded elegance.

Run `/discipline` to grade changed code against these rules.

### Karpathy Guidelines

- **Think before coding.** State assumptions explicitly. Surface tradeoffs. If multiple interpretations exist, ask — don't pick silently.
- **Simplicity first.** Minimum code that solves the problem. No speculative features, abstractions, or error handling for impossible scenarios.
- **Surgical changes.** Touch only what the request requires. Don't improve adjacent code or formatting. Mention unrelated dead code — don't delete it.
- **Goal-driven execution.** Define verifiable success criteria before starting. Multi-step tasks: state the plan with per-step verification checks.

Run `/karpathy-guidelines` to review code against these before sending.

## F# Patterns

Sub-directory CLAUDE.md files load when working in that area:
- `src/CLAUDE.md` — ASP.NET Core gotchas, MSBuild/tooling patterns
- `test/CLAUDE.md` — Expecto + TestHost, task CE gotchas
- `sample/CLAUDE.md` — sample server lifecycle, Datastar env vars

### Repo-wide patterns

- **CE builders**: `ResourceBuilder`, `WebHostBuilder` — the CE ceremony IS the pit of success. Never suggest simplifying it.
- **Extensions**: `[<AutoOpen>] module` + `type X with [<CustomOperation>]`.
- **fsproj compile order**: Types defined before use. Add new files in dependency order.
- **Integer division in thresholds**: Use `overlap * 2 >= total` not `count / 2` (truncation makes thresholds too permissive).
- **`_.Member` shorthand with `Set.ofList`**: Use explicit lambdas — shorthand breaks type inference.
- **Parallel worktree agents**: Limit to 3 concurrent `dotnet build`; each consumes 2-4GB on 16GB machines.
- **Precondition assertions**: `invalidArg` for args, `invalidOp` for state, `failwith` for logic. Check at module boundaries.
- **`Result` over `Option` for diagnostics**: Return `Result<'T, string>` when `None` would discard useful error context.
- **`Option.orElse` for merge patterns**: `base.Field |> Option.orElse enriching.Field`; use `Option.orElseWith` for lazy fallbacks.
- **FS3511 in Release builds**: `task { }` with complex match fails in CI (Release). Fix: extract pure logic into private static members.

## Workflow Rules

### Git workflow

Trunk-based (sole maintainer). PRs fallback for external contributors.

- Feature work in `.claude/worktrees/<name>`. Merge `--ff-only` into master, then push.
- `git config core.hooksPath hooks` required for hooks to fire.
- Normal `git push origin master` OK. Destructive pushes (force without lease, branch deletion, history rewrite) require approval.
- **Merge sequence**: build → test → fantomas → `/verification-before-completion` → `/simplify` → `/expert-review` → ff-merge → push.
- Always `isolation: "worktree"` for implementation agents.
- **Worktree Bash cwd resets to the main repo (master) between calls.** A bare `cd` does NOT persist to the next Bash call — it silently returns to the main checkout. Use ABSOLUTE worktree paths for every command (reads, greps, `dotnet test <abs-path>`), or `cd <abs>` as the first statement *and* verify `git branch --show-current` before trusting output. Prefer the Read tool (absolute path) over `cat`. If a file looks wrong (unexpected type shape / test count), suspect cwd contamination first and re-check with an absolute path before concluding the work is wrong.
- Never push without verifying agent output — may be partial. **Re-run test suites yourself (absolute paths); don't trust agent-reported counts or pasted artifacts** — agents miscount and paste from cwd-contaminated reads. Verify quantitative claims by running the code (e.g. `dotnet fsi` to compute real values), not by reading the report.

External contributors: fill PR template fully with `Closes #XX`. See `.github/PULL_REQUEST_TEMPLATE.md`.

**Issue discipline:**
- Audit every section before closing. Never close with unfulfilled requirements.
- Never create issues, PRs, or take external actions while discussing — wait for explicit go-ahead.

### Project board

`gh project item-list 1 --owner frank-fs --format json` — canonical "what's in flight."
- Update Status: `In Progress` when starting, `Blocked` (+ native dependency) when blocked.
- Sub-issues via `gh api graphql mutation addSubIssue` — no `- [ ] #NNN` in umbrella bodies.
- Track derived from title prefix: `[A*]/[B*]/[C*]/[V*]` → corresponding Track value.

### Planning and communication

- **Always surface questions.** Never auto-answer subagent planning questions. Present to user with recommendation.
- **Thesis-first ACs.** Requirements must be falsifiable HTTP request/response pairs. Template: Thesis → Problem → Definition → Solution → Acceptance Tests → Sources.
- **Never triage expert findings.** All findings potentially blocking until user decides.

### Implementation

- Never hide AST round-trip lossiness — report gaps, don't silently drop data.
- Never dismiss problems as "pre-existing" — surface, investigate, file.
- **Portable concept filter.** "Is this portable or F#-specific?" Portable concepts get full investment.
- No lightweight API (`frank.get "/path"`). CE is the design. On-ramp = docs/examples.
- Prefer skills over commands (`.claude/skills/` vs `.claude/commands/`).
- MPST transitions are projected per-role, not flat FSM. See v7.4.0 Track A spec.

### Strategy (Phase 1)

Prove thesis: naive-client demo, generated-artifact reference app, blog series. No premature optimization (Phase 2) or portability abstractions (Phase 3).

### v7.4.0 context

- v7.3.0 failed: hierarchy flattened to FSM, Link headers never appeared, resource model incomplete. All CLAUDE.md rules exist because of this.
- Three tracks in [AGENT_HYPOTHESIS.md](docs/AGENT_HYPOTHESIS.md): A (REST agent via links), B (Datastar/SSE), C (multi-role SSE).
- Design source: `docs/superpowers/specs/2026-04-2*-*.md`.
- The resource model is the instruction set; interpreters are predefined library code.

## Recurring Skills

| Skill | When | What |
|-------|------|------|
| `/context-dump` | Session start / "where are we?" | GitHub issues, PRs, milestones briefing |
| `/retrospective` | End of session or major task | Mines for CLAUDE.md rules, skill candidates |
| `/simplify` | Post-commit, pre-PR | Parallel review for reuse/quality/efficiency |
| `/discipline` | After writing code, before commit | Holzmann rules check with letter grade |
| `/karpathy-guidelines` | Before sending code | Simplicity/surgical/goal-driven check |
| `/techdebt` | Weekly | Tech debt scan + priority categorization |
| `/expert-review` | Before merging master or PR review | 2-4 expert agents, multi-perspective |

`superpowers:brainstorming` before design work; `superpowers:systematic-debugging` before bug fixes.

## Expert Panel

11 reviewers in `~/.claude/agents/expert-*.md`. Dispatch 2-4 via `/expert-review`.

| Tier | Experts | Focus |
|------|---------|-------|
| 1 (highest gap) | Tim Berners-Lee, Fielding, Darrel Miller | Linked Data / dereferenceable IRIs, HATEOAS, API discovery, HTTP standards |
| 2 (active) | Fowler, @7sharp9 | ASP.NET Core, F# performance |
| 3 (long-term) | Harel, Seemann, Don Syme | Statecharts, purity, F# API design |
| 4 (domain) | Amundsen, Wlaschin, Claude-agent | ALPS, F# DX, agentic consumption |

Tim Berners-Lee is a near-default — include his Linked Data lens in nearly every review unless the change is purely non-data (e.g. build tooling). Otherwise dispatch 2-4 per review based on change type; don't dispatch all 11.
