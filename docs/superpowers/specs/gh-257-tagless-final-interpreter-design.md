---
source: "github issue #257"
title: "Formalize hierarchical runtime as AST interpreter (tagless-final)"
milestone: "v7.4.0"
state: "OPEN"
type: spec
---

# Formalize hierarchical runtime as AST interpreter (tagless-final)

> Extracted from [frank-fs/frank#257](https://github.com/frank-fs/frank/issues/257)

**Probability of Successful Implementation: ~80%**
 
The operation extraction (Phase 1) is mechanical and well-specified — the six operations are named, their current locations are cited, and the existing test suite provides a safety net (AC-1). The tagless-final decision resolves the previously open question of whether the interface is a record of functions, an abstract type, or a module signature — it is a **record of functions** (`TransitionAlgebra<'r>`). This eliminates the main design ambiguity. The DualInterpreter (AC-3) remains the highest-risk acceptance criterion because it requires understanding the AND-state gap from #244 and designing the Fork operation's return type to support dual derivation — but the record-of-functions encoding makes it straightforward to test incrementally by building up the algebra one operation at a time.
 
**To raise to ~88%:** Clarify the Fork operation's return type with respect to dual derivation. Add a concrete example of the order fulfillment transition expressed against the algebra.
 
> Scope reduction: Phase 3 (CE encoding) moves to a separate issue. This issue covers only the operation vocabulary extraction and interpreter algebra definition.
 
## Thesis
 
The statechart AST is already an instruction language and `HierarchicalRuntime` is already its interpreter — but the boundary is implicit. Extracting an explicit interpreter algebra (tagless-final style) enables multiple interpretations of the same statechart program (runtime execution, dual derivation, trace generation, guard pre-evaluation) and establishes the **target specification** for code generation from SCXML.
 
## Problem
 
Today, three modules independently traverse the statechart structure to extract meaning:
 
1. **`HierarchyBridge.fromDocument`** walks `StatechartDocument` → `HierarchySpec` (AST → runtime spec)
2. **`HierarchicalRuntime.transition`** interprets `StateHierarchy` to compute entry/exit sequences, LCA resolution, and history updates
3. **`Dual.deriveWithHierarchy`** re-traverses the hierarchy to compute client obligations, but approximates AND-state semantics because it has its own traversal logic rather than sharing the runtime's
 
The AND-state dual derivation gap (#244, formalism bound 1) exists partly because `Dual.fs` cannot reuse `HierarchicalRuntime`'s entry/exit/fork semantics — they're entangled with `ActiveStateConfiguration` mutation rather than expressed as interpretable operations.
 
Similarly, `Middleware.fs` (`HandleStateful`, lines 67–173) orchestrates a fixed sequence: resolve handlers → evaluate guards → invoke handler → evaluate event guards → execute transition. Each step is an *effect* (consult state, check authorization, dispatch HTTP, persist transition), but they're wired together imperatively rather than composed as an interpreted program.
 
The result is that adding a new interpretation (e.g., "dry-run a transition to check validity without executing it") requires duplicating the traversal logic rather than providing a new interpreter.
 
## Solution
 
Formalize the implicit interpreter boundary using a **tagless-final** style — the statechart operations become a record of functions (the "algebra"), and concrete interpreters provide meaning by supplying different records. This is the natural F# encoding: records of functions are first-class, composable, and require no special machinery.
 
**Phase 1: Extract the operation vocabulary from `HierarchicalRuntime.transition`**
 
The current `transition` function (Hierarchy.fs, lines 365–425) performs these operations in sequence:
 
* `ComputeLCA(source, target) → string` — find lowest common ancestor
* `ExitUpTo(state, lca) → ExitedStates` — walk source to LCA, deactivating and recording history
* `EnterDownTo(lca, target) → EnteredStates` — walk LCA to target, activating composites
* `ForkRegions(andState, children) → unit` — AND-state parallel activation (inside `enterState`)
* `RecordHistory(composite, config) → unit` — snapshot active config for history pseudo-states
* `RestoreHistory(composite, kind) → ActiveStateConfiguration option` — recall for history entry
 
These six operations are the instruction set. Today they're interleaved in one function; the formalization extracts them into an interpretable vocabulary.
 
**Phase 2: Define the interpreter algebra as a record of functions**
 
```fsharp
/// Statechart transition operations as a tagless-final algebra.
/// Each field is an operation; concrete interpreters supply implementations.
type TransitionAlgebra<'r> = {
    ComputeLCA: source: string * target: string -> string option
    Exit: stateId: string -> 'r
    Enter: stateId: string -> 'r
    Fork: regions: string list -> 'r
    RecordHistory: compositeId: string -> 'r
    RestoreHistory: compositeId: string * kind: HistoryKind -> ActiveStateConfiguration option
}
```
 
The shape will evolve during implementation — this is illustrative. The key property: the *program* (transition logic) is written once as a function that takes a `TransitionAlgebra<'r>`; interpreters provide meaning by constructing different algebra records:
 
* **RuntimeAlgebra**: Mutates `ActiveStateConfiguration`, persists via `IStatechartsStore` — what `HierarchicalRuntime.transition` does today
* **TraceAlgebra**: Collects `ExitedStates`/`EnteredStates` lists without mutation — what `HierarchicalTransitionResult` captures today, but separable from runtime
* **DualAlgebra**: Computes client obligations per operation — replaces the separate traversal in `Dual.deriveWithHierarchy`, closing the AND-state gap by reusing the runtime's fork semantics
* **ValidationAlgebra**: Evaluates guards and checks transition legality without executing — enables "dry-run" for the middleware's guard evaluation
 
A transition program is then a function:
 
```fsharp
/// A transition program is a function awaiting an interpreter.
/// "A program is just a function waiting for an interpreter." — Azariah
let pickToPack (alg: TransitionAlgebra<unit>) =
    alg.Exit "Picking"
    alg.RecordHistory "Fulfillment"
    alg.Enter "Packing"
```
 
Composition of interpreters is record merging:
 
```fsharp
/// Compose two algebras — e.g., runtime + trace
let withTrace (runtime: TransitionAlgebra<unit>) (trace: TransitionAlgebra<unit>) =
    { ComputeLCA = fun (s, t) -> runtime.ComputeLCA(s, t)
      Exit = fun s -> runtime.Exit s; trace.Exit s
      Enter = fun s -> runtime.Enter s; trace.Enter s
      Fork = fun rs -> runtime.Fork rs; trace.Fork rs
      RecordHistory = fun c -> runtime.RecordHistory c; trace.RecordHistory c
      RestoreHistory = fun (c, k) -> runtime.RestoreHistory(c, k) }
```
 
## Acceptance Criteria
 
### AC-1: Operation extraction is semantically equivalent
 
```
Given: the existing HierarchicalRuntime.transition test suite
When: the transition logic is refactored to use the TransitionAlgebra record
Then: all existing tests pass without modification
Falsifiable by: any test regression means the extraction changed semantics
```
 
### AC-2: Trace algebra produces identical results to current HierarchicalTransitionResult
 
```
Given: a hierarchy with XOR and AND composites (the existing test fixtures)
When: the same transition is executed via RuntimeAlgebra and TraceAlgebra
Then: TraceAlgebra.ExitedStates == RuntimeAlgebra result.ExitedStates
  AND TraceAlgebra.EnteredStates == RuntimeAlgebra result.EnteredStates
Falsifiable by: any divergence between the two algebras means the vocabulary
  is incomplete
```
 
### AC-3: Dual algebra can traverse AND-state fork operations
 
```
Given: a statechart with AND-composite containing two parallel regions
When: the dual algebra processes a transition that enters the AND-state
Then: the dual result contains obligations for BOTH regions
      (not the current flat-FSM approximation)
Falsifiable by: removing the Fork operation from the algebra and verifying
  the dual result degrades to flat-FSM semantics (the current behavior)
```
 
### AC-4: Validation algebra enables dry-run guard evaluation
 
```
Given: a stateful resource with guards on a transition
When: a request triggers the validation algebra (not the runtime algebra)
Then: guards are evaluated and the result is Allow/Block, but no state mutation
  occurs AND IStatechartsStore.SetState is NOT called
Falsifiable by: instrumenting the store with a call counter; dry-run must show
  zero SetState calls
```
 
### AC-5: Transition programs are functions taking TransitionAlgebra
 
```
Given: a transition program expressed as a function taking TransitionAlgebra<'r>
When: the program is called with RuntimeAlgebra, then TraceAlgebra,
  then a test stub algebra
Then: each algebra produces its own typed result; the program source is
  identical across all three invocations
Falsifiable by: any algebra-specific logic in the program means the
  abstraction leaks
```
 
### AC-6: Algebra record is composable
 
```
Given: a RuntimeAlgebra and a TraceAlgebra
When: the two are composed into a single algebra (e.g., via withTrace)
Then: executing a transition program against the composed algebra produces
  both runtime effects AND trace output
Falsifiable by: the composed algebra missing operations or producing
  different results than running against each algebra separately
```
 
## Citations

- Seemann, M. (2017). [F# Free Monad Recipe](https://blog.ploeh.dk/2017/08/07/f-free-monad-recipe/) — Instructions as DU cases with continuations; interpretation separated from definition. The operation vocabulary (AC-1) follows this pattern but uses tagless-final encoding to avoid HKT limitations.
- Azariah, J. (2025). [Tagless Final in F#: Froggy Tree House](https://johnazariah.github.io/2025/12/12/tagless-final-01-froggy-tree-house.html) — "A program is just a function waiting for an interpreter." The `ITransitionInterpreter<'Result>` interface is the interpreter record; the transition logic is the program.
- Haynes, H. (2025). [Delimited Continuations](https://clef-lang.com/docs/design/concurrency/delimited-continuations/) (SpeakEZ/Clef) — `let!` as `shift`, CE builder as `reset`. The `transition { }` CE's `Bind` is the continuation delimiter; each operation captures "what comes next."
- Ducasse, S. et al. [Seaside framework](https://github.com/SeasideSt/Seaside) — `callcc` for web flow: a statechart state is a suspended continuation. Entering a composite state establishes a new continuation boundary; exiting captures it as history. The CE encoding (AC-5) follows this model.
- Harel, D. (1987). "Statecharts: A Visual Formalism for Complex Systems." — AND-state semantics require parallel region activation (fork) and synchronization (join). The Fork operation in the vocabulary is the minimal instruction needed to close the AND-state dual derivation gap.

## Architectural Decision: Tagless Final over Free Monad
 
All issues in this document assume the **tagless-final** encoding for the interpreter abstraction. This decision was made based on the following analysis:
 
**Why tagless final:**
 
1. **F# has first-class records of functions but no higher-kinded types.** Tagless final's interpreter is a record — construct it, pass it, swap it in tests. No special machinery. Free monads in F# require Seemann's continuation-threading recipe, producing types that are hard to read and debug, or church-encoded free using `obj` boxing at the boundary, sacrificing type safety.
 
2. **Composition is simpler.** Want a trace interpreter that also does runtime mutation? Compose two records. Want a validation-only interpreter? Pass a record that no-ops on mutation operations. No tree to walk, no interpreter function to write.
 
3. **Code generation is cleaner.** `frank-cli extract` emits a function that takes a `TransitionAlgebra<'r>` and calls its operations in sequence. Straightforward string templating. Emitting a properly continuation-threaded DU tree is significantly more complex to generate and harder for developers to read.
 
**Why not free monad:**
 
The free monad's advantages are introspection (inspect program structure before running), serialization (checkpoint a partially-executed program), and structural testing (assert "Exit is called before Enter" by inspecting the tree rather than running against a trace interpreter).
 
These advantages don't apply to Frank's statechart use case because **long-lived workflows are sequences of states, not long transitions.** A saga is not a suspended transition — it's the machine sitting in a state until the next event arrives. Each individual transition is short, synchronous, and runs to completion within a single HTTP request. The `ActiveStateConfiguration` checkpoints progress between transitions via `IStatechartsStore`, not by serializing a half-executed transition program.
 
Harel's hierarchy handles this natively: the order statechart has a Fulfillment region with Pick → Pack → Ship states. Each transition between them is atomic. The long-lived nature of the workflow is expressed as *being in a state*, not as *being mid-transition*. There is no case in the statechart formalism where you need to serialize a half-executed transition program — the formalism decomposes long workflows into sequences of short transitions with checkpointed states between them.
 
**Expert sources for this decision:**
 
* **Seemann, M.** (2017). F# Free Monad Recipe — demonstrated the approach but also its ergonomic cost in F#
* **Azariah, J.** (2025). Tagless Final in F#: Froggy Tree House — "A program is just a function waiting for an interpreter." Records of functions as the natural F# encoding.
* **Harel, D.** (1987). Statecharts — AND-state semantics, hierarchical decomposition of long-lived workflows into short transitions with checkpointed state between them

## Concurrency Model

The interpreter abstraction must compose with — not replace — the existing actor-based concurrency model. MPST role projections are the key constraint that makes this tractable in a multi-user HTTP runtime.

### The interpreter stays pure and synchronous

`HierarchicalRuntime.transition` is currently a pure function: immutable inputs in, result out. The interpreter vocabulary must preserve this property. Async concerns (store access via `IStatechartsStore`, actor serialization via `MailboxProcessor`) remain in the middleware layer that *calls* the interpreter. This means:

- `ITransitionInterpreter` operations are pure and synchronous (like today's `transition`)
- The middleware wraps the interpreter call in the async actor protocol (like today's `HandleStateful`)
- The CE encoding works with synchronous stub interpreters for testing
- The production RuntimeInterpreter is called from within the actor's serialized context

### Per-request isolation vs. shared program

`StateHierarchy` is immutable and shared — built once from the AST. The transition *program* (the operation sequence) is also shared — determined by statechart structure. But interpreter *state* (current `ActiveStateConfiguration`, accumulated `ExitedStates`, the `HistoryRecord` being built) must be per-request. The existing pattern handles this naturally because `transition` takes and returns values. With an interpreter object, instances must not be shared across requests. Precedent: `TemplateMatcher` (CLAUDE.md) — cache the immutable template, create the matcher per-request.

### Roles constrain the concurrency surface

In a multi-user runtime, AND-state regions advanced by different users could be a free-for-all — but MPST role projections prevent this. Each role has a projected view of the statechart (`Dual.deriveWithHierarchy`, `AlpsRole`), and the interpreter for a given request only sees transitions within that projection:

- **Per-region ownership is explicit.** In an AND-composite, each parallel region can be assigned to a role. The interpreter for a given request only sees transitions in its role's regions. Region B doesn't appear in role A's interpreter at all — it's not "partial advancement," it's *complete within the role's projection*.
- **Turn-taking is enforced by the protocol, not the interpreter.** The `NotYourTurn` block reason (409) already exists in the guard system. If the MPST derivation says role A has `MustSelect` and role B has `MayPoll` in state X, the guard rejects out-of-turn requests before the interpreter runs.
- **The continuation belongs to the role, not the instance.** Each role's interaction is a Seaside-style session — a linear sequence of operations with suspension points. AND-state fork creates parallel role sessions, each with its own continuation. The synchronization barrier (join) is where role sessions reconverge.

This also clarifies the dual interpreter's job for AC-3. The AND-state tensor product `T1 ⊗ T2` isn't "combine two arbitrary computations" — it's "combine role A's projection of region 1 with role B's projection of region 2." The role structure provides the composition keys.

### Design principle

Two orthogonal concerns, two separate mechanisms:

- **Interpreter** solves *interpretation plurality* (runtime, trace, dual, validation)
- **Actor + roles** solve *concurrent access* (serialized per-instance state mutations, role-projected turn enforcement)

The interpreter should never attempt to manage concurrency, and the actor should never need to know which interpreter is being used.

## Dependencies

- **Current hierarchy-operational worktree** — should land first; this issue builds on whatever shape the runtime takes after that work completes
- #244 (MPST formalism bounds) — AC-3 partially addresses formalism bound 1 (AND-state dual derivation gap) by enabling the dual interpreter to reuse fork semantics
- **`IStateMachineStore` → `IStatechartsStore` rename** (commit a167a6e5) — the interpreter will use the new store interface
