---
name: expert-review
description: Use when completing a feature, before creating a PR, or when the user asks "what do my experts think?" Dispatches the expert panel as parallel subagents for structured code review from multiple perspectives.
---

# Expert Review

Dispatch expert panel agents in parallel for multi-perspective code review.

## Expert roster

| Agent | Perspective | Tier | When to dispatch |
|-------|-------------|------|-----------------|
| `expert-fielding` | REST, HATEOAS, HTTP semantics | 1 | Always for HTTP/middleware changes |
| `expert-darrel-miller` | API discovery, ALPS, JSON Home, standards | 1 | Always for discovery/profile changes |
| `expert-fowler` | ASP.NET Core, DI, middleware, perf | 2 | Implementation/middleware changes |
| `expert-7sharp9` | F# idioms, allocation, perf | 2 | Implementation changes |
| `expert-seemann` | Purity boundaries, functional architecture | 3 | Architecture changes, new modules |
| `expert-harel` | Statechart formalism, runtime correctness | 3 | Statechart changes only |
| `expert-don-syme` | F# API design, naming, type design | 3 | Public API changes |
| `expert-amundsen` | ALPS profiles, affordance design | 4 | ALPS/profile changes only |
| `expert-wlaschin` | F# DX, explainability, domain modeling | 4 | API surface, DX changes |
| `expert-claude-agent` | Agentic consumption, demo readiness | 4 | Feature additions, scope changes |

## Process

### 1. Determine scope

```bash
git diff --stat <base>..<head>
```

Categorize the change:
- **HTTP/middleware** → Fielding + Miller + Fowler + @7sharp9
- **Discovery/profiles** → Miller + Fielding + Amundsen
- **Statechart** → Harel + Seemann + @7sharp9
- **New public API** → Syme + Wlaschin + Fowler
- **Architecture/new module** → Seemann + Syme + Claude-agent
- **Full feature** → Fielding + Miller + Fowler + @7sharp9 (minimum 4)

Pick 2-4 experts. Don't dispatch all 10 — focused reviews are better than diluted ones.

### 2. Dispatch in parallel

Launch selected experts as parallel subagents. Each gets:
- The git diff range
- The spec/plan (if applicable)
- Instructions to read changed files and output structured findings

### 3. Aggregate findings

Collect all `[EXPERT-{severity}]` findings. Deduplicate (multiple experts may flag the same issue).

**Do NOT triage by severity.** The user decides severity. Instead, assess each finding by **probability of successfully resolving in the current session** (0-100%) with reasoning.

### 4. Present with probability

Present all findings in a single table:

```
| Finding | Fix probability | Reasoning |
|---------|----------------|-----------|
| [FIELDING] src/Foo.fs:42 — Response missing Allow header | 95% | One-line fix, clear spec |
| [FOWLER] src/Bar.fs:15 — Singleton service resolved per-request | 80% | Straightforward DI change |
| [SEEMANN] src/Baz.fs:8 — Pure function extraction | 30% | Needs design discussion |
```

Let the user decide what to fix. Do not sort into "fix now" vs "follow-up" or recommend severity tiers.

If experts disagree, use the priority tiers to break ties (Fielding > Fowler > Harel).
