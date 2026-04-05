# v7.4.0 Design Decisions

Open design decisions that must be resolved before implementation. Each decision, if left unresolved, forces the implementer to make a choice the issue doesn't authorize — increasing the risk of incompatible implementations across related issues.

Resolving all decisions raises per-issue session success from ~92% to ~95% and thesis probability from ~87% to ~90% (Track A).

**Status key**: OPEN = needs your answer. RESOLVED = decided during issue refinement.

---

## Blocking Decisions

These must be resolved before implementation begins. Two implementers given the same issue could produce incompatible designs without these answers.

### 1. TransitionAlgebra<'r> exact record shape

**Issue**: [#286](https://github.com/frank-fs/frank/issues/286)
**Status**: RESOLVED

Three sub-decisions on the algebra's record shape, all resolved:

**1a. Is ComputeLCA part of the algebra or a pure utility?**

**Decision**: Option B — LCA is a parameter, not an algebra operation. ComputeLCA is a pure query on `StateHierarchy`, computed once externally and passed to the program. The algebra is a pure effect algebra (Exit, Enter, Fork, Sequence) with no query operations.

**Rationale**: Keeps the algebra clean for tagless final — interpreters only implement effects, not hierarchy queries. Interpreter composition is trivial (no conflicting query implementations). The generator computes LCA at generation time; the program receives it as data. No realistic scenario requires interpreter-specific LCA behavior.

```fsharp
// LCA computed externally, passed to program
let authorizeToFulfilling<'r> (lca: string) (alg: TransitionAlgebra<'r>) : 'r =
    alg.Exit "Authorize"
    |> alg.Sequence (alg.Enter "Fulfilling")
```

**1b. Does Enter handle composite entry recursively, or does the program explicitly call Fork?**

**Decision**: Option B — Explicit Fork in algebra programs. The CE auto-generates algebra programs from `transition` declarations using the known hierarchy, so users never write Fork manually. Customization happens through interpreters, not custom programs (see Decision 4).

**Rationale**: Explicit Fork is correct at the algebra level — the DualAlgebra needs to see Fork to accumulate per-region obligations, and interpreters shouldn't hide behavior. But requiring users to hand-write Fork in CE code is a pit of failure (three ways to get it wrong: forget Fork, wrong children, forget child Enter). The synthesis: the CE knows the hierarchy and auto-generates correct programs with Fork included. The algebra is explicit; the CE is the pit of success.

```fsharp
// User writes this (declarative, pit of success):
transition PlaceOrder Authorize Fulfilling Unrestricted

// CE internally generates the algebra program:
// alg.Exit "Authorize" |> alg.Sequence (alg.Enter "Fulfilling")
//   |> alg.Sequence (alg.Fork ["Pick"; "Pack"; "Ship"])
//   |> alg.Sequence (alg.Enter "Picking") |> ...
```

**1c. Is 'r always unit, or does it vary per interpreter?**

**Decision**: Option B — `'r` varies per interpreter. This is the fundamental property of tagless final: the representation type is abstract, and each interpreter chooses its own `'r`. Programs are polymorphic: `TransitionAlgebra<'r> -> 'r`.

**Rationale**: `'r = unit` for all interpreters defeats the purpose of tagless final. Interpreters forced to accumulate results via closures build implicit continuations — the same stack-safety problem we chose tagless final to avoid (Free Monad's trampoline problem). With varying `'r`, each interpreter controls its result type: `RuntimeAlgebra<HierarchicalTransitionResult>`, `DualAlgebra<DualResult>`, etc. Codegen emits generic functions (`TransitionAlgebra<'r> -> 'r`), which F# handles natively.

---

### 2. ActiveStateConfiguration API surface in Abstractions

**Issue**: [#286](https://github.com/frank-fs/frank/issues/286)
**Status**: RESOLVED

**Decision**: Option B — export only the opaque type. Programs receive `ActiveStateConfiguration` from `RestoreHistory` and pass it through; they never construct or query it directly.

**Rationale**: Consistent with Decision 1c (`'r` varies per interpreter) — programs are effect sequences that thread opaque values. If `ValidationAlgebra` needs `isActive`, it constructs configurations internally as part of its interpreter logic, not via the Abstractions export. Generated code and hand-written `onTransition` programs should not manipulate state — that's the interpreter's job.

---

### 3. DualAlgebra integration with existing Dual.fs

**Issue**: [#288](https://github.com/frank-fs/frank/issues/288)
**Status**: RESOLVED

**Decision**: Option A — Replace `deriveWithHierarchy` entirely. The dual derivation IS a `DualAlgebra` interpreter. Run a program through it, get the result. No wrapping layer, no legacy API preservation.

**Rationale**: Nothing is published — there are no external consumers to protect. Option C's wrapping layer would be pure overhead: an adapter between an API nobody depends on and the algebra that does the work. The existing implementation is incomplete, not just "working if incomplete" — the AND-state gap (MPST formalism bound 1) is a known hole. A clean algebra-native implementation with explicit Fork closes that gap by design. Public types (`DeriveResult`, `ClientObligation`, etc.) should be redesigned around what the algebra naturally produces rather than contorting the algebra to emit legacy types. The 3 MPST formalism bounds are documentation worth preserving; the implementation gets rewritten.

---

### 4. onTransition relationship to existing transition declarations

**Issue**: [#282](https://github.com/frank-fs/frank/issues/282)
**Status**: RESOLVED

**Decision**: Option D (emerged from Decisions 1b and 1c) — `onTransition` does not exist. Every `transition` declaration auto-generates its algebra program from the hierarchy. Customization happens through interpreters, not custom programs.

**Rationale**: The hierarchy fully determines the program — a transition from A to B through a given hierarchy always produces the same Exit/Enter/Fork sequence. That's what a statechart is. If you want different behavior, you write a custom interpreter (tagless final's customization axis), not a custom program. `onTransition` would mean "my statechart definition doesn't match my intended transitions" — that's a bug, not a customization point. One code path in the middleware, no override mechanism, no escape hatch to maintain. The original options (A: paired ops, B: combined op, C: optional override) all assumed `onTransition` exists in some form; none apply.

---

### 5. Codegen file structure

**Issue**: [#283](https://github.com/frank-fs/frank/issues/283)
**Status**: RESOLVED

**Decision**: Option A — one file `OrderStatechart.Generated.fs` containing types (Event, State, Region, Role DUs) and transition programs.

**Rationale**: F# top-to-bottom ordering within a file handles dependencies naturally (types precede programs). MSBuild targets (#284) only need to insert one file before user code. Users who want to split or customize can pull the generated code into their own files. Given Decision 4 (programs auto-generated, no `onTransition`), the generated file is purely derived from the statechart — users never edit it.

---

### 6. childOf reference mechanism

**Issue**: [#293](https://github.com/frank-fs/frank/issues/293)
**Status**: RESOLVED

**Decision**: Option B — value binding. `childOf parentResource` where `parentResource` is the `let` binding of the parent.

**Rationale**: Compiler-checked references for free — typos are compile errors, not analyzer warnings or startup failures. FRANK102 (nonexistent parent reference) becomes unnecessary for the common case. Cross-module references work via standard F# `open`. Refactoring-safe.

Requires overloads accepting both `Resource` (from `resource` CE) and `StatefulResource` (from `statefulResource` CE), since a child stateful resource could be nested under either a plain resource or another stateful resource.

```fsharp
// Parent is a statefulResource
let order = statefulResource "/orders/{orderId}" { ... }

let pick = statefulResource "/orders/{orderId}/pick" {
    childOf order   // accepts StatefulResource
    ...
}

// Parent is a plain resource
let api = resource "/api" { ... }

let orders = statefulResource "/api/orders/{orderId}" {
    childOf api     // accepts Resource
    ...
}
```

---

### 7. How Frank.Statecharts.Analyzers invokes transition programs

**Issue**: [#296](https://github.com/frank-fs/frank/issues/296)
**Status**: RESOLVED

**Decision**: The two paths have different validation triggers but share the same `ValidationAlgebra` interpreter and rules:

**SCXML-first — build-time analysis (Frank.Statecharts.Analyzers):**
- Generated `.fs` files in `obj/` contain algebra programs (`TransitionAlgebra<'r> -> 'r`)
- The FCS-based analyzer invokes them with a `ValidationAlgebra` at build time
- Errors surface as compiler warnings/errors

**CE-first — startup validation (Frank.Statecharts, not the analyzer):**
- The F# type system handles structural correctness at compile time (algebra operations type-check, `'r` unifies, `childOf` is compiler-checked via Decision 6)
- Semantic properties are validated at startup inside `StatefulResourceBuilder.Run()` or during endpoint registration — the hierarchy and `transition` declarations are fully materialized, so auto-generated algebra programs can be run through `ValidationAlgebra` immediately
- If validation fails, the app fails to start (same pattern as missing DI registrations or bad route templates)
- No build-time CE extraction needed — the `transition` declarations ARE the source of truth; extracting them to regenerate what already exists would be circular

**Shared validation rules** (both paths, same `ValidationAlgebra` interpreter):
- Unreachable states (no inbound transitions)
- Guard gaps (event with guards that don't cover all cases)
- AND-state deadlock (regions that can't all complete)
- Missing Fork (AND-composite entered without forking regions)
- Empty projections (role with no agency in any state)

**Rationale**: The analyzer (`Frank.Statecharts.Analyzers`) serves the SCXML-first path. The startup validation pipeline (inside `Frank.Statecharts`) serves the CE-first path. Both use the same `ValidationAlgebra` interpreter and the same rules — the difference is when and where they're invoked, not what they check. The semantic validation rules (#296) must ship in v7.4.0 regardless of which path triggers them.

---

## Tactical Decisions

These can be decided during implementation without cross-issue impact.

### 8. ~~HistoryKind in Abstractions~~ → Abstractions merged into Core

**Issue**: [#286](https://github.com/frank-fs/frank/issues/286)
**Status**: RESOLVED

**Decision**: No separate `Frank.Statecharts.Abstractions` package. Merge algebra types (`TransitionAlgebra<'r>`, `ActiveStateConfiguration`) into `Frank.Statecharts.Core` alongside existing AST types and `HistoryKind`. One zero-dep foundation package.

**Rationale**: Both packages are zero-dep type definitions. Separating them creates a duplicate `HistoryKind` problem (same concept in two packages) and an extra package to maintain. Generated code (#283) references Core — the only cost is transitively including AST types it doesn't use, which has zero runtime impact. Eliminates the original duplication question entirely.

### 9. Instance ID separator for composite route keys

**Issue**: [#293](https://github.com/frank-fs/frank/issues/293)
**Status**: RESOLVED

**Decision**: `::` separator with URL-encoded parameter values. Example: `/tenants/{tenantId}/orders/{orderId}` → instance ID `tenant1::order42`.

**Rationale**: URL-encoding makes collision structurally impossible — `::` in a parameter value becomes `%3A%3A` before joining. `::` is the conventional cons operator in functional languages; F# developers read it naturally.

### 10. 409/403/404 response body format

**Issue**: [#294](https://github.com/frank-fs/frank/issues/294)
**Status**: RESOLVED

**Decision**: RFC 9457 Problem Details. Frank provides the semantic content (`ProblemDetails` objects with type URI, title, detail, status) and writes through ASP.NET Core's `IProblemDetailsService` for formatting and content negotiation.

**Rationale**: Frank's middleware structures error responses as `ProblemDetails` objects. Registration uses `TryAddSingleton` — first-wins, so if the user has already configured their own `IProblemDetailsService` (e.g., with custom `IProblemDetailsWriter` implementations for HTML), Frank defers. This respects "Library, Not Framework" (Constitution 3) and "ASP.NET Core Native" (Constitution 4). Content negotiation (JSON vs HTML vs XML) is ASP.NET Core's responsibility via the Accept header and registered writers — Frank never owns serialization. If no `IProblemDetailsService` is registered, fall back to plain status code with no body.

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
**Status**: RESOLVED

**Decision**: Use the existing `Frank.Cli` dotnet tool (already `PackAsTool = true`) with a tool manifest (`.config/dotnet-tools.json`). No separate `Frank.Statecharts.Tools` package needed. MSBuild targets shipped in the NuGet package run `dotnet tool restore` before invoking `frank extract`, which auto-installs the tool at the pinned version if not present. `frank init` (#155) creates the tool manifest as part of project scaffolding.

**Rationale**: `Frank.Cli` is already a NuGet tool package. Bundling the binary in a separate Tools package would duplicate what already exists. The tool manifest is the idiomatic .NET pattern for version-pinned tool dependencies — `dotnet tool restore` handles auto-install.

### 12. frank init template engine

**Issue**: [#155](https://github.com/frank-fs/frank/issues/155)
**Status**: RESOLVED

**Decision**: Three-layer approach with `dotnet new` for project scaffolding and Fantomas.Core for F# code generation:

1. **`dotnet new frank-app`** — static project template (NuGet-distributed as `Frank.Templates`). Creates project structure, fsproj, Program.fs skeleton, and `.config/dotnet-tools.json` with Frank.Cli pinned. No statechart awareness. `dotnet tool restore` auto-installs frank.
2. **`frank extract <file>.scxml`** — generates `<File>.Generated.fs` (types + algebra programs) using Fantomas.Core (already a project dependency). Independently useful for existing projects.
3. **`frank scaffold`** — discovers `*.Generated.fs` files in the project, generates handler stubs and webHost CE wiring from the generated types. No SCXML input — reads the generated artifact. If no `.Generated.fs` found, alerts: "No generated files found. Run 'frank extract <file>.scxml' first." Uses Fantomas.Core. Independently useful to regenerate after statechart changes.
4. **`frank init <file>.scxml`** — convenience wrapper that runs `extract` then `scaffold` in sequence.

**Rationale**: Each command is independently useful and composable. `scaffold` reads from the generated artifact, not the source SCXML — simpler command and teaches the correct workflow through error messages. `dotnet new` stays in its lane (static project skeleton). Fantomas.Core handles all F# code generation with proper formatting — no raw string replacement. The `dotnet new` template naturally sets up the tool manifest (Decision 11), so `frank` is available by the time the user needs it.

### 13. Generated module naming conflicts

**Issue**: [#283](https://github.com/frank-fs/frank/issues/283)
**Status**: RESOLVED

**Decision**: Error at generation time. If `frank extract` cannot disambiguate module names (e.g., two input files produce the same PascalCased module name), it alerts the caller with a clear message. The caller then refines the inputs (SCXML, ALPS, etc.) to resolve the conflict.

**Rationale**: Fail early with actionable guidance rather than producing broken code. The caller owns the input naming — Frank shouldn't guess.

### 14. ALPS validator scope

**Issue**: [#302](https://github.com/frank-fs/frank/issues/302)
**Status**: RESOLVED

**Decision**: Semantic consistency only — rt targets, cross-links, type matching. Structural validation (required fields, correct types, unknown keys) is the parser's responsibility.

**Rationale**: Clean split with no gap. The parser ensures structure; the validator ensures meaning. Duplicating structural checks in the validator adds no value.

### 15. CollectorAlgebra reconstruction owner

**Issue**: [#290](https://github.com/frank-fs/frank/issues/290)
**Status**: RESOLVED

**Decision**: `CollectorAlgebra` lives in `Frank.Statecharts.Core` (alongside `TransitionAlgebra<'r>` and `StatechartDocument`, per Decision 8). The reconstruction pipeline (collected operations + metadata → `StatechartDocument`) lives in `Frank.Cli.Core` (near FormatPipeline.fs).

**Rationale**: `CollectorAlgebra` is a pure interpreter of `TransitionAlgebra<'r>` with no ASP.NET Core or runtime dependencies — it belongs with the types it interprets (Core). Reconstruction is a CLI/build-time concern that orchestrates the collector output into a document format.

---

## Decision Dependencies

Decisions that must be resolved together (changing one affects the other):

- **1a + 1b + 5** *(1a, 1b RESOLVED)*: LCA is a parameter (1a=B), Fork is explicit (1b=B). Generated files (5) emit programs that receive LCA as a parameter and include explicit Fork calls. The CE auto-generates these programs from `transition` declarations. Decision 5 can now be resolved independently.
- **1c + 4** *(1c RESOLVED)*: `'r` varies per interpreter (1c=B). Programs are `TransitionAlgebra<'r> -> 'r`. Decision 4 (onTransition relationship) is reshaped: `onTransition` is the escape hatch for custom logic; standard transitions auto-generate algebra programs from the `transition` declaration. The middleware uses the algebra path when a program exists (auto-generated or custom), falls back to `HierarchicalRuntime.transition` otherwise.
- **6 + FRANK102**: If childOf uses value binding (6=B), FRANK102 (nonexistent parent reference) becomes largely unnecessary — the compiler catches it. The analyzer rule can be simplified or removed.
- **7 + #296 scope**: Frank.Statecharts.Analyzers invokes transition programs through the algebra — same mechanism for both CE-first and SCXML-first paths. Both produce functions; the analyzer calls them with validation/collector algebras.
