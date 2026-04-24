# Protocol Types for F#: Unifying Hierarchical Statecharts with Multiparty Session Types

**Date:** 2026-04-21 (updated 2026-04-22 with Appendices N and O; updated 2026-04-23 with v1 scope cuts, §7.1 design rationale, and Appendices P, Q, R)
**Context:** Frank framework architecture exploration
**Status:** Architecture decided with v1 scope cuts recorded. Implementation to begin against the layered build order in §12 after this document is reviewed.

-----

## Reading order

The main body (§1–§13) describes the target architecture: what is being built, the decisions already taken, and the layered build order that avoids wiring everything at once. Appendices A–O capture the supporting detail — full algorithms, alternatives considered and rejected, and history. Skip the appendices on first read; return to them when implementing a layer or re-examining a decision.

-----

## 1. Purpose

Build an F# library (`Frank`) that unifies Harel hierarchical statecharts with Honda/Yoshida/Carbone multiparty session types into a single representation, authored as an F# computation expression (CE), from which correct per-role actors are generated at build time.

Specifically:

- **One authoring surface** — an F# CE — capturing both state hierarchy and multiparty choreography.
- **Generated actors** — complete `MailboxProcessor`-based implementations, one per role, produced from a single canonical protocol definition.
- **First-class effect discipline** — the side-effects permitted at each protocol step are part of the type, enforced by the generator, and available for verification.
- **Verification support** — Z3-backed queries for deadlock freedom, payload refinements, and related properties.
- **Journal-primary execution record** — an in-process journal (in-memory by default in v1; SQLite is the first durability upgrade — see Appendix M) is the canonical record of every semantically relevant event. `Microsoft.Extensions.Logging`, OpenTelemetry, and semantic-graph exports (RDF / PROV-O) are downstream projections (§9). Handlers, interpreters, and the supervisor all read the journal; logs and traces are for operators, not for Frank’s own tooling.

The goal is correctness-by-construction for the communication and state-machine layer, combined with a narrow, well-typed specialization surface (the effect handler) for business logic.

## 2. Architectural overview

Protocol definitions are F# computation expressions that evaluate to a `ProtocolType` value. `ProtocolType` is a coalgebraic description of the global protocol — hierarchical, multiparty, recursive, with effect annotations, explicit scope identity, and explicit communication semantics.

A build-time code generator consumes the `ProtocolType`:

1. Runs the Li/Stutz/Wies/Zufferey automata-theoretic projection (Appendix A) to produce per-role state machines and check implementability.
1. Emits, for each role, a `MailboxProcessor`-based actor that dispatches messages, routes to peers, escalates unhandled events to parent scopes, and invokes effects through a witness-object effect handler.
1. Emits the `IEffectHandler<_,_>` interface the author implements.
1. Emits a structured log-event schema document describing what the actor will emit at runtime.
1. Emits SMT-LIB queries for Z3 verification, invoked inline during code generation (v1 scope; deferred artifact-emission upgrades in Appendix Q).

The author writes:

- The CE protocol definition. (CLI intake from Scribble, SCXML, or other design-document formats is deferred to Appendix R; v1 has a single authored pathway.)
- Discriminated unions naming roles, states, messages, and effect operations.
- The effect handler implementation, injecting dependencies (services, loggers).
- Optional interpreters that consume the journal.

The author does **not** write actor code, message-routing code, scope-escape logic, or effect-dispatch glue. Those are always generated.

## 3. The protocol algebra

The `ProtocolType` value type is the canonical form. A CE builder provides the authoring surface.

> **On placeholders.** The algebra is generic over the types the author supplies. In the sketches below, `'Role`, `'Scope`, `'Message`, and `'Effect` are type parameters, not concrete types. In a real protocol the author defines these as domain DUs — e.g., `type Role = Customer | Merchant | Warehouse`, `type Scope = OrderPhase | InventoryPhase | ShippingPhase`, `type Message = PlaceOrder of OrderData | CheckInventory of ItemId | ...`, `type Effect = ReadOrders | WriteOrders | EmitOrderPlaced | ...`. Any string appearing in code samples as a message name, scope tag, or resource key is illustrative only — read it as standing in for an author DU case. `CommSemantics` is *not* parameterized; it is fixed vocabulary Frank itself defines.

```fsharp
type Channel<'Role, 'Scope> = Channel of from: 'Role * to_: 'Role * within: 'Scope

type CommSemantics =
    | AsyncFIFO of bound: int
    // Extension points: Sync (CSP rendezvous) and AsyncCausal (vector-clock causal delivery)
    // are documented in Appendix K. They apply when targeting backends whose runtime
    // realizes them; MailboxProcessor is structurally AsyncFIFO.

type ProtocolType<'Role, 'Scope, 'Message, 'Effect> =
    | Send     of channel: Channel<'Role, 'Scope>
                * message: 'Message
                * effects: 'Effect list
                * continuation: ProtocolType<'Role, 'Scope, 'Message, 'Effect>
    | Recv     of channel: Channel<'Role, 'Scope>
                * message: 'Message
                * effects: 'Effect list
                * continuation: ProtocolType<'Role, 'Scope, 'Message, 'Effect>
    | Choice   of chooser: 'Role
                * branches: ('Message * ProtocolType<'Role, 'Scope, 'Message, 'Effect>) list
    | Parallel of ProtocolType<'Role, 'Scope, 'Message, 'Effect> list
    | Sequence of ProtocolType<'Role, 'Scope, 'Message, 'Effect>
                * ProtocolType<'Role, 'Scope, 'Message, 'Effect>
    | Nested   of scope: 'Scope
                * comm: CommSemantics
                * permittedEffects: 'Effect list
                * body: ProtocolType<'Role, 'Scope, 'Message, 'Effect>
                * continuation: ProtocolType<'Role, 'Scope, 'Message, 'Effect>
    | History     of scope: 'Scope   // shallow history: resume at last substate of this scope
    | DeepHistory of scope: 'Scope   // deep history: resume at last substate at every level below
    | Rec      of label: RecLabel
                * body: ProtocolType<'Role, 'Scope, 'Message, 'Effect>
    | Var      of label: RecLabel
    | End

and RecLabel = RecLabel of string
```

**What changed from prior drafts:**

- `CommSemantics` narrows to a single-case DU for v1 (`AsyncFIFO`). The other two variants (`Sync`, `AsyncCausal`) move to Appendix K as extension points that activate when targeting backends whose runtime realizes them. Keeping `CommSemantics` as a DU rather than removing it entirely preserves the extension shape — adding `Sync` or `AsyncCausal` later is an additive change to the DU, not a signature change to `Nested`.
- `SupervisionPolicy` is removed from the `Nested` constructor entirely. The v1 supervisor floor (§6) implements a fixed observation-plus-resumption policy; finer-grained supervision requires targeting a supervision-capable backend (Appendix K), where the annotations reappear as backend-specific extensions.
- `RecLabel` unchanged — internal bookkeeping for binders in recursive protocols. The author doesn’t typically name these; the CE builder generates them or they come from source-level markers. It is *not* an author DU.
- `History` and `DeepHistory` unchanged — Harel pseudo-states resolved by journal lookup (§9.1). They have meaning only as transition targets inside a `Nested` scope: entering a `History(scope)` means “resume at the last substate this scope was in when it was last exited”; entering a `DeepHistory(scope)` applies that rule recursively at every level below. Without the journal, history states have no semantics. Not every Harel feature is in the algebra — entry/exit actions, internal vs. external transitions, and explicit final pseudo-states for compound states are documented gaps; see Appendix F.

The CE builder is a thin wrapper that lets the above be written in idiomatic F# style:

```fsharp
type ProtocolBuilder<'Role, 'Scope, 'Message, 'Effect>() =
    member _.Yield(x: ProtocolType<'Role, 'Scope, 'Message, 'Effect>) = x
    member _.Bind(m, f) = Sequence(m, f ())
    member _.Combine(m1, m2) = Sequence(m1, m2)
    member _.Zero() = End
    member _.Delay(f) = f ()

let protocol<'Role, 'Scope, 'Message, 'Effect> =
    ProtocolBuilder<'Role, 'Scope, 'Message, 'Effect>()
```

In practice an F# implementer will inject concrete DUs once and alias the instantiated type (`type MyProto = ProtocolType<Role, Scope, Message, Effect>`) to keep downstream signatures readable. That’s an implementation concern; the algebra itself stays generic.

Example — a three-role order fulfillment with two levels of hierarchy (Customer, Merchant, Warehouse; OrderPhase containing InventoryPhase and ShippingPhase) is written as a single CE that evaluates to a `ProtocolType<Role, Scope, Message, Effect>`. The full example is in Appendix J, with the v1 algebra shape: `Nested` takes no `SupervisionPolicy` argument, and `CommSemantics` values are always `AsyncFIFO n` for some bound.

## 4. Role projection

Projection is the operation that converts a global `ProtocolType` into a per-role state machine. Frank uses the Li/Stutz/Wies/Zufferey algorithm (CAV 2023):

1. Build a global automaton from the `ProtocolType`.
1. For each role, project by erasure — relabel transitions irrelevant to that role as ε.
1. Determinize via subset construction to produce a candidate local implementation.
1. Check implementability with the paper’s PSPACE decision procedure; fail the build with a witness if the protocol is unimplementable.

Full algorithm, F# pipeline sketch, and practical notes are in **Appendix A**.

Frank’s generator consumes both outputs: the per-role state machines drive actor generation, and the implementability check is a build-time gate. The reference implementation is the artifact at [zenodo.org/records/8161741](https://zenodo.org/records/8161741); Frank should port or wrap it rather than re-derive.

## 5. Effects

Every `Send` and `Recv` in the `ProtocolType` carries an effect annotation — the list of side-effects that step may invoke. Every `Nested` scope carries a `permittedEffects` list bounding what effects may occur within it.

The runtime encoding is a **witness-object higher-kinded-type pattern**. An `IEffectHandler<'eff, 'result>` interface carries `Map`, `Bind`, `Return`, and `Invoke`. The author implements this interface with their business logic and dependencies. The generated actor invokes effects through the interface without knowing or caring about the runtime carrier:

```fsharp
type IEffectHandler<'eff, 'result> =
    abstract Map    : ('a -> 'b) -> 'eff<'a> -> 'eff<'b>
    abstract Bind   : ('a -> 'eff<'b>) -> 'eff<'a> -> 'eff<'b>
    abstract Return : 'a -> 'eff<'a>
    abstract Invoke : EffectOp -> 'result
```

The carrier is `Task` (via `task { }` CE) or `Async` — .NET-native, well-maintained, structured concurrency acts as the implicit handler boundary.

The rationale for choosing this encoding — and for rejecting free monads, tagless final via external libraries, and true algebraic effects with delimited continuations — is in **Appendix B**.

The witness-object encoding has a known ergonomics cost: `App<'F, 'A>` brand types appear in every signature mentioning an effectful computation. In practice this cost is absorbed by F#‘s computation-expression machinery — per-protocol generated `EffectBuilder<'F>` CEs hide the brand, with custom operations per effect DU case, applicative `and!` for `Parallel`, `match!` for `Choice` projection. Authors write what looks like ordinary `task` or `async` code; the brand never appears in author-facing types. The composition limits the witness-object pattern inherits from any monadic encoding (Hutton’s constraint/liberation framing — applicatives compose for free, monads do not) are also covered there. See **Appendix N** for the full treatment.

## 6. Communication and supervision

The algebra makes three runtime shape concerns first-class so that projection, actor generation, and verification all have concrete semantics to work with:

- **Scope** — identifies `Nested` regions. Author-supplied DU (e.g., `type Scope = OrderPhase | InventoryPhase | ...`). Bubble-up of unhandled events walks the parent chain in the projected statechart.
- **Channel** — distinguishes channels scoped to different regions. A channel is structurally `from: 'Role * to_: 'Role * within: 'Scope`. A role participating in an outer protocol and an inner `Nested` has distinct channels for each scope; conflating them would create phantom races.
- **`CommSemantics`** — `AsyncFIFO` in v1 (bounded per-pair FIFO). `Sync` (CSP rendezvous) and `AsyncCausal` (vector-clock causal delivery) are backend-activated extensions (Appendix K).

Details and tradeoffs per variant are in **Appendix E**.

**Supervisor — the v1 floor.** The generator emits one `ProtocolSupervisor` per protocol instance, running in the same OS process as its actors. Its policy is fixed — not configurable, not annotation-driven. Behaviors:

- **Crash detection.** Subscribe to each actor’s error event.
- **Journal access.** On actor crash, read the actor’s journal (§9.1) to recover last-committed state. Because the supervisor is same-process, journal access is a direct in-memory read in v1 (SQLite read once durable backends land, per Appendix M).
- **Single resumption attempt.** If the journal is readable and non-empty, spawn a replacement actor and hand it the journal for replay from the last committed state. The replacement resumes with statechart history restored and the projected protocol state intact.
- **Fallback to unwind.** If the journal is unreadable, missing, corrupt, or if the replacement actor also crashes, the supervisor marks the role dead in the shared peer map, emits a supervision event to the journal (for downstream projection to logs/traces), and lets the protocol unwind. Surviving actors see the dead peer on their next send attempt and terminate in turn.

*In-flight messages at the moment of crash are lost.* The journal records committed state, not pending I/O. This is the honest limitation of a good-faith minimal resumption — cross-actor consistency during partial failure is genuinely hard and belongs with the alternative-backend upgrade path (Proto.Actor, Orleans, event-sourced frameworks with at-least-once guarantees), not the MailboxProcessor floor.

*Explicitly out of scope for this floor:*

- Restart strategies (`one-for-one`, `all-for-one`, `rest-for-one`, escalation up a tree).
- Backoff policies, restart limits, retry budgets, circuit breakers.
- Supervision trees; hierarchical supervision with escalation.
- Idempotency and at-least-once delivery guarantees during resumption.
- Cross-process or distributed supervision.

Richer supervision — strategies, backoff, trees, at-least-once — is not more work for Frank’s own runtime; it is work that existing .NET actor frameworks have already done. The intended path for richer semantics is to target one of those frameworks as an alternative generator backend rather than reimplement their machinery inside Frank. Candidates and the mapping discipline, including how the journal interface maps onto each framework’s native persistence, are in **Appendix K**. Appendix K also covers how `SupervisionPolicy` annotations — removed from the v1 algebra — reappear in backend emission when the target backend has native supervision semantics to bind them to.

This floor is deliberately minimal but is a **good-faith resumption implementation**, not a stub — enough to demonstrate that Frank’s architecture supports the capability and that the journal is doing real work, without pretending to production-grade supervision.

## 7. The generated actor

Frank’s v1 generator emits one canonical actor shape: **`HierarchicalMPSTActor`**, the full multiparty-plus-hierarchy-plus-effects case. Protocols with fewer roles, no hierarchy, or restricted effect sets are degenerate instantiations of this one template, not separate templates.

This is deliberate. Frank’s real v1 protocols (two-player game with observers; three-party protocol with parallel layers) already exercise the full template. Maintaining four divergent emission templates — one for each point on the roles-×-hierarchy grid — quadruples the codegen surface, the property-based conformance surface (§8.1), and the generated-code review burden, in exchange for cleaner generated code in cases the v1 protocols do not hit. Optimization passes that specialize the canonical shape for simpler protocols are deferred; they land when profiling or code review identifies specific specializations as worthwhile.

|Shape                        |Roles?|Hierarchy?|Status in v1                                                |
|-----------------------------|------|----------|------------------------------------------------------------|
|`HierarchicalMPSTActor`      |yes   |yes       |**Canonical. The v1 template.**                             |
|`FlatStatechartActor`        |no    |no        |Deferred optimization — single-role + depth-1 specialization|
|`HierarchicalStatechartActor`|no    |yes       |Deferred optimization — single-role specialization          |
|`FlatMPSTActor`              |yes   |no        |Deferred optimization — depth-1 specialization              |

The canonical template is a `MailboxProcessor` skeleton parameterized by the effect handler, the peer actor registry, and the parent scope reference (which is `None` at the outermost scope). Author-supplied specialization lives exclusively in the effect handler. The actor body — state dispatch, message routing, scope escape, effect invocation — is sealed and generated.

```fsharp
// v1 canonical actor shape. The body is generated; the author never edits it.
type HierarchicalMPSTActor<'Role, 'Scope, 'State, 'Message, 'Effect, 'Result>(
    myRole: 'Role,
    effectHandler: IEffectHandler<'Effect, 'Result>,
    peers: Map<'Role, MailboxProcessor<'Message>>,
    parentScope: MailboxProcessor<'Message> option
) =
    member _.Start () = MailboxProcessor.Start (fun inbox -> (* generated body *))
```

The internal shape of the generated body for the hierarchical-plus-parallel case is itself an open question (open question 11, §13). The naive option is ad-hoc async sequencing (`Task.WhenAny`, channel polling, threaded await). The contingency strategy (**Appendix O**) describes a uniform CML-style choice-combinator template using delimited-continuation-style primitives. The decision is settled by the experiment at §12 step 6 (positioned between analysis step 5 and canonical-actor-emission step 7). Adopting a single canonical template makes the experiment *more* important, not less — with one template to commit to, the emission strategy it commits to propagates through every protocol Frank generates.

### 7.1. Design rationale for the canonical actor shape

Code generation’s value lies in reducing the gap between a formal specification and a correct implementation. For protocols that are already simple to implement by hand — flat, single-role statecharts with synchronous communication — that gap is small. A developer can write the state machine directly, verify it locally, and reason about correctness without automation. The payoff from code generation in these cases is marginal relative to the engineering cost of building and maintaining a generator. The protocols where code generation delivers substantial value are those where manual implementation is error-prone: hierarchical structures with nested scopes, multiple concurrent roles, and effects that must be sequenced correctly across role boundaries. Tic-tac-toe with observers, multi-party order fulfillment workflows with parallel approval chains — these are cases where subtle bugs emerge from the interaction between roles and hierarchy. A generated actor network that provably respects the protocol’s structure catches those bugs at emission time, not in production.

Frank’s authoring surface is a computation expression in F# — simple, flat, and declarative. A developer specifying a multi-party protocol writes clean CE code without thinking explicitly about actors, channels, or supervision. Behind that surface, the generator must produce a reliable network of communicating actors that faithfully implements the spec. That translation — from declarative protocol description to a correct, well-coordinated actor network — is where the compiler’s work happens. Code generation without a strict, canonical target shape produces highly variable output quality. Without mathematical grounding and repeatable patterns, a generator has no reliable anchor for decisions about actor scope, role boundaries, channel topology, or effect sequencing across the network. This variability undermines both correctness and the developer’s ability to reason about the generated system’s behavior.

The anchor for that canonical shape is a deliberate duality between the authoring surface and the runtime. Authors write what reads as delimited-continuation-shaped code: sequences of suspensions, resumptions, and choice points expressed declaratively in the CE, drawing on the patterns Reppy’s CML makes precise. The generator takes that author-facing shape and projects it down to an actor network — `MailboxProcessor` instances with mailboxes, peer references, and supervisor-backed restart semantics. What the author reasons about in continuation-shaped terms runs, at the bottom, as actors communicating over bounded FIFO channels. The two sides are not the same formal model; they are paired. The CML framing gives the authoring surface a repeatable vocabulary; the actor model gives the runtime a mature, well-understood execution substrate; the generator is what makes the pairing cohere.

This duality is what a single canonical actor shape makes tractable. With one template to commit to, the continuation-shaped author model has a single, predictable actor-network image on the other side of the generator. Every protocol, no matter how simple or complex, compiles to actors shaped by the same canonical structure — which ensures consistency and correctness across different protocols and generator invocations, and gives developers a predictable mental model of what the runtime network looks like, even though they never write that code themselves. Multiple templates would mean multiple mappings from the author surface to the runtime, each of which has to preserve the duality independently; one template makes the mapping a single artifact to get right.

(The specific application of these CML-style patterns to parallel-composite emission — a separate, codegen-internal concern — is developed in Appendix O as a still-contingent decision. §7.1’s principle argues for a canonical actor shape as the runtime image of the continuation-shaped author model; Appendix O argues for a canonical *emission template* within that shape for parallel composites. Both instances of the same design principle, operating at different layers; the experiment in §12 settles the Appendix O half.)

The canonical actor shape Frank targets is `HierarchicalMPSTActor` — the full case supporting multiple roles, nested hierarchy, and effectful transitions. Simpler protocols still receive this shape because the shape doesn’t impose overhead when the protocol is simple; it’s the semantically correct foundation. Three degenerate optimizations (flat single-role, flat multi-role, hierarchical single-role) are possible — documented in the §7 table as deferred and listed in §12 step 14 as future refinements — but they are not the primary v1 target. The payoff justifies the complexity only when profiling or code-review feedback indicates that generated code size or runtime characteristics warrant specialization.

## 8. Verification

Verification is a list of distinct properties, each with its own query shape, organized into two tiers for v1 plus a future-tier track for deferred properties:

- **Tier 1 — structural, always on.** Implementability, linear channel use, effect discipline, supervision soundness, provenance completeness. No SMT required; the generator walks the tree.
- **Tier 2 — Z3-backed, project-configurable.** Deadlock freedom, race freedom in `Parallel`, payload refinement.

Liveness/progress and resource bounds — the former Tier 3 — move to Appendix Q alongside other deferred verification machinery. They re-enter the v2+ conversation once v1 is shipping.

The full 10-item property table, including the SMT-LIB query shape for each, is in **Appendix D**. Property #10 (provenance completeness) stays Tier 1, with a note that the completeness check activates once the parallel semantic-model work lands the PROV-O vocabulary the journal’s extension field (§9.1) will carry. Until then the check is a structural well-formedness pass; the vocabulary-binding check is additive when the vocabulary is defined.

### 8.1. Conformance testing — the v1 codegen correctness gate

Z3 verification in v1 runs over the *source* `ProtocolType` value — design-time properties like deadlock freedom, race freedom, and payload refinement. It does **not** verify that `Frank.CodeGen`’s emitted actor code correctly implements the projection.

That second class of check — generated-code-versus-projection equivalence — is v1’s correctness gate for codegen, but the mechanism is property-based conformance testing, not SMT. For each generated actor, an FsCheck-style harness generates sequences of protocol events valid under the projected local type and asserts the actor accepts them; it also generates invalid sequences and asserts the actor rejects them. Failures produce concrete counterexample traces rather than SMT counterexamples, which is the right shape for debugging emission bugs. The harness and library structure live in `Frank.Protocol.Testing`, detailed in **Appendix P**.

The SMT-against-emission approach — proving the generated actor’s reachable state graph equals the projection’s — is the higher-assurance upgrade path, documented in **Appendix Q**. It shares the SMT-LIB encoder with Tier 2 protocol verification (§10.9.3) and can be activated per-protocol when the conformance harness’s confidence level is insufficient. V1 ships the conformance harness as the gate; SMT-against-emission is additive.

Z3 integration for Tier 1 structural and Tier 2 property verification runs **inside `Frank.CodeGen`**. A build that produces generated `.fs` files without verification approval is not a valid build. Details — inline invocation, failure reporting, and the deferred artifact-emission upgrade — are in §10.9.

## 9. Journal and projections

Frank’s runtime follows a **single-producer, multi-consumer** architecture for execution records. One canonical in-process journal is written to on every semantically relevant event; downstream *projections* read the journal and emit into specific sinks (logs, traces, semantic-graph formats, interpreter inputs). Nothing in Frank’s runtime writes to two places in parallel.

### 9.1. The journal

The journal is the **canonical record** of a protocol instance’s execution. Everything that reasons about what the protocol has done reads from it.

Consumers of the journal:

- **Harel history resolution** — shallow (`H`) and deep (`H*`) history states read the journal on re-entry to restore the last substate.
- **Crash resumption** — the supervisor (§6) reads a crashed actor’s journal to restart execution at the last committed position.
- **Effect handlers** — handlers can read journal history in-process to make decisions, rather than falling back to external log lookups.
- **Runtime interpreters** — trace reconstruction, reachability, dry-run, replay, debug views (§10.9.3) operate over the journal.
- **Projections** — MEL log records, OpenTelemetry spans, PROV-O / RDF semantic graph data are all derived from journal entries.

*Storage — v1 default.* **In-memory is the v1 default.** The journal lives for the lifetime of the protocol instance and is garbage-collected when the instance ends. This is sufficient to exercise:

- History resolution (all history the protocol needs is same-instance).
- Handler-side journal reads.
- The supervisor’s single-resumption attempt (if the actor crashes and the journal’s owning process still lives, the journal is readable; if the whole process is lost, the protocol instance is lost too, which matches the MailboxProcessor floor’s consistency story).
- All runtime interpreters and projections.

What in-memory does *not* give you: durability across process restarts, operational inspectability via external tools, long-lived forensic history. Those land when SQLite is selected as the first durability upgrade (**Appendix M**). LMDB remains the performance escape hatch if profiling demands it.

Critically, every consumer codes against the **`IProtocolJournal` interface**, not against the backend. Swapping in-memory for SQLite, or SQLite for LMDB, or either for an alternative-backend-native persistence story (Orleans grain state, Proto.Actor event sourcing, Dapr state store — all in Appendix K), is a configuration change, not a rewrite. The interface is the stable contract.

*Scope.* One journal per stateful actor, in-process. The supervisor (same process, §6) has read access to all its actors’ journals for consolidation and resumption. Cross-process or cross-machine journal access is explicitly out of scope for the initial implementation; those use cases go through alternative backends (Appendix K) whose own persistence stories subsume the journal role.

*Minimal core schema.* Frank’s own consumers need the following per entry:

- Event type (state entry, state exit, message sent, message received, effect invoked, scope entry, scope exit).
- Timestamp.
- Actor role (`'Role`).
- Scope path (list of `'Scope` values from outermost to innermost active scope).
- State identifier (projection-specific).
- Message reference if applicable (`'Message` tag and payload, or a payload reference).
- Effect reference if applicable (`'Effect` case and any result).
- Opaque **extension field** for semantic enrichment — a polymorphic bag (arbitrary structured value, serialized when a durable backend is active) that consumers ignore if they don’t understand it.

The extension field is the **intersection point** with the parallel semantic-model work (RDF, OWL, SHACL, PROV-O). That work defines the common core vocabulary and extension mechanism for domain-specific enrichment; Frank’s runtime ships with the field as a pass-through placeholder and commits to not corrupting or discarding its contents. Projections that understand specific extension shapes (a PROV-O projection once the vocabulary is defined) read those shapes; projections that don’t, pass them through or ignore them.

### 9.2. Projections

Projections are stateless transformations over the journal. Each projection has a single responsibility and a single downstream sink.

**`Microsoft.Extensions.Logging` projection** — the canonical observability projection, shipped by default. Emits structured `ILogger<_>` records for every journal entry. Works with every `Microsoft.Extensions.Logging`-compatible sink — Serilog, NLog, console, Seq, Elasticsearch, Grafana Loki, application Insights, whatever the host configures. `Microsoft.Extensions.Logging` is specifically the right layer because it is the .NET idiomatic logging interface and decouples Frank from any particular sink.

**OpenTelemetry projection** — the canonical distributed-tracing projection, shipped alongside MEL. Emits spans around message send/receive, effect invocation, and state transitions. Trace context propagates via message metadata so a multi-role protocol shows up as one connected trace across actors. Recommended when protocols span process boundaries or when end-to-end tracing matters; optional otherwise.

**PROV-O / semantic-graph projection** — the planned projection for the semantic-model parallel track. Reads journal extension fields populated with provenance vocabulary and emits RDF or JSON-LD. Not shipped in the initial Frank release; specified by the semantic-model work. Frank’s contract is that the journal’s extension field will carry whatever payload the semantic work defines, and projections can be added later without changes to the actor runtime.

**Custom projections** — author code that consumes the journal for domain-specific exports. Frank exposes a read interface over the journal (query, subscribe, time-range filter, replay) that custom projections use. Interpreters (§10.9.3) are a specialization of this category.

### 9.3. What this replaces

Previous drafts of this work framed observability as Serilog-first with no internal journal. That framing was wrong: statechart history and crash resumption need an in-process record that external logging sinks cannot provide (completeness, ordering, and read-latency guarantees), and projecting observability from a journal is cleaner than having the runtime emit to observability sinks directly. The new framing is strictly more capable — everything the old framing did is now a projection — and the single-producer property eliminates a class of drift bugs between parallel emission pathways.

## 10. Code-generation pipeline

Three libraries, one build-time task:

- **`Frank.Protocol.Analysis`** — pure F#, no I/O. Takes `ProtocolType`; produces projected state machines, implementability results, and structured descriptions of what generated actors should contain. Fully unit-testable. No dependency on FCS, MSBuild, or any generator framework. This is where the hard logic lives.
- **`Frank.CodeGen`** — takes the structured descriptions from `Frank.Protocol.Analysis` and emits F# source text for actors, handler interfaces, log schemas, and SMT-LIB queries. Uses **Fabulous.AST** to construct F# syntax trees; Fantomas formats the output. Invokes Z3 on the emitted queries before returning success, so a non-verifying protocol fails the build at generation time. Details on Z3 integration and artifact emission are in §10.9.
- **`Frank.Build.MSBuildTask`** — thin MSBuild task. Finds CE input files, invokes `Frank.CodeGen`, writes outputs to the build’s intermediate directory, adds them to the compile list. Extends the MSBuild integration already proven in adjacent work.

The generator does **not** use Myriad. The rationale — including a full enumeration of the specific ways Myriad’s design center conflicts with Frank’s needs — is in **Appendix C**.

Target architecture for emission:

```fsharp
// Inside Frank.CodeGen
open Fabulous.AST
open type Fabulous.AST.Ast

let generateActorFor (protocol: ProtocolType) (role: Role) : string =
    // Analysis in the pure library — no generator coupling
    let projected = Frank.Protocol.Analysis.project role protocol
    let implCheck = Frank.Protocol.Analysis.checkImplementable protocol
    if not implCheck.Ok then failwith implCheck.Reason

    // Emission via Fabulous.AST: structured, compiler-checked, Fantomas-formatted
    Oak() {
        Namespace "Frank.Generated" {
            Module $"{role}Actor" {
                // Build the actor body from projected states.
                // Fabulous.AST combinators ensure well-formed F#.
                // Fantomas handles layout and formatting.
            }
        }
    }
    |> Gen.mkOak
    |> Gen.run
```

## 10.9. Z3 verification: v1 scope and deferred upgrades

### 10.9.1. Where Z3 runs in v1

Z3 verification in v1 is part of `Frank.CodeGen` and targets one thing: **properties of the source `ProtocolType` value**. The pipeline per protocol:

1. Read the CE; evaluate to a `ProtocolType` value.
1. Run `Frank.Protocol.Analysis` — projection, implementability.
1. Emit actors, handler interfaces, and log schemas via Fabulous.AST.
1. Emit SMT-LIB queries over the source `ProtocolType` for Tier 1 structural and Tier 2 property checks (§8).
1. Invoke Z3 on the emitted queries **inline**; fail the build with a pointer to the offending construct if any property returns `sat` on the negation.

The Z3-against-emission approach — proving that `Frank.CodeGen`‘s emitted actor code preserves the projection’s reachable state graph — is **not** part of v1. That role is played by property-based conformance testing (§8.1, **Appendix P**), which is cheaper to build, produces concrete counterexamples, and covers the same class of codegen bugs.

SMT-against-emission remains valuable as a higher-assurance upgrade when conformance testing’s confidence level is insufficient. The full machinery — encoding, artifact emission, caching — is documented in **Appendix Q**.

A build that produces generated `.fs` files without Z3 approval (on the properties that are in scope for v1) is not a valid build. Verification is not optional polish; it is part of the definition of codegen success.

### 10.9.2. Inline invocation, deferred artifact emission

V1 runs Z3 inline and reports pass/fail/timeout with a pointer to the offending construct. SMT-LIB query files, the verification manifest, build caching, cross-solver verification against CVC5/Yices/Bitwuzla, and byte-stable emission for regression pinning — all covered in prior drafts — move to **Appendix Q** as operational extensions.

The argument for the deferral is pragmatic. Inline invocation with failure reporting is enough to make the verification gate real. The artifact story pays back when Z3 heuristics shift across versions, when independent verification against other solvers becomes necessary, or when build caching becomes a meaningful fraction of build time — all real concerns, none urgent for v1.

Nothing in the deferred upgrades changes the inline behavior. Adding artifact emission later is additive: the same queries get written to `obj/frank/verification/` alongside a manifest, and the inline invocation reads from the written artifacts. The v1 build will not need to be rewritten.

### 10.9.3. Z3 as a second path for some planned interpreters

Frank’s interpreters fall into two categories based on their input source, not on their output.

**Runtime interpreters consume the journal (§9).** Trace reconstruction, debug views, audit reports, reachability replay, dry-run simulation — all read the in-process journal directly, using the same query interface projections use. This is the correct input source: the journal is Frank’s single producer of execution records, so interpreters reading from it see the same events any other consumer sees, in canonical form. They do not read log streams (logs are a projection, not a source of truth) and they do not need their own event schema.

**Static-analysis interpreters consume SMT solvers, typically Z3.** They operate on the *protocol description itself* rather than on runtime records. The archetypal example is a **deadlock / reachability interpreter**: encode the protocol’s reachability structure as an SMT formula and ask Z3 whether any deadlock state is reachable. If it is, Z3 produces a counterexample trace — a concrete sequence of role interactions leading to the deadlock — without the protocol ever needing to run. The same pattern applies to message-race analysis (can two parallel branches write the same resource key?). Liveness and resource-bound analyses — former Tier 3 — also fit this shape; they land alongside the tier-upgrade work in **Appendix Q**.

These overlap with the Tier 2 properties from §8, but the *interface* differs: codegen-time verification fails the build on unsat; a static-analysis interpreter is invoked by the author during protocol design, answers questions, and reports without necessarily failing anything. Same Z3 backend, different consumer.

The SMT-LIB emitted by `Frank.CodeGen` for codegen-time verification and the SMT-LIB emitted for static-analysis interpreters share most of their structure. The `ProtocolType`-to-SMT-LIB encoding factors out into a helper library that both consume; the difference is mostly in the query — “prove no deadlock” versus “find a deadlock if one exists,” which is usually the negation.

Two categories of interpreter in Frank, then:

- **Journal-consuming runtime interpreters (§9).** Traces, debug views, audit, replay, dry-run. Read the in-process journal via the read interface §9.1 exposes.
- **Z3-consuming static-analysis interpreters (§10.9.3).** Deadlock detection, race analysis. Read the protocol description; produce SMT-LIB queries; invoke Z3. Liveness and resource-bound analyses activate alongside the Appendix Q tier-upgrade work.

Both are useful; both are worth building; they are distinct in implementation. Neither reads the log stream — logs are a downstream projection of the journal, intended for operators, not for Frank’s own tooling.

## 11. Intake pathway

The v1 generator accepts one input source: the F# CE directly. Authors write the CE together with the DUs for roles, states, messages, and effects; Frank reads the CE and generates.

A second pathway — CLI intake reading Scribble, SCXML, or other design-document formats and emitting F# DU files plus a CE file — is deferred. The CLI extends existing format-parser work from adjacent tooling. Details on the intake format, the lossy-subset documentation for round-trip semantics, and the CLI structure are in **Appendix R**. The runtime artifacts Frank emits are identical across both pathways; only the origin of the CE + DUs differs.

The target end-state beyond v1 is a staged intake pipeline rather than a flat set of format adapters: **sketch formats** (mermaid state diagrams, smcat) at the authoring edge for rapid, whiteboard-shaped design; **rigorous formats** (SCXML, Scribble, the F# CE) as algebra-faithful carriers; and an LLM-assisted **lift** stage that promotes a sketch to a rigorous format with human review before the generator runs against it. The CE remains one rigorous-format option alongside SCXML and Scribble, not the privileged one. Appendix R carries the tier structure, the lift’s failure modes, and the CLI shape. The v1 algebra and codegen do not need to change to accommodate this; the algebra is already the narrow waist every intake path lowers into.

## 12. Build order — layered, independently testable

**This section matters as much as the architecture above.** The prior attempt at this work (Appendix G) failed in part because integration was claimed before it was verified. The layered build order below is designed to make each layer testable in isolation and to make integration a discrete, verifiable step rather than a diffuse assumption.

Build in this order. Do not move to the next layer until the previous is unit-tested.

1. **`ProtocolType` value type + hand-written smoke instances.** Verify the algebra represents the protocols you want to express. No CE yet. No generator yet.
1. **CE builder.** Verify by hand-comparing CE output to the smoke instances from step 1.
1. **`Frank.Protocol.Analysis` — projection only.** Unit test against known global types.
1. **`Frank.Protocol.Analysis` — implementability check.** Port the Li-et-al. PSPACE decision procedure. Unit test against Example 2.2 from the paper (positive and negative cases).
1. **`Frank.Protocol.Analysis` — structured actor descriptions.** Produce the data that `Frank.CodeGen` will consume. This is *not* generated F# source; it is a data description. Unit test.
1. **Experiment between steps 5 and 7 — emission strategy for hierarchical parallels.** Before committing the codegen pipeline to a single emission template for parallel composite states, run a small comparative experiment. Pick one moderately complex statechart from Frank’s design — at least two levels of hierarchy and at least one parallel composite with two or more regions. Emit it twice from the structured actor descriptions produced in step 5: once with ad-hoc async sequencing (`Task.WhenAny`, channel polling, threaded await), once with the CML-style choice-combinator template described in **Appendix O**. Compare on three axes: (a) readability of the generated code; (b) the cost of adding a new region or a new level of nesting; (c) the maintenance burden when the statechart logic changes. Decide before completing step 7. The cost is a few hours; the value is preventing the LLM-anchoring problem (Appendix G) where, once a generation strategy is established, codegen tools tend to perpetuate it even when the early choice was wrong. With v1 shipping one canonical actor template, this decision propagates through every generated actor — making the experiment more important, not less.
1. **`Frank.CodeGen` — canonical `HierarchicalMPSTActor` emission via Fabulous.AST.** Produce F# source text for the full multiparty-plus-hierarchy-plus-effects shape. Compile the output separately to verify it parses and typechecks. Run the compiled actor against a hand-written effect handler and verify behavior end-to-end against a multi-role hierarchical protocol. This is the v1 generator’s big integration step.
1. **`Frank.Protocol.Testing.Traces`** — FsCheck generators for valid and invalid trace sequences derived from a projected local type. Pure library; no actor dependency. Unit test.
1. **`Frank.Protocol.Testing.Conformance`** — the runner that drives generated traces against a generated actor and asserts acceptance/rejection. This is the v1 codegen correctness gate (§8.1). Wire it into the test suite; consider whether to promote it to a build gate for specific protocols (Appendix P discusses the trade).
1. **`Frank.CodeGen` — handler interface and `EffectBuilder<'F>` emission.** Emit `IEffectHandler<'Effect, 'Result>` and the per-protocol CE builder that absorbs the witness-object brand (§5; Appendix N).
1. **`Frank.CodeGen` — log-schema emission.** Produce the structured document describing what the generated actor emits to the journal at runtime.
1. **`Frank.Build.MSBuildTask`.** Integrate with the existing MSBuild machinery. End-to-end test: author writes a CE, build produces compiled actors, actor runs.
1. **(Deferred) `Frank.CodeGen` — SMT-LIB query emission and Z3 execution.** Start with Tier 1 properties, then Tier 2. Inline Z3 invocation; `Microsoft.Z3` dependency. Artifact emission and manifest are further deferred to Appendix Q operational extensions.
1. **(Deferred) Actor-shape optimization passes.** Specialize the canonical template for single-role protocols, for flat protocols, and for single-role flat protocols (three specializations in total). Each specialization is a codegen pass that replaces the canonical emission with a more compact shape when the input protocol has the relevant degenerate structure. Drive the work by profiling and generated-code review, not speculatively.
1. **(Deferred) Pathway 2 — CLI intake, design-doc parsers.** Build on top of existing format-parser work (Appendix R).
1. **(Deferred) Z3-backed static-analysis interpreters.** Deadlock / reachability, race analysis (§10.9.3). Shares the SMT-LIB encoder with step 13.
1. **(Deferred) Z3-against-emission for higher-assurance conformance.** Per Appendix Q. Activates per-protocol when property-based conformance testing’s confidence level is insufficient.
1. **(Deferred) Liveness, resource bounds, timed automata.** Former Tier 3 properties and the timed-automata layer sketch. Appendix Q.

**Discipline checks, non-negotiable:**

- Every layer has unit tests that pass without the layer above it present.
- Integration tests are *additional*, never substitutes for unit tests.
- Do not claim LLM-generated code is integrated until a test that exercises the integration passes. Write the test first.
- Do not move on to step N+1 if step N has any unexplained behavior.
- Commit after every working step. Branch before major integration work.
- When an LLM says “it’s wired up,” assume it is not, and verify.

Appendix G explains where these rules come from.

## 13. Open questions

### Closed since the prior draft

- **Former #4 (default communication semantics).** Closed. `AsyncFIFO` is the v1 default and the v1 algebra’s only case. `Sync` and `AsyncCausal` re-enter via Appendix K when a backend that realizes them is targeted.
- **Former #7 (Scribble and SCXML round-trip semantics).** Closed for v1. The single v1 intake pathway is the CE directly; intake formats are deferred to Appendix R.

### Reframed

- **#5 (Verification tier configuration).** Now a two-tier question for v1 (Tier 1 always on, Tier 2 project-configurable). Former Tier 3 properties move to Appendix Q as deferred upgrades.
- **#9 (Journal event schema — the stable core).** Unchanged in substance; updated to note in-memory is the v1 backend, and the schema must serialize cleanly when SQLite/LMDB backends land (Appendix M).

### Still open

1. **Implementability porting strategy.** Port the Li-et-al. decision procedure into F# directly, or shell out to a packaged version of the artifact at [zenodo.org/records/8161741](https://zenodo.org/records/8161741)? Port probably; prototype may shell out.
1. **Nested-state event semantics — precise rule.** With an explicit `'Scope` parameter, bubble-up is a parent-chain walk over scope values. The modal-refinement rule that says *what* a role’s local type inherits from its containing `Nested` needs formalization. Larsen-style modal transition systems are the likely foundation.
1. **Role refinement operator.** A child `Nested` narrows both the valid message set and the permitted effect set for a role inside it. Formalize as modal refinement rather than ad-hoc projection logic.
1. *(Former #4 closed — see above.)*
1. **Verification tier configuration.** Attribute or project-level setting that selects the active tier per generated unit. Tier 1 default everywhere; Tier 2 opt-in per protocol.
1. **PROV-O vocabulary mapping.** The `Emit` effect and property #10 give the hook. Define the exact PROV-O mapping and how it populates the journal’s extension field (§9.1), which a PROV-O projection will then consume. Coordinate with the parallel semantic-model work. Note: PROV-O landing is scheduled before Frank v1 completion, so the mapping question will close by the time step 11 (log-schema emission) lands.
1. *(Former #7 closed — see above.)*
1. **Timed-automata layer.** Real systems care about deadlines; the `Timer` effect is a placeholder. Needs a separate sketch. Appendix Q.
1. **Journal event schema — the stable core.** The minimal schema §9.1 sketches needs to be pinned down: exact event-type enumeration, serialization format for payload references, extension-field encoding convention. The schema must serialize cleanly when SQLite/LMDB backends land. Coordinate with the parallel semantic-model work that defines the extension vocabulary.
1. **Clef runtime target.** Once .NET is validated, verify portability by targeting Clef. Natural cross-compilation test.
1. **Choice-structure emission target — commit to it or stay with ad-hoc async?** Appendix O describes a uniform codegen template for hierarchical parallels using CML-style choice combinators backed by delimited-continuation-style primitives. Decision deferred pending the experiment at §12 step 6. **More important under the single-canonical-template regime** (§7), because the emission strategy propagates through every generated actor. The decision must be settled before step 7 of the build order completes.
1. **Delimited-continuation primitive — custom type or F#-native async/task?** If the choice-structure emission target is adopted (open question 11), the underlying primitive can either be a custom `Continuation<'a>` / `Shift` / `reset` type matching the HoPac / CML literature, or an emulation using F#‘s native `async` / `task` CE with deliberate suspension boundaries. The custom type makes suspension points explicit (clearer codegen template) but adds either a runtime dependency on something HoPac-shaped or a hand-rolled implementation on `Async.FromContinuations`. The native approach reuses what’s already there but the suspension points are implicit (the runtime decides where to yield), trading some structural clarity for fewer moving parts. Decide together with open question 11; settling one without the other risks committing to a primitive without a use case or to a use case without a workable primitive.

### New

1. **Trace generation strategy for the conformance harness.** Exhaustive enumeration up to a depth bound, random generation with coverage metrics, or a hybrid? Exhaustive is tractable for small protocols but blows up fast; random scales better but gives no completeness guarantee. The literature on model-based testing for session types has prior art worth surveying before committing. Decision required before step 8 of the build order completes.
1. **Conformance gate — test-time or build-time?** Running the conformance harness as part of the test suite keeps build times honest; running it as a build gate raises the confidence level at the cost of longer builds. The right answer is probably protocol-dependent (high-stakes protocols opt in to the build gate). Appendix P discusses the decision shape; attribute or project-flag surface is TBD.
1. **Sketch-vs-rigorous source-of-truth convention.** When the staged intake pipeline lands (§11, Appendix R), projects using sketch formats (mermaid, smcat) alongside rigorous formats (SCXML, Scribble, CE) need a convention for which artifact is authoritative in version control. If the sketch is source and the rigorous format is a derived build artifact, the author’s artifact stays legible and the lift is always re-runnable; merge conflicts resolve at the sketch layer. If the rigorous format is source and the sketch is a regenerated documentation view, the algebra-faithful representation is the canonical one and the lift only runs when the sketch is edited directly. The lock-file pattern from the v7.3.2 semantic-discovery work handles either case, but the convention affects review diffs, merge behavior, and what the LLM lift is allowed to rewrite. Probably per-project configuration; the default is “sketch as source, rigorous format as derived” because it matches the workflow a non-code author expects. Does not block v1 and does not need to close before the staged pipeline ships, but the CLI’s commit discipline has to pick a default.

### Decisions recorded (full rationale in appendices)

- Coalgebraic session types as the canonical internal representation (§2; Appendix B).
- F# CE as the authoring surface (§3).
- Algebra generic over author-supplied `'Role`, `'Scope`, `'Message`, `'Effect` DUs; `CommSemantics` (narrowed to `AsyncFIFO` for v1) and `RecLabel` fixed internal vocabulary (§3).
- `SupervisionPolicy` removed from the v1 algebra; re-enters via alternative-backend emission (Appendix K).
- Witness-object HKT pattern on `Task`/`Async` as the effect encoding (§5; Appendix B).
- Per-protocol generated `EffectBuilder<'F>` CEs absorb the witness-object brand from author-facing code (§5; Appendix N).
- **Single canonical actor template (`HierarchicalMPSTActor`) in v1** (§7). The three simpler shapes are deferred optimization targets. Real v1 protocols already exercise the full template. Design rationale in §7.1.
- **Journal-primary architecture.** Single in-process journal per actor is the canonical execution record; `Microsoft.Extensions.Logging`, OpenTelemetry, PROV-O, and interpreters are all downstream projections. No dual-producer emission paths. (§9)
- **Journal storage backend — v1.** In-memory is the v1 default; SQLite and LMDB are durability and performance upgrades respectively (Appendix M). Shared `IProtocolJournal` interface makes the swap a configuration change.
- **Minimum viable Harel.** Depth, orthogonal regions, shallow and deep history are first-class in the algebra. Entry/exit actions, internal vs. external transitions, and explicit final pseudo-states for compound states are documented gaps. (§3; Appendix F.1)
- Three-library code-gen pipeline on FCS + Fabulous.AST + existing MSBuild integration, rejecting Myriad (§10; Appendix C). **Fabulous.AST is the day-one emission target**, not a later migration — structured AST emission has already proven more reliable than string-based emission in adjacent work.
- Z3 verification integrated into `Frank.CodeGen`. V1 scope is source-protocol verification only (Tier 1 + Tier 2). Inline invocation, no artifact emission. Generated-code-versus-projection equivalence is handled by property-based conformance testing (§8.1, Appendix P). SMT-against-emission, artifact emission, and former Tier 3 properties are deferred to Appendix Q.
- **Conformance testing as the v1 codegen correctness gate.** `Frank.Protocol.Testing` as a sibling library to `Frank.Protocol.Analysis`; FsCheck-based trace generation from projected local types. (§8.1; Appendix P)
- **Supervisor floor — observation-plus-resumption.** Same-process supervisor with read access to each actor’s journal. On crash: read journal, attempt single resumption from last committed state, fall back to unwind on failure. In-flight messages at the moment of crash are lost. Fixed policy in v1; configurable supervision comes from targeting an existing actor framework as an alternative generator backend. (§6; Appendix K)
- Layered, independently testable build order, with the choice-of-emission experiment positioned at step 6 between structured actor descriptions (step 5) and canonical actor emission (step 7), and the conformance harness following the canonical actor template (§12; Appendix G).
- Frank is positioned as a design and engineering contribution, not a research contribution. One potentially novel formal extension — hierarchical statecharts combined with MPST projection — is flagged as contingent on a proper literature survey (Appendix L).

-----

# Appendices

-----

## Appendix A — The Li-et-al. projection algorithm

The CAV 2023 paper separates **synthesis** (building a candidate per-role state machine) from **implementability checking** (deciding whether the global type admits a faithful distributed implementation). Synthesis is automata-theoretic and always runs. Implementability is a decision procedure the paper shows is in PSPACE, improving the prior EXPSPACE bound.

### A.1. Step 1 — Build the global automaton GAut(G)

Given a global type *G*, construct an NFA whose states are syntactic positions in *G* and whose transitions are labeled by synchronous send-receive *interactions* of the form `p→q:m` (role *p* sends message *m* to role *q*).

- Choices in *G* become non-deterministic branching from the choice point.
- Recursion (μt. G) becomes back-edges to the binder state.
- The empty type 0 is the only accepting state.

The alphabet Σ_sync is the set of all `p→q:m` interactions appearing in *G*. Call this automaton GAut(G) = (Q_G, Σ_sync, δ_G, q₀, F_G).

### A.2. Step 2 — Projection by erasure GAut(G)↓p

For each role *p*, define Σ_p ⊆ Σ_sync as the interactions in which *p* participates as either sender or receiver. Construct GAut(G)↓p by relabeling every transition:

- if the interaction is in Σ_p, keep it (or split it into a send `p!q:m` or receive `q?p:m` event for *p*);
- otherwise replace its label with ε.

Formally: GAut(G)↓p = (Q_G, Σ_p ⊎ {ε}, δ↓, q₀, F_G). The state space and transition graph stay the same; only labels change. This is the paper’s **homomorphism automaton** for *p*: a homomorphic image of GAut(G) under the alphabet projection ⇓Σ_p, where x⇓Σ_p = x if x ∈ Σ_p and ε otherwise.

The result is an NFA with ε-transitions, typically non-deterministic because erased branches collapse together.

### A.3. Step 3 — Subset construction (determinization)

Apply the standard NFA-to-DFA subset construction to GAut(G)↓p. Each state of the resulting DFA corresponds to a *set* of states from GAut(G); the start state is the ε-closure of q₀; transitions on each symbol a ∈ Σ_p go to the ε-closure of the union of a-successors of every NFA state in the current subset.

Call this DFA C(G,p) — the **candidate local implementation** for role *p*. The paper proves L(C(G,p)) = L(G)⇓Σ_p: the candidate exactly captures the *p*-relevant trace projections of the global type.

### A.4. Step 4 — Implementability check

The hard question: is the family {C(G,p)}_{p ∈ P} actually a faithful distributed implementation of *G*? Two failure modes are well-known:

- **Sender ambiguity** — a non-chooser role cannot tell which branch was selected because no later message in its projection distinguishes them.
- **Receiver ambiguity** — symmetrical; the receiver cannot tell which sender’s message it is waiting for.

The paper gives succinct conditions characterizing implementability — predicates over GAut(G) checkable in PSPACE. They cover (informally):

- Every branch point in *G* is *eventually distinguishable* in every relevant role’s local view.
- Asynchronous send/receive ordering is consistent across roles.
- No role gets stuck waiting on a message no path will ever produce.

If the conditions hold, {C(G,p)} is a correct CSM (communicating state machine) implementation of *G*. If not, the paper’s witness extraction names the specific subprotocol that breaks implementability.

### A.5. F# sketch — the full pipeline

```fsharp
// === Step 1: Build GAut(G) ===
type Interaction = { Sender: Role; Receiver: Role; Message: string }

type GlobalAut = {
    States: int Set
    Alphabet: Interaction Set
    Delta: Map<int * Interaction, int Set>   // NFA: maps to a set of states
    Start: int
    Accept: int Set
}

let buildGAut (g: ProtocolType) : GlobalAut = failwith "TODO: tree -> automaton"

// === Step 2: Projection by erasure ===
type LocalEvent =
    | Send of to_: Role * msg: string
    | Recv of from_: Role * msg: string
    | Eps

let alphabetFor (role: Role) (g: GlobalAut) : Interaction Set =
    g.Alphabet |> Set.filter (fun i -> i.Sender = role || i.Receiver = role)

let project (role: Role) (g: GlobalAut) : NFAWithEps =
    let sigmaP = alphabetFor role g
    let relabel (i: Interaction) : LocalEvent =
        if   i.Sender   = role then Send(i.Receiver, i.Message)
        elif i.Receiver = role then Recv(i.Sender, i.Message)
        else Eps
    {
        States = g.States
        Alphabet = sigmaP
        Delta =
            g.Delta
            |> Map.toSeq
            |> Seq.map (fun ((q, i), qs) -> (q, relabel i), qs)
            |> Map.ofSeq
        Start = g.Start
        Accept = g.Accept
    }

// === Step 3: Subset construction ===
let epsClosure (nfa: NFAWithEps) (states: int Set) : int Set = failwith "TODO: standard"

let subsetConstruct (nfa: NFAWithEps) : DFA<int Set, LocalEvent> =
    let start = epsClosure nfa (Set.singleton nfa.Start)
    let mutable seen = Set.singleton start
    let mutable transitions = Map.empty
    let work = System.Collections.Generic.Queue([start])
    while work.Count > 0 do
        let q = work.Dequeue()
        for sym in nfa.Alphabet do
            let succ =
                q |> Set.collect (fun s ->
                        Map.tryFind (s, sym) nfa.Delta
                        |> Option.defaultValue Set.empty)
                  |> epsClosure nfa
            if not (Set.isEmpty succ) then
                transitions <- Map.add (q, sym) succ transitions
                if not (Set.contains succ seen) then
                    seen <- Set.add succ seen
                    work.Enqueue(succ)
    { States = seen
      Transitions = transitions
      Start = start
      Accept = seen |> Set.filter (Set.intersect nfa.Accept >> Set.isEmpty >> not) }

// === Step 4: Implementability conditions ===
type ImplementabilityResult =
    | Implementable
    | SenderAmbiguity   of branchPoint: int * conflictingRoles: Role list
    | ReceiverAmbiguity of waitingState: int * ambiguousSenders: Role list
    | OrphanMessage     of dangling: Interaction

let checkImplementable (g: GlobalAut) (locals: Map<Role, DFA<_,_>>) : ImplementabilityResult =
    failwith "TODO: port from the artifact at zenodo.org/records/8161741 (CAV 2023 §5)"

// === The complete pipeline ===
let projectGlobal (g: ProtocolType) (roles: Role list) =
    let gaut = buildGAut g
    let locals =
        roles
        |> List.map (fun r -> r, gaut |> project r |> subsetConstruct)
        |> Map.ofList
    match checkImplementable gaut locals with
    | Implementable -> Ok locals
    | failure       -> Error failure
```

### A.6. Practical notes

- **Do not roll your own implementability check.** Steps 1–3 are textbook. Step 4 is the paper’s contribution; the conditions are subtle. The artifact at [Zenodo 8161741](https://zenodo.org/records/8161741) is the reference implementation. Wrap or port it; do not paraphrase from memory.
- **Synchronous vs asynchronous.** The paper handles asynchronous semantics by *splitting* each `p→q:m` into a send event followed by a receive event before projection. Implementability conditions differ between the two settings.
- **State explosion.** Subset construction is exponential in the worst case. For typical business-workflow protocols this is fine; the paper’s PSPACE result ensures the *check* itself does not blow up exponentially.
- **Predecessor.** Majumdar, Mukund, Stutz, Zufferey (CONCUR 2021, [LIPIcs.CONCUR.2021.35](https://drops.dagstuhl.de/entities/document/10.4230/LIPIcs.CONCUR.2021.35)) — sound but not complete on the same class of MSTs. CAV 2023 subsumes it.

-----

## Appendix B — Effect encodings considered

Four encodings were considered for making effects first-class in the protocol algebra. The decision (§5) is the **witness-object HKT pattern**, without depending on the Higher library, with `Task`/`Async` as the carrier. This appendix explains the alternatives and why they were rejected, then closes with the ergonomics treatment that hides the brand from authors (§B.6, expanded in Appendix N).

### B.1. Encoding A — Free monad

Each effect constructor takes the operation’s parameters plus a continuation `'result -> Eff<'a>` consuming the result. The effect tree is built as data; an interpreter walks the tree.

```fsharp
type Eff<'a> =
    | DbRead  of key: string                    * cont: (string -> Eff<'a>)
    | DbWrite of key: string * value: string    * cont: (unit   -> Eff<'a>)
    | Call    of service: string * payload: obj * cont: (obj    -> Eff<'a>)
    | Pure    of 'a

let rec bind (m: Eff<'a>) (f: 'a -> Eff<'b>) : Eff<'b> =
    match m with
    | Pure x           -> f x
    | DbRead(k, c)     -> DbRead(k, fun r -> bind (c r) f)
    | DbWrite(k, v, c) -> DbWrite(k, v, fun r -> bind (c r) f)
    | Call(s, p, c)    -> Call(s, p, fun r -> bind (c r) f)
```

**Benefit:** the tree is reified data — inspectable, optimizable, serializable, replayable.
**Cost:** AST allocation per protocol step, real at scale. See Seemann’s [free monad recipe](https://blog.ploeh.dk/2017/08/07/f-free-monad-recipe/) for the F# canonical treatment.

**Rejected because:** the PROV-O provenance story can be satisfied with structured logging from the handler (§9), not by inspecting an effect AST. The allocation cost therefore buys nothing Frank needs.

### B.2. Encoding B — Tagless final (Carette/Kiselyov/Shan)

Programs are polymorphic over an interpreter interface. No AST is built; programs compose as plain function calls and the compiler erases the abstraction.

```fsharp
type IEffectInterpreter<'repr<_>> =
    abstract DbRead  : key: string                    -> 'repr<string>
    abstract DbWrite : key: string * value: string    -> 'repr<unit>
    abstract Call    : service: string * payload: obj -> 'repr<obj>
    abstract Return  : value: 'a                      -> 'repr<'a>
    abstract Bind    : m: 'repr<'a> * f: ('a -> 'repr<'b>) -> 'repr<'b>
```

**Benefit:** zero AST allocation; strong fit when AST inspection is not needed.
**Cost:** F# has no first-class higher-kinded types. The carrier `'repr<_>` must be encoded, typically via SRTPs or a brand/witness pattern. This is verbose.

**Not adopted as the library pattern because:** the idiomatic F# ecosystem has no standard HKT encoding. Adopting one means either depending on the [Higher](https://github.com/palladin/Higher) library (unmaintained against recent .NET) or writing ad-hoc SRTP machinery. **However, the witness-object variant of this encoding — scoped to the specific effect signatures Frank needs — is the chosen approach.** Taking the pattern without the library avoids the dependency risk.

*Parallel technique for the encoding lineage.* The witness-object / brand-type approach traces to Yallop & White, “Lightweight Higher-Kinded Polymorphism” (FLOPS 2014), which the Higher library ports to F#. A separate lineage with the same motivation — encoding higher-kinded generic programming in a language without native HKT — is Lämmel & Peyton Jones’s “Scrap Your Boilerplate with Class” (ICFP 2005), which uses recursive type-class dictionaries to dispatch generic traversals. SYB3 does not port directly to F# (no type classes), but the conceptual parallel is worth noting: both reify a mechanism the base type system cannot express. Yallop-White reifies type-level application (`App<F, A>`); SYB3 reifies open, extensible generic functions via dictionary composition. Frank adopts the Yallop-White shape because it lands natively in F# via interfaces and branded types; SYB3 is listed for reference only.

### B.3. Encoding C — True algebraic effects with handlers

Effects are operations on an abstract effect signature; **handlers** are first-class language constructs that intercept operations and reify continuations. Implemented natively by Koka, Eff, OCaml 5, and Frank-the-language (Lindley/McBride/McLaughlin).

```
// Pseudo-syntax — not F#:
effect DbRead  : string -> string
effect DbWrite : string * string -> unit

handler inMemoryHandler {
    return x              -> (x, currentState)
    DbRead k       resume -> resume (Map.find k currentState)
    DbWrite (k, v) resume -> withState (Map.add k v currentState) (resume ())
}
```

**Rejected for F# target because:** F# does not natively support effect handlers. Simulating them requires either free-monad encoding (B.1) or delimited-control via a runtime that F# does not ship with.

Keep as the **cross-compilation target** if a Koka, OCaml-5, or Frank-language port is pursued in the future.

### B.4. Delimited continuations — the connection

Algebraic effects (C) and delimited continuations are two sides of the same coin. The “d” in `shift`/`reset` (Danvy & Filinski, “Representing Control,” 1990) stands for *delimited* — a continuation captured up to a handler boundary rather than up to the end of the program. Koka, Eff, OCaml 5, and Frank implement effect handlers via delimited continuations under the hood.

**.NET has no native `shift`/`reset`.** Hopac implemented delimited control on F# async circa 2015; it is unmaintained and a non-starter for new dependencies. The practical substitute is **structured async scopes** (`async { ... }` or `task { ... }`): lexical nesting stands in for the handler boundary, with single-shot resumption rather than multi-shot. For session-type use, single-shot is sufficient — a handler either resumes the protocol or aborts.

(Delimited continuations come back as a *codegen-internal* primitive in the contingency strategy of Appendix O — not as an author-facing handler mechanism, but as the underlying machinery for emitting hierarchical-parallel state machines uniformly. The `shift`/`reset` model is the conceptual anchor there even when the emitted code is built on top of `Async.FromContinuations` or `task` rather than native delimited-continuation primitives.)

### B.5. Decision — witness-object HKT on `Task`/`Async`

Concrete form: a `IEffectHandler<'eff, 'result>` interface carrying `Map`, `Bind`, `Return`, and `Invoke`, where `'eff` and `'result` are specialized per protocol via SRTPs or direct parameterization. Pattern-matched from Higher; dependency on Higher avoided. The carrier is `Task` (via `task { }`) or `Async`, giving structured concurrency as the implicit delimiter.

The author implements this interface with business logic and injected dependencies (services, loggers). The generated actor invokes effects through the interface without knowing or caring about the runtime carrier.

This is the surface shown in §5.

### B.6. Authoring ergonomics — CE smoothing of the witness-object pattern

The verbosity argument against the witness-object encoding (B.2) is mitigated in practice by wrapping the handler in an F# computation expression. The `App<'F, _>` brand stays inside the CE builder; authors write `let!`, `and!`, `match!`, and custom operations against concrete types and never touch the brand directly. Because Frank’s generator owns emission, both the per-protocol `IEffectHandler<'F>` interface and the per-protocol `EffectBuilder<'F>` are emitted together — the verbosity that would have been hand-written is now generated, and the author surface looks like ordinary `task` or `async` code.

Full details — including the applicative-`and!` mapping for `Parallel`, `match!` for `Choice` projection, custom operations per effect DU case, and resumable code as a zero-allocation future option — are in **Appendix N**. That appendix also covers what CEs *do not* smooth over, including the effect-composition limit the witness-object pattern inherits from any monadic encoding.

-----

## Appendix C — Code-generation technology considered

The decision (§10) is **FCS + Fabulous.AST + a bespoke MSBuild task**, reusing existing MSBuild integration shape from adjacent work. This appendix covers what was considered and why each alternative was rejected.

### C.1. Terminology — “F# source generators” is not a feature

Before anything else: **F# has no native source-generator facility** analogous to Roslyn Source Generators in C#. The language suggestion at [fslang-suggestions#864](https://github.com/fsharp/fslang-suggestions/issues/864) has been open since April 2020, is estimated at cost “XXXXL,” has no implementation work and no milestone. The F# team’s stated position is that Type Providers and Myriad cover the use case.

When F# practitioners say “source generator,” they mean one of: Myriad, a Roslyn source generator in C# whose output F# consumes via interop, or an ad-hoc MSBuild task using FCS directly. Frank should not plan around native F# source generators arriving.

The real platform pieces available today:

|Tool                             |Role                                                                   |Status                                                                                                 |
|---------------------------------|-----------------------------------------------------------------------|-------------------------------------------------------------------------------------------------------|
|**Myriad**                       |Build-time F# code generation via MSBuild, text templating over FCS AST|[MoiraeSoftware/Myriad](https://github.com/MoiraeSoftware/Myriad) — small team, 0.8.x                  |
|**FCS** (F# Compiler Service)    |The F# compiler as a library — parse, typecheck, inspect, emit         |Tracks compiler; Microsoft-maintained                                                                  |
|**Fantomas.Core**                |F# source formatter; also usable to emit clean code from a syntax tree |[fsprojects/fantomas](https://github.com/fsprojects/fantomas), active                                  |
|**Fabulous.AST**                 |CE-based DSL for constructing F# AST on top of Fantomas                |[edgarfgp/Fabulous.AST](https://github.com/edgarfgp/Fabulous.AST), v1.2 stable, v2.0 in progress       |
|**Type Providers**               |Compile-time type generation from external schemas; a different model  |[SDK](https://github.com/fsprojects/FSharp.TypeProviders.SDK), stable                                  |
|**Roslyn Source Generators (C#)**|Real and first-class — emit C# that F# consumes via interop            |[Microsoft docs](https://learn.microsoft.com/en-us/dotnet/csharp/roslyn-sdk/source-generators-overview)|

### C.2. Myriad — why not

Myriad is a text-templating code generator. Its design center is:

- single-file input, single-file output,
- derive-style transformations (given these DUs, emit lenses/accessors/serializers),
- no cross-file analysis, no external tool invocation, no algorithmic work inside the generator.

Frank’s interacting-actor use case exceeds this design center on multiple axes. Items 1, 2, 3, and 5 below are already tripped by Frank’s primary use case before a line of generator code is written.

1. **Cross-file inputs.** A protocol CE lives in one file; its DUs for roles, messages, states, and effects typically live in another (or several). The actor must reference all of them coherently. Myriad plugins are invoked per input file; coordinating across files requires either shared state between plugin invocations or convention-based discovery — fragile, outside the framework’s happy path.
1. **Multiple coordinated outputs per protocol.** For each protocol Frank needs to emit: the per-role actor(s), the `IEffectHandler<_,_>` interface, the log-event schema, SMT-LIB verification queries, and optionally Scribble or SCXML serializations. Myriad’s default is one output per input. Producing this set means stacking plugins or building one that emits multi-part output — both push the model.
1. **Algorithmic analysis at generation time.** The Li-et-al. projection (Appendix A) is automaton construction plus a PSPACE implementability check. Myriad is not an analysis framework. Running this inside a plugin means embedding a nontrivial library into the plugin assembly, which stops being templating and starts being a custom code-gen engine wearing Myriad’s skin.
1. **External tool invocation during generation.** Z3 for verification queries. Scribble or SCXML parsers for Pathway 2. Fantomas for output formatting. All doable from a plugin, all outside the design center.
1. **Extracting values from computation expressions.** The cleanest way to read a `ProtocolType` value from the author’s CE is to *evaluate* the CE — compile and run the author’s file. Myriad reads the syntactic AST, not evaluated values. Either Frank implements partial CE desugaring by hand (fragile, will diverge from the compiler) or requires protocols to be plain values without CE sugar (constrains authoring ergonomics that §3 set up as a goal).
1. **Shared generated helpers.** The `IEffectHandler<'eff, 'result>` witness template should live once in a shared Frank library, not be regenerated per protocol. Keeping that boundary crisp under Myriad adds friction.
1. **Byte-stable output.** Generated files should diff cleanly across runs. Myriad achieves this only if the generator code is disciplined about ordering. Enumerating a `Dictionary` or `Set` without explicit sorting produces non-deterministic output. A footgun, not a framework flaw, but real.

**Honest read:** Myriad is the fastest path to a prototype *if* the use case fits its design center. Frank’s does not. Adopting Myriad means building a custom code-gen engine inside its plugin API; the framework supplies the plugin wiring and not much else. The existing MSBuild integration from adjacent work supplies the same wiring with fewer constraints.

### C.3. Fabulous.AST — what it is and why it slots in

Fabulous.AST is a CE-based DSL for constructing F# syntax trees, built on top of Fantomas. It is **not** a template engine, plugin framework, or build-time generator — it is a library you call from your own code to produce an AST that Fantomas then formats.

The stack:

```
your code-gen logic
    ↓ builds an AST via
Fabulous.AST (ergonomic DSL)
    ↓ produces
Fantomas Oak node tree
    ↓ rendered by
Fantomas.Core.CodeFormatter
    ↓ outputs
formatted F# source text
```

What this buys Frank:

- Syntactically valid output by construction. String-concatenation bugs do not exist.
- Fantomas formatting for free.
- Compile-time structural checking in the generator itself — if the AST is malformed, you learn at generator-compile time, not by producing invalid output.
- A CE authoring surface in the generator that mirrors the CE authoring surface for the protocol: the two layers feel similar to write.

The Fantomas project’s guidance on code generation is effectively: do not concatenate strings; build ASTs. Fabulous.AST makes that tractable.

### C.4. Roslyn Source Generators, Type Providers — why not

- **Roslyn Source Generators (C#)** work for Frank only via C#-to-F# interop: you would write a C# generator whose output F# consumes. Awkward, indirect, and gives up the F# type system in the part of the pipeline that most wants it.
- **Type Providers** are a different model: compile-time type generation from external schemas. Good for intake (reading Scribble/SCXML into types) but not for emitting arbitrary F# code. Could serve a role in Pathway 2, but not as the primary generator.

### C.5. Existing MSBuild integration from adjacent work

Frank inherits the MSBuild integration shape from the adjacent tooling effort. That integration already handles: finding input files, invoking a generation step, dropping outputs into the build’s intermediate directory, adding them to the compile list. Extending it for Frank is plumbing work, not architecture work.

This changes the build-order calculus substantially. The Myriad-vs-FCS framing would have been “start with Myriad for the MSBuild integration Myriad provides; migrate to a bespoke task later when needed.” With the MSBuild integration already available, Myriad’s last real value-add disappears. FCS + Fabulous.AST becomes the primary recommendation outright.

-----

## Appendix D — Verification properties enumerated

“Verify the protocol” is not a single Z3 query. It is a list, each item with its own SMT-LIB shape. `Frank.CodeGen` emits one query per property per generated unit; `Frank.Verification` runs them and fails the build if any returns `sat` on the negation of the property.

|# |Property                      |Description                                                                               |Tool                                                                                                                   |
|--|------------------------------|------------------------------------------------------------------------------------------|-----------------------------------------------------------------------------------------------------------------------|
|1 |**Implementability**          |Global type admits a non-empty projection for every role with no synthesis ambiguity      |Automata construction (Li et al. 2023); no SMT                                                                         |
|2 |**Deadlock freedom**          |No reachable global state where some role is waiting on a message no role can send        |CFSM reachability; SMT for guard satisfiability                                                                        |
|3 |**Linear channel use**        |Each `Channel` is used exactly once per protocol path (no double-use, no orphan)          |Structural; type-system-checkable                                                                                      |
|4 |**Effect discipline**         |Every `Effect` invoked inside a `Nested` scope appears in that scope’s `permittedEffects` |Pure tree walk; no SMT                                                                                                 |
|5 |**Race freedom in `Parallel`**|Effects in concurrent branches commute, or operate on disjoint resource keys              |SMT: assert non-disjoint write sets unreachable                                                                        |
|6 |**Payload refinement**        |When message *m* is sent with payload *p*, predicate `P(p)` holds                         |SMT (the Session* sweet spot)                                                                                          |
|7 |**Liveness / progress**       |Every branch reachable; no role waits forever under fair scheduling                       |Model checking (CTL/LTL backend) — **deferred; see Appendix Q**                                                        |
|8 |**Supervision soundness**     |Failure of any role in a `Nested` scope is handled by some ancestor’s supervision policy  |Structural + reachability                                                                                              |
|9 |**Resource bounds**           |Total resource consumption along any path stays under budget                              |SMT with linear arithmetic (cf. Nomos) — **deferred; see Appendix Q**                                                  |
|10|**Provenance completeness**   |Every state transition emits a PROV-O `Activity` record sufficient to reconstruct the path|Structural; checked by the generator. **V1 activates basic PROV-O vocabulary; full vocabulary deferred to Appendix Q.**|

**Tier mapping (v1 scope, two tiers):**

- **Tier 1 (always on):** 1, 3, 4, 8, 10. Cheap, structural, no SMT; catches most real bugs.
- **Tier 2 (configurable):** 2, 5, 6. Z3-backed on the source protocol, inline during code generation; pays a build-time cost. **Not** run against emitted SMT-LIB artifacts in v1 — generated-code equivalence is covered by property-based conformance testing (Appendix P).
- **Deferred (Appendix Q):** 7, 9. Liveness and resource bounds are operational extensions whose tooling (model checker, linear-arithmetic resource solver) is not in the v1 Z3-inline path. The ADR carries the property definitions so v1 does not rule them out; the verification pipeline picks them up when Appendix Q lands.

-----

## Appendix E — Communication semantics variants

`Send` and `Recv` in the algebra describe *that* a message moves, not *how*. The runtime shape choices are first-class in the algebra so that projection and verification can reason about them.

### E.1. `CommSemantics`

In v1, `CommSemantics` is a single constructor:

- **`AsyncFIFO of bound: int`** — asynchronous with bounded per-pair FIFO mailboxes. Standard mailbox-actor semantics. Deadlock can occur on mailbox overflow.

Two additional semantics are defined in the algebra but are not part of the v1 core surface; they are documented here so the algebra does not rule them out and are activated through the backend extension mechanism described in Appendix K.5:

- **`Sync`** — synchronous rendezvous, CSP-style. Deadlock conditions are tightest; verification is simplest. Appropriate for tightly-coupled in-process actors on a backend that provides rendezvous natively.
- **`AsyncCausal`** — vector-clock causal delivery. Useful when ordering matters across roles but FIFO-per-pair is insufficient. Requires a backend with a causal-delivery primitive.

Both extensions are authored via backend-scoped attributes on channels or protocols; projection and verification reason about them only when the backend activates them.

### E.2. `Channel` and `Scope`

`Channel<'Role, 'Scope> = from: 'Role * to_: 'Role * within: 'Scope`. Channels are scoped to regions; a role participating in an outer protocol and an inner `Nested` has distinct channels for each. Conflating them would create phantom races. Sibling-to-sibling messaging within a `Nested` uses channels scoped to that region; cross-scope messaging uses channels scoped to a common ancestor.

The `'Scope` parameter also makes bubble-up computable: an unhandled event walks the parent chain in the projected statechart until it hits a handler — a parent-chain walk, not an implicit runtime search. Because `'Scope` is an author DU rather than a stringly-typed tag, the compiler enforces that every scope reference corresponds to a declared case.

**Note on `SupervisionPolicy`:** In the v1 algebra (§3) `Nested` no longer carries a `SupervisionPolicy`. The v1 supervisor behavior is a fixed restart-on-crash floor (§6), not an algebra-level concern. Backend-specific supervision annotations (Erlang/OTP-style `Restart` / `RestartAll` / `Escalate` / `Stop`) live in Appendix K.6 as a backend extension, not as a v1 core DU.

-----

## Appendix F — What the model does not yet address

Known gaps, deliberately out of scope for the first implementation:

### F.1. Harel statechart features

The algebra covers a practical subset of Harel’s statechart formalism. What’s in and what’s out:

**Covered:**

- **Hierarchical nesting (depth).** `Nested` construct; compound states can contain substates to arbitrary depth.
- **Orthogonal regions (concurrency).** `Parallel` inside a `Nested`.
- **Shallow history.** `History` pseudo-state. Resolved at runtime by reading the journal (§9.1) for the last substate of that scope.
- **Deep history.** `DeepHistory` pseudo-state. Journal-resolved recursively at every level below.
- **Initial state.** Implicit — the `body` of a `Nested` is the entry point on scope entry.
- **Event bubble-up (scope escape).** Part-designed — see §6. An unhandled message or effect in an inner scope walks the parent chain of active scopes in the projected statechart. The modal-refinement rule (§13 open question #2) is still to be formalized.

**Gaps:**

- **Entry and exit actions on compound states.** Not first-class in the algebra. Can be approximated via `Effect` annotations on the first/last transition, but that’s not the same thing — true entry/exit actions fire on every entry/exit of the compound state, including entries via history pseudo-states. Addition would take the form of `onEntry: 'Effect list` and `onExit: 'Effect list` fields on `Nested`.
- **Internal vs. external transitions.** In Harel, a transition that targets its own source state via an external transition exits and re-enters (firing exit/entry actions); an internal transition does not. Without first-class entry/exit actions, this distinction is moot and is not represented.
- **Final pseudo-states for compound states.** `End` serves as a final state at the top level. Compound states do not have a distinct way to signal completion-causing-containing-scope-to-advance; this is implicit in the `continuation` field of `Nested`. Adequate for straightforward protocols; not fully faithful to Harel’s completion-event semantics.
- **Conditional / junction pseudo-states.** No first-class construct. Branching is via `Choice`.
- **Explicit concurrent region synchronization.** `Parallel` waits for all branches to complete implicitly. No first-class join pseudo-state for waiting on a subset.

These gaps are **documentation debt, not architectural debt** — each can be added to the algebra later without changing the projection operator, the journal schema, or the generator pipeline. Prioritization follows demand: entry/exit actions are the most likely first addition, since they show up in real protocols frequently.

### F.2. Temporal and dynamic features

- **Time and timeouts.** The `Timer` effect is a placeholder. Real-time properties (deadlines, bounded latency) need a timed-automata layer not yet sketched.
- **Dynamic role membership.** The model assumes a fixed role set. Parameterized MPST (Yoshida et al.) extends this; integration is future work.

### F.3. Production-hardening features

- **Failure semantics beyond the minimal supervisor floor.** Network partitions, Byzantine roles, at-least-once delivery, cross-actor consistency during partial failure. These layer on top of the protocol algebra via alternative backends (Appendix K), not into it.
- **Journal retention and compaction.** The journal grows with every event. For long-running protocol instances, retention and compaction strategies are needed. Initial implementation: journal lives as long as the protocol instance does, no compaction.

-----

## Appendix G — Prior-work retrospective and discipline rules

The prior exploratory pass on Frank’s code-gen pipeline was partially completed: MSBuild integration shape was proven, CLI parsers for several input formats were written, a generation scheme serializing object instances to binary was implemented, and a pivot toward source-file generation was in progress. That work stalled for two intertwined reasons.

### G.1. What caused the prior stall

**No settled authoring structure to target.** The AST-with-free-monad approach was rejected for boilerplate weight. Tagless final was attempted but did not finalize. The decision converged on the witness-object HKT pattern (pattern-matched from Higher, without the dependency) only in the current design session. Without a settled target shape, generator work kept producing code that then had to be thrown away.

**LLM-assisted implementation produced incoherent integration.** An earlier Claude session (Opus 4.6) repeatedly reported integration as complete when it was not. Whole sections of the pipeline were written in files that looked wired but did not execute end-to-end paths. Diagnosing the gaps after the fact cost more time than writing the code initially saved.

### G.2. The discipline rules this produces

The layered build order in §12 is designed to prevent a recurrence. The specific rules are:

1. **Every layer has unit tests that pass without the layer above it.** Integration tests are additional, never substitutes.
1. **Write the integration test before asking an LLM (or anyone) to do the integration.** The test is the acceptance criterion; no criterion means no done.
1. **Treat any claim of “this is wired up” as unverified** until a test that exercises the wiring passes. This applies to LLM output, to your own output, to anyone’s output.
1. **Commit after every working step.** Branch before major integration work.
1. **Do not move on to step N+1 if step N has unexplained behavior.**
1. **Use this document as the architectural anchor.** Do not let an LLM re-derive architecture on the fly during implementation. If the architecture is wrong, update the document first; do not drift.

### G.3. What carries forward from the prior work

- The MSBuild integration shape — proven, reusable.
- The CLI structure and existing format parsers — usable for Pathway 2 once the CE surface is solid.
- The decision *not* to go further with free-monad or binary-serialization approaches — confirmed.

What does not carry forward is the partially-integrated generator code. That is being scrapped. The current plan starts over on generator logic, retaining only the pieces named above.

### G.4. Why the §12 emission-strategy experiment is positioned where it is

The experiment at step 6 of the build order (introduced 2026-04-22, alongside Appendices N and O; retained through the v1 scope cuts of 2026-04-23) exists because of this same dynamic. Once the canonical actor template is committed in step 7, every generated actor will follow its emission pattern by path of least resistance — and so will any LLM-assisted iteration on them. If the wrong emission strategy is committed first, the wrong strategy gets propagated through the entire v1 generator pipeline before anyone notices. Under the v1 single-canonical-template regime (§7), this propagation risk is *higher* than it was under the prior four-template regime, because there is no second template to serve as a contrast point.

The experiment is cheap (a few hours), the comparison is concrete (two emissions of the same statechart, evaluated on three named axes), and the decision deadline is firm (before step 7 lands the canonical actor template). That structure is designed specifically against the failure mode this appendix documents.

-----

## Appendix H — Cross-language precedents

For reference, the broader landscape of session-type implementations in other languages. None of these directly solves Frank’s problem, but each illustrates one point in the design space.

|Language  |Project                        |Approach                          |Link                                                                                                                                |
|----------|-------------------------------|----------------------------------|------------------------------------------------------------------------------------------------------------------------------------|
|Haskell   |`sessiontypes`, `typed-session`|Type families + GADTs             |[sessiontypes](https://hackage.haskell.org/package/sessiontypes), [typed-session](https://hackage.haskell.org/package/typed-session)|
|Haskell   |Lindley & Morris               |Embedding via type families       |[Paper](https://homepages.inf.ed.ac.uk/slindley/papers/fst-extended.pdf)                                                            |
|Rust      |`session_types`, Ferrite, `par`|Typestate pattern                 |[session_types](https://crates.io/crates/session_types), [faiface/par](https://github.com/faiface/par)                              |
|Rust      |Rumpsteak                      |Generates Rust APIs from Scribble |[GitHub](https://github.com/zakcutner/rumpsteak)                                                                                    |
|TypeScript|STMonitor, MPST-TS             |Routed MPST                       |[CC 2021](https://doi.org/10.1145/3446804.3446854)                                                                                  |
|Scala     |lchannels, Effpi               |Akka Typed integration            |[Effpi](https://github.com/alcestes/effpi)                                                                                          |
|F*        |Session*                       |Refinement-typed API from Scribble|[arXiv:2009.06541](https://arxiv.org/abs/2009.06541)                                                                                |

A curated index: Simon Fowler’s [session types implementations collection](https://simonjf.com/2016/05/28/session-type-implementations.html).

-----

## Appendix I — References

**Statecharts**

- Harel (1987) — [Science of Computer Programming](https://www.sciencedirect.com/science/article/pii/0167642387900359)
- Harel (2007) HOPL III — [ACM DL](https://dl.acm.org/doi/10.1145/1238844.1238845)

**Multiparty session types (foundations)**

- Honda, Yoshida, Carbone (2008/2016) — [JACM DOI](https://doi.org/10.1145/2827695)
- Wadler (2012) Propositions as Sessions — [PDF](https://homepages.inf.ed.ac.uk/wadler/papers/propositions-as-sessions/propositions-as-sessions-jfp.pdf)
- Deniélou, Yoshida (2012) MPST meet communicating automata — [DOI](https://doi.org/10.1007/978-3-642-28869-2_10)

**Coalgebraic view**

- Keizer, Basold, Pérez (2021) ESOP — [arXiv:2011.05712](https://arxiv.org/abs/2011.05712)
- Keizer, Basold, Pérez (2022) TOPLAS — [ACM DOI](https://doi.org/10.1145/3527633)

**Projection with automata**

- Li, Stutz, Wies, Zufferey (2023) CAV — *Complete Multiparty Session Type Projection with Automata.* [arXiv:2305.17079](https://arxiv.org/abs/2305.17079) | [Springer](https://doi.org/10.1007/978-3-031-37709-9_17) | [Artifact (Zenodo)](https://zenodo.org/records/8161741)
- Majumdar, Mukund, Stutz, Zufferey (2021) CONCUR — predecessor. [LIPIcs.CONCUR.2021.35](https://drops.dagstuhl.de/entities/document/10.4230/LIPIcs.CONCUR.2021.35)

**Verification**

- Zhou, Ferreira, Hu, Neykova, Yoshida (2020) Session* OOPSLA — [arXiv:2009.06541](https://arxiv.org/abs/2009.06541)
- Dependent Session Types (2025) — [arXiv:2510.19129](https://arxiv.org/abs/2510.19129)

**Effect encodings**

- Carette, Kiselyov, Shan (2009) JFP — *Finally Tagless, Partially Evaluated.* [DOI](https://doi.org/10.1017/S0956796809007205)
- Seemann (2017) — *F# free monad recipe.* [Blog](https://blog.ploeh.dk/2017/08/07/f-free-monad-recipe/)
- Plotkin, Pretnar (2009) ESOP — *Handlers of Algebraic Effects.* [DOI](https://doi.org/10.1007/978-3-642-00590-9_7)
- Lindley, McBride, McLaughlin (2017) POPL — *Do Be Do Be Do* (Frank-the-language). [DOI](https://doi.org/10.1145/3009837.3009897)
- Danvy, Filinski (1990) LFP — *Abstracting Control* / *Representing Control.* [DOI](https://doi.org/10.1145/91556.91622)
- Yallop, White (2014) FLOPS — *Lightweight Higher-Kinded Polymorphism.* [Paper](https://www.cl.cam.ac.uk/~jdy22/papers/lightweight-higher-kinded-polymorphism.pdf)
- Lämmel, Peyton Jones (2005) ICFP — *Scrap Your Boilerplate with Class: Extensible Generic Functions.* [MSR](https://www.microsoft.com/en-us/research/publication/scrap-your-boilerplate-with-class/) | [PDF](https://www.microsoft.com/en-us/research/wp-content/uploads/2016/07/gmap3.pdf)
- Palladinos — [Higher](https://github.com/palladin/Higher) (pattern-match, do not adopt)

**Applicative functors and the constraint/liberation framing** (added for Appendix N)

- McBride, Paterson (2008) JFP — *Applicative Programming with Effects.* [DOI](https://doi.org/10.1017/S0956796807006326)
- Hutton, Fulger (in *Programming with Effects*) — applicative-vs-monadic composition framing.

**Effect-session correspondence**

- Orchard, Yoshida (2016) POPL — *Effects as Sessions, Sessions as Effects.* [DOI](https://doi.org/10.1145/2837614.2837634)
- Orchard, Yoshida (2015) PLACES — *Using session types as an effect system.* [arXiv:1602.03591](https://arxiv.org/abs/1602.03591)
- Hillerström, Lindley, Atkey, Sivaramakrishnan (2017) FSCD — *Continuation Passing Style for Effect Handlers.* [LIPIcs.FSCD.2017.18](https://drops.dagstuhl.de/entities/document/10.4230/LIPIcs.FSCD.2017.18)
- Forster, Kammar, Lindley, Pretnar (2019) JFP — *On the Expressive Power of User-Defined Effects.* [DOI](https://doi.org/10.1017/S0956796819000121)
- Reynolds (1972) ACM National Conference — *Definitional Interpreters for Higher-Order Programming Languages.* Reprinted Higher-Order & Symbolic Computation 11(4), 1998. [DOI](https://doi.org/10.1023/A:1010027404223)
- Gibbons (2022) Programming Journal — *Continuation-Passing Style, Defunctionalization, Accumulations, and Associativity.* [arXiv:2111.10413](https://arxiv.org/abs/2111.10413)

**Concurrent ML / synchronous combinators / delimited continuations** (added for Appendix O)

- Reppy (1999) — *Concurrent Programming in ML.* Cambridge University Press.
- Reppy, Russo, Xiao (2009) ICFP — *Parallel Concurrent ML.* [DOI](https://doi.org/10.1145/1631687.1596588)
- Hopac (Kallio et al.) — F# port of the CML model. [GitHub](https://github.com/Hopac/Hopac) (unmaintained — pattern reference only)

**Nested / compositional MPST (adjacent to Frank’s hierarchy concern)**

- Demangeon, Honda (2012) CONCUR — *Nested Protocols in Session Types.* [DOI](https://doi.org/10.1007/978-3-642-32940-1_20)
- Capecchi, Giachino, Yoshida (2010) FSTTCS — *Global Escape in Multiparty Sessions.* [LIPIcs.FSTTCS.2010.338](https://drops.dagstuhl.de/entities/document/10.4230/LIPIcs.FSTTCS.2010.338)
- Gheri, Yoshida (2023) POPL / PACMPL — *Hybrid Multiparty Session Types.* [arXiv:2302.01979](https://arxiv.org/abs/2302.01979)
- Zhou, Gheri, Yoshida et al. (2021) — *Communicating Finite State Machines and an Extensible Toolchain for MPST* (nuScr, with nested-protocol extension as a case study). [DOI](https://doi.org/10.1007/978-3-030-86593-1_2)
- Udomsrirungruang, Yoshida (2025) POPL — *Top-Down or Bottom-Up? Complexity Analyses of Synchronous Multiparty Session Types.* [PDF](https://mrg.cs.ox.ac.uk/publications/top-down-or-bottom-up-complexity-analyses-of-synchronous-multiparty-session-types/main.pdf)

**Tools**

- F* — [fstar-lang.org](https://fstar-lang.org) | [GitHub](https://github.com/FStarLang/FStar)
- Z3 — [GitHub](https://github.com/Z3Prover/z3)
- F# Compiler Service (FCS) — [GitHub](https://github.com/dotnet/fsharp/tree/main/src/Compiler/Service)
- Fantomas — [fsprojects/fantomas](https://github.com/fsprojects/fantomas) | [Generating code docs](https://fsprojects.github.io/fantomas/docs/end-users/GeneratingCode.html)
- Fabulous.AST — [edgarfgp/Fabulous.AST](https://github.com/edgarfgp/Fabulous.AST) | [docs](https://edgarfgp.github.io/Fabulous.AST/)
- Myriad — [MoiraeSoftware/Myriad](https://github.com/MoiraeSoftware/Myriad) (considered and rejected; see Appendix C)
- Scribble / nuScr — [nuscr.dev](https://nuscr.dev)
- F# language suggestion #864 (source generators; vaporware) — [fslang-suggestions#864](https://github.com/fsharp/fslang-suggestions/issues/864)
- Roslyn Source Generators (C#) — [Microsoft Learn](https://learn.microsoft.com/en-us/dotnet/csharp/roslyn-sdk/source-generators-overview)

-----

## Appendix J — Order fulfillment example in full

Three roles (Customer, Merchant, Warehouse), two levels of hierarchy. The outer `OrderPhase` envelope contains nested `InventoryPhase` and `ShippingPhase`. Parallel regions inside `ShippingPhase` run shipment and payment concurrently.

First the author-supplied DUs that instantiate the algebra’s type parameters:

```fsharp
type Role    = Customer | Merchant | Warehouse
type Scope   = OrderPhase | InventoryPhase | ShippingPhase
type Message =
    | PlaceOrder         of OrderData
    | CheckInventory     of ItemId
    | InventoryStatus    of InventoryResult
    | OrderConfirmed     of OrderId
    | OrderDenied        of OrderId
    | PrepareShipment    of OrderId
    | ShipmentDispatched of TrackingId
    | ProcessPayment     of PaymentData
    | PaymentConfirmed   of ReceiptId
type Effect =
    | ReadOrders
    | WriteOrders
    | ReadInventory
    | WriteShipments
    | CallPaymentGateway
    | EmitOrderPlaced
    | EmitOrderConfirmed
    | EmitOrderDenied
    | EmitShipmentDispatched
    | EmitPaymentConfirmed

type Proto = ProtocolType<Role, Scope, Message, Effect>
```

Then the protocol value. All scope tags, message names, and effect names below are DU cases — the compiler catches typos, the projection sees structured values, and there are no strings in protocol positions:

```fsharp
let orderFulfillment : Proto =
    Nested(
        OrderPhase,
        AsyncFIFO 16,
        Escalate,
        [ReadOrders; WriteOrders; EmitOrderPlaced],

        // Level 1: Customer places the order
        Send(Channel(Customer, Merchant, OrderPhase),
             PlaceOrder placeholder,
             [WriteOrders; EmitOrderPlaced],
          Recv(Channel(Customer, Merchant, OrderPhase),
               PlaceOrder placeholder,
               [],

            // Level 2: InventoryPhase
            Nested(
                InventoryPhase,
                Sync,
                Restart,
                [ReadInventory],

                Send(Channel(Merchant, Warehouse, InventoryPhase),
                     CheckInventory placeholder,
                     [ReadInventory],
                  Recv(Channel(Merchant, Warehouse, InventoryPhase),
                       CheckInventory placeholder,
                       [],
                    Send(Channel(Warehouse, Merchant, InventoryPhase),
                         InventoryStatus placeholder,
                         [],
                      Recv(Channel(Warehouse, Merchant, InventoryPhase),
                           InventoryStatus placeholder,
                           [],
                        End)))),

                // Merchant decides
                Choice(Merchant, [
                    OrderConfirmed placeholder,
                      Send(Channel(Merchant, Customer, OrderPhase),
                           OrderConfirmed placeholder,
                           [EmitOrderConfirmed],
                        Recv(Channel(Merchant, Customer, OrderPhase),
                             OrderConfirmed placeholder,
                             [],

                          // Level 2: ShippingPhase (parallel regions)
                          Nested(
                              ShippingPhase,
                              AsyncFIFO 8,
                              Escalate,
                              [WriteShipments; CallPaymentGateway],

                              Parallel [
                                  // Shipping branch
                                  Send(Channel(Merchant, Warehouse, ShippingPhase),
                                       PrepareShipment placeholder,
                                       [WriteShipments],
                                    Recv(Channel(Merchant, Warehouse, ShippingPhase),
                                         PrepareShipment placeholder,
                                         [],
                                      Send(Channel(Warehouse, Customer, ShippingPhase),
                                           ShipmentDispatched placeholder,
                                           [EmitShipmentDispatched],
                                        Recv(Channel(Warehouse, Customer, ShippingPhase),
                                             ShipmentDispatched placeholder,
                                             [],
                                          End))))

                                  // Payment branch
                                  Send(Channel(Customer, Merchant, ShippingPhase),
                                       ProcessPayment placeholder,
                                       [CallPaymentGateway],
                                    Recv(Channel(Customer, Merchant, ShippingPhase),
                                         ProcessPayment placeholder,
                                         [],
                                      Send(Channel(Merchant, Customer, ShippingPhase),
                                           PaymentConfirmed placeholder,
                                           [EmitPaymentConfirmed],
                                        Recv(Channel(Merchant, Customer, ShippingPhase),
                                             PaymentConfirmed placeholder,
                                             [],
                                          End))))
                              ],
                              End))))

                    OrderDenied placeholder,
                      Send(Channel(Merchant, Customer, OrderPhase),
                           OrderDenied placeholder,
                           [EmitOrderDenied],
                        Recv(Channel(Merchant, Customer, OrderPhase),
                             OrderDenied placeholder,
                             [],
                          End))
                ])))),

        End)
```

`placeholder` stands in for the payload values at the protocol-algebra level — a protocol value describes the *shape* of the exchange, not the runtime payloads. The generator and the projection operate on message *tags* (DU cases), not on payload values; payloads flow through at runtime. An implementer will likely factor this with a tag-only DU for protocol construction separate from the payload-bearing DU used at runtime, or use an approach like unit-payload constructor values (`PlaceOrder Unchecked.defaultof<_>`). That is an implementation choice the sketch leaves open.

With the CE builder, most of the parenthesization collapses and the expression reads as a sequence of `Send`/`Recv`/`Nested`/`Choice` operations, similar in shape to a Scribble protocol but native F#.

-----

## Appendix K — Alternative runtime backends

The initial Frank runtime is `MailboxProcessor` on F#’s `Async`, with the lightweight supervisor described in §6. That combination is minimal, dependency-free, and sufficient to exercise the full generator pipeline and the protocol algebra end-to-end. It is not the only viable runtime target.

Richer supervision semantics — strategy dispatch, restart backoff, restart limits, supervision trees, actor lifecycle hooks, persistence, clustering, remote actors — exist in mature .NET actor frameworks. Reimplementing them inside Frank would be a significant engineering project duplicating well-trodden ground. The intended path, when Frank’s protocols need those semantics, is to target one of these frameworks as an **alternative generator backend**: the same CE, the same `Frank.Protocol.Analysis` output, the same `IEffectHandler<_,_>` interface, but emission targets the framework’s actor primitives rather than `MailboxProcessor`.

This is a codegen-level concern, not a protocol-algebra concern. The author’s CE does not change; the generated code does.

### K.1. Candidate frameworks

|Framework            |Model                                                        |Notes                                                                                                                                                                                                                                  |
|---------------------|-------------------------------------------------------------|---------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------|
|**Proto.Actor**      |Classical actor model, faithful to Akka patterns, .NET-native|Full supervision strategies, remote actors, persistence plugins. [GitHub](https://github.com/asynkron/protoactor-dotnet)                                                                                                               |
|**Akka.NET**         |Port of Akka (Scala/Java) to .NET                            |Most mature supervision story. Heavier runtime and configuration footprint. [akka.net](https://getakka.net/)                                                                                                                           |
|**Microsoft Orleans**|Virtual actors (grains)                                      |Different conceptual model — grain activation/deactivation is implicit. Applicable when the role set is long-lived, addressable by identity, and distributed. [Orleans docs](https://learn.microsoft.com/en-us/dotnet/orleans/overview)|
|**Dapr Actors**      |Sidecar-based, language-agnostic                             |State persistence built in. Could bridge Frank to non-.NET participants. [Dapr actors](https://docs.dapr.io/developing-applications/building-blocks/actors/actors-overview/)                                                           |

### K.2. Mapping discipline

Each backend maps the v1 canonical actor template `HierarchicalMPSTActor` from §7 (and, once they land, the deferred specializations from §7) onto its own primitives. The mapping table a backend has to satisfy:

|Frank concept                          |Required in the backend                                                                                                                   |
|---------------------------------------|------------------------------------------------------------------------------------------------------------------------------------------|
|One actor per role                     |One framework primitive per role (Akka actor, Proto.Actor actor, Orleans grain, Dapr actor)                                               |
|`Send` / `Recv` (`AsyncFIFO` v1)       |Framework’s ask/tell / message-send primitive                                                                                             |
|Hierarchical `Nested` regions          |Framework’s parent-child or grain-hierarchy mechanism, if any; otherwise emulated in generated state                                      |
|`History` / `DeepHistory` resolution   |Journal lookup (same contract as the MailboxProcessor floor)                                                                              |
|Supervision (v1 restart-on-crash floor)|Native framework restart semantics; Appendix K.6 documents richer annotations as backend extensions                                       |
|`IEffectHandler<_,_>`                  |Unchanged — the handler interface is runtime-agnostic                                                                                     |
|`IProtocolJournal`                     |Framework’s native persistence adapted to the journal interface: Orleans grain state, Proto.Actor event-sourcing plugins, Dapr state store|
|Observability projections (MEL, OTel)  |Unchanged — consume the journal the same way                                                                                              |
|Peer references                        |Framework’s typed actor reference (`PID`, `IActorRef`, `IGrainFactory`, etc.)                                                             |

The CE and the effect handler stay identical across backends. The generator is the only layer that changes. Critically, the journal interface is the stable contract: each backend maps its native persistence story onto `IProtocolJournal`, and every downstream consumer (interpreters, projections, supervisor logic) works unchanged. Orleans grains already persist; Proto.Actor has event-sourcing plugins; Dapr has a state store building block. In each case the integration work is “expose these as `IProtocolJournal`,” not “reimplement the journal.”

### K.3. Why defer

All of these backends are deferred work. Reasons to hold:

- **The MailboxProcessor floor is enough to validate the algebra.** Projection, implementability, effect-message correspondence, and verification all exercise cleanly without a heavyweight actor runtime.
- **Framework commitment is long-term.** Adopting Proto.Actor or Akka.NET commits Frank’s users to that framework’s lifecycle, configuration, and learning curve. Making that commitment before the protocol algebra is stable would constrain design choices for the wrong reason.
- **Swapping backends later is a generator-level change.** Once the CE authoring surface and `Frank.Protocol.Analysis` are stable, adding a new backend is a matter of writing one more emission pathway. It does not force rewrites of authored protocols.
- **Multi-backend support is itself a later decision.** The question of whether Frank ships with one backend or lets the author pick is a product question that does not need to be answered until at least one backend beyond MailboxProcessor is built.

### K.4. When to revisit

Revisit when one of the following is true:

- A protocol in production needs supervision semantics the minimal floor does not provide (restart-on-failure, at-least-once delivery on role crash, distributed role placement).
- A protocol needs to span process or machine boundaries and the MailboxProcessor assumption of single-process execution breaks.
- A protocol’s role lifecycle needs to be longer than a single protocol instance (virtual-actor territory — grain activation semantics matter).

The first backend to add under those conditions is probably Proto.Actor, for closest conceptual match to MailboxProcessor plus full supervision. Orleans is the right target if the virtual-actor model becomes relevant. Akka.NET is a last resort given its configuration weight; Dapr is interesting only if multi-language participation becomes a requirement.

### K.5. Communication-semantics extensions per backend

The v1 algebra fixes `CommSemantics = AsyncFIFO of bound: int` (Appendix E.1). `Sync` and `AsyncCausal` are documented in the algebra so v1 does not foreclose them, but they are not active on the MailboxProcessor floor and do not have first-class constructors in the v1 core DU. A backend that provides the corresponding primitive activates them through a backend-scoped attribute on the channel or protocol, parsed by `Frank.Protocol.Analysis` only when the backend is selected.

Indicative mapping:

|Extension       |MailboxProcessor           |Proto.Actor                        |Akka.NET                       |Orleans                                            |Dapr                           |
|----------------|---------------------------|-----------------------------------|-------------------------------|---------------------------------------------------|-------------------------------|
|`Sync`          |Not available              |Request/await on `PID` with timeout|`Ask` pattern                  |Grain method invocation (effectively sync)         |Actor method invocation        |
|`AsyncFIFO` (v1)|Native mailbox             |Native mailbox                     |Native mailbox                 |Grain request ordering (single-threaded activation)|Actor turn-based concurrency   |
|`AsyncCausal`   |Requires vector-clock layer|Not native; requires middleware    |Not native; requires middleware|Not native; requires middleware                    |Not native; requires middleware|

Authors opt into an extension per channel, e.g., `[<Backend(ProtoActor); CommSemantics(Sync)>]` on a channel declaration. The generator rejects the attribute if the selected backend does not support the semantics. Verification reasons about the extension only when it is activated; the v1 Tier 1/Tier 2 properties remain computable under `AsyncFIFO` alone.

### K.6. Supervision-policy extensions per backend

The v1 `Nested` constructor does not carry a `SupervisionPolicy` (Appendix E.2 note). The v1 supervisor floor (§6) is restart-on-crash for a single actor with in-memory journal replay, and that behavior is fixed. Richer Erlang/OTP-style policies are backend extensions, expressed as backend-scoped attributes on `Nested` scopes rather than as algebra-level DU cases.

Indicative mapping:

|Policy      |MailboxProcessor floor (v1)  |Proto.Actor                     |Akka.NET                        |Orleans                                   |Dapr              |
|------------|-----------------------------|--------------------------------|--------------------------------|------------------------------------------|------------------|
|`Restart`   |Fixed default behavior       |`OneForOneStrategy` with restart|`OneForOneStrategy` with restart|Grain reactivation on failure             |Actor reactivation|
|`RestartAll`|Not available                |`AllForOneStrategy` with restart|`AllForOneStrategy` with restart|Not native (explicit grain-group teardown)|Not native        |
|`Escalate`  |Not available (process-level)|Supervisor escalation           |Supervisor escalation           |Not native                                |Not native        |
|`Stop`      |Kills actor (no replay)      |Stop strategy                   |Stop strategy                   |Grain deactivation                        |Actor deactivation|

Sketch of the opt-in pattern:

```fsharp
[<Backend(ProtoActor); Supervision(RestartAll)>]
let orderRegion = nested {
    // ...
}
```

As with K.5, the generator rejects the attribute if the selected backend does not support the policy. The v1 Tier 1 supervision-soundness property (Appendix D, row 8) is defined against the floor; richer policies broaden the property’s reach but do not change its shape.

-----

## Appendix L — What’s novel and what isn’t

This appendix exists because the rest of the doc can read as more ambitious than it is. The honest position: Frank is primarily a **design and engineering contribution**, not a research contribution. Every major theoretical piece it uses is already in the literature. What Frank does is combine them into a working F# library targeting .NET actors, with two possible modest formal extensions that would need to be confirmed against the literature before being claimed.

### L.1. Directly borrowed and cited — not novel

Each of these is adopted wholesale from existing work:

|Piece                                            |Source                                                                                                                           |Status                                                   |
|-------------------------------------------------|---------------------------------------------------------------------------------------------------------------------------------|---------------------------------------------------------|
|Hierarchical state machines                      |Harel (1987)                                                                                                                     |40 years of literature                                   |
|Multiparty session types                         |Honda, Yoshida, Carbone (2008, 2016)                                                                                             |Foundational                                             |
|Coalgebraic view of session types                |Keizer, Basold, Pérez (2021, 2022)                                                                                               |Frank uses this as the internal unification              |
|Automata-based MPST projection + implementability|Li, Stutz, Wies, Zufferey (CAV 2023)                                                                                             |The projection algorithm Frank implements                |
|Effects ↔ sessions correspondence                |Orchard, Yoshida (POPL 2016)                                                                                                     |Theoretical anchor for the effect-message wiring         |
|CPS compilation of effect handlers               |Hillerström, Lindley, Atkey, Sivaramakrishnan (FSCD 2017)                                                                        |Compilation scheme onto `Async.FromContinuations`        |
|Effect handlers ≡ delimited continuations        |Forster, Kammar, Lindley, Pretnar (JFP 2019)                                                                                     |Justifies the DCont framing                              |
|Defunctionalization                              |Reynolds (1972)                                                                                                                  |Reifies continuations as successor-state dispatch        |
|Witness-object HKT encoding                      |Yallop, White (FLOPS 2014); Palladinos’s [Higher](https://github.com/palladin/Higher)                                            |Pattern-matched, library not depended on                 |
|Applicative composition framing                  |McBride, Paterson (2008); Hutton                                                                                                 |Used in Appendix N to bound the witness-object’s reach   |
|First-class synchronous combinators              |Reppy, Concurrent ML (1999); Hopac on F#                                                                                         |Pattern reference for the Appendix O codegen contingency |
|F# + session types + SMT verification            |Neykova, Hu, Yoshida, Abdeljallal (Session Type Providers, CC 2018); Zhou, Ferreira, Hu, Neykova, Yoshida (Session*, OOPSLA 2020)|Closest existing cousins to Frank on .NET; Session* on F*|
|Scribble-to-target code generation               |Scribble toolchain (Yoshida group and collaborators, 2010s–present)                                                              |Existing practice for many targets                       |

None of these is a Frank contribution. The doc cites them at the points where it uses them.

### L.2. Possibly novel — small formal extensions, not confirmed

Two places where Frank appears to go modestly beyond the published literature. Both are flagged as *possibly* novel because the literature on session types and statecharts is large and the author has not done an exhaustive survey. Before claiming either as a contribution in any venue, a proper survey is required.

**L.2.1. Hierarchical statecharts integrated with multiparty session types as one algebra, with projection preserving the hierarchy.** Li et al. (CAV 2023) project flat MPST to communicating state machines. Demangeon and Honda (CONCUR 2012) introduce *nested protocols* in session types — protocol-composition-style nesting, where a protocol can invoke a sub-protocol as a unit. Scribble implements this; NuScr ships it as an extension. Gheri and Yoshida (Hybrid MPST, 2023) handle compositionality of MPST via subprotocols, a related but distinct notion. Capecchi, Giachino, and Yoshida (FSTTCS 2010) introduce global escape as an exception-like mechanism, which is hierarchy-adjacent. None of these combines *Harel-style state nesting within a single role’s local type* — hierarchy as depth, orthogonal regions, history states, event bubble-up from inner to outer states — with MPST projection in a single global-type syntax. Frank’s `Nested` construct and its projection rule appear to land in that gap.

If this is genuinely not in the literature, Frank’s `Nested` construct and its projection rule are a modest formal extension. Not a new theorem — more like “here is the syntax, here is the extended projection operator, here is the proof it commutes with Li-et-al.‘s construction when nesting is trivial, plus the modal-refinement rule for what a role’s local type inherits from its containing `Nested`.” Systems-paper flavor, not theory-paper flavor.

**L.2.2. Codegen-time verification of generated actor code against its source protocol, with emitted SMT-LIB artifacts.** Session* (Neykova et al. 2018) verifies source protocols with refinements at compile time via the type provider. Frank’s full design extends this in two ways: it verifies the generated code’s behavior against the projection (not just the protocol against itself), and it emits the SMT-LIB queries as build artifacts for reproducibility, independent verification, and build caching. V1 ships the source-protocol half via inline Z3 (§10.9) and covers the generated-code half empirically through property-based conformance testing (§8.1, Appendix P); artifact emission and closed-form Z3-against-emission are Appendix Q deferred work. The claim is about what the design *supports*, not what v1 alone demonstrates.

The protocol-verification half is within existing practice. The generated-code-verification half — running Z3 over both source and emitted artifacts in one pipeline — is close to but not exactly in the published literature. If written up, it’s a tools or experience paper, not a theory paper.

Neither of these is a reason on its own to publish. Taken together with Frank as a working library, they could support a single short paper in a tools track or workshop.

### L.3. Engineering-novel — combination, not contribution

The particular combination of design choices is not present in any existing system I could find:

- F# CE authoring surface
- Coalgebraic session types as the canonical internal form
- Li-et-al. projection with implementability checking
- Witness-object HKT pattern on `Task`/`Async` for effects, with per-protocol generated `EffectBuilder` CEs absorbing the brand
- Delimited-continuation framing onto `Async.FromContinuations`, possibly extended (per Appendix O contingency) to a uniform CML-style choice-combinator codegen template for hierarchical parallels
- Fabulous.AST emission, Fantomas formatting, FCS-based pipeline
- Integrated codegen-time Z3 verification, with source-protocol inline invocation in v1 and SMT-LIB artifact emission + Z3-against-emission as designed-for upgrades (Appendix Q)
- Journal-primary runtime with a pluggable backend (in-memory in v1; SQLite and LMDB as durability and performance upgrades) and `Microsoft.Extensions.Logging` / OpenTelemetry as downstream projections
- Lightweight supervisor floor with defined escape hatch to Proto.Actor / Akka.NET / Orleans
- Single-pathway intake in v1 (hand-authored CE), with multi-format intake (Scribble / SCXML / custom DSLs) as designed-for expansion (Appendix R)

No existing system combines these. That’s a **design synthesis**, not a research novelty. It matters for practitioners who want this combination; it does not matter for program committees.

### L.4. Explicitly not new

To prevent scope misreading:

- Frank is **not** a new type system.
- Frank is **not** a new session-type calculus.
- Frank is **not** a new algebraic-effects theory.
- Frank is **not** a new projection algorithm — it implements Li et al.’s.
- Frank is **not** a new verification logic — it uses SMT via Z3.
- Frank is **not** a new actor model — it generates code onto an existing one (`MailboxProcessor`) and structures its runtime to allow swapping in others.

### L.5. What Frank *is*

- An F# library that unifies several existing formalisms (statecharts, MPST, coalgebraic session types, algebraic effects) into a single author-facing CE.
- A generator that implements known correspondences (effects ↔ sessions, handlers ↔ delimited continuations, projection via automata) onto the .NET actor model.
- A toolchain that combines codegen with integrated static verification in a way that’s close to but may not quite be in the existing published practice.

Framed this way, Frank is useful and defensible. Framing it as a research contribution would overstate what’s actually new. The two flagged items in §L.2 are the only places where a research claim might live, and both require a proper literature survey before being made.

-----

## Appendix M — Journal storage backends: in-memory default, SQLite upgrade, LMDB escape hatch

The journal (§9.1) is the canonical in-process record of protocol execution. Its storage backend is a narrow, orthogonal concern — the journal’s consumers (history resolution, resumption, interpreters, projections) see a stable query interface regardless of what’s underneath. This appendix documents the v1 default choice, the first durability upgrade, and the path to a higher-throughput alternative if performance demands it.

### M.0. In-memory — the v1 default

Frank’s v1 `IStatechartJournal` default is an **in-memory journal**. The journal is a per-actor, in-process collection of entries holding the history needed for shallow-history and deep-history resolution, scope-path lookups, and projection subscriptions. It satisfies `IProtocolJournal` in full (§M.4); what it does not provide is durability across process crashes.

Reasons for in-memory as v1 default:

- **No operational footprint.** No file layout decision, no schema migration, no database file to colocate with deployments. A Frank protocol runs standalone with no persistence configuration.
- **Matches the v1 supervisor floor.** The v1 supervisor (§6) is an in-process restart-on-crash floor: if the process dies, the actor dies with it, and in-flight messages are lost. An in-memory journal has exactly the same crash semantics. Pairing a durable journal with a non-durable supervisor would be a mismatch the author could not reason about.
- **Fastest path to exercising the algebra.** The verification pipeline, projection pipeline, and code generator all run identically against in-memory; the persistence choice does not gate end-to-end validation.
- **Swap, don’t change.** Upgrading to SQLite (§M.1) is a configuration change at protocol startup; it does not alter authored code, generated code, or any downstream consumer.

In-memory is appropriate when the protocol’s durability requirement is satisfied by the process lifetime: short-running workflows, tests, exploratory prototypes, and protocols whose external effects (database writes, outgoing messages) already provide the durable record the business cares about. It is not appropriate when resumption across crashes is required; that case upgrades to §M.1.

### M.1. SQLite — the first durability upgrade

Reasons for SQLite as the durability-upgrade default when in-memory is insufficient:

- **Transactional durability.** Every state transition commits as an ACID transaction; a crash mid-transition leaves the journal reflecting the last committed state, not a partial write. This is exactly what resumption needs.
- **Crash safety.** WAL mode gives well-understood crash-recovery semantics without author configuration.
- **Queryable history.** Interpreters (§10.9.3) and handlers that need historical state issue SQL queries against structured data. No custom indexing, no hand-rolled serialization, no bespoke query language.
- **Operational story.** SQLite databases are single files, inspectable with standard tooling (`sqlite3` CLI, any GUI browser), usable for ad-hoc debugging. If a protocol instance misbehaved in production, the journal is a file an operator can copy and open.
- **Portability.** SQLite runs on every platform .NET runs on. No native dependencies beyond what’s already in the BCL ecosystem.
- **Mature .NET story.** `Microsoft.Data.Sqlite` is Microsoft-maintained and widely deployed; writeable from F# as easily as from C#.

*Architectural pattern adopted from practice.* The journal-as-colocated-storage-per-actor model matches Cloudflare Durable Objects and similar per-entity persistence designs. Each actor owns its journal namespace; the journal lives with the actor’s execution; it is the source of truth for state, not replayed from an external log. This is the opposite of event-sourced-via-shared-log designs (Kafka + consumer state), and it’s the right match for the MailboxProcessor floor — low operational complexity, single-process semantics, no coordination protocol.

*File layout decision — one database per actor, or one per protocol instance?* Deferred to implementation; both are viable. One-per-actor matches Durable Objects most directly. One-per-instance gives the supervisor trivially-cheap read access to every actor’s journal in the protocol instance (single connection, joined queries). Initial implementation can start with one-per-instance and refactor if a specific workload hits contention.

### M.2. Where SQLite starts to hurt

SQLite is excellent up to roughly the write throughput a single process can drive against a single file under serialized transactions. It starts to show strain in the following regimes:

- **Very high-frequency state transitions.** Protocols with thousands of journal writes per second per actor, especially if many actors share a single database file. WAL mode helps but has ceilings.
- **Very large journals.** Long-running protocol instances whose journals grow into the tens of gigabytes. SQLite handles this correctness-wise but query latency on complex joins degrades.
- **Heavy concurrent writers.** If many actors share a database and all write frequently, SQLite’s single-writer discipline serializes them; per-actor files sidestep this but introduce a different operational overhead (file count).

None of these is a theoretical failure mode — they’re empirical. Frank’s position is “start with SQLite; profile first; only swap if the profile demands it.”

### M.3. LMDB — the escape hatch

LMDB is the recommended alternative if SQLite can’t keep up for a specific workload. It gives up:

- **Ad-hoc queryability.** LMDB is a key-value store with cursor iteration. No SQL, no declarative queries. Interpreters and tooling that relied on SQL need to be rewritten against the key-value API or run against a replicated SQLite mirror.
- **Standard tooling.** `sqlite3` CLI doesn’t exist for LMDB. Inspection tooling is thinner.
- **Cross-process ease.** SQLite’s file-based model makes moving databases between machines trivial. LMDB’s memory-mapped format is similarly portable but with more environmental assumptions.

It regains:

- **Higher write throughput.** Memory-mapped, append-oriented, lock-free-reader semantics. For append-heavy workloads (which journals are), this is the right shape.
- **Lower read latency.** Readers don’t block writers and vice versa; there’s no serialization contention.
- **Better behavior at scale.** Large databases don’t slow down queries the way they can in SQLite.

The empirical trigger for switching is specific: **if profiling shows journal writes are the protocol’s bottleneck** — not total execution latency, not handler latency, not message-passing throughput, but journal write throughput specifically — switch the affected protocol’s journal to LMDB.

### M.4. The shared interface that enables the swap

Both backends implement the same `IProtocolJournal` interface. The interface is the stable contract; backends are interchangeable. Handlers, interpreters, projections, and the supervisor all code against the interface, never against a specific backend.

The core operations the interface must support:

- Append a journal entry (the atomic write).
- Query entries by scope path, by event type, by time range, or by role.
- Read the most recent entry for a given scope (shallow history resolution).
- Read the recursive last-entry chain for a scope (deep history resolution).
- Subscribe to new entries (for projections and live interpreters).

Backend-specific operations (SQL query on SQLite, cursor scan on LMDB) are implementation details of the specific backend, not part of the interface.

### M.5. Reference precedents

- **Cloudflare Durable Objects** — per-entity colocated storage, strong consistency within an object, SQLite-backed in the current implementation. Direct architectural precedent for Frank’s per-actor journal.
- **Gleam’s actor storage patterns** — similar per-actor persistence colocated with execution in BEAM/Gleam ecosystems.

Both are cited as existence proofs that the per-entity-journal architecture is a working pattern at production scale, not a Frank invention.

-----

## Appendix N — Computation-expression smoothing of the witness-object pattern

The witness-object HKT encoding adopted in §5 has a known ergonomics cost: the `App<'F, 'A>` brand appears in every type signature that mentions an effectful computation. This appendix documents how the F# computation-expression machinery hides that brand from authors. The verbosity exists in the generated builder, which the author never reads. It also documents the limit of what CE smoothing can do — specifically, the effect-composition constraint the witness-object pattern inherits from any monadic encoding.

### N.1. The basic shape

The generator emits, alongside `IEffectHandler<'F>`, a per-protocol `EffectBuilder<'F>` whose `Bind`, `Return`, and `ReturnFrom` delegate to the handler. The brand only appears in the builder’s method signatures and in lift helpers; everything an author writes inside the CE looks like ordinary monadic code:

```fsharp
type EffectBuilder<'F>(h: IEffectHandler<'F>) =
    member _.Bind(m: App<'F, 'a>, f: 'a -> App<'F, 'b>) = h.Bind f m
    member _.Return x = h.Return x
    member _.ReturnFrom m = m
    member _.Zero () = h.Return ()
    // Combine, Delay, TryWith, TryFinally, Using, For, While...

let eff (h: IEffectHandler<'F>) = EffectBuilder<'F>(h)
```

Author code reads naturally:

```fsharp
eff handler {
    let! orderId   = invoke (WriteOrder data)
    let! inventory = invoke (CheckInventory orderId)
    return orderId, inventory
}
```

The brand only appears in the builder constructor and in lift helpers. Every line of author code that *would* have shown `App<TaskBrand, _>` shows nothing.

### N.2. Custom operations per effect DU case

`[<CustomOperation>]` decorators let each effect DU case become a CE keyword. The generator emits one custom operation per declared `'Effect` case, so the available operations exactly match the protocol’s permitted effect set. The compiler enforces this — an effect not in the algebra is not a callable operation in the CE.

```fsharp
eff handler {
    read "order:123"          // [<CustomOperation>] for Read
    write "key" value         // [<CustomOperation>] for Write
    publish "topic" payload   // [<CustomOperation>] for Publish
}
```

### N.3. Applicative `and!` for `Parallel`

F# 5+’s `MergeSources` / `BindReturn` machinery compiles `let! ... and! ... and! ...` to a single fork-join rather than nested Binds. After projection, a `Parallel` of *n* branches is exactly an *n*-way applicative — static arity, no continuation threading. This is one of the cleanest correspondences in the entire pipeline: `Parallel` in the algebra → applicative `and!` in the emission.

```fsharp
eff handler {
    let! shipResult = shipBranch
    and! payResult  = payBranch
    return shipResult, payResult
}
```

The generator emits `MergeSources` automatically for every `Parallel` node in the projected protocol. Branch results combine into a tuple; the structured-concurrency boundary is the CE itself. This is probably the strongest argument for putting the effect surface in a CE rather than calling the handler methods directly.

### N.4. `match!` for `Choice` projection

`match!` turns a `Choice` projection into idiomatic F# at the use site without any visible monadic plumbing:

```fsharp
match! invoke (CheckInventory id) with
| Available    -> // ...
| OutOfStock   -> // ...
| BackorderEta d -> // ...
```

### N.5. Resumable code as a zero-allocation option

`[<ResumableCode>]` is the long-game answer to the per-step allocation argument that motivated the rejection of free monads (B.1). Since F# 6, the same state-machine generator that compiles `task { }` to a zero-allocation struct is exposed to user CEs. A generated effect builder using it compiles to the same shape as `task`, removing the per-step allocation cost.

Worth holding in reserve until profiling demands it — the unspecialized version is sufficient for v1, and resumable code adds nontrivial complexity to the builder. Document as an upgrade path, not a day-one requirement.

### N.6. What CEs do not smooth over

- **Brand inference still requires the handler in scope** at the CE construction site. There is no ambient-handler trick equivalent to algebraic effects’ implicit handler resolution — `let eff = EffectBuilder<'F>(h)` (or equivalent) has to be in scope at every call site.
- **Error messages still leak `App<_, _>`** when overload resolution fails inside builder methods. The brand is hidden in success paths only; when something goes wrong, the user sees the underlying machinery.
- **Cross-CE interop with `task` / `async`** needs an explicit lift or an overloaded `Bind` per carrier. `let! x = task { ... }` inside `eff { ... }` requires a generated overload.
- **Effect composition does not come for free.** This is the deepest limitation and warrants its own subsection (§N.7).

### N.7. Effect composition — the constraint/liberation reading

The applicative-functors literature (McBride & Paterson 2008; Hutton’s *Programming with Effects* and related notes) gives the framing: applicative effects compose for free; monadic effects do not. The witness-object pattern, like any monadic encoding, inherits this constraint.

The concrete shape of the problem: each `IEffectHandler<'F>` is for a single effect type `'F`. Composing effect *types* — for example, threading `Async` *inside* a domain effect, or stacking `Reader + State + IO` — has the standard three options:

1. **One monolithic handler that encompasses all the effects at once.** Coarse but workable; loses modular handler implementations.
1. **Separate handlers and lift between them explicitly.** The lift code is exactly the boilerplate a free monad’s transformer stack would generate automatically. The “viscera” the witness-object surface was supposed to hide shows up in the lift code:
   
   ```fsharp
   eff handler {
       let! result = invoke SomeEffect
       match result with
       | Ok value ->
           let asyncValue = someAsyncComputation value
           // Manually lift Async back into App<'F, _>:
           let! unwrapped =
               asyncValue
               |> Async.StartAsTask
               |> Task.map handler.Return
               |> invoke
           return unwrapped
       | Error e -> // ...
   }
   ```
1. **Applicative `and!` only.** Composes for free, but only when branches are *independent* — no result of one branch can drive control flow into another. The moment one branch’s result determines what the next branch does, you are back in the monadic regime and option 1 or option 2 applies.

Note that this is *not* a flaw in the witness-object pattern specifically; monad transformers (the obvious-looking alternative) also do not compose for free. They require one stack written per combination of effects, just at a different layer. The Hutton constraint/liberation framing is the honest read: monads liberate per-effect expressiveness at the cost of constraining cross-effect composition. There is no encoding that makes both free in a language without first-class effect handlers.

The practical question for Frank: how many real protocol workflows need monadic sequencing across effect *types*, versus how many can be expressed as applicative parallelism within a single effect type? If the answer is “mostly applicative within one type,” the no-free-composition constraint does not bite in practice. If real protocols routinely sequence across effect types, the codegen needs an answer (probably option 1 — generated combined handlers per protocol).

The Voltron composition discipline already established for the framework points toward option 1: composition lives at the *algebra* level, before emission. The generator produces one handler interface per composed effect set per protocol, and the CE is built against that. Composition friction moves to codegen time, where it is tractable.

-----

## Appendix O — CML-style choice combinators as a codegen target (contingency)

This appendix describes a candidate emission template for hierarchical statecharts with parallel regions, drawing on the Concurrent ML / Hopac tradition of first-class synchronization combinators backed by delimited continuations. **It is documented as a contingency strategy, not a commitment.** The decision whether to adopt it is open question 11 in §13, settled by the experiment described in §12.

The key framing point: this is a **codegen-internal** pattern, not author-facing. Authors continue to write `Parallel` / `Choice` / `Nested` in their protocol CEs exactly as in §3; the question is only how `Frank.CodeGen` emits the corresponding actor body for hierarchical parallel composite states.

### O.1. The problem this addresses

Hierarchical statecharts in the Harel formalism (and the SCXML serialization of them) put parallel composite states inside other composite states inside still other composite states. Each parallel region is a concurrent subprocess; entering a parallel composite starts all its regions simultaneously; an external event or completion causes a transition out, with synchronization across regions.

The naive emission strategy is ad-hoc async: generate `Task.WhenAny` calls, channel polling, explicit await sequences, with the synchronization logic threaded through the actor’s main loop. This works, but the generated code shape varies with the structure of the statechart — three regions emit one shape, four emit a slightly different one, nested parallels emit yet another. Codegen ends up with many adjacent templates, and adding a new statechart pattern often means writing new emission code.

The alternative — well-known from CML and from Hopac — is to express parallelism declaratively as a *choice structure*, with the runtime resolving which branch fires based on guard readiness. The shape of the generated code stops varying with the statechart; only the contents of each branch and the depth of nesting change. This matches the CML insight that the guard-before-body structure is the synchronization point: you are not generating async sequencing at all, you are generating a declarative choice structure that the runtime interprets as “pick the first ready branch.”

### O.2. The candidate template

Each parallel composite state in the hierarchical statechart compiles to a single emission template:

- One `reset` boundary at the parallel composite state itself.
- For each region in the parallel composite, one `ChoiceBranch` with a guard (region-ready predicate) and a body (the region’s state machine wrapped in a `shift`).
- A single dispatch primitive picks the first ready branch and resumes its continuation.

```fsharp
type Continuation<'a> = Shift  // primitive — see §O.6 for what this actually is

type ChoiceBranch<'a> = {
    guard: unit -> bool
    body:  Shift
}

let generateParallelComposite (regions: StatechartRegion list) =
    let branches =
        regions |> List.map (fun r ->
            { guard = fun () -> r.isReady ()
              body  = shift (fun k -> r.execute () |> k) })
    reset (fun () ->
        branches
        |> List.tryFind (fun b -> b.guard ())
        |> Option.map (fun b -> b.body))
```

Hierarchy is preserved by nesting: a region can itself contain a parallel composite, which compiles to its own `reset` boundary with its own branches inside the outer region’s body. Sequential states inside a region compile to shifted continuations chained by ordinary CE composition.

The template is **the same regardless of statechart shape**. Two regions or ten, two levels of nesting or six, the emission is one `reset` per parallel composite, one `ChoiceBranch` per region, one dispatch primitive per `reset`. The mapping is:

- statechart → outer `reset` boundary
- parallel composite → `reset` boundary with branches
- sequential state → shifted continuation
- nesting depth → CE nesting depth

### O.3. Why this might be worth adopting

- **Uniform codegen target.** The generator emits one shape; downstream tooling (interpreters, debug views, journal projections) can rely on the structure.
- **Predictable scaling.** Complexity in the input statechart maps to nesting depth in the output, not to a different emission template per case.
- **LLM-anchoring resistance.** When codegen tools (including LLM-assisted ones) generate code, they tend to anchor on existing patterns. A single uniform template gives them less room to drift than several adjacent ad-hoc templates. Appendix G describes the failure mode this is designed to prevent; §G.4 specifically connects that to why the §12 experiment lands between steps 5 and 7.
- **Deliberate-by-default rather than improvised under pressure.** Once complexity emerges in a real statechart — and with hierarchical composites it will — having the pattern already in hand means not improvising codegen templates on the fly while debugging.

### O.4. Why to defer until the experiment

- The simpler ad-hoc async emission may be sufficient for the protocols Frank actually targets in v1. If most workflows are sequential with shallow parallelism, the choice-structure machinery is overhead.
- Delimited continuations on .NET are not first-class. The implementation either reuses HoPac (unmaintained, dependency risk) or emulates them on top of `Async.FromContinuations` / `task` with explicit suspension boundaries (more code, less explicit).
- Adopting this commits the entire codegen pipeline to one emission shape. Pivoting later is expensive — exactly the LLM-anchoring problem in reverse.
- “Could be useful” and “necessary” are different questions. The experiment in §O.5 is the way to tell which one applies to Frank’s actual statecharts.

### O.5. The experiment that decides

Per §12, before completing step 7 of the build order:

1. Pick one moderately complex statechart from Frank’s design — at least two levels of hierarchy, at least one parallel composite with two or more regions.
1. Emit it twice from the same structured actor description (the output of step 5): once with ad-hoc async sequencing, once with the choice-structure template above.
1. Compare on three axes:
- **Readability of the generated code.** Which version is easier to read with no prior context?
- **Cost of extension.** Which version is easier to extend with another region or another level of nesting?
- **Maintenance burden.** Which version is easier to debug or modify when the statechart logic changes?
1. If the choice-structure version is meaningfully clearer or more extensible, adopt it as the default emission template before step 7 commits to a canonical emission shape for the `HierarchicalMPSTActor` template.
1. If the ad-hoc version is comparable, defer Appendix O indefinitely and document the decision; mark open question 11 closed in favor of ad-hoc emission.

Cost: a few hours of codegen exploration. Value: a deliberate emission strategy chosen against real evidence rather than improvised under deadline pressure, with a written decision record either way.

### O.6. Continuation primitive — open

If the choice-structure template is adopted, the underlying continuation primitive is itself an open question (open question 12 in §13):

**Option A — custom `Continuation<'a>` / `Shift` / `reset` type.** Matches the Hopac and CML literature directly. Suspension points are explicit; the codegen template is structurally clear about where each branch yields control back to the choice resolver and where another branch resumes. Adds either a runtime dependency on something Hopac-shaped (a non-starter for new code) or a hand-rolled implementation of delimited-continuation primitives on top of `Async.FromContinuations`.

**Option B — F# native `async` or `task` with explicit suspension boundaries.** Reuses the runtime that’s already there. Suspension is implicit (the runtime decides where to yield), which is a less explicit codegen template but avoids new primitives. Whether this can express the guard-and-dispatch shape cleanly enough is itself an experimental question — the suspension boundaries that `async` / `task` give you may be coarser-grained than what the choice structure wants.

The decision should be settled together with whether to adopt the choice-structure template at all. Settling one without the other risks committing to a primitive without a use case or to a use case without a workable primitive. Both should be exercised in the §O.5 experiment so the comparison is between specific full implementations rather than between an abstract pattern and a concrete one.

### O.7. Multi-role implications

A `Choose` resolved inside one role’s projection that affects another role’s expected protocol state needs a synchronization signal — otherwise the roles drift. In an HMST projection, this typically means the choice winner is broadcast to other roles as a protocol event, which their local types constrain them to receive before continuing.

The codegen template above handles the single-role case directly; the multi-role case requires that the chosen branch include a `Send` to the other affected roles as part of its body. This is handled at the projection level (the global type already declares the synchronization message), so the choice-structure template does not need special multi-role machinery — it inherits the synchronization from the Li-et-al. projection.

That covers the main risk: a choice combinator pattern borrowed from single-process CML semantics could otherwise miss the cross-role synchronization that HMST specifically provides. Done correctly, the template composes cleanly with the §4 projection. The HMST projection is what does the real work; the choice-structure template is just the shape of the emitted body.

### O.8. Status

Marked as a **Phase 0.5 contingency** — designed for, not committed to. The §12 experiment is the only thing that turns this from contingency into commitment, or from contingency into closed-out alternative. Either outcome is fine; what is *not* fine is starting the codegen pipeline at scale without the decision having been made deliberately one way or the other.

-----

## Appendix P — Property-based conformance testing

This appendix specifies the v1 conformance-testing harness that replaces build-time SMT verification *against emitted code*. §8 establishes that v1 Z3 verification runs inline against the **source protocol** (the algebra value produced by `Frank.Protocol.Analysis`), not against SMT-LIB artifacts emitted from generated code. The evidence that generated code preserves those source-level properties is provided here: a property-based test suite that runs the generated actor network against traces derived from the source protocol and asserts the projected local behaviors match.

### P.1. Role in the v1 architecture

- **What source-level Z3 verifies:** the protocol algebra value is deadlock-free, race-free in `Parallel`, payload-refined, and implementable. If those properties hold on the algebra, they hold of *the specification*.
- **What conformance testing verifies:** the generated actor network, executed against the handler, produces message traces consistent with the Li-et-al. projection of the source protocol for every role. If conformance holds, the implementation-of-the-spec question is answered by a substantial sample of behaviors, not by formal equivalence.
- **What is intentionally *not* verified in v1:** generated-code-versus-projection equivalence as a closed mathematical property. That is the job of Z3-against-emission, which is deferred to Appendix Q.2.

This is a pragmatic split: v1 ships with formal verification where it is cheap and decisive (source protocol) and with high-coverage empirical verification where formal equivalence is expensive to set up (generated code). The algebra does not foreclose closing that gap later; it only declines to do so as a v1 requirement.

### P.2. Library structure

The harness ships as `Frank.Protocol.Testing`, built on FsCheck:

- `Frank.Protocol.Testing.Traces` — generators that produce protocol traces from the source protocol algebra value. Each trace is a sequence of role-visible events consistent with the protocol’s global type: `Send`/`Recv` pairs, `Choose` resolutions, `Nested` entries/exits, `History`/`DeepHistory` resumptions.
- `Frank.Protocol.Testing.Conformance` — the conformance runner. Takes a source protocol, its generated actor network, the effect handler, and an `IProtocolJournal`; drives the actor network through generated traces; asserts each role’s emitted event sequence is a valid local projection of the trace.
- `Frank.Protocol.Testing.Builders` — CE-style builders to declare the conformance suite per protocol. One line per protocol in a test project invokes the full generated-trace sweep.

### P.3. What the harness asserts

Per trace, per role:

1. **Local-type conformance.** The role’s observable event sequence is accepted by the Li-et-al. projection of the source protocol for that role.
1. **Effect discipline.** Every `Effect` invoked by the role’s actor during the trace appears in the enclosing scope’s `permittedEffects`.
1. **History resolution.** Resumption after a simulated `History` or `DeepHistory` transition reads the expected scope path from the journal.
1. **Supervision floor.** Under the v1 supervisor, a simulated actor crash results in restart and replay; the post-replay event sequence matches the pre-crash sequence from the last committed journal entry forward.

Assertions 1–3 cover Tier 1 and Tier 2 properties at the implementation level; assertion 4 closes the loop between supervisor behavior and journal semantics that the v1 floor relies on.

### P.4. Trace generation strategy (open question 13)

Two candidate strategies, settled as an open question (§13, new #13):

- **Exhaustive within bounds.** Enumerate all traces up to a bounded depth (scope nesting, message count, choice resolutions). Gives deterministic coverage; bound selection is a judgment call.
- **Randomized with shrinking.** Sample traces uniformly from the accepting automaton of the global type; rely on FsCheck’s shrinking to minimize counterexamples. Scales better to deep protocols; coverage is probabilistic.

Default v1 position is **randomized with shrinking, bounded depth on hierarchical nesting**. Exhaustive mode is available as an opt-in per protocol for small protocols where the bound is tractable. The choice is not adversarial: both approaches share the generator and the oracle; they differ only in the sampling discipline over traces.

### P.5. Test-time versus build-time gate (open question 14)

Conformance testing can run as a build-time gate (fail the build if a generated trace is rejected) or as a test-time suite (fail the CI test run). Settled as an open question (§13, new #14). Default v1 position is **test-time suite** on the grounds that build-time coverage should remain fast; a conformance sweep over a non-trivial protocol may take seconds to tens of seconds and is appropriate in CI, not on every incremental build.

The algebra does not depend on this choice; both modes use the same harness.

### P.6. Relationship to Appendix Q.2 (Z3-against-emission)

Conformance testing and Z3-against-emission address the same question — does generated code preserve source-protocol properties — with different tools. Conformance testing is high-coverage, empirical, cheap to stand up, and scales with test-time budget. Z3-against-emission is formal, closed, expensive to stand up, and requires the SMT-LIB artifact pipeline described in Q.1.

V1 ships conformance. V1+N, when the artifact pipeline exists, adds Z3-against-emission as a stronger complement; conformance testing does not retire, because random-trace coverage of behaviors not directly modeled in Z3 (e.g., supervisor behavior under crash injection) remains valuable.

-----

## Appendix Q — Operational extensions and deferred verification

This appendix collects the verification and projection capabilities that are defined by the algebra but are not in the v1 shipping scope. Each item is described at the level of what would need to be built, with enough detail that the v1 architecture does not preclude it. None of them is active in v1.

### Q.1. SMT-LIB artifact emission

**What it is.** The code generator, alongside emitting F# actor code, emits an SMT-LIB file per generated unit encoding the unit’s state space and constraints. The emitted artifact is a canonical text form; `Frank.Verification` runs Z3 against it as a separate pass.

**What it enables.** A durable, inspectable, version-controllable verification artifact — the generated SMT-LIB is readable and diffable independently of the F# code. Reviewers and external auditors can run Z3 against it without running the Frank toolchain.

**Why deferred from v1.** Emission is non-trivial: the encoding has to be stable under generator refactors, the artifact has to stay readable, and the inline v1 Z3 pass already covers source-level verification. Artifact emission is the prerequisite for Q.2 and Q.3, but not for any v1-scope property.

### Q.2. Z3 against emission — closed-form generated-code conformance

**What it is.** With Q.1 in hand, `Frank.Verification` proves generated code equivalent to the Li-et-al. projection of the source protocol by reducing both to SMT-LIB and asserting equivalence. This is the formal closure of the gap Appendix P addresses empirically.

**What it enables.** A complete chain of trust from source protocol to generated implementation: source-level Z3 proves properties of the spec; Z3-against-emission proves the generated code implements the spec. No runtime sampling.

**Why deferred from v1.** Expensive to build (depends on Q.1), narrow incremental value given conformance testing (Appendix P) already covers the pragmatic case, and not on the critical path to a running Frank protocol. The property definitions in Appendix D row 1–6 and row 8–10 are written so Z3-against-emission can pick them up when Q.1 and Q.2 land.

### Q.3. Deferred verification properties

The following properties (Appendix D) are defined in the algebra but are not checked in v1:

- **Row 7 — Liveness / progress.** Every branch reachable; no role waits forever under fair scheduling. Requires a model-checking backend (CTL/LTL); Z3 alone does not settle it. Target tool is a dedicated model checker (e.g., NuSMV, TLA+) invoked from `Frank.Verification` as a separate pass.
- **Row 9 — Resource bounds.** Total resource consumption along any path stays under budget. Requires linear-arithmetic reasoning over annotated resource costs (cf. Nomos). Target tool is Z3 with arithmetic theories, invoked against a resource-annotated projection of the protocol.
- **Timed-automata properties.** Deadlines, timeouts, and rate limits expressed against a timed-automata layer atop the session-type core. Requires a timed-automata verifier (UPPAAL or equivalent). Out of v1 algebra scope but not precluded.

None of these is ruled out by the v1 algebra; each requires a distinct verifier integration that does not fit in the v1 inline Z3 pass.

### Q.4. Projections beyond MEL and OpenTelemetry

V1 ships two journal projections: Microsoft.Extensions.Logging (MEL) and OpenTelemetry (OTel). The journal’s shape (§9) supports other projections without changing the journal itself; v1 does not ship them:

- **PROV-O / JSON-LD provenance projection.** Every journal entry projected as a PROV-O `Activity` linked to a `Plan` (the protocol) and `Entity` values (payloads and scope identifiers). JSON-LD serialization. Downstream consumers: audit graphs, W3C-standard provenance queries. The v1 algebra annotates journal entries with the fields this projection needs (Appendix D row 10); activating PROV-O is a matter of writing the projection code and the vocabulary mapping.
- **Event-store export.** Projection into an external event store (EventStoreDB, Kafka, Kinesis) for systems that want a durable shared log alongside the per-actor journal. This is complementary to, not a replacement for, the colocated-per-actor model in §9.
- **Time-travel debugger.** Interpreter over the journal that replays a protocol instance step-by-step in a developer UI. Nontrivial UI work; v1 ships the journal query surface the debugger would sit on, not the UI.
- **Prometheus metrics projection.** State transition counts, effect invocation histograms, and mailbox depths exposed as Prometheus metrics. Straightforward once the projection framework is in place.

Each of these is “projection code plus a schema mapping”; none requires algebra changes.

-----

## Appendix R — Intake formats beyond the v1 CE

V1 has a single authored intake pathway: the F# computation expression (§11). The algebra was designed to accept protocols from other intake formats, and the internal representation (`Frank.Protocol.Analysis`) is format-agnostic. This appendix documents the deferred formats and the shape of the future CLI that would accept them.

The deferred intake pathway is a **staged pipeline**, not a flat set of format adapters. Authors working in sketch formats at the whiteboard layer are promoted, with LLM assistance and human review, to rigorous formats that the generator consumes. The CE is one rigorous-format option; it is no longer privileged once the pipeline lands.

### R.1. Why deferred

The CE is Frank’s opinionated authoring surface for v1. Shipping multiple intake pathways before v1 would (a) fragment the documentation and tooling story before the CE is validated, (b) require a separate CLI surface and its command set to be designed alongside the core library, and (c) introduce import fidelity risks (lossy subsets) that would need to be documented and maintained per format. None of these is blocked by the v1 algebra; all are deferred until the CE pathway is shipping and at least one external-format use case is concrete.

### R.2. Tiered intake

Intake formats are not all the same kind of artifact. Sketch formats are where humans think; rigorous formats are where the algebra lives. Separating them makes the lift stage a first-class concern rather than an after-thought.

#### R.2.1. Sketch formats

Fast, whiteboard-shaped, legible without a manual. Structurally lossy relative to the algebra — they omit constructs the generator needs. Authors should feel no obligation to stay inside a sketch format; it is the starting point, not the commit point.

- **Mermaid state diagrams.** Hierarchy and transitions; no role semantics, no effect annotations, no scoped channels, no communication semantics. History states are expressible by convention but not formally distinguished from ordinary states.
- **smcat.** Similar expressive range to mermaid for statecharts, with slightly better support for compound and parallel states. Same gaps around roles, effects, and channels.
- **UML state machine diagrams (constrained subset).** Where tooling produces UML XMI natively. Usable as a sketch input; the structurally-rich subset of UML (e.g., submachine states, deferred event triggers) is not fully honored and the import fails closed on constructs that do not map.

Sketch formats feed the **lift** stage (R.3). They do not feed the generator directly.

#### R.2.2. Rigorous formats

Algebra-faithful. Every algebra construct has a representation in the rigorous format, with documented exceptions where the format is missing the construct (in which case the import fails closed rather than papering over).

- **Scribble.** MPST’s reference authoring format. Scribble global and local types map directly onto Frank’s algebra. Most constructs are lossless; extensions specific to Scribble’s runtime (e.g., Monitor integration) are not imported. Scribble is the rigorous format for protocol-heavy, multi-party intake.
- **SCXML.** W3C state-chart XML. Maps onto Frank’s statechart constructs (hierarchy, history, parallel). Transition conditions map to `Guard` values; actions map to effects subject to the enclosing scope’s `permittedEffects`. SCXML is the rigorous format for statechart-heavy, single-role or role-light intake.
- **F# CE.** The v1 authoring surface. Remains a first-class rigorous format, used by authors who prefer to stay in code.
- **Custom DSLs.** Third parties can implement an `IProtocolFrontend` that parses their DSL into `Frank.Protocol.Analysis` values. The interface is narrow: parse into the algebra, return diagnostics, done. A DSL implemented against this interface is a rigorous format by construction.

Rigorous formats feed the generator directly. The generator is agnostic to which rigorous format it came from.

### R.3. The lift stage

Promoting a sketch to a rigorous format is a distinct pipeline stage. It is not parsing; it is structured translation where the target is underspecified in the source and has to be filled in.

The lift is LLM-assisted because the missing constructs (roles, effects, channels, communication semantics, scope identity) are exactly the things a pattern-matching translator can propose and a human can review. Determinism comes from committing the lift output, not from the lift itself: the sketch and the committed rigorous artifact are both in version control, and the lift re-runs when the sketch changes, regenerating the rigorous artifact for re-review rather than silently mutating it.

The lift’s contract:

1. Read the sketch and identify algebra constructs present, constructs inferrable with high confidence, and constructs that must be supplied (roles, effects, channels, scoped communication semantics).
1. Emit a draft rigorous-format artifact with inferrable constructs filled in and supplied-construct slots marked as proposals with provenance (“inferred from state name `AwaitingApproval`: role `Approver`, effect `CaptureApprovalDecision`”).
1. Produce a structured diff summary for human review: what was translated directly, what was inferred, what was proposed, what requires author input.
1. Wait for author acceptance before any generator pass runs against the rigorous artifact.

A lift that silently fills in role assignments and ships them to the generator without review re-creates the v7.3.0 failure mode at a higher layer: the generated system “works” but its semantics do not match the author’s intent. The review gate is non-negotiable.

### R.4. Lossy-subset documentation

Each format — sketch and rigorous — has constructs that do not map onto Frank’s algebra. These are documented per format rather than being silently dropped:

- Sketch formats are structurally lossy relative to the algebra; the lift stage exists precisely to close this gap with LLM assistance and author review.
- Rigorous formats are algebra-faithful by design but still have edge-case losses: Scribble features depending on runtime monitoring that has no Frank equivalent, SCXML features that rely on ECMAScript evaluation rather than typed guards, DSL-specific extensions that authors expect to be preserved but have no algebra home.

The import pipeline fails closed on unsupported constructs at every stage: the author gets a diagnostic naming the construct and the reason it is not imported, rather than a partial import that silently loses behavior. The lift stage’s failure mode is specifically “proposed rigorous-format output contains unresolved slots the author must fill”; the generator is never invoked on a rigorous artifact with unresolved slots.

### R.5. CLI structure

The deferred CLI ships as `Frank.Intake.CLI` (name provisional). Commands separate the sketch-to-rigorous lift from the rigorous-to-algebra parse:

```
# Rigorous-to-algebra (parse; no LLM required)
frank intake <format> <source-path> --out <analysis-path>
frank intake scribble Protocol.scr --out Protocol.analysis.json
frank intake scxml Protocol.scxml --out Protocol.analysis.json

# Sketch-to-rigorous (lift; LLM-assisted with human review)
frank lift <sketch-format> <source-path> --to <rigorous-format> --out <rigorous-path>
frank lift mermaid Design.mmd --to scxml --out Design.scxml
frank lift smcat Design.smcat --to scribble --out Design.scr

# Combined convenience (lift + intake, for scripting after review)
frank build <sketch-path> --out <analysis-path>
```

The `lift` command produces a rigorous-format artifact plus a structured diff summary for review. `intake` is a pure parse that requires no LLM and has deterministic output. The combined `build` form is for scripted pipelines after a review has already accepted the lift output; it does not bypass the review gate, only the interactive step.

The output of `intake` is a serialized `Frank.Protocol.Analysis` value; downstream stages (code generation, verification, conformance testing) run against it identically to the CE pathway. The CE pathway does not need the CLI because the compiler sees the CE value directly.

### R.6. Relationship to the CE

The CE is not retired when external formats land. It remains a rigorous-format option for authors who prefer to stay in code. Authors who prefer to start from a diagram use the sketch-to-rigorous lift; the rigorous format they land on (SCXML, Scribble, or CE) is their choice, not Frank’s. An artifact authored in any rigorous format can be re-emitted as any other rigorous format via the analysis value, for authors who want to switch surfaces mid-project; this is one of the things the analysis-value abstraction exists for.

### R.7. Source-of-truth convention

Once sketch and rigorous formats coexist in a project, the convention of which is authoritative in version control is a real decision (see §13 open question 15). The default this appendix recommends is **sketch as source, rigorous format as derived**:

- The sketch is committed and reviewed; the rigorous format is regenerated from it.
- The lock file (following the v7.3.2 semantic-discovery pattern) records the lift’s mappings from sketch constructs to rigorous-format constructs, making the derivation deterministic and reviewable across runs.
- Drift detection flags when the rigorous format is edited directly without a corresponding sketch change; this is allowed (authors sometimes need to work at the rigorous layer) but surfaces as a diff the sketch must eventually absorb.

Projects where the author is working directly at the rigorous layer — F# CE authors, Scribble authors writing from a formal specification — should invert the convention: rigorous format as source, no sketch or sketch as documentation view. The CLI’s commit discipline supports both, with a per-project default.