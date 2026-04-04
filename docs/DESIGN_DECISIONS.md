# v7.4.0 Design Decisions

Open design decisions that must be resolved before implementation. Each decision, if left unresolved, forces the implementer to make a choice the issue doesn't authorize — increasing the risk of incompatible implementations across related issues.

Resolving all decisions raises per-issue session success from ~92% to ~95% and thesis probability from ~87% to ~90% (Track A).

**Status key**: OPEN = needs your answer. RESOLVED = decided during issue refinement.

---

## Blocking Decisions

These must be resolved before implementation begins. Two implementers given the same issue could produce incompatible designs without these answers.

### 1. TransitionAlgebra<'r> exact record shape

**Issue**: [#286](https://github.com/frank-fs/frank/issues/286)  
**Status**: OPEN

The issue shows an illustrative record shape and says "the shape will evolve during implementation." Three specific questions:

**1a. Is ComputeLCA part of the algebra or a pure utility?**

ComputeLCA doesn't mutate state — it's a query on StateHierarchy, not an operation. Should it be:

- **Option A**: An algebra field (`ComputeLCA: string * string -> string option`). Pro: programs are self-contained. Con: the algebra is a mix of queries and mutations.
- **Option B**: A standalone function on StateHierarchy, called before the algebra. Programs receive the LCA as a parameter. Pro: cleaner separation. Con: programs need extra context.

```fsharp
// Option A: LCA in the algebra
let authorizeToFulfilling (alg: TransitionAlgebra<unit>) =
    let _lca = alg.ComputeLCA("Authorize", "Fulfilling")
    alg.Exit "Authorize"
    alg.Enter "Fulfilling"

// Option B: LCA as external parameter
let authorizeToFulfilling (lca: string) (alg: TransitionAlgebra<unit>) =
    alg.Exit "Authorize"
    alg.Enter "Fulfilling"
```

**Recommendation**: Option A. Keeps programs self-contained, which matters for code generation (#283) — the generator doesn't need to know about external LCA computation.

**1b. Does Enter handle composite entry recursively, or does the program explicitly call Fork?**

When entering an AND-composite, the runtime must activate all child regions. Is this:

- **Option A**: Implicit — `alg.Enter "Fulfilling"` detects it's an AND-composite and internally calls Fork. Pro: programs are simpler. Con: the algebra hides behavior.
- **Option B**: Explicit — the program calls `alg.Enter "Fulfilling"` then `alg.Fork ["Pick"; "Pack"; "Ship"]`. Pro: programs are transparent. Con: programs must know the hierarchy structure.

```fsharp
// Option A: Enter handles Fork internally
let authorizeToFulfilling (alg: TransitionAlgebra<unit>) =
    alg.Exit "Authorize"
    alg.Enter "Fulfilling"  // internally forks Pick, Pack, Ship

// Option B: Program explicitly forks
let authorizeToFulfilling (alg: TransitionAlgebra<unit>) =
    alg.Exit "Authorize"
    alg.Enter "Fulfilling"
    alg.Fork ["Pick"; "Pack"; "Ship"]
    alg.Enter "Picking"
    alg.Enter "Packing"
    alg.Enter "Shipping"
```

**Recommendation**: Option B. Explicit Fork makes the AND-state semantics visible in the program, which is critical for the DualAlgebra (#288) — it needs to see Fork to accumulate per-region obligations. Option A would hide Fork inside Enter, making the DualAlgebra's job harder.

**1c. Is 'r always unit, or does it vary per interpreter?**

- **Option A**: `TransitionAlgebra<unit>` for all interpreters. Interpreters accumulate results via closure state (TraceAlgebra accumulates ExitedStates in a ResizeArray; DualAlgebra accumulates obligations). Pro: one program type for all algebras. Con: results are side effects, not return values.
- **Option B**: `'r` varies — `TransitionAlgebra<HierarchicalTransitionResult>` for runtime, `TransitionAlgebra<DualResult>` for dual. Pro: results are explicit. Con: programs are parameterized by result type, complicating code generation.

**Recommendation**: Option A. All algebras use `unit`. The accumulator-via-closure pattern is already established in the refinement comments on #257 and matches TraceAlgebra precedent. Code generation (#283) emits `TransitionAlgebra<unit> -> unit` functions.

---

### 2. ActiveStateConfiguration API surface in Abstractions

**Issue**: [#286](https://github.com/frank-fs/frank/issues/286)  
**Status**: OPEN

ActiveStateConfiguration must be in `Frank.Statecharts.Abstractions` because `RestoreHistory` returns `ActiveStateConfiguration option`. But how much of its API?

Currently ActiveStateConfiguration has: `add`, `remove`, `isActive`, `toSet`, `empty`, `fromSet`.

- **Option A**: Export the full module. Pro: generated code can manipulate configurations. Con: breaks encapsulation, generated code shouldn't be manipulating state directly.
- **Option B**: Export only the opaque type. Pro: generated code can only pass it through (receive from RestoreHistory, hand to runtime). Con: if any algebra operation needs to construct or query a configuration, it can't.
- **Option C**: Export the type + `empty` + `isActive` (read-only query). Pro: validation can check state membership. Con: still partially exposed.

**Recommendation**: Option B. Generated transition programs should not manipulate ActiveStateConfiguration — that's the runtime's job. Programs call `RestoreHistory`, get back an `option`, and the runtime decides what to do with it. If ValidationAlgebra needs `isActive`, it constructs the configuration internally, not via the Abstractions package.

---

### 3. DualAlgebra integration with existing Dual.fs

**Issue**: [#288](https://github.com/frank-fs/frank/issues/288)  
**Status**: OPEN

Dual.fs is 35KB with complex dual derivation logic. How does DualAlgebra relate to it?

- **Option A**: Replace `deriveWithHierarchy` entirely. DualAlgebra IS the dual derivation. Pro: one code path. Con: high risk, massive refactor of working (if incomplete) code.
- **Option B**: Compose alongside — DualAlgebra is an alternative dual derivation path for programs expressed against the algebra. `deriveWithHierarchy` remains for backward compatibility. Pro: incremental. Con: two code paths.
- **Option C**: Wrap — `deriveWithHierarchy` internally runs programs through DualAlgebra. The existing API surface doesn't change; the implementation becomes algebra-based. Pro: safest migration. Con: requires expressing existing Dual.fs logic as algebra programs.

**Recommendation**: Option C for v7.4.0. Wrap the existing API with algebra internals. The public interface (`DeriveResult`, `ClientObligation`, etc.) stays the same. The implementation changes from ad-hoc hierarchy traversal to algebra interpretation. This is the lowest-risk path that still closes the AND-state gap (because Fork is now visible to the dual logic).

---

### 4. onTransition relationship to existing transition declarations

**Issue**: [#282](https://github.com/frank-fs/frank/issues/282)  
**Status**: OPEN

Today: `transition PlaceOrder Pending Authorize Unrestricted` declares metadata (source, target, constraint, safety).
Proposed: `onTransition PlaceOrder authorizeToFulfilling` registers the algebra program.

- **Option A**: Two separate CE operations. `transition` declares metadata; `onTransition` declares the program. Must match by event name. Pro: backward compatible, metadata and program are orthogonal. Con: easy to declare one without the other.
- **Option B**: Combined operation: `transition PlaceOrder Pending Authorize Unrestricted authorizeToFulfilling`. Pro: can't have metadata without program. Con: breaks existing API, all samples must change.
- **Option C**: `onTransition` is optional. If present, the middleware uses the algebra program. If absent, the middleware falls back to `HierarchicalRuntime.transition` (existing behavior). Pro: fully backward compatible, opt-in. Con: two code paths in middleware.

**Recommendation**: Option C. The algebra is an opt-in layer. Resources that don't declare `onTransition` work exactly as they do today. Resources that do declare it get the algebra path. This matches the layered architecture — the thesis can be proven without the algebra (Layer 3), and the algebra (Layer 4) strengthens guarantees for resources that opt in.

---

### 5. Codegen file structure

**Issue**: [#283](https://github.com/frank-fs/frank/issues/283)  
**Status**: OPEN

For `OrderStatechart.scxml`, what files does `frank-cli extract --format fsharp` generate?

- **Option A**: One file `OrderStatechart.Generated.fs` containing types (Event, State, Region, Role DUs) and transition programs. Pro: simple, one file to manage. Con: large generated file, types and programs mixed.
- **Option B**: Two files `OrderStatechart.Types.fs` + `OrderStatechart.Programs.fs`. Types first in compilation order. Pro: clean separation. Con: MSBuild targets must order both correctly.
- **Option C**: One file per DU type + one for programs. Pro: granular. Con: many files, complex ordering.

**Recommendation**: Option A. One file. The generated module is `OrderStatechart.Generated` with types at the top and programs below — F# compilation order within a file is top-to-bottom, so types naturally precede programs. The MSBuild targets (#284) only need to insert one file before user code, not manage ordering of multiple generated files.

---

### 6. childOf reference mechanism

**Issue**: [#293](https://github.com/frank-fs/frank/issues/293)  
**Status**: OPEN

How does the child resource reference its parent?

- **Option A**: By string name — `childOf "order"` matches `name "order"` on the parent statefulResource. Pro: simple, works at compile time (string literal). Con: stringly-typed, typos caught only by analyzer (FRANK102) or at startup.
- **Option B**: By value binding — `childOf orderResource` where `orderResource` is the `let` binding of the parent. Pro: compiler-checked, refactoring-safe. Con: requires the parent to be in scope; cross-module references need explicit imports.
- **Option C**: By route template — `childOf "/orders/{orderId}"` matches the parent's route. Pro: unambiguous. Con: verbose, route templates are implementation details.

**Recommendation**: Option B. Value binding gives compiler-checked references for free. FRANK102 becomes unnecessary for the common case (typos are compile errors, not analyzer warnings). Cross-module references work via standard F# `open` — no special resolution needed.

```fsharp
// Option B example
let order = statefulResource "/orders/{orderId}" { ... }

let pick = statefulResource "/orders/{orderId}/pick" {
    childOf order   // compiler-checked reference
    ...
}
```

---

### 7. How Frank.Statecharts.Analyzers invokes transition programs

**Issue**: [#296](https://github.com/frank-fs/frank/issues/296)  
**Status**: OPEN

The analyzer has FCS typed trees (source code), not a running application. How does it "run" transition programs through validation interpreters?

Both the CE-first and SCXML-first paths produce the same thing: **a set of structured instructions** (algebra operations). The paths converge at the algebra:

```
CE-first:     F# code → run through algebra → instructions → generators/analyzers
SCXML-first:  SCXML → parse → generate F# → run through algebra → instructions → generators/analyzers
```

The CE-first path is simpler — the program already exists as runnable F# code. Run it through a ValidationAlgebra or CollectorAlgebra and you have the instruction set. No parsing, no reconstruction.

The SCXML-first path is harder — it requires parsing a design document, translating it into F# code, then running that code through the algebra. The translation step (#283) is where complexity lives.

For the analyzer, this means:

- **CE-first programs**: Run through ValidationAlgebra at analysis time. The program is a function `TransitionAlgebra<unit> -> unit` — call it with a validation algebra, read the results. The FCS typed tree gives you the function binding; invoking it with a stub algebra is straightforward.
- **SCXML-first programs**: The generated `.fs` files in `obj/` contain the same functions. The analyzer can either invoke them (same as CE-first) or parse the generated file structurally (the template is predictable).

**Recommendation**: The analyzer invokes transition programs through the algebra — same mechanism for both paths. The program is a function; the analyzer calls it with a validation/collector algebra and reads the accumulated results. This is the simplest approach and works for both CE-first and SCXML-first.

#296's rules (unreachable states, guard gaps, empty projections, AND-state deadlock) are the compile-time safeguards against the v7.3.0 failure pattern. They must ship in v7.4.0.

---

## Tactical Decisions

These can be decided during implementation without cross-issue impact.

### 8. HistoryKind in Abstractions

**Issue**: [#286](https://github.com/frank-fs/frank/issues/286)

Duplicate the 3-case DU from `Frank.Statecharts.Ast`, or reference it?

**Recommendation**: Duplicate. It's 3 lines. Avoids a dependency on Frank.Statecharts.Core from Abstractions.

```fsharp
type HistoryKind = Shallow | Deep
```

(Ast.HistoryKind has 3 cases but the third may be implementation-specific. Verify before duplicating.)

### 9. Instance ID separator for composite route keys

**Issue**: [#293](https://github.com/frank-fs/frank/issues/293)

`::` was proposed for joining multiple route parameter values. Risk: a parameter value could contain `::`.

**Recommendation**: URL-encode parameter values before joining. `::` is safe as separator because `%3A%3A` won't appear in URL-encoded values. Example: `/tenants/{tenantId}/orders/{orderId}` → instance ID `tenant1::order42`.

### 10. 409/403/404 response body format

**Issue**: [#294](https://github.com/frank-fs/frank/issues/294)

**Recommendation**: RFC 9457 Problem Details (`application/problem+json`). Consistent with ASP.NET Core's built-in problem details middleware. Example:

```json
{
  "type": "https://frank-web.dev/problems/region-not-active",
  "title": "Region not active",
  "status": 409,
  "detail": "The order must be in Fulfilling state before pick operations are available."
}
```

### 11. frank-cli distribution for MSBuild

**Issue**: [#284](https://github.com/frank-fs/frank/issues/284)

**Recommendation**: Bundle the CLI binary inside the Frank.Statecharts.Tools NuGet package (same pattern as FsGrpc.Tools). No separate tool install required. The MSBuild targets invoke the bundled binary directly. This eliminates the chicken-and-egg problem for #155 (frank init) — installing the NuGet package gets both the targets and the CLI.

### 12. frank init template engine

**Issue**: [#155](https://github.com/frank-fs/frank/issues/155)

**Recommendation**: Raw string replacement with `%PLACEHOLDER%` markers. F# file templates are checked into the frank-cli project as embedded resources. No external template engine dependency. The placeholders are: `%PROJECT_NAME%`, `%ROOT_NAMESPACE%`, `%SCXML_FILE%`, `%RESOURCE_NAME%`.

### 13. Generated module naming conflicts

**Issue**: [#283](https://github.com/frank-fs/frank/issues/283)

**Recommendation**: Error at generation time. If two SCXML files produce the same PascalCased module name, `frank-cli extract` fails with: `"Naming conflict: both 'OrderStatechart.scxml' and 'Order-Statechart.scxml' produce module name 'OrderStatechart.Generated'. Rename one of the SCXML files."`

### 14. ALPS validator scope

**Issue**: [#302](https://github.com/frank-fs/frank/issues/302)

**Recommendation**: Semantic consistency only (rt targets, cross-links, type matching). JSON schema validation is a separate concern — the ALPS spec doesn't have a formal JSON Schema, and structural validation is better handled by the parser.

### 15. CollectorAlgebra reconstruction owner

**Issue**: [#290](https://github.com/frank-fs/frank/issues/290)

**Recommendation**: The CollectorAlgebra itself lives in `Frank.Statecharts` (near the other algebras). The reconstruction pipeline (collected edges + metadata → StatechartDocument) lives in `Frank.Cli.Core` (near FormatPipeline.fs). The algebra is runtime-available; the reconstruction is a CLI/build-time concern.

---

## Decision Dependencies

Decisions that must be resolved together (changing one affects the other):

- **1a + 1b + 5**: If ComputeLCA is in the algebra (1a=A) and Fork is explicit (1b=B), the generated file (5) must emit both ComputeLCA calls and Fork calls. All three affect what generated code looks like.
- **1c + 4**: If 'r is always unit (1c=A) and onTransition is optional (4=C), the middleware needs to know whether to use the algebra path or the fallback path based on whether `onTransition` was declared.
- **6 + FRANK102**: If childOf uses value binding (6=B), FRANK102 (nonexistent parent reference) becomes largely unnecessary — the compiler catches it. The analyzer rule can be simplified or removed.
- **7 + #296 scope**: Frank.Statecharts.Analyzers invokes transition programs through the algebra — same mechanism for both CE-first and SCXML-first paths. Both produce functions; the analyzer calls them with validation/collector algebras.
