# Protocol Types for F#: Unifying Hierarchical Statecharts with Multiparty Session Types

**Date:** 2026-04-21
**Context:** Frank framework architecture exploration
**Status:** Architecture decided. Implementation to begin against the layered build order in §12 after this document is reviewed.

---

## Reading order

The main body (§1–§13) describes the target architecture: what is being built, the decisions already taken, and the layered build order that avoids wiring everything at once. Appendices A–I capture the supporting detail — full algorithms, alternatives considered and rejected, and history. Skip the appendices on first read; return to them when implementing a layer or re-examining a decision.

---

## 1\. Purpose

Build an F# library (`Frank`) that unifies Harel hierarchical statecharts with Honda/Yoshida/Carbone multiparty session types into a single representation, authored as an F# computation expression (CE), from which correct per-role actors are generated at build time.

Specifically:

- **One authoring surface** — an F# CE — capturing both state hierarchy and multiparty choreography.
- **Generated actors** — complete `MailboxProcessor`-based implementations, one per role, produced from a single canonical protocol definition.
- **First-class effect discipline** — the side-effects permitted at each protocol step are part of the type, enforced by the generator, and available for verification.
- **Verification support** — Z3-backed queries for deadlock freedom, payload refinements, and related properties.
- **Journal-primary execution record** — an in-process journal (SQLite-backed by default) is the canonical record of every semantically relevant event. `Microsoft.Extensions.Logging`, OpenTelemetry, and semantic-graph exports (RDF / PROV-O) are downstream projections (§9). Handlers, interpreters, and the supervisor all read the journal; logs and traces are for operators, not for Frank's own tooling.

The goal is correctness-by-construction for the communication and state-machine layer, combined with a narrow, well-typed specialization surface (the effect handler) for business logic.

## 2\. Architectural overview

Protocol definitions are F# computation expressions that evaluate to a `ProtocolType` value. `ProtocolType` is a coalgebraic description of the global protocol — hierarchical, multiparty, recursive, with effect annotations, explicit scope identity, and explicit communication semantics.

A build-time code generator consumes the `ProtocolType`:

1. Runs the Li/Stutz/Wies/Zufferey automata-theoretic projection (Appendix A) to produce per-role state machines and check implementability.
2. Emits, for each role, a `MailboxProcessor`-based actor that dispatches messages, routes to peers, escalates unhandled events to parent scopes, and invokes effects through a witness-object effect handler.
3. Emits the `IEffectHandler<\_,\_>` interface the author implements.
4. Emits a structured log-event schema document describing what the actor will emit at runtime.
5. Emits SMT-LIB queries for Z3 verification, consumed by a separate build step.

The author writes:

- The CE protocol definition (or generates it from a design document — Scribble, SCXML, or another format — via a CLI parser).
- Discriminated unions naming roles, states, messages, and effect operations.
- The effect handler implementation, injecting dependencies (services, loggers).
- Optional interpreters that consume the journal.

The author does **not** write actor code, message-routing code, scope-escape logic, or effect-dispatch glue. Those are always generated.

## 3\. The protocol algebra

The `ProtocolType` value type is the canonical form. A CE builder provides the authoring surface.

> \*\*On placeholders.\*\* The algebra is generic over the types the author supplies. In the sketches below, `'Role`, `'Scope`, `'Message`, and `'Effect` are type parameters, not concrete types. In a real protocol the author defines these as domain DUs — e.g., `type Role = Customer | Merchant | Warehouse`, `type Scope = OrderPhase | InventoryPhase | ShippingPhase`, `type Message = PlaceOrder of OrderData | CheckInventory of ItemId | ...`, `type Effect = ReadOrders | WriteOrders | EmitOrderPlaced | ...`. Any string appearing in code samples as a message name, scope tag, or resource key is illustrative only — read it as standing in for an author DU case. `SupervisionPolicy` and `CommSemantics` are \*not\* parameterized; they are fixed vocabulary Frank itself defines.

```fsharp
type Channel<'Role, 'Scope> = Channel of from: 'Role \* to\_: 'Role \* within: 'Scope

type SupervisionPolicy = Restart | RestartAll | Escalate | Stop

type CommSemantics =
    | Sync
    | AsyncFIFO of bound: int
    | AsyncCausal

type ProtocolType<'Role, 'Scope, 'Message, 'Effect> =
    | Send     of channel: Channel<'Role, 'Scope>
                \* message: 'Message
                \* effects: 'Effect list
                \* continuation: ProtocolType<'Role, 'Scope, 'Message, 'Effect>
    | Recv     of channel: Channel<'Role, 'Scope>
                \* message: 'Message
                \* effects: 'Effect list
                \* continuation: ProtocolType<'Role, 'Scope, 'Message, 'Effect>
    | Choice   of chooser: 'Role
                \* branches: ('Message \* ProtocolType<'Role, 'Scope, 'Message, 'Effect>) list
    | Parallel of ProtocolType<'Role, 'Scope, 'Message, 'Effect> list
    | Sequence of ProtocolType<'Role, 'Scope, 'Message, 'Effect>
                \* ProtocolType<'Role, 'Scope, 'Message, 'Effect>
    | Nested   of scope: 'Scope
                \* comm: CommSemantics
                \* supervision: SupervisionPolicy
                \* permittedEffects: 'Effect list
                \* body: ProtocolType<'Role, 'Scope, 'Message, 'Effect>
                \* continuation: ProtocolType<'Role, 'Scope, 'Message, 'Effect>
    | History     of scope: 'Scope   // shallow history: resume at last substate of this scope
    | DeepHistory of scope: 'Scope   // deep history: resume at last substate at every level below
    | Rec      of label: RecLabel
                \* body: ProtocolType<'Role, 'Scope, 'Message, 'Effect>
    | Var      of label: RecLabel
    | End

and RecLabel = RecLabel of string
```

`RecLabel` is internal bookkeeping for binders in recursive protocols — the author doesn't typically name these; the CE builder generates them or they come from source-level markers. It is _not_ an author DU.

`History` and `DeepHistory` are Harel pseudo-states. They have meaning only as transition targets inside a `Nested` scope: entering a `History(scope)` means "resume at the last substate this scope was in when it was last exited"; entering a `DeepHistory(scope)` applies that rule recursively at every level below. The journal (§9.1) is the mechanism that makes these resolvable at runtime — without it, history states have no semantics. Not every Harel feature is in the algebra — entry/exit actions, internal vs. external transitions, and explicit final pseudo-states for compound states are documented gaps; see Appendix F.

The CE builder is a thin wrapper that lets the above be written in idiomatic F# style:

```fsharp
type ProtocolBuilder<'Role, 'Scope, 'Message, 'Effect>() =
    member \_.Yield(x: ProtocolType<'Role, 'Scope, 'Message, 'Effect>) = x
    member \_.Bind(m, f) = Sequence(m, f ())
    member \_.Combine(m1, m2) = Sequence(m1, m2)
    member \_.Zero() = End
    member \_.Delay(f) = f ()

let protocol<'Role, 'Scope, 'Message, 'Effect> =
    ProtocolBuilder<'Role, 'Scope, 'Message, 'Effect>()
```

In practice an F# implementer will inject concrete DUs once and alias the instantiated type (`type MyProto = ProtocolType<Role, Scope, Message, Effect>`) to keep downstream signatures readable. That's an implementation concern; the algebra itself stays generic.

Example — a three-role order fulfillment with two levels of hierarchy (Customer, Merchant, Warehouse; OrderPhase containing InventoryPhase and ShippingPhase) is written as a single CE that evaluates to a `ProtocolType<Role, Scope, Message, Effect>`. The full example is in Appendix J.

## 4\. Role projection

Projection is the operation that converts a global `ProtocolType` into a per-role state machine. Frank uses the Li/Stutz/Wies/Zufferey algorithm (CAV 2023):

1. Build a global automaton from the `ProtocolType`.
2. For each role, project by erasure — relabel transitions irrelevant to that role as ε.
3. Determinize via subset construction to produce a candidate local implementation.
4. Check implementability with the paper's PSPACE decision procedure; fail the build with a witness if the protocol is unimplementable.

Full algorithm, F# pipeline sketch, and practical notes are in **Appendix A**.

Frank's generator consumes both outputs: the per-role state machines drive actor generation, and the implementability check is a build-time gate. The reference implementation is the artifact at [zenodo.org/records/8161741](https://zenodo.org/records/8161741); Frank should port or wrap it rather than re-derive.

## 5\. Effects

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

## 6\. Communication and supervision

The algebra makes four runtime shape concerns first-class so that projection, actor generation, and verification all have concrete semantics to work with:

- **Scope** — identifies `Nested` regions. Author-supplied DU (e.g., `type Scope = OrderPhase | InventoryPhase | ...`). Bubble-up of unhandled events walks the parent chain in the projected statechart.
- **Channel** — distinguishes channels scoped to different regions. A channel is structurally `from: 'Role \* to\_: 'Role \* within: 'Scope`. A role participating in an outer protocol and an inner `Nested` has distinct channels for each scope; conflating them would create phantom races.
- **`CommSemantics`** — `Sync` (rendezvous), `AsyncFIFO` (bounded per-pair FIFO), or `AsyncCausal`. Fixed vocabulary. The choice changes which deadlock conditions are reachable.
- **`SupervisionPolicy`** — `Restart`, `RestartAll`, `Escalate`, or `Stop`, borrowed from Erlang/OTP vocabulary. Fixed vocabulary.

Details and tradeoffs per variant are in **Appendix E**.

**Supervisor — the initial floor.** The generator emits one `ProtocolSupervisor` per protocol instance, running in the same OS process as its actors. Its behaviors:

- **Crash detection.** Subscribe to each actor's error event.
- **Journal access.** On actor crash, read the actor's journal (§9.1) to recover last-committed state. Because the supervisor is same-process, journal access is a direct SQLite/LMDB read — no IPC.
- **Single resumption attempt.** If the journal is readable and non-empty, spawn a replacement actor and hand it the journal for replay from the last committed state. The replacement resumes with statechart history restored and the projected protocol state intact.
- **Fallback to unwind.** If the journal is unreadable, missing, corrupt, or if the replacement actor also crashes, the supervisor marks the role dead in the shared peer map, emits a supervision event to the journal (for downstream projection to logs/traces), and lets the protocol unwind. Surviving actors see the dead peer on their next send attempt and terminate in turn.

_In-flight messages at the moment of crash are lost._ The journal records committed state, not pending I/O. This is the honest limitation of a good-faith minimal resumption — cross-actor consistency during partial failure is genuinely hard and belongs with the alternative-backend upgrade path (Proto.Actor, Orleans, event-sourced frameworks with at-least-once guarantees), not the MailboxProcessor floor.

_Explicitly out of scope for this floor:_

- Restart strategies (`one-for-one`, `all-for-one`, `rest-for-one`, escalation up a tree).
- Backoff policies, restart limits, retry budgets, circuit breakers.
- Supervision trees; hierarchical supervision with escalation.
- Idempotency and at-least-once delivery guarantees during resumption.
- Cross-process or distributed supervision.

The `SupervisionPolicy` annotations carried in the CE are parsed and stored but not yet acted on; the generator emits a build-time warning when it sees a non-default policy. Richer supervision — strategies, backoff, trees, at-least-once — is not more work for Frank's own runtime; it is work that existing .NET actor frameworks have already done. The intended path for richer semantics is to target one of those frameworks as an alternative generator backend rather than reimplement their machinery inside Frank. Candidates and the mapping discipline, including how the journal interface maps onto each framework's native persistence, are in **Appendix K**.

This floor is deliberately minimal but is a **good-faith resumption implementation**, not a stub — enough to demonstrate that Frank's architecture supports the capability and that the journal is doing real work, without pretending to production-grade supervision.

## 7\. The generated actor

Frank's generator selects one of four actor-shape templates based on the protocol's structure:

| Shape                         | Roles? | Hierarchy? | Applies when                                     |
| ----------------------------- | ------ | ---------- | ------------------------------------------------ |
| `FlatStatechartActor`         | no     | no         | single-participant state machine                 |
| `HierarchicalStatechartActor` | no     | yes        | single-participant with nested substates         |
| `FlatMPSTActor`               | yes    | no         | multiparty, no nested scopes                     |
| `HierarchicalMPSTActor`       | yes    | yes        | the full case (the §3 order-fulfillment example) |

Each template is a fixed `MailboxProcessor` skeleton parameterized by the effect handler, the peer actor registry (for shapes with roles), and the parent scope (for shapes with hierarchy). Author-supplied specialization lives exclusively in the effect handler. The actor body — state dispatch, message routing, scope escape, effect invocation — is sealed and generated.

```fsharp
// Representative signature for the full case.
// The body is generated; the author never edits it.
type HierarchicalMPSTActor<'Role, 'Scope, 'State, 'Message, 'Effect, 'Result>(
    myRole: 'Role,
    effectHandler: IEffectHandler<'Effect, 'Result>,
    peers: Map<'Role, MailboxProcessor<'Message>>,
    parentScope: MailboxProcessor<'Message> option
) =
    member \_.Start () = MailboxProcessor.Start (fun inbox -> (\* generated body \*))
```

## 8\. Verification

Verification is a list of distinct properties, each with its own query shape, organized into three tiers:

- **Tier 1 — structural, always on.** Implementability, linear channel use, effect discipline, supervision soundness, provenance completeness. No SMT required; the generator walks the tree.
- **Tier 2 — Z3-backed, project-configurable.** Deadlock freedom, race freedom in `Parallel`, payload refinement.
- **Tier 3 — opt-in for high-assurance.** Liveness/progress, resource bounds. May require model checking or linear arithmetic beyond what Z3 alone gives you.

The full 10-item property table, including the SMT-LIB query shape for each, is in **Appendix D**.

Z3 integration runs **inside `Frank.CodeGen`**, verifying both the source protocol and the generated internals as part of the pipeline. A build that produces generated `.fs` files without Z3 approval is not a valid build. Details — including artifact emission, caching, and a second path for static-analysis interpreters — are in §10.9.

## 9\. Journal and projections

Frank's runtime follows a **single-producer, multi-consumer** architecture for execution records. One canonical in-process journal is written to on every semantically relevant event; downstream _projections_ read the journal and emit into specific sinks (logs, traces, semantic-graph formats, interpreter inputs). Nothing in Frank's runtime writes to two places in parallel.

### 9.1. The journal

The journal is the **canonical record** of a protocol instance's execution. Everything that reasons about what the protocol has done reads from it.

Consumers of the journal:

- **Harel history resolution** — shallow (`H`) and deep (`H\*`) history states read the journal on re-entry to restore the last substate.
- **Crash resumption** — the supervisor (§6) reads a crashed actor's journal to restart execution at the last committed position.
- **Effect handlers** — handlers can read journal history in-process to make decisions, rather than falling back to external log lookups.
- **Runtime interpreters** — trace reconstruction, reachability, dry-run, replay, debug views (§10.9.3) operate over the journal.
- **Projections** — MEL log records, OpenTelemetry spans, PROV-O / RDF semantic graph data are all derived from journal entries.

_Storage._ **SQLite is the default backend.** LMDB is a performance escape hatch if profiling shows SQLite is not keeping up under a particular workload. Tradeoffs, swap criteria, and the shared interface that makes the swap a configuration change are in **Appendix M**.

_Scope._ One journal per stateful actor, in-process. The supervisor (same process, §6) has read access to all its actors' journals for consolidation and resumption. Cross-process or cross-machine journal access is explicitly out of scope for the initial implementation; those use cases go through alternative backends (Appendix K) whose own persistence stories subsume the journal role.

_Minimal core schema._ Frank's own consumers need the following per entry:

- Event type (state entry, state exit, message sent, message received, effect invoked, scope entry, scope exit).
- Timestamp.
- Actor role (`'Role`).
- Scope path (list of `'Scope` values from outermost to innermost active scope).
- State identifier (projection-specific).
- Message reference if applicable (`'Message` tag and payload, or a payload reference).
- Effect reference if applicable (`'Effect` case and any result).
- Opaque **extension field** for semantic enrichment — a polymorphic bag (JSON column in SQLite) that consumers ignore if they don't understand it.

The extension field is the **intersection point** with the parallel semantic-model work (RDF, OWL, SHACL, PROV-O). That work defines the common core vocabulary and extension mechanism for domain-specific enrichment; Frank's runtime ships with the field as a pass-through placeholder and commits to not corrupting or discarding its contents. Projections that understand specific extension shapes (a PROV-O projection once the vocabulary is defined) read those shapes; projections that don't, pass them through or ignore them.

### 9.2. Projections

Projections are stateless transformations over the journal. Each projection has a single responsibility and a single downstream sink.

**`Microsoft.Extensions.Logging` projection** — the canonical observability projection, shipped by default. Emits structured `ILogger<\_>` records for every journal entry. Works with every `Microsoft.Extensions.Logging`-compatible sink — Serilog, NLog, console, Seq, Elasticsearch, Grafana Loki, application Insights, whatever the host configures. `Microsoft.Extensions.Logging` is specifically the right layer because it is the .NET idiomatic logging interface and decouples Frank from any particular sink.

**OpenTelemetry projection** — the canonical distributed-tracing projection, shipped alongside MEL. Emits spans around message send/receive, effect invocation, and state transitions. Trace context propagates via message metadata so a multi-role protocol shows up as one connected trace across actors. Recommended when protocols span process boundaries or when end-to-end tracing matters; optional otherwise.

**PROV-O / semantic-graph projection** — the planned projection for the semantic-model parallel track. Reads journal extension fields populated with provenance vocabulary and emits RDF or JSON-LD. Not shipped in the initial Frank release; specified by the semantic-model work. Frank's contract is that the journal's extension field will carry whatever payload the semantic work defines, and projections can be added later without changes to the actor runtime.

**Custom projections** — author code that consumes the journal for domain-specific exports. Frank exposes a read interface over the journal (query, subscribe, time-range filter, replay) that custom projections use. Interpreters (§10.9.3) are a specialization of this category.

### 9.3. What this replaces

Previous drafts of this work framed observability as Serilog-first with no internal journal. That framing was wrong: statechart history and crash resumption need an in-process record that external logging sinks cannot provide (completeness, ordering, and read-latency guarantees), and projecting observability from a journal is cleaner than having the runtime emit to observability sinks directly. The new framing is strictly more capable — everything the old framing did is now a projection — and the single-producer property eliminates a class of drift bugs between parallel emission pathways.

## 10\. Code-generation pipeline

Three libraries, one build-time task:

- **`Frank.Protocol.Analysis`** — pure F#, no I/O. Takes `ProtocolType`; produces projected state machines, implementability results, and structured descriptions of what generated actors should contain. Fully unit-testable. No dependency on FCS, MSBuild, or any generator framework. This is where the hard logic lives.
- **`Frank.CodeGen`** — takes the structured descriptions from `Frank.Protocol.Analysis` and emits F# source text for actors, handler interfaces, log schemas, and SMT-LIB queries. Uses **Fabulous.AST** to construct F# syntax trees; Fantomas formats the output. Invokes Z3 on the emitted queries before returning success, so a non-verifying protocol fails the build at generation time. Details on Z3 integration and artifact emission are in §10.9.
- **`Frank.Build.MSBuildTask`** — thin MSBuild task. Finds CE input files, invokes `Frank.CodeGen`, writes outputs to the build's intermediate directory, adds them to the compile list. Extends the MSBuild integration already proven in adjacent work.

The generator does **not** use Myriad. The rationale — including a full enumeration of the specific ways Myriad's design center conflicts with Frank's needs — is in **Appendix C**.

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

## 10.9. Z3 verification: artifacts and static-analysis interpreters

### 10.9.1. Where Z3 runs

Z3 verification is part of `Frank.CodeGen`, not a separate downstream library. The pipeline per protocol:

1. Read the CE; evaluate to a `ProtocolType` value.
2. Run `Frank.Protocol.Analysis` — projection, implementability.
3. Emit actors, handler interfaces, and log schemas via Fabulous.AST.
4. Emit SMT-LIB queries over both the source protocol and the generated internals.
5. Invoke Z3 on the emitted queries; fail the build with a pointer to the offending construct if any property returns `sat` on the negation.

Z3 does two jobs simultaneously:

- **Protocol-design verification.** Properties of the `ProtocolType` value itself — deadlock freedom, race freedom in parallel regions, payload refinement. Independent of codegen output.
- **Generated-internals verification.** Properties about what `Frank.CodeGen` actually emitted — that the generated actor's state dispatch preserves the projection, that handler-invocation points match the declared effect set, that scope-escape logic matches the hierarchy.

The second category is where Z3 earns real keep. Codegen bugs are exactly the kind of thing manual static analysis misses — a particular hierarchical-plus-parallel combination drops a transition, a scope-escape path has a subtly wrong predicate. Queries that formalize "the generated code's reachable state graph equals the projection's" catch that class of bug automatically.

A build that produces generated `.fs` files without Z3 approval is not a valid build. Verification is not optional polish; it is part of the definition of codegen success.

### 10.9.2. SMT-LIB artifact emission

The SMT-LIB queries emit as files to the build output directory (`obj/frank/verification/`) alongside a manifest (`verification-manifest.json`) recording, per query: the property it verifies, the Z3 version that ran it, the result (pass / fail / timeout), and how long it took.

The files are generated artifacts, not source. They go in `obj/`, not in the repo. CI archives them per build for audit and regression purposes.

The reasons for emitting rather than running inline and discarding:

- **Reproducibility.** Z3's heuristics change across versions; a query that returns `unsat` today may time out on a future version or the reverse. Preserved queries can be re-run against any version to bisect.
- **Debuggability.** When a property fails, the author loads the SMT-LIB in Z3's interactive shell, runs `(get-model)`, and inspects the counterexample. Inline-and-discard loses this.
- **Independent verification.** The same queries can be cross-checked against CVC5, Yices, or Bitwuzla. Only possible if queries exist as portable SMT-LIB.
- **Build caching.** If the CE is unchanged, the queries are unchanged, and Z3's result can be cached — but only with a stable artifact to hash against.
- **Byte-stability.** With deterministic variable ordering and property naming, unchanged protocols produce byte-identical SMT-LIB across generator versions. That makes cache hits real and diffs meaningful.

Optional future extension: a `--keep-queries <dir>` flag for pinning a specific query as a regression test for a historical bug. Not day one.

### 10.9.3. Z3 as a second path for some planned interpreters

Frank's interpreters fall into two categories based on their input source, not on their output.

**Runtime interpreters consume the journal (§9).** Trace reconstruction, debug views, audit reports, reachability replay, dry-run simulation — all read the in-process journal directly, using the same query interface projections use. This is the correct input source: the journal is Frank's single producer of execution records, so interpreters reading from it see the same events any other consumer sees, in canonical form. They do not read log streams (logs are a projection, not a source of truth) and they do not need their own event schema.

**Static-analysis interpreters consume SMT solvers, typically Z3.** They operate on the _protocol description itself_ rather than on runtime records. The archetypal example is a **deadlock / reachability interpreter**: encode the protocol's reachability structure as an SMT formula and ask Z3 whether any deadlock state is reachable. If it is, Z3 produces a counterexample trace — a concrete sequence of role interactions leading to the deadlock — without the protocol ever needing to run. The same pattern applies to liveness / progress analysis (is every branch reachable under fair scheduling?), resource-bound analysis (does total consumption along any path exceed a budget?), and message-race analysis (can two parallel branches write the same resource key?).

These overlap with the Tier 2 and Tier 3 properties from §8, but the _interface_ differs: codegen-time verification fails the build on unsat; a static-analysis interpreter is invoked by the author during protocol design, answers questions, and reports without necessarily failing anything. Same Z3 backend, different consumer.

The SMT-LIB emitted by `Frank.CodeGen` for codegen-time verification and the SMT-LIB emitted for static-analysis interpreters share most of their structure. The `ProtocolType`-to-SMT-LIB encoding factors out into a helper library that both consume; the difference is mostly in the query — "prove no deadlock" versus "find a deadlock if one exists," which is usually the negation.

Two categories of interpreter in Frank, then:

- **Journal-consuming runtime interpreters (§9).** Traces, debug views, audit, replay, dry-run. Read the in-process journal via the read interface §9.1 exposes.
- **Z3-consuming static-analysis interpreters (§10.9.3).** Deadlock detection, liveness, resource bounds. Read the protocol description; produce SMT-LIB queries; invoke Z3.

Both are useful; both are worth building; they are distinct in implementation. Neither reads the log stream — logs are a downstream projection of the journal, intended for operators, not for Frank's own tooling.

## 11\. Two intake pathways

The generator accepts two input sources. Both produce identical inputs to `Frank.Protocol.Analysis`.

**Pathway 1 — CE-forward (hand-authored).** The author writes the F# CE directly, together with the DUs for roles, states, messages, and effects. Frank reads the CE and generates.

**Pathway 2 — design-doc-forward (CLI intake).** A separate CLI tool (extending existing format parsers in the adjacent tooling) reads a Scribble protocol, an SCXML document, or another supported format. It emits F# DU files plus a CE file. These generated files are checked in and **maintained by the author going forward** — either by hand editing or by re-running the CLI and accepting the diff. Frank's generator then runs over them exactly as in Pathway 1.

The runtime artifacts Frank emits are identical across the two pathways; only the origin of the CE + DUs differs.

## 12\. Build order — layered, independently testable

**This section matters as much as the architecture above.** The prior attempt at this work (Appendix G) failed in part because integration was claimed before it was verified. The layered build order below is designed to make each layer testable in isolation and to make integration a discrete, verifiable step rather than a diffuse assumption.

Build in this order. Do not move to the next layer until the previous is unit-tested.

1. **`ProtocolType` value type + hand-written smoke instances.** Verify the algebra represents the protocols you want to express. No CE yet. No generator yet.
2. **CE builder.** Verify by hand-comparing CE output to the smoke instances from step 1.
3. **`Frank.Protocol.Analysis` — projection only.** Unit test against known global types.
4. **`Frank.Protocol.Analysis` — implementability check.** Port the Li-et-al. PSPACE decision procedure. Unit test against Example 2.2 from the paper (positive and negative cases).
5. **`Frank.Protocol.Analysis` — structured actor descriptions.** Produce the data that `Frank.CodeGen` will consume. This is _not_ generated F# source; it is a data description. Unit test.
6. **`Frank.CodeGen` — actor emission for `FlatStatechartActor`.** Use Fabulous.AST. Produce F# source text. Compile the output separately to verify it parses and typechecks. Run the compiled actor against a hand-written effect handler and verify behavior end-to-end.
7. **Repeat step 6** for `HierarchicalStatechartActor`, `FlatMPSTActor`, and `HierarchicalMPSTActor`.
8. **`Frank.CodeGen` — handler interface emission.**
9. **`Frank.CodeGen` — log-schema emission.**
10. **`Frank.Build.MSBuildTask`.** Integrate with the existing MSBuild machinery. End-to-end test: author writes a CE, build produces compiled actors, actor runs.
11. **(Deferred) `Frank.CodeGen` — SMT-LIB query emission and Z3 execution.** Start with Tier 1 properties, then Tier 2. Queries emit to `obj/frank/verification/` alongside a manifest (§10.9.2). Z3 invocation via `Microsoft.Z3`; a failed property fails the build.
12. **(Deferred) Pathway 2.** CLI intake, design-doc parsers. Build on top of existing format-parser work.
13. **(Deferred) Z3-backed static-analysis interpreters.** Deadlock / reachability, liveness, resource bounds (§10.9.3). Shares the SMT-LIB encoder with step 11.

**Discipline checks, non-negotiable:**

- Every layer has unit tests that pass without the layer above it present.
- Integration tests are _additional_, never substitutes for unit tests.
- Do not claim LLM-generated code is integrated until a test that exercises the integration passes. Write the test first.
- Do not move on to step N+1 if step N has any unexplained behavior.
- Commit after every working step. Branch before major integration work.
- When an LLM says "it's wired up," assume it is not, and verify.

Appendix G explains where these rules come from.

## 13\. Open questions

Decisions remaining:

1. **Implementability porting strategy.** Port the Li-et-al. decision procedure into F# directly, or shell out to a packaged version of the artifact at [zenodo.org/records/8161741](https://zenodo.org/records/8161741)? Port probably; prototype may shell out.
2. **Nested-state event semantics — precise rule.** With an explicit `'Scope` parameter, bubble-up is a parent-chain walk over scope values. The modal-refinement rule that says _what_ a role's local type inherits from its containing `Nested` needs formalization. Larsen-style modal transition systems are the likely foundation.
3. **Role refinement operator.** A child `Nested` narrows both the valid message set and the permitted effect set for a role inside it. Formalize as modal refinement rather than ad-hoc projection logic.
4. **Default communication semantics.** `Sync` vs `AsyncFIFO` vs `AsyncCausal` per `Nested`. Pick a default; make it overridable.
5. **Verification tier configuration.** Attribute or project-level setting that selects the active tier per generated unit. Tier 1 default everywhere; Tier 3 opt-in only.
6. **PROV-O vocabulary mapping.** The `Emit` effect and property #10 give the hook. Define the exact PROV-O mapping and how it populates the journal's extension field (§9.1), which a PROV-O projection will then consume. Coordinate with the parallel semantic-model work.
7. **Scribble and SCXML round-trip semantics.** Scribble has no effects or supervision; round-tripping loses the §6 extensions. Document the lossy subset explicitly.
8. **Timed-automata layer.** Real systems care about deadlines; the `Timer` effect is a placeholder. Needs a separate sketch.
9. **Journal event schema — the stable core.** The minimal schema §9.1 sketches needs to be pinned down: exact event-type enumeration, serialization format for payload references, extension-field encoding convention. Coordinate with the parallel semantic-model work that defines the extension vocabulary.
10. **Clef runtime target.** Once .NET is validated, verify portability by targeting Clef. Natural cross-compilation test.

Decisions recorded (covered in the main body; full rationale in appendices):

- Coalgebraic session types as the canonical internal representation (§2; Appendix B).
- F# CE as the authoring surface (§3).
- Algebra generic over author-supplied `'Role`, `'Scope`, `'Message`, `'Effect` DUs; `SupervisionPolicy`, `CommSemantics`, `RecLabel` fixed internal vocabulary (§3).
- Witness-object HKT pattern on `Task`/`Async` as the effect encoding (§5; Appendix B).
- Four actor-shape templates, fully generated, sealed to the author (§7).
- **Journal-primary architecture.** Single in-process journal per actor is the canonical execution record; `Microsoft.Extensions.Logging`, OpenTelemetry, PROV-O, and interpreters are all downstream projections. No dual-producer emission paths. (§9)
- **Journal storage backend.** SQLite is the default; LMDB is a performance escape hatch if profiling demands it. Shared `IProtocolJournal` interface makes the swap a configuration change. (§9; Appendix M)
- **Minimum viable Harel.** Depth, orthogonal regions, shallow and deep history are first-class in the algebra. Entry/exit actions, internal vs. external transitions, and explicit final pseudo-states for compound states are documented gaps. (§3; Appendix F.1)
- Three-library code-gen pipeline on FCS + Fabulous.AST + existing MSBuild integration, rejecting Myriad (§10; Appendix C).
- Z3 verification integrated into `Frank.CodeGen`, not a separate library. Verifies both the source protocol and the generated internals. SMT-LIB artifacts emitted to `obj/` for reproducibility (§8; §10.9).
- **Supervisor floor — observation-plus-resumption.** Same-process supervisor with read access to each actor's journal. On crash: read journal, attempt single resumption from last committed state, fall back to unwind on failure. In-flight messages at the moment of crash are lost. Richer supervision (strategies, backoff, trees, at-least-once) comes from targeting an existing actor framework as an alternative generator backend. (§6; Appendix K)
- Layered, independently testable build order (§12; Appendix G).
- Frank is positioned as a design and engineering contribution, not a research contribution. One potentially novel formal extension — hierarchical statecharts combined with MPST projection — is flagged as contingent on a proper literature survey (Appendix L).

---

# Appendices

---

## Appendix A — The Li-et-al. projection algorithm

The CAV 2023 paper separates **synthesis** (building a candidate per-role state machine) from **implementability checking** (deciding whether the global type admits a faithful distributed implementation). Synthesis is automata-theoretic and always runs. Implementability is a decision procedure the paper shows is in PSPACE, improving the prior EXPSPACE bound.

### A.1. Step 1 — Build the global automaton GAut(G)

Given a global type _G_, construct an NFA whose states are syntactic positions in _G_ and whose transitions are labeled by synchronous send-receive _interactions_ of the form `p→q:m` (role _p_ sends message _m_ to role _q_).

- Choices in _G_ become non-deterministic branching from the choice point.
- Recursion (μt. G) becomes back-edges to the binder state.
- The empty type 0 is the only accepting state.

The alphabet Σ_sync is the set of all `p→q:m` interactions appearing in \_G_. Call this automaton GAut(G) = (Q_G, Σ_sync, δ_G, q₀, F_G).

### A.2. Step 2 — Projection by erasure GAut(G)↓p

For each role _p_, define Σ_p ⊆ Σ_sync as the interactions in which \_p_ participates as either sender or receiver. Construct GAut(G)↓p by relabeling every transition:

- if the interaction is in Σ_p, keep it (or split it into a send `p!q:m` or receive `q?p:m` event for \_p_);
- otherwise replace its label with ε.

Formally: GAut(G)↓p = (Q*G, Σ_p ⊎ {ε}, δ↓, q₀, F_G). The state space and transition graph stay the same; only labels change. This is the paper's **homomorphism automaton** for \_p*: a homomorphic image of GAut(G) under the alphabet projection ⇓Σ_p, where x⇓Σ_p = x if x ∈ Σ_p and ε otherwise.

The result is an NFA with ε-transitions, typically non-deterministic because erased branches collapse together.

### A.3. Step 3 — Subset construction (determinization)

Apply the standard NFA-to-DFA subset construction to GAut(G)↓p. Each state of the resulting DFA corresponds to a _set_ of states from GAut(G); the start state is the ε-closure of q₀; transitions on each symbol a ∈ Σ_p go to the ε-closure of the union of a-successors of every NFA state in the current subset.

Call this DFA C(G,p) — the **candidate local implementation** for role _p_. The paper proves L(C(G,p)) = L(G)⇓Σ_p: the candidate exactly captures the \_p_-relevant trace projections of the global type.

### A.4. Step 4 — Implementability check

The hard question: is the family {C(G,p)}\_{p ∈ P} actually a faithful distributed implementation of _G_? Two failure modes are well-known:

- **Sender ambiguity** — a non-chooser role cannot tell which branch was selected because no later message in its projection distinguishes them.
- **Receiver ambiguity** — symmetrical; the receiver cannot tell which sender's message it is waiting for.

The paper gives succinct conditions characterizing implementability — predicates over GAut(G) checkable in PSPACE. They cover (informally):

- Every branch point in _G_ is _eventually distinguishable_ in every relevant role's local view.
- Asynchronous send/receive ordering is consistent across roles.
- No role gets stuck waiting on a message no path will ever produce.

If the conditions hold, {C(G,p)} is a correct CSM (communicating state machine) implementation of _G_. If not, the paper's witness extraction names the specific subprotocol that breaks implementability.

### A.5. F# sketch — the full pipeline

```fsharp
// === Step 1: Build GAut(G) ===
type Interaction = { Sender: Role; Receiver: Role; Message: string }

type GlobalAut = {
    States: int Set
    Alphabet: Interaction Set
    Delta: Map<int \* Interaction, int Set>   // NFA: maps to a set of states
    Start: int
    Accept: int Set
}

let buildGAut (g: ProtocolType) : GlobalAut = failwith "TODO: tree -> automaton"

// === Step 2: Projection by erasure ===
type LocalEvent =
    | Send of to\_: Role \* msg: string
    | Recv of from\_: Role \* msg: string
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
    let work = System.Collections.Generic.Queue(\[start])
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
    | SenderAmbiguity   of branchPoint: int \* conflictingRoles: Role list
    | ReceiverAmbiguity of waitingState: int \* ambiguousSenders: Role list
    | OrphanMessage     of dangling: Interaction

let checkImplementable (g: GlobalAut) (locals: Map<Role, DFA<\_,\_>>) : ImplementabilityResult =
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

- **Do not roll your own implementability check.** Steps 1–3 are textbook. Step 4 is the paper's contribution; the conditions are subtle. The artifact at [Zenodo 8161741](https://zenodo.org/records/8161741) is the reference implementation. Wrap or port it; do not paraphrase from memory.
- **Synchronous vs asynchronous.** The paper handles asynchronous semantics by _splitting_ each `p→q:m` into a send event followed by a receive event before projection. Implementability conditions differ between the two settings.
- **State explosion.** Subset construction is exponential in the worst case. For typical business-workflow protocols this is fine; the paper's PSPACE result ensures the _check_ itself does not blow up exponentially.
- **Predecessor.** Majumdar, Mukund, Stutz, Zufferey (CONCUR 2021, [LIPIcs.CONCUR.2021.35](https://drops.dagstuhl.de/entities/document/10.4230/LIPIcs.CONCUR.2021.35)) — sound but not complete on the same class of MSTs. CAV 2023 subsumes it.

---

## Appendix B — Effect encodings considered

Four encodings were considered for making effects first-class in the protocol algebra. The decision (§5) is the **witness-object HKT pattern**, without depending on the Higher library, with `Task`/`Async` as the carrier. This appendix explains the alternatives and why they were rejected.

### B.1. Encoding A — Free monad

Each effect constructor takes the operation's parameters plus a continuation `'result -> Eff<'a>` consuming the result. The effect tree is built as data; an interpreter walks the tree.

```fsharp
type Eff<'a> =
    | DbRead  of key: string                    \* cont: (string -> Eff<'a>)
    | DbWrite of key: string \* value: string    \* cont: (unit   -> Eff<'a>)
    | Call    of service: string \* payload: obj \* cont: (obj    -> Eff<'a>)
    | Pure    of 'a

let rec bind (m: Eff<'a>) (f: 'a -> Eff<'b>) : Eff<'b> =
    match m with
    | Pure x           -> f x
    | DbRead(k, c)     -> DbRead(k, fun r -> bind (c r) f)
    | DbWrite(k, v, c) -> DbWrite(k, v, fun r -> bind (c r) f)
    | Call(s, p, c)    -> Call(s, p, fun r -> bind (c r) f)
```

**Benefit:** the tree is reified data — inspectable, optimizable, serializable, replayable.
**Cost:** AST allocation per protocol step, real at scale. See Seemann's [free monad recipe](https://blog.ploeh.dk/2017/08/07/f-free-monad-recipe/) for the F# canonical treatment.

**Rejected because:** the PROV-O provenance story can be satisfied with structured logging from the handler (§9), not by inspecting an effect AST. The allocation cost therefore buys nothing Frank needs.

### B.2. Encoding B — Tagless final (Carette/Kiselyov/Shan)

Programs are polymorphic over an interpreter interface. No AST is built; programs compose as plain function calls and the compiler erases the abstraction.

```fsharp
type IEffectInterpreter<'repr<\_>> =
    abstract DbRead  : key: string                    -> 'repr<string>
    abstract DbWrite : key: string \* value: string    -> 'repr<unit>
    abstract Call    : service: string \* payload: obj -> 'repr<obj>
    abstract Return  : value: 'a                      -> 'repr<'a>
    abstract Bind    : m: 'repr<'a> \* f: ('a -> 'repr<'b>) -> 'repr<'b>
```

**Benefit:** zero AST allocation; strong fit when AST inspection is not needed.
**Cost:** F# has no first-class higher-kinded types. The carrier `'repr<\_>` must be encoded, typically via SRTPs or a brand/witness pattern. This is verbose.

**Not adopted as the library pattern because:** the idiomatic F# ecosystem has no standard HKT encoding. Adopting one means either depending on the [Higher](https://github.com/palladin/Higher) library (unmaintained against recent .NET) or writing ad-hoc SRTP machinery. **However, the witness-object variant of this encoding — scoped to the specific effect signatures Frank needs — is the chosen approach.** Taking the pattern without the library avoids the dependency risk.

_Parallel technique for the encoding lineage._ The witness-object / brand-type approach traces to Yallop \& White, "Lightweight Higher-Kinded Polymorphism" (FLOPS 2014), which the Higher library ports to F#. A separate lineage with the same motivation — encoding higher-kinded generic programming in a language without native HKT — is Lämmel \& Peyton Jones's "Scrap Your Boilerplate with Class" (ICFP 2005), which uses recursive type-class dictionaries to dispatch generic traversals. SYB3 does not port directly to F# (no type classes), but the conceptual parallel is worth noting: both reify a mechanism the base type system cannot express. Yallop-White reifies type-level application (`App<F, A>`); SYB3 reifies open, extensible generic functions via dictionary composition. Frank adopts the Yallop-White shape because it lands natively in F# via interfaces and branded types; SYB3 is listed for reference only.

### B.3. Encoding C — True algebraic effects with handlers

Effects are operations on an abstract effect signature; **handlers** are first-class language constructs that intercept operations and reify continuations. Implemented natively by Koka, Eff, OCaml 5, and Frank-the-language (Lindley/McBride/McLaughlin).

```
// Pseudo-syntax — not F#:
effect DbRead  : string -> string
effect DbWrite : string \* string -> unit

handler inMemoryHandler {
    return x              -> (x, currentState)
    DbRead k       resume -> resume (Map.find k currentState)
    DbWrite (k, v) resume -> withState (Map.add k v currentState) (resume ())
}
```

**Rejected for F# target because:** F# does not natively support effect handlers. Simulating them requires either free-monad encoding (B.1) or delimited-control via a runtime that F# does not ship with.

Keep as the **cross-compilation target** if a Koka, OCaml-5, or Frank-language port is pursued in the future.

### B.4. Delimited continuations — the connection

Algebraic effects (C) and delimited continuations are two sides of the same coin. The "d" in `shift`/`reset` (Danvy \& Filinski, "Representing Control," 1990) stands for _delimited_ — a continuation captured up to a handler boundary rather than up to the end of the program. Koka, Eff, OCaml 5, and Frank implement effect handlers via delimited continuations under the hood.

**.NET has no native `shift`/`reset`.** Hopac implemented delimited control on F# async circa 2015; it is unmaintained and a non-starter for new dependencies. The practical substitute is **structured async scopes** (`async { ... }` or `task { ... }`): lexical nesting stands in for the handler boundary, with single-shot resumption rather than multi-shot. For session-type use, single-shot is sufficient — a handler either resumes the protocol or aborts.

### B.5. Decision — witness-object HKT on `Task`/`Async`

Concrete form: a `IEffectHandler<'eff, 'result>` interface carrying `Map`, `Bind`, `Return`, and `Invoke`, where `'eff` and `'result` are specialized per protocol via SRTPs or direct parameterization. Pattern-matched from Higher; dependency on Higher avoided. The carrier is `Task` (via `task { }`) or `Async`, giving structured concurrency as the implicit delimiter.

The author implements this interface with business logic and injected dependencies (services, loggers). The generated actor invokes effects through the interface without knowing or caring about the runtime carrier.

This is the surface shown in §5.

---

## Appendix C — Code-generation technology considered

The decision (§10) is **FCS + Fabulous.AST + a bespoke MSBuild task**, reusing existing MSBuild integration shape from adjacent work. This appendix covers what was considered and why each alternative was rejected.

### C.1. Terminology — "F# source generators" is not a feature

Before anything else: **F# has no native source-generator facility** analogous to Roslyn Source Generators in C#. The language suggestion at [fslang-suggestions#864](https://github.com/fsharp/fslang-suggestions/issues/864) has been open since April 2020, is estimated at cost "XXXXL," has no implementation work and no milestone. The F# team's stated position is that Type Providers and Myriad cover the use case.

When F# practitioners say "source generator," they mean one of: Myriad, a Roslyn source generator in C# whose output F# consumes via interop, or an ad-hoc MSBuild task using FCS directly. Frank should not plan around native F# source generators arriving.

The real platform pieces available today:

| Tool                              | Role                                                                    | Status                                                                                                  |
| --------------------------------- | ----------------------------------------------------------------------- | ------------------------------------------------------------------------------------------------------- |
| **Myriad**                        | Build-time F# code generation via MSBuild, text templating over FCS AST | [MoiraeSoftware/Myriad](https://github.com/MoiraeSoftware/Myriad) — small team, 0.8.x                   |
| **FCS** (F# Compiler Service)     | The F# compiler as a library — parse, typecheck, inspect, emit          | Tracks compiler; Microsoft-maintained                                                                   |
| **Fantomas.Core**                 | F# source formatter; also usable to emit clean code from a syntax tree  | [fsprojects/fantomas](https://github.com/fsprojects/fantomas), active                                   |
| **Fabulous.AST**                  | CE-based DSL for constructing F# AST on top of Fantomas                 | [edgarfgp/Fabulous.AST](https://github.com/edgarfgp/Fabulous.AST), v1.2 stable, v2.0 in progress        |
| **Type Providers**                | Compile-time type generation from external schemas; a different model   | [SDK](https://github.com/fsprojects/FSharp.TypeProviders.SDK), stable                                   |
| **Roslyn Source Generators (C#)** | Real and first-class — emit C# that F# consumes via interop             | [Microsoft docs](https://learn.microsoft.com/en-us/dotnet/csharp/roslyn-sdk/source-generators-overview) |

### C.2. Myriad — why not

Myriad is a text-templating code generator. Its design center is:

- single-file input, single-file output,
- derive-style transformations (given these DUs, emit lenses/accessors/serializers),
- no cross-file analysis, no external tool invocation, no algorithmic work inside the generator.

Frank's interacting-actor use case exceeds this design center on multiple axes. Items 1, 2, 3, and 5 below are already tripped by Frank's primary use case before a line of generator code is written.

1. **Cross-file inputs.** A protocol CE lives in one file; its DUs for roles, messages, states, and effects typically live in another (or several). The actor must reference all of them coherently. Myriad plugins are invoked per input file; coordinating across files requires either shared state between plugin invocations or convention-based discovery — fragile, outside the framework's happy path.
2. **Multiple coordinated outputs per protocol.** For each protocol Frank needs to emit: the per-role actor(s), the `IEffectHandler<\_,\_>` interface, the log-event schema, SMT-LIB verification queries, and optionally Scribble or SCXML serializations. Myriad's default is one output per input. Producing this set means stacking plugins or building one that emits multi-part output — both push the model.
3. **Algorithmic analysis at generation time.** The Li-et-al. projection (Appendix A) is automaton construction plus a PSPACE implementability check. Myriad is not an analysis framework. Running this inside a plugin means embedding a nontrivial library into the plugin assembly, which stops being templating and starts being a custom code-gen engine wearing Myriad's skin.
4. **External tool invocation during generation.** Z3 for verification queries. Scribble or SCXML parsers for Pathway 2. Fantomas for output formatting. All doable from a plugin, all outside the design center.
5. **Extracting values from computation expressions.** The cleanest way to read a `ProtocolType` value from the author's CE is to _evaluate_ the CE — compile and run the author's file. Myriad reads the syntactic AST, not evaluated values. Either Frank implements partial CE desugaring by hand (fragile, will diverge from the compiler) or requires protocols to be plain values without CE sugar (constrains authoring ergonomics that §3 set up as a goal).
6. **Shared generated helpers.** The `IEffectHandler<'eff, 'result>` witness template should live once in a shared Frank library, not be regenerated per protocol. Keeping that boundary crisp under Myriad adds friction.
7. **Byte-stable output.** Generated files should diff cleanly across runs. Myriad achieves this only if the generator code is disciplined about ordering. Enumerating a `Dictionary` or `Set` without explicit sorting produces non-deterministic output. A footgun, not a framework flaw, but real.

**Honest read:** Myriad is the fastest path to a prototype _if_ the use case fits its design center. Frank's does not. Adopting Myriad means building a custom code-gen engine inside its plugin API; the framework supplies the plugin wiring and not much else. The existing MSBuild integration from adjacent work supplies the same wiring with fewer constraints.

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

The Fantomas project's guidance on code generation is effectively: do not concatenate strings; build ASTs. Fabulous.AST makes that tractable.

### C.4. Roslyn Source Generators, Type Providers — why not

- **Roslyn Source Generators (C#)** work for Frank only via C#-to-F# interop: you would write a C# generator whose output F# consumes. Awkward, indirect, and gives up the F# type system in the part of the pipeline that most wants it.
- **Type Providers** are a different model: compile-time type generation from external schemas. Good for intake (reading Scribble/SCXML into types) but not for emitting arbitrary F# code. Could serve a role in Pathway 2, but not as the primary generator.

### C.5. Existing MSBuild integration from adjacent work

Frank inherits the MSBuild integration shape from the adjacent tooling effort. That integration already handles: finding input files, invoking a generation step, dropping outputs into the build's intermediate directory, adding them to the compile list. Extending it for Frank is plumbing work, not architecture work.

This changes the build-order calculus substantially. The Myriad-vs-FCS framing would have been "start with Myriad for the MSBuild integration Myriad provides; migrate to a bespoke task later when needed." With the MSBuild integration already available, Myriad's last real value-add disappears. FCS + Fabulous.AST becomes the primary recommendation outright.

---

## Appendix D — Verification properties enumerated

"Verify the protocol" is not a single Z3 query. It is a list, each item with its own SMT-LIB shape. `Frank.CodeGen` emits one query per property per generated unit; `Frank.Verification` runs them and fails the build if any returns `sat` on the negation of the property.

| #   | Property                       | Description                                                                                | Tool                                            |
| --- | ------------------------------ | ------------------------------------------------------------------------------------------ | ----------------------------------------------- |
| 1   | **Implementability**           | Global type admits a non-empty projection for every role with no synthesis ambiguity       | Automata construction (Li et al. 2023); no SMT  |
| 2   | **Deadlock freedom**           | No reachable global state where some role is waiting on a message no role can send         | CFSM reachability; SMT for guard satisfiability |
| 3   | **Linear channel use**         | Each `Channel` is used exactly once per protocol path (no double-use, no orphan)           | Structural; type-system-checkable               |
| 4   | **Effect discipline**          | Every `Effect` invoked inside a `Nested` scope appears in that scope's `permittedEffects`  | Pure tree walk; no SMT                          |
| 5   | **Race freedom in `Parallel`** | Effects in concurrent branches commute, or operate on disjoint resource keys               | SMT: assert non-disjoint write sets unreachable |
| 6   | **Payload refinement**         | When message _m_ is sent with payload _p_, predicate `P(p)` holds                          | SMT (the Session\* sweet spot)                  |
| 7   | **Liveness / progress**        | Every branch reachable; no role waits forever under fair scheduling                        | Model checking (CTL/LTL backend)                |
| 8   | **Supervision soundness**      | Failure of any role in a `Nested` scope is handled by some ancestor's supervision policy   | Structural + reachability                       |
| 9   | **Resource bounds**            | Total resource consumption along any path stays under budget                               | SMT with linear arithmetic (cf. Nomos)          |
| 10  | **Provenance completeness**    | Every state transition emits a PROV-O `Activity` record sufficient to reconstruct the path | Structural; checked by the generator            |

**Tier mapping:**

- **Tier 1 (always on):** 1, 3, 4, 8, 10. Cheap, structural, no SMT; catches most real bugs.
- **Tier 2 (configurable):** 2, 5, 6. Z3-backed; pays a build-time cost.
- **Tier 3 (opt-in):** 7, 9. Model checking and resource analysis; for paths where the cost is worth it.

---

## Appendix E — Communication semantics variants

`Send` and `Recv` in the algebra describe _that_ a message moves, not _how_. The runtime shape choices are first-class in the algebra so that projection and verification can reason about them.

### E.1. `CommSemantics`

- **`Sync`** — synchronous rendezvous, CSP-style. Deadlock conditions are tightest; verification is simplest. Appropriate for tightly-coupled in-process actors.
- **`AsyncFIFO of bound: int`** — asynchronous with bounded per-pair FIFO mailboxes. Standard mailbox-actor semantics. Deadlock can occur on mailbox overflow.
- **`AsyncCausal`** — vector-clock causal delivery. Useful when ordering matters across roles but FIFO-per-pair is insufficient.

### E.2. `SupervisionPolicy` (Erlang/OTP vocabulary)

- **`Restart`** — restart the failed child; keep siblings running.
- **`RestartAll`** — restart all children in the region.
- **`Escalate`** — bubble the failure to the parent scope's supervisor.
- **`Stop`** — tear down the region entirely and propagate.

### E.3. `Channel` and `Scope`

`Channel<'Role, 'Scope> = from: 'Role \* to\_: 'Role \* within: 'Scope`. Channels are scoped to regions; a role participating in an outer protocol and an inner `Nested` has distinct channels for each. Conflating them would create phantom races. Sibling-to-sibling messaging within a `Nested` uses channels scoped to that region; cross-scope messaging uses channels scoped to a common ancestor.

The `'Scope` parameter also makes bubble-up computable: an unhandled event walks the parent chain in the projected statechart until it hits a handler — a parent-chain walk, not an implicit runtime search. Because `'Scope` is an author DU rather than a stringly-typed tag, the compiler enforces that every scope reference corresponds to a declared case.

---

## Appendix F — What the model does not yet address

Known gaps, deliberately out of scope for the first implementation:

### F.1. Harel statechart features

The algebra covers a practical subset of Harel's statechart formalism. What's in and what's out:

**Covered:**

- **Hierarchical nesting (depth).** `Nested` construct; compound states can contain substates to arbitrary depth.
- **Orthogonal regions (concurrency).** `Parallel` inside a `Nested`.
- **Shallow history.** `History` pseudo-state. Resolved at runtime by reading the journal (§9.1) for the last substate of that scope.
- **Deep history.** `DeepHistory` pseudo-state. Journal-resolved recursively at every level below.
- **Initial state.** Implicit — the `body` of a `Nested` is the entry point on scope entry.
- **Event bubble-up (scope escape).** Part-designed — see §6. An unhandled message or effect in an inner scope walks the parent chain of active scopes in the projected statechart. The modal-refinement rule (§13 open question #2) is still to be formalized.

**Gaps:**

- **Entry and exit actions on compound states.** Not first-class in the algebra. Can be approximated via `Effect` annotations on the first/last transition, but that's not the same thing — true entry/exit actions fire on every entry/exit of the compound state, including entries via history pseudo-states. Addition would take the form of `onEntry: 'Effect list` and `onExit: 'Effect list` fields on `Nested`.
- **Internal vs. external transitions.** In Harel, a transition that targets its own source state via an external transition exits and re-enters (firing exit/entry actions); an internal transition does not. Without first-class entry/exit actions, this distinction is moot and is not represented.
- **Final pseudo-states for compound states.** `End` serves as a final state at the top level. Compound states do not have a distinct way to signal completion-causing-containing-scope-to-advance; this is implicit in the `continuation` field of `Nested`. Adequate for straightforward protocols; not fully faithful to Harel's completion-event semantics.
- **Conditional / junction pseudo-states.** No first-class construct. Branching is via `Choice`.
- **Explicit concurrent region synchronization.** `Parallel` waits for all branches to complete implicitly. No first-class join pseudo-state for waiting on a subset.

These gaps are **documentation debt, not architectural debt** — each can be added to the algebra later without changing the projection operator, the journal schema, or the generator pipeline. Prioritization follows demand: entry/exit actions are the most likely first addition, since they show up in real protocols frequently.

### F.2. Temporal and dynamic features

- **Time and timeouts.** The `Timer` effect is a placeholder. Real-time properties (deadlines, bounded latency) need a timed-automata layer not yet sketched.
- **Dynamic role membership.** The model assumes a fixed role set. Parameterized MPST (Yoshida et al.) extends this; integration is future work.

### F.3. Production-hardening features

- **Failure semantics beyond the minimal supervisor floor.** Network partitions, Byzantine roles, at-least-once delivery, cross-actor consistency during partial failure. These layer on top of the protocol algebra via alternative backends (Appendix K), not into it.
- **Journal retention and compaction.** The journal grows with every event. For long-running protocol instances, retention and compaction strategies are needed. Initial implementation: journal lives as long as the protocol instance does, no compaction.

---

## Appendix G — Prior-work retrospective and discipline rules

The prior exploratory pass on Frank's code-gen pipeline was partially completed: MSBuild integration shape was proven, CLI parsers for several input formats were written, a generation scheme serializing object instances to binary was implemented, and a pivot toward source-file generation was in progress. That work stalled for two intertwined reasons.

### G.1. What caused the prior stall

**No settled authoring structure to target.** The AST-with-free-monad approach was rejected for boilerplate weight. Tagless final was attempted but did not finalize. The decision converged on the witness-object HKT pattern (pattern-matched from Higher, without the dependency) only in the current design session. Without a settled target shape, generator work kept producing code that then had to be thrown away.

**LLM-assisted implementation produced incoherent integration.** An earlier Claude session (Opus 4.6) repeatedly reported integration as complete when it was not. Whole sections of the pipeline were written in files that looked wired but did not execute end-to-end paths. Diagnosing the gaps after the fact cost more time than writing the code initially saved.

### G.2. The discipline rules this produces

The layered build order in §12 is designed to prevent a recurrence. The specific rules are:

1. **Every layer has unit tests that pass without the layer above it.** Integration tests are additional, never substitutes.
2. **Write the integration test before asking an LLM (or anyone) to do the integration.** The test is the acceptance criterion; no criterion means no done.
3. **Treat any claim of "this is wired up" as unverified** until a test that exercises the wiring passes. This applies to LLM output, to your own output, to anyone's output.
4. **Commit after every working step.** Branch before major integration work.
5. **Do not move on to step N+1 if step N has unexplained behavior.**
6. **Use this document as the architectural anchor.** Do not let an LLM re-derive architecture on the fly during implementation. If the architecture is wrong, update the document first; do not drift.

### G.3. What carries forward from the prior work

- The MSBuild integration shape — proven, reusable.
- The CLI structure and existing format parsers — usable for Pathway 2 once the CE surface is solid.
- The decision _not_ to go further with free-monad or binary-serialization approaches — confirmed.

What does not carry forward is the partially-integrated generator code. That is being scrapped. The current plan starts over on generator logic, retaining only the pieces named above.

---

## Appendix H — Cross-language precedents

For reference, the broader landscape of session-type implementations in other languages. None of these directly solves Frank's problem, but each illustrates one point in the design space.

| Language   | Project                          | Approach                           | Link                                                                                                                                 |
| ---------- | -------------------------------- | ---------------------------------- | ------------------------------------------------------------------------------------------------------------------------------------ |
| Haskell    | `sessiontypes`, `typed-session`  | Type families + GADTs              | [sessiontypes](https://hackage.haskell.org/package/sessiontypes), [typed-session](https://hackage.haskell.org/package/typed-session) |
| Haskell    | Lindley \& Morris                | Embedding via type families        | [Paper](https://homepages.inf.ed.ac.uk/slindley/papers/fst-extended.pdf)                                                             |
| Rust       | `session\_types`, Ferrite, `par` | Typestate pattern                  | [session_types](https://crates.io/crates/session_types), [faiface/par](https://github.com/faiface/par)                               |
| Rust       | Rumpsteak                        | Generates Rust APIs from Scribble  | [GitHub](https://github.com/zakcutner/rumpsteak)                                                                                     |
| TypeScript | STMonitor, MPST-TS               | Routed MPST                        | [CC 2021](https://doi.org/10.1145/3446804.3446854)                                                                                   |
| Scala      | lchannels, Effpi                 | Akka Typed integration             | [Effpi](https://github.com/alcestes/effpi)                                                                                           |
| F\*        | Session\*                        | Refinement-typed API from Scribble | [arXiv:2009.06541](https://arxiv.org/abs/2009.06541)                                                                                 |

A curated index: Simon Fowler's [session types implementations collection](https://simonjf.com/2016/05/28/session-type-implementations.html).

---

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

- Li, Stutz, Wies, Zufferey (2023) CAV — _Complete Multiparty Session Type Projection with Automata._ [arXiv:2305.17079](https://arxiv.org/abs/2305.17079) | [Springer](https://doi.org/10.1007/978-3-031-37709-9_17) | [Artifact (Zenodo)](https://zenodo.org/records/8161741)
- Majumdar, Mukund, Stutz, Zufferey (2021) CONCUR — predecessor. [LIPIcs.CONCUR.2021.35](https://drops.dagstuhl.de/entities/document/10.4230/LIPIcs.CONCUR.2021.35)

**Verification**

- Zhou, Ferreira, Hu, Neykova, Yoshida (2020) Session\* OOPSLA — [arXiv:2009.06541](https://arxiv.org/abs/2009.06541)
- Dependent Session Types (2025) — [arXiv:2510.19129](https://arxiv.org/abs/2510.19129)

**Effect encodings**

- Carette, Kiselyov, Shan (2009) JFP — _Finally Tagless, Partially Evaluated._ [DOI](https://doi.org/10.1017/S0956796809007205)
- Seemann (2017) — _F# free monad recipe._ [Blog](https://blog.ploeh.dk/2017/08/07/f-free-monad-recipe/)
- Plotkin, Pretnar (2009) ESOP — _Handlers of Algebraic Effects._ [DOI](https://doi.org/10.1007/978-3-642-00590-9_7)
- Lindley, McBride, McLaughlin (2017) POPL — _Do Be Do Be Do_ (Frank-the-language). [DOI](https://doi.org/10.1145/3009837.3009897)
- Danvy, Filinski (1990) LFP — _Abstracting Control_ / _Representing Control._ [DOI](https://doi.org/10.1145/91556.91622)
- Yallop, White (2014) FLOPS — _Lightweight Higher-Kinded Polymorphism._ [Paper](https://www.cl.cam.ac.uk/~jdy22/papers/lightweight-higher-kinded-polymorphism.pdf)
- Lämmel, Peyton Jones (2005) ICFP — _Scrap Your Boilerplate with Class: Extensible Generic Functions._ [MSR](https://www.microsoft.com/en-us/research/publication/scrap-your-boilerplate-with-class/) | [PDF](https://www.microsoft.com/en-us/research/wp-content/uploads/2016/07/gmap3.pdf)
- Palladinos — [Higher](https://github.com/palladin/Higher) (pattern-match, do not adopt)

**Effect-session correspondence**

- Orchard, Yoshida (2016) POPL — _Effects as Sessions, Sessions as Effects._ [DOI](https://doi.org/10.1145/2837614.2837634)
- Orchard, Yoshida (2015) PLACES — _Using session types as an effect system._ [arXiv:1602.03591](https://arxiv.org/abs/1602.03591)
- Hillerström, Lindley, Atkey, Sivaramakrishnan (2017) FSCD — _Continuation Passing Style for Effect Handlers._ [LIPIcs.FSCD.2017.18](https://drops.dagstuhl.de/entities/document/10.4230/LIPIcs.FSCD.2017.18)
- Forster, Kammar, Lindley, Pretnar (2019) JFP — _On the Expressive Power of User-Defined Effects._ [DOI](https://doi.org/10.1017/S0956796819000121)
- Reynolds (1972) ACM National Conference — _Definitional Interpreters for Higher-Order Programming Languages._ Reprinted Higher-Order \& Symbolic Computation 11(4), 1998. [DOI](https://doi.org/10.1023/A:1010027404223)
- Gibbons (2022) Programming Journal — _Continuation-Passing Style, Defunctionalization, Accumulations, and Associativity._ [arXiv:2111.10413](https://arxiv.org/abs/2111.10413)

**Nested / compositional MPST (adjacent to Frank's hierarchy concern)**

- Demangeon, Honda (2012) CONCUR — _Nested Protocols in Session Types._ [DOI](https://doi.org/10.1007/978-3-642-32940-1_20)
- Capecchi, Giachino, Yoshida (2010) FSTTCS — _Global Escape in Multiparty Sessions._ [LIPIcs.FSTTCS.2010.338](https://drops.dagstuhl.de/entities/document/10.4230/LIPIcs.FSTTCS.2010.338)
- Gheri, Yoshida (2023) POPL / PACMPL — _Hybrid Multiparty Session Types._ [arXiv:2302.01979](https://arxiv.org/abs/2302.01979)
- Zhou, Gheri, Yoshida et al. (2021) — _Communicating Finite State Machines and an Extensible Toolchain for MPST_ (nuScr, with nested-protocol extension as a case study). [DOI](https://doi.org/10.1007/978-3-030-86593-1_2)
- Udomsrirungruang, Yoshida (2025) POPL — _Top-Down or Bottom-Up? Complexity Analyses of Synchronous Multiparty Session Types._ [PDF](https://mrg.cs.ox.ac.uk/publications/top-down-or-bottom-up-complexity-analyses-of-synchronous-multiparty-session-types/main.pdf)

**Tools**

- F\* — [fstar-lang.org](https://fstar-lang.org) | [GitHub](https://github.com/FStarLang/FStar)
- Z3 — [GitHub](https://github.com/Z3Prover/z3)
- F# Compiler Service (FCS) — [GitHub](https://github.com/dotnet/fsharp/tree/main/src/Compiler/Service)
- Fantomas — [fsprojects/fantomas](https://github.com/fsprojects/fantomas) | [Generating code docs](https://fsprojects.github.io/fantomas/docs/end-users/GeneratingCode.html)
- Fabulous.AST — [edgarfgp/Fabulous.AST](https://github.com/edgarfgp/Fabulous.AST) | [docs](https://edgarfgp.github.io/Fabulous.AST/)
- Myriad — [MoiraeSoftware/Myriad](https://github.com/MoiraeSoftware/Myriad) (considered and rejected; see Appendix C)
- Scribble / nuScr — [nuscr.dev](https://nuscr.dev)
- F# language suggestion #864 (source generators; vaporware) — [fslang-suggestions#864](https://github.com/fsharp/fslang-suggestions/issues/864)
- Roslyn Source Generators (C#) — [Microsoft Learn](https://learn.microsoft.com/en-us/dotnet/csharp/roslyn-sdk/source-generators-overview)

---

## Appendix J — Order fulfillment example in full

Three roles (Customer, Merchant, Warehouse), two levels of hierarchy. The outer `OrderPhase` envelope contains nested `InventoryPhase` and `ShippingPhase`. Parallel regions inside `ShippingPhase` run shipment and payment concurrently.

First the author-supplied DUs that instantiate the algebra's type parameters:

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
        \[ReadOrders; WriteOrders; EmitOrderPlaced],

        // Level 1: Customer places the order
        Send(Channel(Customer, Merchant, OrderPhase),
             PlaceOrder placeholder,
             \[WriteOrders; EmitOrderPlaced],
          Recv(Channel(Customer, Merchant, OrderPhase),
               PlaceOrder placeholder,
               \[],

            // Level 2: InventoryPhase
            Nested(
                InventoryPhase,
                Sync,
                Restart,
                \[ReadInventory],

                Send(Channel(Merchant, Warehouse, InventoryPhase),
                     CheckInventory placeholder,
                     \[ReadInventory],
                  Recv(Channel(Merchant, Warehouse, InventoryPhase),
                       CheckInventory placeholder,
                       \[],
                    Send(Channel(Warehouse, Merchant, InventoryPhase),
                         InventoryStatus placeholder,
                         \[],
                      Recv(Channel(Warehouse, Merchant, InventoryPhase),
                           InventoryStatus placeholder,
                           \[],
                        End)))),

                // Merchant decides
                Choice(Merchant, \[
                    OrderConfirmed placeholder,
                      Send(Channel(Merchant, Customer, OrderPhase),
                           OrderConfirmed placeholder,
                           \[EmitOrderConfirmed],
                        Recv(Channel(Merchant, Customer, OrderPhase),
                             OrderConfirmed placeholder,
                             \[],

                          // Level 2: ShippingPhase (parallel regions)
                          Nested(
                              ShippingPhase,
                              AsyncFIFO 8,
                              Escalate,
                              \[WriteShipments; CallPaymentGateway],

                              Parallel \[
                                  // Shipping branch
                                  Send(Channel(Merchant, Warehouse, ShippingPhase),
                                       PrepareShipment placeholder,
                                       \[WriteShipments],
                                    Recv(Channel(Merchant, Warehouse, ShippingPhase),
                                         PrepareShipment placeholder,
                                         \[],
                                      Send(Channel(Warehouse, Customer, ShippingPhase),
                                           ShipmentDispatched placeholder,
                                           \[EmitShipmentDispatched],
                                        Recv(Channel(Warehouse, Customer, ShippingPhase),
                                             ShipmentDispatched placeholder,
                                             \[],
                                          End))))

                                  // Payment branch
                                  Send(Channel(Customer, Merchant, ShippingPhase),
                                       ProcessPayment placeholder,
                                       \[CallPaymentGateway],
                                    Recv(Channel(Customer, Merchant, ShippingPhase),
                                         ProcessPayment placeholder,
                                         \[],
                                      Send(Channel(Merchant, Customer, ShippingPhase),
                                           PaymentConfirmed placeholder,
                                           \[EmitPaymentConfirmed],
                                        Recv(Channel(Merchant, Customer, ShippingPhase),
                                             PaymentConfirmed placeholder,
                                             \[],
                                          End))))
                              ],
                              End))))

                    OrderDenied placeholder,
                      Send(Channel(Merchant, Customer, OrderPhase),
                           OrderDenied placeholder,
                           \[EmitOrderDenied],
                        Recv(Channel(Merchant, Customer, OrderPhase),
                             OrderDenied placeholder,
                             \[],
                          End))
                ])))),

        End)
```

`placeholder` stands in for the payload values at the protocol-algebra level — a protocol value describes the _shape_ of the exchange, not the runtime payloads. The generator and the projection operate on message _tags_ (DU cases), not on payload values; payloads flow through at runtime. An implementer will likely factor this with a tag-only DU for protocol construction separate from the payload-bearing DU used at runtime, or use an approach like unit-payload constructor values (`PlaceOrder Unchecked.defaultof<\_>`). That is an implementation choice the sketch leaves open.

With the CE builder, most of the parenthesization collapses and the expression reads as a sequence of `Send`/`Recv`/`Nested`/`Choice` operations, similar in shape to a Scribble protocol but native F#.

---

## Appendix K — Alternative runtime backends

The initial Frank runtime is `MailboxProcessor` on F#'s `Async`, with the lightweight supervisor described in §6. That combination is minimal, dependency-free, and sufficient to exercise the full generator pipeline and the protocol algebra end-to-end. It is not the only viable runtime target.

Richer supervision semantics — strategy dispatch, restart backoff, restart limits, supervision trees, actor lifecycle hooks, persistence, clustering, remote actors — exist in mature .NET actor frameworks. Reimplementing them inside Frank would be a significant engineering project duplicating well-trodden ground. The intended path, when Frank's protocols need those semantics, is to target one of these frameworks as an **alternative generator backend**: the same CE, the same `Frank.Protocol.Analysis` output, the same `IEffectHandler<\_,\_>` interface, but emission targets the framework's actor primitives rather than `MailboxProcessor`.

This is a codegen-level concern, not a protocol-algebra concern. The author's CE does not change; the generated code does.

### K.1. Candidate frameworks

| Framework             | Model                                                         | Notes                                                                                                                                                                                                                                   |
| --------------------- | ------------------------------------------------------------- | --------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------- |
| **Proto.Actor**       | Classical actor model, faithful to Akka patterns, .NET-native | Full supervision strategies, remote actors, persistence plugins. [GitHub](https://github.com/asynkron/protoactor-dotnet)                                                                                                                |
| **Akka.NET**          | Port of Akka (Scala/Java) to .NET                             | Most mature supervision story. Heavier runtime and configuration footprint. [akka.net](https://getakka.net/)                                                                                                                            |
| **Microsoft Orleans** | Virtual actors (grains)                                       | Different conceptual model — grain activation/deactivation is implicit. Applicable when the role set is long-lived, addressable by identity, and distributed. [Orleans docs](https://learn.microsoft.com/en-us/dotnet/orleans/overview) |
| **Dapr Actors**       | Sidecar-based, language-agnostic                              | State persistence built in. Could bridge Frank to non-.NET participants. [Dapr actors](https://docs.dapr.io/developing-applications/building-blocks/actors/actors-overview/)                                                            |

### K.2. Mapping discipline

Each backend maps the four actor-shape templates from §7 onto its own primitives. The mapping table a backend has to satisfy:

| Frank concept                         | Required in the backend                                                                                                                    |
| ------------------------------------- | ------------------------------------------------------------------------------------------------------------------------------------------ |
| One actor per role                    | One framework primitive per role (Akka actor, Proto.Actor actor, Orleans grain, Dapr actor)                                                |
| `Send` / `Recv`                       | Framework's ask/tell / message-send primitive                                                                                              |
| Hierarchical `Nested` regions         | Framework's parent-child or grain-hierarchy mechanism, if any; otherwise emulated in generated state                                       |
| `History` / `DeepHistory` resolution  | Journal lookup (same contract as the MailboxProcessor floor)                                                                               |
| `SupervisionPolicy` annotations       | Native framework supervision strategy — the annotation now has semantics                                                                   |
| `IEffectHandler<\_,\_>`               | Unchanged — the handler interface is runtime-agnostic                                                                                      |
| `IProtocolJournal`                    | Framework's native persistence adapted to the journal interface: Orleans grain state, Proto.Actor event-sourcing plugins, Dapr state store |
| Observability projections (MEL, OTel) | Unchanged — consume the journal the same way                                                                                               |
| Peer references                       | Framework's typed actor reference (`PID`, `IActorRef`, `IGrainFactory`, etc.)                                                              |

The CE and the effect handler stay identical across backends. The generator is the only layer that changes. Critically, the journal interface is the stable contract: each backend maps its native persistence story onto `IProtocolJournal`, and every downstream consumer (interpreters, projections, supervisor logic) works unchanged. Orleans grains already persist; Proto.Actor has event-sourcing plugins; Dapr has a state store building block. In each case the integration work is "expose these as `IProtocolJournal`," not "reimplement the journal."

### K.3. Why defer

All of these backends are deferred work. Reasons to hold:

- **The MailboxProcessor floor is enough to validate the algebra.** Projection, implementability, effect-message correspondence, and verification all exercise cleanly without a heavyweight actor runtime.
- **Framework commitment is long-term.** Adopting Proto.Actor or Akka.NET commits Frank's users to that framework's lifecycle, configuration, and learning curve. Making that commitment before the protocol algebra is stable would constrain design choices for the wrong reason.
- **Swapping backends later is a generator-level change.** Once the CE authoring surface and `Frank.Protocol.Analysis` are stable, adding a new backend is a matter of writing one more emission pathway. It does not force rewrites of authored protocols.
- **Multi-backend support is itself a later decision.** The question of whether Frank ships with one backend or lets the author pick is a product question that does not need to be answered until at least one backend beyond MailboxProcessor is built.

### K.4. When to revisit

Revisit when one of the following is true:

- A protocol in production needs supervision semantics the minimal floor does not provide (restart-on-failure, at-least-once delivery on role crash, distributed role placement).
- A protocol needs to span process or machine boundaries and the MailboxProcessor assumption of single-process execution breaks.
- A protocol's role lifecycle needs to be longer than a single protocol instance (virtual-actor territory — grain activation semantics matter).

The first backend to add under those conditions is probably Proto.Actor, for closest conceptual match to MailboxProcessor plus full supervision. Orleans is the right target if the virtual-actor model becomes relevant. Akka.NET is a last resort given its configuration weight; Dapr is interesting only if multi-language participation becomes a requirement.

---

## Appendix L — What's novel and what isn't

This appendix exists because the rest of the doc can read as more ambitious than it is. The honest position: Frank is primarily a **design and engineering contribution**, not a research contribution. Every major theoretical piece it uses is already in the literature. What Frank does is combine them into a working F# library targeting .NET actors, with two possible modest formal extensions that would need to be confirmed against the literature before being claimed.

### L.1. Directly borrowed and cited — not novel

Each of these is adopted wholesale from existing work:

| Piece                                             | Source                                                                                                                             | Status                                                      |
| ------------------------------------------------- | ---------------------------------------------------------------------------------------------------------------------------------- | ----------------------------------------------------------- |
| Hierarchical state machines                       | Harel (1987)                                                                                                                       | 40 years of literature                                      |
| Multiparty session types                          | Honda, Yoshida, Carbone (2008, 2016)                                                                                               | Foundational                                                |
| Coalgebraic view of session types                 | Keizer, Basold, Pérez (2021, 2022)                                                                                                 | Frank uses this as the internal unification                 |
| Automata-based MPST projection + implementability | Li, Stutz, Wies, Zufferey (CAV 2023)                                                                                               | The projection algorithm Frank implements                   |
| Effects ↔ sessions correspondence                | Orchard, Yoshida (POPL 2016)                                                                                                       | Theoretical anchor for the effect-message wiring            |
| CPS compilation of effect handlers                | Hillerström, Lindley, Atkey, Sivaramakrishnan (FSCD 2017)                                                                          | Compilation scheme onto `Async.FromContinuations`           |
| Effect handlers ≡ delimited continuations         | Forster, Kammar, Lindley, Pretnar (JFP 2019)                                                                                       | Justifies the DCont framing                                 |
| Defunctionalization                               | Reynolds (1972)                                                                                                                    | Reifies continuations as successor-state dispatch           |
| Witness-object HKT encoding                       | Yallop, White (FLOPS 2014); Palladinos's [Higher](https://github.com/palladin/Higher)                                              | Pattern-matched, library not depended on                    |
| F# + session types + SMT verification             | Neykova, Hu, Yoshida, Abdeljallal (Session Type Providers, CC 2018); Zhou, Ferreira, Hu, Neykova, Yoshida (Session\*, OOPSLA 2020) | Closest existing cousins to Frank on .NET; Session\* on F\* |
| Scribble-to-target code generation                | Scribble toolchain (Yoshida group and collaborators, 2010s–present)                                                                | Existing practice for many targets                          |

None of these is a Frank contribution. The doc cites them at the points where it uses them.

### L.2. Possibly novel — small formal extensions, not confirmed

Two places where Frank appears to go modestly beyond the published literature. Both are flagged as _possibly_ novel because the literature on session types and statecharts is large and the author has not done an exhaustive survey. Before claiming either as a contribution in any venue, a proper survey is required.

**L.2.1. Hierarchical statecharts integrated with multiparty session types as one algebra, with projection preserving the hierarchy.** Li et al. (CAV 2023) project flat MPST to communicating state machines. Demangeon and Honda (CONCUR 2012) introduce _nested protocols_ in session types — protocol-composition-style nesting, where a protocol can invoke a sub-protocol as a unit. Scribble implements this; NuScr ships it as an extension. Gheri and Yoshida (Hybrid MPST, 2023) handle compositionality of MPST via subprotocols, a related but distinct notion. Capecchi, Giachino, and Yoshida (FSTTCS 2010) introduce global escape as an exception-like mechanism, which is hierarchy-adjacent. None of these combines _Harel-style state nesting within a single role's local type_ — hierarchy as depth, orthogonal regions, history states, event bubble-up from inner to outer states — with MPST projection in a single global-type syntax. Frank's `Nested` construct and its projection rule appear to land in that gap.

If this is genuinely not in the literature, Frank's `Nested` construct and its projection rule are a modest formal extension. Not a new theorem — more like "here is the syntax, here is the extended projection operator, here is the proof it commutes with Li-et-al.'s construction when nesting is trivial, plus the modal-refinement rule for what a role's local type inherits from its containing `Nested`." Systems-paper flavor, not theory-paper flavor.

**L.2.2. Codegen-time verification of generated actor code against its source protocol, with emitted SMT-LIB artifacts.** Session\* (Neykova et al. 2018) verifies source protocols with refinements at compile time via the type provider. Frank extends this in two ways: it verifies the generated code's behavior against the projection (not just the protocol against itself), and it emits the SMT-LIB queries as build artifacts for reproducibility, independent verification, and build caching (§10.9.2).

The protocol-verification half is within existing practice. The generated-code-verification half — running Z3 over both source and emitted artifacts in one pipeline — is close to but not exactly in the published literature. If written up, it's a tools or experience paper, not a theory paper.

Neither of these is a reason on its own to publish. Taken together with Frank as a working library, they could support a single short paper in a tools track or workshop.

### L.3. Engineering-novel — combination, not contribution

The particular combination of design choices is not present in any existing system I could find:

- F# CE authoring surface
- Coalgebraic session types as the canonical internal form
- Li-et-al. projection with implementability checking
- Witness-object HKT pattern on `Task`/`Async` for effects
- Delimited-continuation framing onto `Async.FromContinuations`
- Fabulous.AST emission, Fantomas formatting, FCS-based pipeline
- Integrated codegen-time Z3 verification with SMT-LIB artifact emission
- Journal-primary runtime with SQLite-default storage and `Microsoft.Extensions.Logging` / OpenTelemetry as downstream projections
- Lightweight supervisor floor with defined escape hatch to Proto.Actor / Akka.NET / Orleans
- Two-pathway intake (hand-authored CE, or CLI from Scribble / SCXML / other formats)

No existing system combines these. That's a **design synthesis**, not a research novelty. It matters for practitioners who want this combination; it does not matter for program committees.

### L.4. Explicitly not new

To prevent scope misreading:

- Frank is **not** a new type system.
- Frank is **not** a new session-type calculus.
- Frank is **not** a new algebraic-effects theory.
- Frank is **not** a new projection algorithm — it implements Li et al.'s.
- Frank is **not** a new verification logic — it uses SMT via Z3.
- Frank is **not** a new actor model — it generates code onto an existing one (`MailboxProcessor`) and structures its runtime to allow swapping in others.

### L.5. What Frank _is_

- An F# library that unifies several existing formalisms (statecharts, MPST, coalgebraic session types, algebraic effects) into a single author-facing CE.
- A generator that implements known correspondences (effects ↔ sessions, handlers ↔ delimited continuations, projection via automata) onto the .NET actor model.
- A toolchain that combines codegen with integrated static verification in a way that's close to but may not quite be in the existing published practice.

Framed this way, Frank is useful and defensible. Framing it as a research contribution would overstate what's actually new. The two flagged items in §L.2 are the only places where a research claim might live, and both require a proper literature survey before being made.

---

## Appendix M — Journal storage backends: SQLite default, LMDB escape hatch

The journal (§9.1) is the canonical in-process record of protocol execution. Its storage backend is a narrow, orthogonal concern — the journal's consumers (history resolution, resumption, interpreters, projections) see a stable query interface regardless of what's underneath. This appendix documents the default choice and the path to an alternative if performance demands it.

### M.1. SQLite — the default

Frank ships with SQLite as the journal backend. Reasons:

- **Transactional durability.** Every state transition commits as an ACID transaction; a crash mid-transition leaves the journal reflecting the last committed state, not a partial write. This is exactly what resumption needs.
- **Crash safety.** WAL mode gives well-understood crash-recovery semantics without author configuration.
- **Queryable history.** Interpreters (§10.9.3) and handlers that need historical state issue SQL queries against structured data. No custom indexing, no hand-rolled serialization, no bespoke query language.
- **Operational story.** SQLite databases are single files, inspectable with standard tooling (`sqlite3` CLI, any GUI browser), usable for ad-hoc debugging. If a protocol instance misbehaved in production, the journal is a file an operator can copy and open.
- **Portability.** SQLite runs on every platform .NET runs on. No native dependencies beyond what's already in the BCL ecosystem.
- **Mature .NET story.** `Microsoft.Data.Sqlite` is Microsoft-maintained and widely deployed; writeable from F# as easily as from C#.

_Architectural pattern adopted from practice._ The journal-as-colocated-storage-per-actor model matches Cloudflare Durable Objects and similar per-entity persistence designs. Each actor owns its journal namespace; the journal lives with the actor's execution; it is the source of truth for state, not replayed from an external log. This is the opposite of event-sourced-via-shared-log designs (Kafka + consumer state), and it's the right match for the MailboxProcessor floor — low operational complexity, single-process semantics, no coordination protocol.

_File layout decision — one database per actor, or one per protocol instance?_ Deferred to implementation; both are viable. One-per-actor matches Durable Objects most directly. One-per-instance gives the supervisor trivially-cheap read access to every actor's journal in the protocol instance (single connection, joined queries). Initial implementation can start with one-per-instance and refactor if a specific workload hits contention.

### M.2. Where SQLite starts to hurt

SQLite is excellent up to roughly the write throughput a single process can drive against a single file under serialized transactions. It starts to show strain in the following regimes:

- **Very high-frequency state transitions.** Protocols with thousands of journal writes per second per actor, especially if many actors share a single database file. WAL mode helps but has ceilings.
- **Very large journals.** Long-running protocol instances whose journals grow into the tens of gigabytes. SQLite handles this correctness-wise but query latency on complex joins degrades.
- **Heavy concurrent writers.** If many actors share a database and all write frequently, SQLite's single-writer discipline serializes them; per-actor files sidestep this but introduce a different operational overhead (file count).

None of these is a theoretical failure mode — they're empirical. Frank's position is "start with SQLite; profile first; only swap if the profile demands it."

### M.3. LMDB — the escape hatch

LMDB is the recommended alternative if SQLite can't keep up for a specific workload. It gives up:

- **Ad-hoc queryability.** LMDB is a key-value store with cursor iteration. No SQL, no declarative queries. Interpreters and tooling that relied on SQL need to be rewritten against the key-value API or run against a replicated SQLite mirror.
- **Standard tooling.** `sqlite3` CLI doesn't exist for LMDB. Inspection tooling is thinner.
- **Cross-process ease.** SQLite's file-based model makes moving databases between machines trivial. LMDB's memory-mapped format is similarly portable but with more environmental assumptions.

It regains:

- **Higher write throughput.** Memory-mapped, append-oriented, lock-free-reader semantics. For append-heavy workloads (which journals are), this is the right shape.
- **Lower read latency.** Readers don't block writers and vice versa; there's no serialization contention.
- **Better behavior at scale.** Large databases don't slow down queries the way they can in SQLite.

The empirical trigger for switching is specific: **if profiling shows journal writes are the protocol's bottleneck** — not total execution latency, not handler latency, not message-passing throughput, but journal write throughput specifically — switch the affected protocol's journal to LMDB.

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

- **Cloudflare Durable Objects** — per-entity colocated storage, strong consistency within an object, SQLite-backed in the current implementation. Direct architectural precedent for Frank's per-actor journal.
- **Gleam's actor storage patterns** — similar per-actor persistence colocated with execution in BEAM/Gleam ecosystems.

Both are cited as existence proofs that the per-entity-journal architecture is a working pattern at production scale, not a Frank invention.
