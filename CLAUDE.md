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

**Sample E2E tests:** Sample apps may have `test-e2e.sh` scripts that serve as acceptance tests against library code. Check for these (`find sample/ -name "test-e2e.sh"`) and run them when working on issues that affect sample behavior. These scripts start the sample, make HTTP requests, and verify responses — they test the library through the sample, not the sample itself.

## Project Structure

- `src/` — Library projects, multi-target net8.0/net9.0/net10.0
- `test/` — Test projects, target net10.0 only
- `sample/` — Sample apps (TicTacToe, Datastar variants)
- `.claude/worktrees/` — Git worktrees for feature branches (gitignored)
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

## Code Discipline (Holzmann Rules)

Adapted from Gerard Holzmann's "Power of Ten" (NASA/JPL). These apply to all code — library, test, and sample. AI-generated code violates them routinely. Rules that overlap with F#'s inherent strengths are noted; they remain here because they apply when generating code in any language and because AI can still violate them even in F#.

9. **Keep It Linear.** Max two levels of nesting. No control flow requiring a diagram. Flatten with early returns, pipeline operators, or extracted functions. F#'s pipeline style and pattern matching naturally enforce this — but AI still generates nested `match`-in-`match`-in-`if`. Push back when it does.
10. **Bound Every Loop.** Every loop, retry, poll, and recursive call needs an explicit maximum. No "practically never exceeds N." What happens when the cap is hit must be defined. AI-generated retry logic and recursive crawlers routinely lack caps.
11. **One Function, One Job.** Describable in one sentence without "and." Hard limit: 60 lines. Long functions mean the decomposition hasn't happened yet. (Extends #8.)
12. **State Your Assumptions.** Preconditions and invariants belong in code — `invalidArg`, `invalidOp`, `assert`, or `failwith` — not in comments. At least one precondition check per public function at system boundaries. F#'s Hindley-Milner type inference already encodes many assumptions at compile time, which is a genuine advantage — runtime assertions cover what the type system cannot (value ranges, non-empty collections, valid state transitions).
13. **Narrow Your State.** No module-level `mutable`. Pass dependencies explicitly. Data lives as close to its use as possible. F# defaults to immutable bindings, which helps — but AI still reaches for class-level fields and module-level state. (Extends #2.)
14. **Surface Your Side Effects.** I/O, mutations, and network calls must be obvious at the call site. Don't bury writes inside helpers with innocent names. Structural separation: pure computation functions vs side-effectful orchestration. (Extends #2.)
15. **One Layer of Indirection.** Minimize abstraction depth. If tracing a call requires navigating more than one layer of dynamic dispatch or callback, simplify. Favour linear composition over decoded elegance. (Extends #4.)

## F# Patterns

Cross-cutting F# patterns that apply repo-wide. **Subdirectory-specific gotchas live in nested CLAUDE.md files** — they load only when working in that area:

- `src/Frank.Statecharts/CLAUDE.md` — round-trip lossiness, `CompositeKind` shadowing, smcat collection, `Result` over `Option` for diagnostics, `writePresent`/`writeOptional`, FS3511 in Release, `Option.orElse` merge, JSON escaping, `evaluateEventGuards`, MPST projection.
- `src/Frank.Discovery/CLAUDE.md` — RFC 8288 URI Link headers + `HasTemplateLinks`, `StringValues` on `Headers.Append`, `TemplateMatcher` thread-safety, `GetMetadata<T>` `class` constraint, `Response.OnStarting`, `AddSingleton` vs `TryAddSingleton`, `Allow` content header, CE delegation, `useX`/`useXWith`.
- `src/Frank.Cli.MSBuild/CLAUDE.md` — `.props` vs `.targets` evaluation timing, `dotnet fsi` resource verification, NuGet tool cache stale binaries.
- `test/CLAUDE.md` — Expecto + TestHost, `let!` type annotations, `ResourceEndpointDataSource` internal, manual `WebHostSpec` construction, `use` vs `let` in task CEs, reflection-based DU sync tests, handler overload disambiguation, `IWebHostBuilder.Configure` extension.
- `sample/CLAUDE.md` — sample server lifecycle and Datastar test environment variables.

Repo-wide:

- **CE builders**: `ResourceBuilder`, `WebHostBuilder` — the CE ceremony IS the pit of success. Never suggest simplifying it.
- **Extensions**: `[<AutoOpen>] module` + `type X with [<CustomOperation>]`.
- **fsproj compile order matters**: Types must be defined before use. Add new files in dependency order.
- **Integer division in threshold checks**: `count / 2` truncates (3/2=1), making thresholds more permissive than intended. Use multiplication instead: `overlap * 2 >= total` gives honest "at least half" semantics without rounding issues.
- **`_.Member` shorthand can break type inference with `Set.ofList`**: `List.map _.AbsoluteUri |> Set.ofList` may fail with "Uri does not support comparison" because the compiler doesn't resolve the intermediate `string` type. Use explicit lambdas: `List.map (fun (u: Uri) -> u.AbsoluteUri) |> Set.ofList`.
- **Parallel worktree agents cause OOM on 16GB machines**: Limit to 3 concurrent agents spawning `dotnet build`. Each build + NuGet restore can consume 2-4GB. Stagger builds or use `--no-restore` after the first successful build.
- **Precondition assertions**: Use `invalidArg` for argument validation, `invalidOp` for state violations, `failwith` for logic errors. At module boundaries, check before proceeding — don't rely on downstream code to fail with a cryptic message. Types encode what they can; assertions cover value ranges, non-empty collections, and valid state transitions.

## Workflow Rules

### Git workflow

Frank operates in two modes depending on contributor type. Default is trunk-based; PRs are a fallback.

**Default — trunk-based (sole maintainer).** Used when working in this session as the maintainer.

- **Worktrees → fast-forward merge into master → push.** Feature work happens in `.claude/worktrees/<name>` on a feature branch. Merge fast-forward only into local master, then push. Small targeted changes (config, docs, single-file fixes, rule updates) go straight to master in a worktree.
- **Setup required:** `git config core.hooksPath hooks` so pre-commit, pre-push, post-merge, and other hooks fire. Without this, the protections below are inactive.
- **Master is protected.** Two layers:
  - **Server (`.github/rulesets/master-protection.json`):** blocks deletion, requires linear history, requires "Build and pack" CI status check to pass on the SHA before push lands.
  - **Local (`hooks/pre-push`):** blocks deletes and non-fast-forward pushes whose local tracking ref is stale (replicates `--force-with-lease` semantics). Feature branches unrestricted.
- **Push policy is graduated.** Normal `git push origin master` does not require user approval. Destructive pushes (`--force` without lease, branch deletion, history rewrite from stale state) DO require approval. Background/scheduled-agent pushes always require approval. Escape hatch: `FRANK_ALLOW_DESTRUCTIVE_PUSH=1 git push ...`.
- **Standard merge sequence:**
  1. Push feature branch (CI runs on `**` per `ci.yml`)
  2. Wait for "Build and pack" status to pass on the feature SHA
  3. `git fetch origin && git merge --ff-only <feature-branch>` (in main worktree on master)
  4. `git push origin master` (server sees passing status, allows)
- **Parallel worktree merge ordering.** When parallel worktrees touch the same file in different regions, merge the branch with the fewest changes to shared files first.
- **Always use `isolation: "worktree"` for implementation/fix agents.** Non-isolated agents lack Bash/Read permissions even when pointed at accessible paths. For fix agents on existing branches, include `git fetch origin <branch> && git checkout <branch>` in the prompt.
- **Worktree agent Bash commands must be standalone.** Compound commands (`cd /path && dotnet build`) don't match pre-approved `Bash(dotnet:*)` patterns. Instruct agents to run `cd /path/to/worktree` as a separate Bash call first (working directory persists), then run standalone commands. Also `mkdir -p nupkg` in each worktree before building.
- **Verify fix agent output before merging.** Always build+test fix agent output in a clean worktree before fast-forwarding master. Agent output may be partial due to rate limits or permission failures — never assume it's complete.
- **Verification sequence before merging master.** Before fast-forwarding into local master: build → test → fantomas → `/verification-before-completion` → `/simplify` → `/expert-review`. Address findings before merging. The post-merge hook re-runs build as a backstop.

**Fallback — PR mode (external contributors, occasional release PRs).** Used when an external contributor opens a PR or when the maintainer explicitly opts into PR review. Authoritative rules live in `.github/PULL_REQUEST_TEMPLATE.md` and `CONTRIBUTING.md`. In summary:

- Fill out the PR template fully — Summary, Requirements table (per-requirement status: implemented / deferred / blocked), Test evidence, Reviewer checklist.
- Include `Closes #XX` in the body to auto-close related issues.
- Run the full verification sequence before requesting review.

**Issue and external-action discipline (both modes):**

- **Section-by-section audit before closing issues.** When an issue has multiple sections (e.g., "Operational MPST" + "Wadler/dual"), verify every requirement in every section before closing.
- **Never close issues with unfulfilled-dependency requirements.** If an issue depends on open issues, leave it open. Either split it (done-now vs blocked) or add a comment listing what's blocked. The user decides when to close.
- **Never create issues, PRs, or take external actions while discussing.** Wait for explicit go-ahead ("yes", "create it", "go ahead"). Presenting a draft is not permission to act on it. Applies to issues, comments, releases, ruleset changes, anything visible to others.

### Planning and communication
- **Always surface questions.** Never auto-answer planning/discovery questions from subagents. Present to user with recommendation.
- **Report autonomous decisions.** Maintain a running decisions table (Decision, Rationale, Impact) when making choices without explicit user confirmation.
- **Use reviewer-informed questions.** Draw on the expert panel perspectives when asking clarifying questions. "What do my experts recommend?" = consult the reviewer panel.
- **Thesis-first acceptance criteria.** Issue requirements must be falsifiable HTTP request/response pairs that test the thesis, not file:line implementation instructions. Use the template: Thesis → Problem → Definition → Solution → Acceptance Tests → Sources. Include negative tests where possible (remove the crutch, prove the real mechanism works). A test is falsifiable if a wrong implementation can produce a failing result — if the correct output can be produced without the correct mechanism, the test is not falsifiable enough.
- **Never triage expert findings without consent.** When presenting expert review results, all findings are potentially blocking until the user says otherwise. Do not sort into "fix now" vs "follow-up" or "framework bug" vs "sample issue" — present them and let the user decide.

### Implementation
- **Never hide AST round-trip lossiness.** If a format parser encounters a field that has no home in `TransitionEdge`/`StateNode`/`StatechartDocument`, that is a gap in the AST — report it, don't silently drop data. Format-specific visual styling (smcat colors) goes in annotations. Semantic information (roles, payload types, composite kinds) must be first-class AST fields. Round-trip tests (parse → AST → generate same format) are required for every format; any information loss is a failing test.
- **Never blame pre-existing issues.** Surface, investigate, and file issues. Never dismiss problems as "not my change."
- **Portable concept filter.** When scoping work, ask: "Is this a portable concept or an F#-specific detail?" Portable concepts (statechart semantics, ALPS discovery, affordance projection) get full investment. F#-specific details (CE syntax, DU encoding, .NET middleware) are implemented but not over-invested.
- **No lightweight API.** Never suggest a simplified `frank.get "/path" handler` alternative. The CE is the design. On-ramp is solved by docs/examples.
- **Prefer skills over commands.** Use `.claude/skills/` (portable across repos via plugins) rather than `.claude/commands/` (repo-local only). Skills support YAML frontmatter, model selection, and isolation modes.
- **MPST transitions are projected, not flat FSM.** Transition declarations in the CE reflect per-role agency from the MPST projection, not the flat FSM's transition function. A role only has transitions from states where it is the active participant. Example: `CancelOrder` from Authorize exists in the FSM but is NOT declared for Customer because Authorize is a PaymentService state. The flat FSM is the implementation; the projections are the protocol contract.

### Strategy (Phase 1: demonstrate the thesis)
- Prove the ideas work: naive-client demo, generated-artifact reference app, blog series
- Don't optimize prematurely (that's Phase 2)
- Don't abstract for portability prematurely (that's Phase 3)
- The "is this portable?" filter avoids dead ends but doesn't drive new work

### v7.4.0 context
- **v7.4.0 completes what v7.3.0 was supposed to deliver.** Everything in v7.4.0 scope was originally intended for v7.3.0 but was silently deferred by subagents who made autonomous scope decisions. The v7.3.0 implementation had extensive passing tests but failed when integrated — the hierarchy was flattened to a flat FSM, Link headers never appeared in HTTP responses, and the resource model was missing intended pieces.
- **All CLAUDE.md rules, verification workflows, and acceptance criteria methodology exist because of v7.3.0's failure.** Falsifiable HTTP request/response ACs, e2e test requirements, expert panel review, and the "never auto-answer subagent questions" rule all came from this experience.
- **Three thesis proof tracks** documented in [AGENT_HYPOTHESIS.md](docs/AGENT_HYPOTHESIS.md): Track A (REST agent navigates via links), Track B (reactive streaming agent via Datastar/SSE with deferred events), Track C (concurrent multi-role agents with role-projected SSE).
- **Design decisions** consolidated in [DECISIONS.md](docs/DECISIONS.md) (~400 decisions by era). Contradictions and suspect findings in [AUDIT.md](docs/AUDIT.md).
- **The resource model is the instruction set.** Both CE-first and SCXML-first paths produce the same structured instructions. Interpreters (runtime, validation, collection, etc.) are predefined library code, not generated.

## Recurring Skills

Run these at the suggested cadence to maintain quality and capture learning.

| Skill | When to run | What it does |
|-------|-------------|--------------|
| `/context-dump` | Start of session, after a break, or "where are we?" | Aggregates GitHub issues, PRs, milestones, recent activity into a briefing |
| `/retrospective` | End of every session or major task | Mines session for CLAUDE.md rules, skill candidates, agent candidates |
| `/simplify` | After completing a feature (post-commit, pre-PR) | Parallel agents review changed code for reuse, quality, efficiency |
| `/techdebt` | Weekly or before cleanup sprints | Scans code + GitHub for tech debt, categorizes by priority, proposes fixes |
| `/expert-review` | Before merging master, before requesting PR review, or "what do my experts think?" | Dispatches 2-4 expert agents in parallel for multi-perspective review |

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
