# Frank Design Documents

## Frank's Design Philosophy

Frank is a lightweight F# library for building hypermedia web applications on ASP.NET Core. Its core principles:

- **Resource-oriented** — HTTP resources are first-class citizens, defined as computation expressions with typed route patterns, method handlers, and metadata
- **Library, not framework** — Frank wraps ASP.NET Core; it doesn't replace it. Authentication, routing, DI, and middleware are ASP.NET Core's responsibility. Frank adds the resource abstraction layer on top
- **Idiomatic F#** — computation expressions, discriminated unions, pattern matching, and pure functions as the primary modeling tools
- **Opt-in extensibility** — each extension (`Frank.Auth`, `Frank.OpenApi`, `Frank.Statecharts`, `Frank.Affordances`, `Frank.LinkedData`, `Frank.Datastar`, `Frank.Discovery`) is a separate package that adds capability without requiring the others

## Where Frank Is Heading

Frank's extensions are converging toward a broader goal: **applications that are legible to both humans and machines**. This is driven by three tracks of work:

### Stateful Hypermedia

[Frank.Statecharts](STATECHARTS.md) makes resources whose HTTP surface changes based on domain state — available methods, status codes, and representations all depend on where the resource is in its lifecycle. This is HATEOAS enforced at the framework level, not by convention.

### Semantic Self-Description

The [Semantic Resources](SEMANTIC-RESOURCES.md) initiative (tracked in [#80](https://github.com/frank-fs/frank/issues/80)) makes applications serve machine-interpretable models of themselves — RDF graphs, ALPS profiles, SHACL constraints, provenance annotations. An agent that speaks HTTP can discover what the application does, what constraints it enforces, and how its state has changed over time.

### Design-First Development

The [Spec Pipeline](SPEC-PIPELINE.md) (tracked in [#57](https://github.com/frank-fs/frank/issues/57)) enables a bidirectional workflow: start from design documents (WSD, SCXML, ALPS), generate implementations, then extract specs from running applications to verify and refine the design. The pipeline creates a continuous feedback loop between design intent and running code.

Together, these tracks make Frank applications into **reflective artifacts** — systems that participate in their own evolution by exposing enough structure for agents to understand, evaluate, and propose changes.

## Directory Guide

| Path | Contents |
|------|----------|
| `superpowers/specs/` | All feature specifications — from Spec Kit (001-016), Kitty Specs (ks-*), GitHub issues (gh-*), and Superpowers (date-prefixed) |
| `superpowers/plans/` | All implementation plans — paired with specs |
| [DECISIONS.md](DECISIONS.md) | Complete decision catalog (~400 decisions organized by era) |
| [AUDIT.md](AUDIT.md) | Contradictions, evolution timeline, suspect findings, dropped designs, and [reset plan](AUDIT.md#reset-plan-types-first-inside-out) with per-component salvageability analysis |

## Design Documents

| Document | Description |
|----------|-------------|
| [SEMANTIC-RESOURCES.md](SEMANTIC-RESOURCES.md) | Agent-legible applications: the vision for self-describing apps, the reflection/refinement feedback loop, and how the semantic layer connects to the spec pipeline |
| [SPEC-PIPELINE.md](SPEC-PIPELINE.md) | Bidirectional design spec pipeline: WSD + SCXML + ALPS as the core trio, verification loop, LLM-assisted codegen philosophy, and CLI integration |
| [STATECHARTS.md](STATECHARTS.md) | Application-level state machines: the `statefulResource` CE, guards, transitions, hierarchical statecharts, and the `IStateMachineStore` abstraction |
| [COMPARISON.md](COMPARISON.md) | How Frank.Statecharts differs from Webmachine and Freya — application-level vs. protocol-level state machines |
| [AGENT_HYPOTHESIS.md](AGENT_HYPOTHESIS.md) | Feature combination probability assessment for agentic API consumption |
| [MULTI-PARTY_SESSIONS.md](MULTI-PARTY_SESSIONS.md) | Runtime enforcement of multi-party protocol discipline |
| [REFERENCES.md](REFERENCES.md) | Cited works organized by category |
| [VIEW_ENGINE_COMPARISON.md](VIEW_ENGINE_COMPARISON.md) | Comparison of HTML rendering approaches for Frank.Datastar |

## Spec Naming Conventions

| Prefix | Source | Era |
|--------|--------|-----|
| `001-016` | Spec Kit | v7.0–v7.2 (sound, released) |
| `ks-001` through `ks-033` | Kitty Specs | v7.3.0 (sound subset migrated; suspect specs archived separately) |
| `gh-NNN` | GitHub issues | v7.4.0 design specs extracted from issue bodies |
| `YYYY-MM-DD-*` | Superpowers | v7.3.1+ design documents |
