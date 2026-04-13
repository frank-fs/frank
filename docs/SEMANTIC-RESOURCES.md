# Semantic Resources: Agent-Legible Applications

Frank's semantic resource layer makes applications self-describing — not just "here are my routes" but a machine-interpretable model of what the application is, what it does, how it's structured, and how its state changes over time. An agent (LLM or otherwise) with access to this model has enough context to use the application, evaluate it, and propose changes to it.

## The Core Idea

A Frank application augmented with semantic metadata becomes a **reflective artifact** — one that participates in its own evolution. The application doesn't just serve data; it serves a queryable description of itself that enables a feedback loop: build → deploy → reflect → refine → rebuild.

This is inspired by a proven pattern: extraction of structured data into an RDF graph, followed by agent-driven review and refinement of that graph, producing improved outputs. Applied to web applications, the same pattern means an application's data, metadata, and structure can be queried and used to reflect over current behavior and help refine future iterations.

## What "Agent-Legible" Means

An agent interacting with a Frank application over standard HTTP can:

1. **Discover capabilities** — `OPTIONS /` returns supported media types; `Link` headers on any response point to the discovery endpoint
2. **Retrieve the semantic model** — content negotiation on `GET /` yields RDF representations (`application/ld+json`, `text/turtle`, `application/rdf+xml`) describing the full resource graph
3. **Inspect affordances** — `Accept: application/alps+json` on `GET /` returns the ALPS profile: semantic descriptors, transition types (`safe`/`unsafe`/`idempotent`), return types, and parameter schemas
4. **Inspect statecharts** — `Accept: application/scxml+xml` or `Accept: application/vnd.xstate+json` returns the state machine definition for stateful resources
5. **Query the graph** — the RDF model is structured for use with external SPARQL tools or any graph query mechanism

No custom agent protocol is required. The interface is HTTP with content negotiation — durable standards that predate and will outlast any specific agent framework.

## The Feedback Loop

The semantic layer enables a cycle that operates at two levels:

### Application-Level Reflection

```
Build Frank app
    → App serves semantic model (RDF, ALPS, SCXML)
    → Agent queries model, identifies gaps
        (e.g., "resource X accepts POST but has no validation constraints")
    → Agent proposes refinements
    → Developer (or agent) implements changes
    → Updated app serves updated model
    → Cycle continues
```

### Design-Level Verification

When combined with the [spec pipeline](SPEC-PIPELINE.md), the feedback loop extends to design artifacts:

```
Design spec (WSD, SCXML, ALPS)
    → LLM-assisted implementation → Running Frank app
    → Extract spec from running app
    → Compare extracted spec against original design
    → Identify drift, refine design or implementation
    → Cycle continues
```

The extracted specs from a running application become verification artifacts for the design intent. See [SPEC-PIPELINE.md](SPEC-PIPELINE.md) for details on the bidirectional pipeline.

## Architecture

The semantic layer comprises four extensions built in dependency order:

```
Frank.LinkedData (#75)  ──────┬──────────────────┐
  RDF model, content neg,     │                   │
  OPTIONS + Link discovery    │                   │
                              │                   │
Frank.Statecharts (#87)  ─────┤                   │
  Runtime state machines      │                   │
  (see STATECHARTS.md)        │                   │
                              │                   │
          Frank.Validation (#76)                  │
            SHACL constraints                     │
                              │                   │
          Frank.Provenance (#77) ─────────────────┤
            PROV-O state change annotations       │
                                                  │
                    (External SPARQL tools) ───────┘
```

### Frank.LinkedData (#75) — Foundation

Extends Frank resources with an RDF model and content negotiation for semantic media types. This is the foundation everything else builds on. Includes:

- RDF model per resource derived from Frank's existing resource graph
- Content negotiation for `application/ld+json`, `text/turtle`, `application/rdf+xml`
- Spec media types via `useAppSpec`: ALPS, SCXML, XState, smcat, Mermaid, PlantUML
- `OPTIONS` support returning available media types for discovery
- `Link` headers pointing to the discovery endpoint

### Frank.Validation (#76) — Constraints

SHACL shapes as semantic request/response constraints. Complements F#'s structural type validation with business rule preconditions and cross-resource constraints. Does not need to be exhaustive — even partial SHACL coverage adds value for agent-driven analysis.

### Frank.Provenance (#77) — State Change History

> **Status:** The provenance `IHostedService` registers for `IObservable<TransitionEvent>` from DI, but `Frank.Statecharts` never publishes one — the bridge was never wired. Additionally, `Frank.Provenance.TransitionEvent` (non-generic, singular `PreviousState`/`NewState`) and `Frank.Statecharts.TransitionEvent<'S,'E,'C>` (generic, flat `ExitedStates`/`EnteredStates` lists) are incompatible types. See [AUDIT.md](AUDIT.md) Suspect 1.

PROV-O annotations for resource state changes, built on `Frank.Statecharts`' `onTransition` observable. Enables agents to reason about *how* the application reached its current state, not just what the state is.

### External SPARQL

Frank does not embed a SPARQL engine. The RDF model is structured so that external SPARQL tools can query it. This keeps Frank lightweight while enabling powerful graph queries for those who need them.

## Design Principles

**Build on durable standards.** HTTP, content negotiation, RDF, ALPS, SCXML — these are decades-old standards that will outlast any specific agent protocol or LLM framework.

**The semantic surface is the interface.** No custom agent protocol, no framework-specific discovery mechanism. An agent that speaks HTTP can understand the application.

**Opt-in, incremental.** A Frank application with zero semantic configuration works exactly as before. Each extension (`useLinkedData`, `useAppSpec`, `useValidation`, `useProvenance`) adds capability without requiring the others.

**Let tooling evolve independently.** Frank provides the semantic model; how agents consume it is not Frank's concern. Today that might be an LLM with HTTP tool use; tomorrow it might be something else entirely.

## Relationship to the Semantic Web

This work draws on ideas from the early Semantic Web vision — a worldwide data graph where applications expose machine-interpretable descriptions of themselves. Frank's ambition is narrower: make individual applications self-describing enough that agents can reason about them. But in doing so, it contributes to the broader idea that applications can participate in a web of structured, queryable, semantically typed data — even if that web starts within a single organization's tools.

## References

- [RDF 1.2](https://www.w3.org/TR/rdf12-concepts/) — Resource Description Framework
- [ALPS Specification](http://alps.io/spec/drafts/draft-01.html) — Application-Level Profile Semantics
- [SHACL](https://www.w3.org/TR/shacl/) — Shapes Constraint Language
- [PROV-O](https://www.w3.org/TR/prov-o/) — Provenance Ontology
- [JSON-LD](https://www.w3.org/TR/json-ld11/) — JSON for Linking Data
- [Frank.Statecharts](STATECHARTS.md) — Application-level state machines
- [Spec Pipeline](SPEC-PIPELINE.md) — Bidirectional design spec pipeline
- [Comparison with Webmachine and Freya](COMPARISON.md) — How Frank.Statecharts differs from protocol-level state machines
