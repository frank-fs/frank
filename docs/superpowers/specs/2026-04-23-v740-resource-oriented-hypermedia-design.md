# Resource-Oriented Hypermedia with Role Projections

**Date:** 2026-04-23
**Status:** Design proposed, awaiting review. Implementation begins after Track A (v7.4.0) lands.
**Version target:** v7.4.0
**Build sequence:** Track B (v7.3.2) → Track A (v7.4.0) → Track C (v7.4.0)
**Track:** C (Resource-Oriented Hypermedia)

-----

## Reading order

§1 establishes Track C’s identity and what it is not. §2 introduces the systems pipeline that organizes the rest of the document. §3 grounds the design in HMBS. §4–§7 specify the operational pieces: resource binding, affordance derivation, representation agnosticism, HTTP discipline. §8 covers composition with sibling packages. §9 carries the salvage triage from the archived statecharts ADR — which prior decisions migrate, which reshape, which are defunct under the v7.4.0 protocol-types foundation. §10 is the build order. §11 is open questions.

The supporting material — the protocol algebra (Track A), the vocabulary lock and semantic codegen (Track B), the authoring loop — lives in their own documents. Track C consumes their outputs and is silent on their internals.

-----

## 1. Purpose and identity

Frank renders per-role projections of a multi-party protocol as resources with state-dependent affordances. Affordances are derived from what the projected local type permits, independent of representation format. The same affordance machinery enriches opaque content (JPEG, PDF, plain text) with navigable metadata and enriches hypermedia content (HTML, Siren, JSON:API) with semantic grounding and role-enforced constraints.

This is the v7.4.0 thesis: agents can navigate any Frank application by reading standard HTTP discovery surfaces alone, regardless of what the response body looks like. The protocol layer is in the headers, the role projection is in the affordance set, the semantic grounding is in the vocabulary IRIs. The body can be anything.

**Build sequencing within v7.4.0.** Track B shipped in v7.3.2 with the vocabulary CE, convention engine, lock file, and MSBuild codegen. Track A ships first within v7.4.0 with the protocol algebra, projection, runtime, and verification. Track C follows Track A within v7.4.0 and bears the work of tying everything together — Track A’s projected local types and Track B’s vocabulary lock become observable as a working HTTP framework only after Track C lands. This document assumes both prerequisites are complete by the time Track C work begins; references to “Track A” and “Track B” throughout point to artifacts that already exist.

What Track C is *not*:

- It is not the protocol algebra. That is Track A.
- It is not the vocabulary registry or semantic codegen. That is Track B.
- It is not a view engine or a representation framework. The developer picks their own.
- It is not a re-derivation of the archived statecharts ADR. It is a salvage operation on that ADR’s HTTP-binding layer, reshaped for the v7.4.0 foundation.

What Track C is:

- The function that binds `(role × projected local type × current scope path × journal head × vocabulary lock) → resource representation`.
- The HTTP discipline that makes that representation correct (RFC 9110, RFC 8288, RFC 9457).
- The composition discipline that lets sibling packages (LinkedData, Validation, Provenance, Discovery) layer cleanly on top.
- The integration point where Track A’s protocol runtime and Track B’s vocabulary surface become the resource-oriented hypermedia framework users actually deploy. Track C is what makes v7.4.0 shippable as a coherent release rather than two independent libraries.

A candidate identity sentence:

> Frank renders per-role projections of a multi-party protocol as resource-oriented hypermedia, with affordances derived from the projected local type and semantic annotations sourced from the vocabulary lock — enriching both opaque and hypermedia representations while remaining agnostic about the representation format itself.

Everything else in this document derives from or supports that sentence. If a section accretes content that does not, it is residue from the archived ADR that Track A or B has already taken over and should be dropped rather than ported.

-----

## 2. The systems pipeline

Frank organizes resource rendering as a deterministic three-stage pipeline. Each stage is a *system* in the entity-component-system sense — orthogonal to the others, composable in fixed order, with a single responsibility.

```
┌─────────────────────────┐    ┌─────────────────────────┐    ┌─────────────────────────┐
│ Role Projection System  │───▶│   Protocol System       │───▶│   Rendering System      │
│                         │    │                         │    │                         │
│ Global protocol +       │    │ Local protocol +        │    │ Projected state +       │
│ requesting role →       │    │ current scope path →    │    │ affordances +           │
│ projected local type    │    │ enabled affordances     │    │ format → representation │
└─────────────────────────┘    └─────────────────────────┘    └─────────────────────────┘
```

**Role Projection System (first).** Takes the global protocol from Track A and the requesting role’s identity. Produces the projected local type that role sees. This is *not* a filter applied to the global protocol’s output — it is a mechanically derived local type via the Li/Stutz/Wies/Zufferey projection (Track A, Appendix A). Player X and Player O do not run the same protocol with different filters; they run mechanically distinct local types derived from the same global protocol. Role projection runs first because the protocol Frank executes against is the role-projected one, not the global one.

**Protocol System (second).** Takes the projected local type and the current scope path (where in the nested or AND-state hierarchy this role currently is, derived from journal head). Produces the set of affordances enabled at this position — which transitions can fire, with what message types, against what guards, with what permitted effects. This is purely structural: given a local type and a scope path, the available affordances are determined.

**Rendering System (third).** Takes the affordances, the entity’s component data visible to this role, and the requested representation format. Produces the response body. *Frank does not own this stage.* The developer chooses their representation (HTML, Siren, JSON:API, plain JSON, opaque PDF/JPEG), picks a view engine or writes their own, and consumes the affordance surface as structured data. Frank provides the inputs; the developer provides the rendering.

**Why this order matters.** A common mistake is to think of role projection as a filter applied to the global protocol’s output. It is not. The protocol *itself* is role-specific. The Customer’s local type and the Seller’s local type are different protocols, derived from the same global protocol but with different states, transitions, and obligations. Running role projection first ensures the Protocol System operates on the correct local type, not on the global one with a downstream visibility filter.

**Entity-component-system as metaphor.** ECS is a useful framing for Frank’s pipeline but a metaphor, not a formalism. The metaphor lights up because Frank’s pipeline is structurally ECS-shaped: entities (resources, identified by URL) carry components (semantic data, protocol state, journal history) and systems transform them in fixed order. The ECS query analogue is `(entity, role, format) → rendered representation`, with the systems pipeline as the deterministic implementation. The metaphor breaks down where ECS systems are dynamically composed per frame; Frank’s three systems are fixed and ordered. The principle still holds: each system is independent, works at its own level of abstraction, and the output of one feeds the next.

The formal underpinnings of ECS are in data-oriented design, which does not directly apply to web programming. The metaphor’s value is in naming the layering — separating *what the resource is* (entity), *what is true about it for this role* (components after projection), *what can happen to it* (protocol), and *how it is painted* (rendering) — without conflating them.

-----

## 3. HMBS as the formal anchor

The HMBS model (Oliveira/Turine/Masiero, ACM TOIS 19(1), 2001) formalizes the connection between statecharts and hypermedia. Frank generalizes it two ways.

**Original HMBS.** A hyperdocument is a tuple `Hip = ⟨ST, P, m, ae, N⟩`:

|Component     |Definition                        |
|--------------|----------------------------------|
|`ST`          |Statechart structure              |
|`P`           |Set of pages (content units)      |
|`m : Sₛ → P`  |State-to-page mapping             |
|`ae : Anc → E`|Anchor-to-event mapping           |
|`N`           |Visibility level (hierarchy depth)|

**Multi-party HMBS (Frank’s extension).** Frank generalizes both `m` and `ae` for role-projected hypermedia:

|Component                               |Frank generalization                                      |
|----------------------------------------|----------------------------------------------------------|
|`ST`                                    |Projected local type (per role, post-Li-et-al. projection)|
|`P`                                     |Resource representations — opaque or hypermedia           |
|`m : (Role, ScopePath) → Resource`      |Per-role projected page mapping                           |
|`ae : (Role, ProtocolSend) → Affordance`|Affordances derived from projected sends                  |
|`N`                                     |Visibility level over projected scopes                    |

Two things to notice:

1. **HMBS is single-actor.** The original model has one user navigating one hyperdocument. Frank’s contribution beyond HMBS is the multi-party piece: the global protocol guarantees cross-role coherence (every role’s projected hyperdocument is consistent with every other role’s), which HMBS-on-its-own cannot do. This is the actually novel contribution and the right framing for the academic positioning of Frank.
1. **HMBS is representation-agnostic.** The mapping `m` produces “pages” with no commitment to a representation format. Frank inherits this directly. The role-projected hyperdocument is structurally defined; how it is *painted* is the rendering system’s concern.

The HMBS framing also clarifies the relationship to the operational-vs-denotational pivot in v7.4.0. HMBS is the part of the design *least affected* by that foundation change, because it is about hyperdocument/state correspondence rather than about the underlying state-machine formalism. It survives the pivot intact and becomes the natural connective tissue between the archived ADR and Track C.

-----

## 4. The resource binding

A Frank resource is a single URL backed by a (possibly hierarchical) protocol, projected per role. The resource is the boundary; the protocol’s hierarchy is internal. AND-state composition within the protocol is *not* multiple resources — it is one resource with orthogonal sub-protocols.

```fsharp
type ResourceBinding<'TEvent, 'Role, 'Scope, 'Message, 'Effect> = {
    /// One URL. The boundary of the resource.
    Path: string
    Name: string

    /// The protocol this resource implements. May be hierarchical (Nested,
    /// Parallel) but is rooted at one ProtocolType value.
    Protocol: ProtocolType<'Role, 'Scope, 'Message, 'Effect>

    /// Role projections, derived from Protocol via the Li/Stutz/Wies/Zufferey
    /// algorithm at build time. One projected local type per role.
    RoleProjections: Map<'Role, LocalType<'Scope, 'Message, 'Effect>>

    /// Predicate-keyed handlers: which handler fires for which scope path.
    /// Predicate is over the projected local type's scope tree, not over a
    /// flat StateId space.
    Handlers: (ScopePredicate * Handler<'TEvent>) list

    /// Predicate-keyed affordances: which affordances are advertised when
    /// this role is at this scope path.
    Affordances: (ScopePredicate * Affordance<'Message> list) list

    /// Optional validation shapes per scope, sourced from the vocabulary lock.
    Validation: (ScopePredicate * NodeShape) list option

    /// Optional provenance configuration. Derives PROV-O from the journal
    /// via the Frank.Protocol.Http.Provenance bridge package.
    Provenance: ProvenanceOptions option

    /// Event mapping: domain events → protocol messages.
    EventMapper: 'TEvent -> 'Message

    /// Optional parent resource for child-of composition. Capability-bounded:
    /// child sees its own scope, not the parent's full configuration.
    Parent: obj option
}
```

**One resource, one URL, one protocol.** A Frank resource at `/orders/{id}` runs one protocol value. That protocol may be deeply nested with parallel regions — a top-level `Nested` for the order envelope containing parallel sub-protocols for fulfillment and payment, each in turn containing their own scopes. All of that hierarchy is *internal* to the resource. The HTTP boundary stays at `/orders/{id}`.

**Hierarchical protocols at the application level.** A web application typically has a top-level protocol describing the relationships between its resources, with each resource as a nested scope. This is not enforced by Frank — applications can ship resources with no shared parent protocol — but it is the recommended modeling pattern, especially for applications where role projection across the whole app needs to be coherent (e.g., a Customer’s view of the storefront should be consistent across the home page, product pages, and cart).

**Entity identity vs. resource identity.** A Product (entity, in CQRS aggregate-root terms) has an identity that persists across contexts. The Customer’s view of Product, the Seller’s view, and the Analyst’s view may be three different *resources* (different URLs, different protocols, different role projections) sharing the same entity identity. The entity is the aggregate root; the resources are projected views over it.

This is also why role projection comes first in §2. The Customer’s resource at `/products/{id}` and the Seller’s resource at `/sellers/products/{id}` may share an entity identity but execute different protocols. The role determines which protocol the URL resolves to, and the entity identity is the integration point that lets cross-context updates remain coherent.

**Cross-resource interaction.** When the Seller updates inventory, the Customer’s view changes. This is *not* a shared protocol — it is two resources whose protocols both reference the same underlying entity, with the journal as the integration point. The MPST projection guarantees the protocols stay coherent; the journal carries the actual state. First-class cross-resource messaging across bounded contexts is deferred (§11 #2).

-----

## 5. Affordance derivation

An affordance is a structured value:

```fsharp
type Affordance<'Message> = {
    /// Hypermedia link relation. Resolves through the vocabulary lock to
    /// a semantic IRI (e.g., "approve" → "schema:AcceptAction").
    Rel: string

    /// Target URL (relative or absolute).
    Href: string

    /// HTTP method, if this affordance maps to one. Some affordances are
    /// purely informational (e.g., rel="describedby" with no method).
    Method: HttpMethod option

    /// Content types this affordance accepts in request body, when applicable.
    Accepts: string list

    /// Human-readable title, for representations that surface labels.
    Title: string option

    /// Reference to the protocol message this affordance triggers, if any.
    /// Used by the Protocol System to dispatch incoming requests.
    MessageRef: 'Message option

    /// Safety classification. Drives HTTP method selection and Allow header
    /// composition per RFC 9110.
    Safety: TransitionSafety
}

and TransitionSafety =
    | Safe         // GET/HEAD: idempotent, no side effects
    | Idempotent   // PUT/DELETE: side effects, idempotent
    | Unsafe       // POST: side effects, not idempotent
```

The Protocol System derives affordances by walking the projected local type. For each `Send` the role can issue at the current scope path, an affordance is emitted. For each `Choice` branch the role can take, an affordance per branch. For each scope-exit transition, an affordance for completing the scope. The rules are mechanical and deterministic.

**Safety classification.** Safety is annotated on the protocol’s `Send` constructor via its effect set, not inferred from the message name. A `Send(channel, ApproveOrder, [WriteOrders; EmitOrderApproved], …)` declares Unsafe via its effect set including a write effect. A `Send(channel, GetOrderStatus, [ReadOrders], …)` declares Safe via its effect set being read-only. The mapping from effect set to safety is defined in Track A; Track C consumes it.

**Predicate-keyed affordances.** Affordances are bound to *predicates over scope paths*, not to single states. This handles AND-state composition correctly: an affordance might require the protocol to be in scope `Approved` AND scope `PaymentCleared` simultaneously. Single-scope cases stay concise via convenience wrappers. Predicates evaluate against the projected local type’s scope tree at the journal’s current head.

**Per-region affordances in composite resources.** A resource backed by a protocol with parallel regions (an Amazon-home-page-shaped resource with SearchBar, ProductList, RecommendationPanel, ShoppingCart all running concurrently) emits affordances *per region*. Each region’s affordances are independent — SearchBar offers “submit query”; Cart offers “checkout”; they coexist in the same response. The role projection determines what each region offers for the requesting role; the rendering system composes the per-region affordances into the final document.

-----

## 6. Representation agnosticism

Frank does not pick a representation format. It provides the affordance surface; the developer picks how to render it. This is load-bearing for the v7.4.0 thesis.

**The fit-of-success formula.** An LLM or rule-based agent navigating a Frank application receives:

- `Allow` header with the HTTP methods enabled at this scope path for this role
- `Link` headers with typed relations (RFC 8288), each grounded in the vocabulary lock
- Response body in whatever representation the developer chose
- Optionally, semantic metadata embedded in the body (RDF-a in HTML, JSON-LD in JSON, microdata, Hydra)

Even if the body is opaque (`text/plain`, `image/jpeg`, `application/pdf`), the headers carry enough structure for an agent to navigate. The agent does not need to parse the body to know what actions are available; it reads the affordances from the headers and the linked discovery documents.

If the body *is* hypermedia (HTML, Siren, JSON:API), the affordances can also be rendered into the body. Frank does not dictate this; the developer’s view engine consumes the affordance list and decides whether to render it as `<form>`, `_links`, Siren actions, or anything else.

**Enriching opaque content.** A `/contracts/{id}` resource returning `application/pdf`:

- Body: the PDF (opaque to most agents)
- Headers: `Allow: GET, POST`, `Link: </contracts/42/sign>; rel="sign"; method="POST"`, `Link: </contracts/42/audit>; rel="audit"`, `Link: </alps/contracts>; rel="profile"`
- An agent can sign the contract by following the link, retrieve the audit history via PROV-O, and never parse the PDF.

The PDF stays a PDF. The workflow state machine determines what actions are legal. The role projection determines what *this signer* can do right now. PROV-O tracks lineage. An LLM asking “what’s the status of this contract?” gets back the protocol state and the linked provenance graph without needing to interpret PDF form fields.

**Enriching hypermedia content.** A `/orders/{id}` resource returning `text/html`:

- Body: HTML rendering of the order, with forms and links the user clicks
- Headers: same affordance machinery as the opaque case
- The view engine can additionally annotate the HTML with RDF-a derived from the same vocabulary lock that grounds the headers

The submit button in the HTML and the `Link: </orders/42/approve>; rel="approve"` header both resolve to `schema:AcceptAction` because they share the lock-file mapping. The HTML is not just navigable by agents that parse HTML — it is grounded in the same shared semantics as the headers. Hypermedia content gains *semantic grounding* on top of its existing structure; opaque content gains *navigable structure* it did not have. Both kinds of content benefit; neither is privileged.

**Blossian correctness.** The role projection makes invalid transitions structurally absent from the affordance surface. When it is Player O’s turn in tic-tac-toe, Player X’s affordance list is empty for move-related rels — no `Link: </games/42/move>; rel="move"; method="POST"`, no `<form>` in the rendered HTML, no Siren actions. Player X cannot represent an invalid move because the affordance surface does not offer one.

This is not server-side validation rejecting bad requests after the fact. It is the absence of bad requests’ surface area in the first place — the Scott Wlaschin “make illegal states unrepresentable” principle, applied at the hypermedia layer rather than at the type layer. Most applications do this with client-side JavaScript hiding buttons (fragile, bypassable) or with server-side rejection (correct, but requires a round-trip and error handling). Frank does it structurally: the unauthorized affordance never appears.

**The view engine boundary.** The developer’s view engine receives the affordance list as structured data. Three authoring patterns are common:

1. *Hand-authored templates* — the developer iterates over affordances in their template language and renders each as appropriate. Most explicit, most verbose.
1. *Convention-based rendering* — a reusable component library maps affordance properties (Rel, Safety, Method) to template partials. Frank may ship one of these for HTML; others can exist for Siren, JSON:API, etc.
1. *Vocabulary-aware rendering* — a service consults the vocabulary lock at view time and annotates the rendered output with RDF-a, microdata, or JSON-LD inline.

All three patterns consume the same affordance surface. None is privileged by Frank. Whether to ship pattern 2 or 3 as a Frank component or as a sample-app pattern is open (§11 #1).

-----

## 7. HTTP discipline

The HTTP-shaped concerns from the archived ADR migrate to Track C intact. They are not statechart-specific; they are protocol-correctness disciplines for any HTTP framework that wants to be RFC-compliant.

**`BlockReason` → HTTP status mapping.** Every reason the Protocol System rejects an incoming request maps to a defined HTTP status, rendered as RFC 9457 Problem Details (`application/problem+json`):

|BlockReason                                      |HTTP status                     |
|-------------------------------------------------|--------------------------------|
|`NoEnabledTransitions`, `GuardFailedAll`         |400 Bad Request                 |
|`InvalidEvent`                                   |400 Bad Request                 |
|`Forbidden`                                      |403 Forbidden                   |
|`RoleNotInProtocol`, `MessageNotPermittedForRole`|403 Forbidden (new under v7.4.0)|
|`NotYourTurn`, `ParentInactive`                  |409 Conflict                    |
|`PreconditionFailed`                             |412 Precondition Failed         |
|`ParentNotFound`                                 |404 Not Found                   |

Frank registers `IProblemDetailsService` with `TryAddSingleton` so existing application registrations are respected.

**Required headers.**

- 405 Method Not Allowed responses MUST include the `Allow` header per RFC 9110 §15.5.6, populated from the Allow interpreter of `IAffordanceAlgebra`.
- HEAD is always included alongside GET in `Allow` per RFC 9110 §9.3.2.
- 202 Accepted responses MUST include `Content-Location` pointing to the resource URI per RFC 9110 §15.3.3, so clients can discover the resource after a transition.
- Link headers follow RFC 8288 with typed relations; relations resolve to vocabulary IRIs via the lock file.

**Per-role discovery surface.** Each resource serves:

- **OPTIONS** with role-projected `Allow` header and `Link: <profile-url>; rel="profile"` pointing to the role-projected ALPS profile.
- **ALPS profile** at the profile URI: descriptors per scope (state in HMBS terms), `safe`/`unsafe`/`idempotent` action descriptors per transition, `rt` (return type) links between scopes. The ALPS document is the structured representation of the role-projected local type.
- **JSON Home** at the resource entry point: directory of available resources for this role, with relation types mapped to vocabulary terms. The role projection ensures each role sees only the resources and affordances it is permitted to interact with.

The discovery surface is deterministic given the role-projected local type. Two roles fetching `/.well-known/json-home` against the same Frank app see different documents — not because Frank applies a post-hoc filter, but because each role’s projection is a different local type and the discovery surface is computed from that.

-----

## 8. Composition

Frank’s runtime concerns compose through the existing sibling packages plus a small set of bridge packages, each owning one cross-concern integration. Everything is opt-in. An application may use `Frank.Protocol.Http` alone for basic stateful HTTP resources, add `Frank.Protocol.Http.Discovery` to advertise itself to agents, add `Frank.Protocol.Http.Semantic` to enrich validation and representations with vocabulary-grounded semantics, add `Frank.Protocol.Http.Provenance` to expose audit trails. The composition matrix from the archived ADR (§5.3) survives the v7.4.0 pivot intact.

**Sibling packages, standalone deliverables:**

|Package              |Standalone deliverable                                                                                                                                                                                                                                                                                                                |
|---------------------|--------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------|
|`Frank.Validation`   |SHACL validation on request/response bodies, sourced from generated shapes (Track B).                                                                                                                                                                                                                                                 |
|`Frank.LinkedData`   |Content negotiation across RDF representations (JSON-LD, Turtle, RDF/XML), sourced from the generated graph (Track B).                                                                                                                                                                                                                |
|`Frank.Provenance`   |HTTP-level audit (request, response, status, principal). Protocol-unaware.                                                                                                                                                                                                                                                            |
|`Frank.Discovery`    |Static ALPS profiles, JSON Home, OPTIONS responses, Link headers — sourced from the resource binding plus the generated discovery surface (Track B).                                                                                                                                                                                  |
|`Frank.Protocol.Http`|Stateful resources backed by `ProtocolType` with role projections and predicate-keyed bindings. Owns affordance derivation, RFC 9110 / RFC 9457 discipline, basic Allow/Link headers, BlockReason mapping, and per-role OPTIONS. Does not generate ALPS, RDF, SHACL, or PROV-O on its own — those come from the bridge packages below.|

`Frank.Protocol.Http` is the smallest possible Track A integration. A resource bound through it will respond to HTTP requests with the correct status codes, advertise the basic affordance set, and reject unauthorized transitions with proper Problem Details. It will not, on its own, advertise itself richly to agents — that is what the discovery bridge adds.

**Bridge packages.** Each bridge owns one cross-concern integration. They are opt-in and independently usable; an application picks any subset.

|Bridge package                  |Bridges                                                                                |Responsibility                                                                                                                                                                                                                                                                                                     |
|--------------------------------|---------------------------------------------------------------------------------------|-------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------|
|`Frank.Protocol.Http.Discovery` |Track A projections + `Frank.Discovery` + Track B vocabulary lock                      |Per-role ALPS profiles, role-scoped JSON Home directories, OPTIONS responses with profile links, vocabulary-grounded Link relations. The agent-discovery surface beyond basic RFC 9110 Allow.                                                                                                                      |
|`Frank.Protocol.Http.Semantic`  |Track A projections + `Frank.Validation` + `Frank.LinkedData` + Track B vocabulary lock|State-dependent SHACL validation (the shape applied to a request body depends on the role’s current scope path) and state-dependent RDF representations (the graph negotiated for a response varies with scope). Both rely on the same vocabulary lock; bundling them avoids duplicating the lock-resolution layer.|
|`Frank.Protocol.Http.Provenance`|Track A journal + `Frank.Provenance` + Track B vocabulary lock                         |Derives PROV-O from the protocol journal, using the journal’s extension field (per Track A §9.1) for vocabulary-grounded annotations. Reusable from CLI tools and offline replay.                                                                                                                                  |

Bridge packages are thin: they consume the upstream package’s output and the projected local type, and produce the downstream concern’s input. They never reimplement either side. The granularity is deliberate — an application that needs PROV-O audit but does not want SHACL validation pulls in `Frank.Protocol.Http.Provenance` and skips `Frank.Protocol.Http.Semantic`.

**`childOf` composition.** When one resource is a child of another in a hierarchical protocol composition, the child receives a capability boundary — `ProtocolContext` exposing `Send`/`CurrentScope`/`Affordances` for its own scope, but *not* the parent’s full configuration or journal. This survives from the archived ADR reshaped for the v7.4.0 algebra and lives in `Frank.Protocol.Http`.

-----

## 9. Salvage triage from the archived ADR

The archived `STATECHARTS_ARCHITECTURE_DECISIONS.md` carried the operational-statechart-centric design that v7.4.0 replaces with the coalgebraic protocol-types foundation. Track C inherits the HTTP-binding layer of that design, reshaped per the new foundation. This triage is the explicit relationship between the archived ADR and Track C — it makes the cost of the denotational pivot legible.

**Migrates intact:**

|Decision                                                 |Why it survives                                                                                     |
|---------------------------------------------------------|----------------------------------------------------------------------------------------------------|
|HMBS framing (§2.2)                                      |About hyperdocument/state correspondence, agnostic to formalism.                                    |
|Composition matrix and standalone-deliverable rule (§5.3)|About package boundaries, not about statecharts.                                                    |
|Bridge-package pattern (§6.6)                            |Architectural pattern, generalizes across concerns.                                                 |
|`TransitionSafety` taxonomy + HTTP method selection      |RFC 9110 semantics.                                                                                 |
|RFC 9457 Problem Details + `BlockReason` mapping         |RFC compliance.                                                                                     |
|RFC 9110 Allow/HEAD/Content-Location discipline          |RFC compliance.                                                                                     |
|`IAffordanceAlgebra` with Link/Allow/ALPS interpreters   |Compositional, format-agnostic.                                                                     |
|`childOf` composition + capability boundary              |Resource-hierarchical, not statechart-specific.                                                     |
|`StatefulResourceBinding` tuple shape (§6.7)             |Most fields survive; the `Statechart` field reshapes.                                               |
|Predicate-keyed bindings (AD-5)                          |Necessary for AND-state composition; the predicate carrier changes but the binding pattern is right.|

**Reshapes:**

|Decision                                                |New shape                                                                                                                                                                                                                                             |
|--------------------------------------------------------|------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------|
|`StatefulResourceBinding.Statechart: StatechartDocument`|Becomes `Protocol: ProtocolType<…>` plus the projected per-role local types.                                                                                                                                                                          |
|`RoleParticipation` schema                              |Collapses into the algebra’s `'Role` parameter; `Triggers`/`Observes`/`Forbidden` become redundant once `Send`/`Recv`/`Choice` carry role identity directly. Closed-world mode survives as a thin wrapper around the Li-et-al. implementability check.|
|Affordance derivation per role                          |Uses the projected local type (post-Li-et-al.) instead of the §10.1 hand-rolled projection.                                                                                                                                                           |
|`ConfigurationPredicate`                                |Becomes `ScopePredicate` — a predicate over the projected local type’s scope tree at journal head. Carrier changes; binding pattern survives.                                                                                                         |
|`IStatechartJournal`                                    |Becomes `IProtocolJournal` per Track A §9.1, with the extension field for semantic enrichment.                                                                                                                                                        |
|`BlockReason`                                           |Gains MPST-shaped cases (`RoleNotInProtocol`, `MessageNotPermittedForRole`) alongside the existing ones.                                                                                                                                              |
|`Frank.Statecharts.Provenance` bridge package           |Renamed `Frank.Protocol.Http.Provenance`; same shape, journal source changes from statechart journal to protocol journal.                                                                                                                             |

**Defunct under the v7.4.0 foundation:**

|Decision                                                         |Why it goes                                                                                                             |
|-----------------------------------------------------------------|------------------------------------------------------------------------------------------------------------------------|
|AD-2 three algebras with disjoint carriers                       |The protocol algebra is one thing; multi-algebra discipline doesn’t translate.                                          |
|AD-10 SCXML algorithm transliteration                            |Replaced by actor dispatch over the projected local type (Track A §6, §7).                                              |
|`IStatechartBuilder` / `ITransitionAlgebra` as authoring surfaces|Replaced by the protocol CE (Track A §3).                                                                               |
|Macrostep semantics as authored                                  |Actor model handles dispatch differently.                                                                               |
|Tagless-final encoding as load-bearing pattern                   |Witness-object HKT (Track A §5) is the v7.4.0 effect encoding; tagless-final is no longer needed for transition algebra.|

Decisions not listed here either had no HTTP-binding relevance or are absorbed into Track A or Track B. This triage is meant to be exhaustive at the level of architectural decisions; if a specific decision is missing, treat that as an oversight worth flagging.

-----

## 10. Build order

Track C builds in this order. Each step is independently testable against falsifiable HTTP request/response pairs, per the Track A Appendix G discipline.

1. **`ResourceBinding` shape and CE builder.** Reshape the archived `StatefulResourceBuilder` to consume `ProtocolType` and the role-projection map from Track A. Hand-author smoke instances. Verify no dependency on archived statechart machinery survives. (In `Frank.Protocol.Http`.)
1. **`ScopePredicate` evaluation.** Predicate language over the projected local type’s scope tree. Unit test against known projections from Track A.
1. **Affordance derivation.** Walk the projected local type plus current scope path; emit `Affordance` list. Unit test against known protocol fragments. Closes once hierarchical role scoping (§11 #3) is settled.
1. **`IAffordanceAlgebra` interpreters.** Migrate `LinkHeaderAlgebra`, `AllowHeaderAlgebra` from the archived ADR. Unit test each against known affordance lists. (`ALPSAlgebra` moves to step 6 with the discovery bridge.)
1. **HTTP discipline middleware.** Allow/HEAD enforcement, `BlockReason` → Problem Details, Content-Location for 202, ETag/conditional request handling, basic per-role OPTIONS with Allow header. End-to-end HTTP test per concern. This completes `Frank.Protocol.Http` as the standalone-usable base package.
1. **`Frank.Protocol.Http.Discovery` — per-role ALPS, JSON Home, profile-linked OPTIONS.** Bridge package. Migrate `ALPSAlgebra` from the archived ADR; consume Track B’s static `Frank.Discovery` output and enrich with role-projected and scope-dependent content. End-to-end test: agent at `GET /` discovers all reachable resources and their affordances for its role.
1. **`Frank.Protocol.Http.Semantic` — state-dependent SHACL and RDF.** Bridge package. Consume Track B’s generated shapes and graph; enrich `Frank.Validation` with scope-dependent shapes and `Frank.LinkedData` with scope-dependent representations. Two composition tests, one per concern.
1. **`Frank.Protocol.Http.Provenance` — PROV-O from journal.** Bridge package. Derive PROV-O from the protocol journal using the journal’s extension field for vocabulary-grounded annotations. Composition test with Track A’s journal and Track B’s vocabulary lock.
1. **`childOf` composition with capability boundary.** Hierarchical protocol composition across resources. End-to-end test: child resource correctly receives parent-scoped context without parent journal access. (In `Frank.Protocol.Http`.)
1. **Capstone test.** Multi-role protocol with three or more roles (Track A’s order-fulfillment example or equivalent), each receiving role-projected affordances. Naive client navigates the full protocol from `GET /` using only HTTP discovery, no out-of-band knowledge. This is the v7.4.0 Track A/B/C integration test that proves the Agent Hypothesis end-to-end. Requires `Frank.Protocol.Http` plus all three bridge packages.

**Discipline checks (per archived ADR Appendix G):**

- Every layer has unit tests that pass without the layer above present.
- Integration tests are additional, never substitutes.
- Do not claim integration until a falsifiable HTTP test exercises it.
- Commit after every working step.

-----

## 11. Open questions

1. **Vocabulary-aware view engine in Frank or sample.** The vocabulary-lookup-at-render-time pattern (§6 third pattern) is useful enough to be a Frank-shipped component but specific enough to a representation choice that a sample-app pattern may suffice. Decide before §6’s authoring guidance is finalized in user-facing docs.
1. **Cross-resource interaction.** When two resources reference the same entity (Customer’s `/products/{id}` and Seller’s `/sellers/products/{id}`), how does an update through one surface in the other? v7.4.0 says “through the underlying entity’s journal”; v7.5 may want first-class cross-resource messaging. Not load-bearing for v7.4.0.
1. **Hierarchical role scoping.** Open question §13 #2/#3 in the Track A ADR. Affects how affordance derivation handles role visibility across nested scopes. Must close before step 3 of the build order completes.
1. **Multi-instance protocol management.** A single resource binding may have many concurrent protocol instances (one per business entity). Instance-locating from the inbound URL, instance lifecycle, journal sharing across instances — all unspecified at the binding layer. Likely resolves through the auth-context-to-role binding plus the URL routing convention; needs explicit design before v7.4.0 ships.
1. **`Frank.OpenApi` positioning.** ALPS becomes the primary discovery surface. OpenAPI may stay as a secondary emission target for clients that don’t speak HATEOAS, or may be deprecated. The composition matrix needs a row for it either way.
1. **Cross-context entity identity.** When the Customer and Seller views of Product share an entity identity but project different protocols, how is the entity identity itself represented in the response? `Link: <product-canonical-iri>; rel="canonical"`? An IRI in the vocabulary lock? Open.
1. **WebTransport readiness.** The reactive projection (Datastar SSE today) is transport-agnostic at the journal-projection layer per Track A §9. When WebTransport ships in target browsers, swapping the transport should be a bridge-package change, not a Track C change. Worth a short note in the bridge-package documentation when the swap becomes plausible.
1. **Composite-resource rendering convention.** When a resource backs a protocol with parallel regions, the per-region affordances need to flow into the rendered representation in a way the developer can compose easily. Per-region “slots” the view engine can address (similar to template region patterns)? Per-region sub-models in the affordance surface? Pick a convention before the capstone test (step 12) ships.

-----

## References

- Track A — `docs/superpowers/specs/2026-04-21-v740-protocol-types-design.md`. Protocol algebra, projection, runtime, verification.
- Track B — `docs/superpowers/specs/2026-04-20-v732-semantic-discovery-design.md`. Vocabulary registry, semantic codegen, lock file.
- Authoring loop — `docs/AUTHORING_WORKFLOW.md`. Sketch → lift → generate → inspect → iterate.
- Agent thesis — `docs/AGENT_HYPOTHESIS.md`. Feature combinations and probability ladder.
- Archived statecharts ADR — `docs/STATECHARTS_ARCHITECTURE_DECISIONS.md`. The HTTP-binding layer Track C salvages.
- Oliveira, Turine, Masiero (2001). *A Statechart-Based Model for Hypermedia Applications*. ACM TOIS 19(1), 28–52. The HMBS formal anchor.
- Li, Stutz, Wies, Zufferey (2023). *Complete Multiparty Session Type Projection with Automata*. CAV 2023. The projection algorithm Track A uses; consumed by Track C as the source of role-projected local types.
- Fielding (2000). *Architectural Styles and the Design of Network-based Software Architectures*. The REST formulation Frank implements.
- Wlaschin, S. *Designing with Types: Making Illegal States Unrepresentable*. The principle Track C applies at the hypermedia layer rather than at the type layer.
- RFC 9110 — HTTP Semantics.
- RFC 9457 — Problem Details for HTTP APIs.
- RFC 8288 — Web Linking.