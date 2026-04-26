# Frank Design Documents

## Frank's Design Philosophy

Frank is a lightweight F# library for building hypermedia web applications on ASP.NET Core. Its core principles:

- **Resource-oriented** — HTTP resources are first-class citizens, defined as computation expressions with typed route patterns, method handlers, and metadata.
- **Library, not framework** — Frank wraps ASP.NET Core; it doesn't replace it. Authentication, routing, DI, and middleware are ASP.NET Core's responsibility. Frank adds the resource abstraction layer on top.
- **Idiomatic F#** — computation expressions, discriminated unions, pattern matching, and pure functions as the primary modeling tools.
- **Opt-in extensibility** — each extension is a separate package that adds capability without requiring the others.

## Where Frank Is Heading

Frank's extensions are converging toward a broader goal: **applications that are legible to both humans and machines**. The architectural source of truth for the next two minor releases lives in `superpowers/specs/`:

| Spec | Scope |
|------|-------|
| [2026-04-20 — v7.3.2 Semantic Discovery](superpowers/specs/2026-04-20-v732-semantic-discovery-design.md) | The semantic / discovery surface for v7.3.2 (Track B). |
| [2026-04-21 — v7.4.0 Protocol Types](superpowers/specs/2026-04-21-v740-protocol-types-design.md) | Protocol types and MPST projection for v7.4.0 (Track A). |
| [2026-04-23 — v7.4.0 Resource-Oriented Hypermedia](superpowers/specs/2026-04-23-v740-resource-oriented-hypermedia-design.md) | Resource model, affordances, and HTTP surface for v7.4.0 (Track C). |

Everything else in this directory either supports those specs (theory, reference material) or is preserved as the as-built record of the five v7.3.2 shipping packages.

## Shipping Packages (v7.3.2)

- `Frank` — Core CE builders (`resource`, `webHost`), ETag/conditional-request middleware, content negotiation.
- `Frank.Auth` — Resource-level authorization extensions.
- `Frank.OpenApi` — OpenAPI document generation with F# type schemas.
- `Frank.Datastar` — Datastar SSE integration.
- `Frank.Analyzers` — F# Analyzers for compile-time error detection.

Planned for v7.3.2 / v7.4.0 (do not exist in source yet — will be created per the specs above): `Frank.Semantic`, `Frank.Validation`, `Frank.LinkedData`, `Frank.Provenance`, `Frank.Discovery`, `Frank.Cli`.

## Top-level Documents

| Document | Description |
|----------|-------------|
| [AGENT_HYPOTHESIS.md](AGENT_HYPOTHESIS.md) | Thesis: feature-combination probability assessment for agentic API consumption; runtime-agent vs. authoring-agent roles. |
| [AUTHORING_WORKFLOW.md](AUTHORING_WORKFLOW.md) | The author + agent workflow loop, derived from the three architectural specs. |
| [SEMANTIC-RESOURCES.md](SEMANTIC-RESOURCES.md) | Vision: agent-legible applications via RDF/ALPS/SHACL/PROV-O and the reflection/refinement feedback loop. |
| [SPEC-PIPELINE.md](SPEC-PIPELINE.md) | Bidirectional design-spec pipeline: WSD/SCXML/ALPS as input/output, cross-validation, codegen philosophy. |
| [STATECHARTS.md](STATECHARTS.md) | User-facing introduction to stateful resources and statecharts. |
| [MULTI-PARTY_SESSIONS.md](MULTI-PARTY_SESSIONS.md) | Runtime enforcement of multi-party protocol discipline; mapping MPST onto Frank's layers. |
| [COMPARISON.md](COMPARISON.md) | Frank's stateful-resource layer vs. Webmachine and Freya — application-level vs. protocol-level state machines. |
| [VIEW_ENGINE_COMPARISON.md](VIEW_ENGINE_COMPARISON.md) | HTML rendering options for Frank.Datastar (F# strings, Oxpecker.ViewEngine, Hox). |
| [REFERENCES.md](REFERENCES.md) | Bibliography. |

## Directory Guide

| Path | Contents |
|------|----------|
| `superpowers/specs/2026-04-*-design.md` | The three architectural specs that drive v7.3.2 and v7.4.0 work. |
| `superpowers/specs/shipped/` | As-built design specs for the five v7.3.2 shipping packages (001–016). |
| `superpowers/plans/shipped/` | Implementation plans paired with `specs/shipped/`. |

History — earlier design eras (v7.3.0 Kitty Specs, v7.3.1 GitHub-issue specs, the consolidated `DECISIONS.md` / `AUDIT.md` / `STATECHARTS_ARCHITECTURE_DECISIONS.md` documents, and superseded date-prefixed specs) — is preserved in git history. Recover any of those with `git log -- <path>` followed by `git show <sha>:<path>`.
