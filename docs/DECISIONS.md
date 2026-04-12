# Frank Decision Log

Consolidated record of design decisions across the project. Decisions are extracted from design documents, GitHub issues, PR descriptions, and spec files.

**Status key**: Active = current and valid. Superseded = replaced by a later decision. Suspect = may have been influenced by flat-semantics assumptions (review in Phase 2).

---

## v7.4.0 Algebra and Interpreter Decisions

Extracted from [DESIGN_DECISIONS.md](DESIGN_DECISIONS.md). All resolved during issue refinement for the v7.4.0 milestone.

### D-001: LCA is a parameter, not an algebra operation

- **Source**: [#286](https://github.com/frank-fs/frank/issues/286), DESIGN_DECISIONS.md §1a
- **Status**: Active
- **Decision**: `ComputeLCA` is a pure query on `StateHierarchy`, computed once externally and passed to the program. The algebra is a pure effect algebra (Exit, Enter, Fork, Sequence) with no query operations.
- **Rationale**: Keeps the algebra clean for tagless final — interpreters only implement effects, not hierarchy queries. Interpreter composition is trivial.

### D-002: Explicit Fork in algebra programs

- **Source**: [#286](https://github.com/frank-fs/frank/issues/286), DESIGN_DECISIONS.md §1b
- **Status**: Active
- **Decision**: The CE auto-generates algebra programs from `transition` declarations using the known hierarchy, so users never write Fork manually. Fork is explicit at the algebra level — the DualAlgebra needs to see Fork to accumulate per-region obligations.
- **Rationale**: Explicit Fork is correct at the algebra level. The CE knows the hierarchy and auto-generates correct programs with Fork included. The algebra is explicit; the CE is the pit of success.

### D-003: 'r varies per interpreter (tagless final)

- **Source**: [#286](https://github.com/frank-fs/frank/issues/286), DESIGN_DECISIONS.md §1c
- **Status**: Active
- **Decision**: `'r` varies per interpreter. Programs are polymorphic: `TransitionAlgebra<'r> -> 'r`. Each interpreter chooses its own `'r`.
- **Rationale**: `'r = unit` for all interpreters defeats the purpose of tagless final. Varying `'r` means each interpreter controls its result type.

### D-004: ActiveStateConfiguration is opaque

- **Source**: [#286](https://github.com/frank-fs/frank/issues/286), DESIGN_DECISIONS.md §2
- **Status**: Active
- **Decision**: Export only the opaque type. Programs receive `ActiveStateConfiguration` from `RestoreHistory` and pass it through; they never construct or query it directly.
- **Rationale**: Programs are effect sequences that thread opaque values. Interpreters manipulate state, not programs.

### D-005: DualAlgebra replaces deriveWithHierarchy entirely

- **Source**: [#288](https://github.com/frank-fs/frank/issues/288), DESIGN_DECISIONS.md §3
- **Status**: Active
- **Decision**: Replace `deriveWithHierarchy` entirely. The dual derivation IS a `DualAlgebra` interpreter. No wrapping layer, no legacy API preservation.
- **Rationale**: Nothing is published. The AND-state gap is a known hole. A clean algebra-native implementation with explicit Fork closes that gap by design.

### D-006: onTransition does not exist

- **Source**: [#282](https://github.com/frank-fs/frank/issues/282), DESIGN_DECISIONS.md §4
- **Status**: Active
- **Decision**: Every `transition` declaration auto-generates its algebra program from the hierarchy. Customization happens through interpreters, not custom programs.
- **Rationale**: The hierarchy fully determines the program. If you want different behavior, write a custom interpreter (tagless final's customization axis), not a custom program.

### D-007: Single generated file per statechart

- **Source**: [#283](https://github.com/frank-fs/frank/issues/283), DESIGN_DECISIONS.md §5
- **Status**: Active
- **Decision**: One file `OrderStatechart.Generated.fs` containing types (Event, State, Region, Role DUs) and transition programs.
- **Rationale**: F# top-to-bottom ordering handles dependencies naturally. MSBuild targets only need to insert one file.

### D-008: childOf uses value binding

- **Source**: [#293](https://github.com/frank-fs/frank/issues/293), DESIGN_DECISIONS.md §6
- **Status**: Active
- **Decision**: `childOf parentResource` where `parentResource` is the `let` binding of the parent. Compiler-checked references.
- **Rationale**: Typos are compile errors, not analyzer warnings or startup failures. Requires overloads accepting both `Resource` and `StatefulResource`.

### D-009: Two-path validation (build-time + startup)

- **Source**: [#296](https://github.com/frank-fs/frank/issues/296), DESIGN_DECISIONS.md §7
- **Status**: Active
- **Decision**: SCXML-first gets build-time analysis via FCS-based analyzer. CE-first gets startup validation in `StatefulResourceBuilder.Run()`. Both use the same `ValidationAlgebra` interpreter and rules.
- **Rationale**: Each path has different validation triggers but shared rules. The analyzer serves SCXML-first; startup validation serves CE-first.

### D-010: Algebra types in Frank.Statecharts.Core

- **Source**: [#286](https://github.com/frank-fs/frank/issues/286), DESIGN_DECISIONS.md §8
- **Status**: Active
- **Decision**: No separate `Frank.Statecharts.Abstractions` package. Merge algebra types into `Frank.Statecharts.Core` alongside existing AST types. One zero-dep foundation package.
- **Rationale**: Both packages are zero-dep type definitions. Separating them creates a duplicate `HistoryKind` problem.

### D-011: Instance ID uses :: separator

- **Source**: [#293](https://github.com/frank-fs/frank/issues/293), DESIGN_DECISIONS.md §9
- **Status**: Active
- **Decision**: `::` separator with URL-encoded parameter values. Example: `tenant1::order42`.
- **Rationale**: URL-encoding makes collision structurally impossible. `::` is the conventional cons operator in functional languages.

### D-012: RFC 9457 Problem Details for error responses

- **Source**: [#294](https://github.com/frank-fs/frank/issues/294), DESIGN_DECISIONS.md §10
- **Status**: Active
- **Decision**: Frank provides `ProblemDetails` objects and writes through ASP.NET Core's `IProblemDetailsService`. Uses `TryAddSingleton` (first-wins).
- **Rationale**: Respects "Library, Not Framework" and "ASP.NET Core Native" constitution rules. Content negotiation is ASP.NET Core's responsibility.

### D-013: frank-cli distributed via existing dotnet tool

- **Source**: [#284](https://github.com/frank-fs/frank/issues/284), DESIGN_DECISIONS.md §11
- **Status**: Active
- **Decision**: Use existing `Frank.Cli` dotnet tool with a tool manifest. No separate Tools package. MSBuild targets run `dotnet tool restore` before invoking `frank extract`.
- **Rationale**: `Frank.Cli` is already a NuGet tool package. The tool manifest is the idiomatic .NET pattern.

### D-014: frank init uses three-layer approach

- **Source**: [#155](https://github.com/frank-fs/frank/issues/155), DESIGN_DECISIONS.md §12
- **Status**: Active
- **Decision**: `dotnet new frank-app` (static template) + `frank extract` (SCXML → F#) + `frank scaffold` (generated types → handler stubs). `frank init` is a convenience wrapper.
- **Rationale**: Each command is independently useful and composable. `scaffold` reads from the generated artifact, not SCXML directly.

### D-015: Generated module naming conflicts are errors

- **Source**: [#283](https://github.com/frank-fs/frank/issues/283), DESIGN_DECISIONS.md §13
- **Status**: Active
- **Decision**: Error at generation time if module names cannot be disambiguated. The caller refines inputs to resolve.
- **Rationale**: Fail early with actionable guidance rather than producing broken code.

### D-016: ALPS validator is semantic only

- **Source**: [#302](https://github.com/frank-fs/frank/issues/302), DESIGN_DECISIONS.md §14
- **Status**: Active
- **Decision**: Semantic consistency only (rt targets, cross-links, type matching). Structural validation is the parser's responsibility.
- **Rationale**: Clean split with no gap. Parser ensures structure; validator ensures meaning.

### D-017: CollectorAlgebra in Core, reconstruction in CLI

- **Source**: [#290](https://github.com/frank-fs/frank/issues/290), DESIGN_DECISIONS.md §15
- **Status**: Active
- **Decision**: `CollectorAlgebra` lives in `Frank.Statecharts.Core`. The reconstruction pipeline lives in `Frank.Cli.Core`.
- **Rationale**: `CollectorAlgebra` is a pure interpreter of `TransitionAlgebra<'r>` with no runtime dependencies.

---

## Earlier Architectural Decisions

### D-018: Resource-oriented design (Constitution)

- **Source**: CLAUDE.md Constitution §1
- **Status**: Active
- **Decision**: Resources are the primary abstraction, not URL patterns with handlers. The `resource` CE is the central API. Hypermedia over static specs.
- **Rationale**: Founding principle. All other decisions flow from this.

### D-019: Library, not framework (Constitution)

- **Source**: CLAUDE.md Constitution §3
- **Status**: Active
- **Decision**: No view engine, no ORM, no auth system. Compose with ASP.NET Core, don't replace it.
- **Rationale**: Founding principle. Minimizes lock-in, maximizes composability.

### D-020: No lightweight API (CE is the design)

- **Source**: CLAUDE.md, feedback memory
- **Status**: Active
- **Decision**: Never suggest a simplified `frank.get "/path" handler` alternative. The CE ceremony IS the pit of success. On-ramp is solved by docs/examples, not by reducing the abstraction.
- **Rationale**: The CE structure encodes the design decisions that make resources self-describing.

### D-021: Trunk-based development

- **Source**: CLAUDE.md Workflow Rules
- **Status**: Active
- **Decision**: Commit directly to master. Use worktrees for multi-commit features. Small, targeted changes go straight to master.
- **Rationale**: Simplifies branching strategy for a single-developer project with CI.

---

## Suspect Decisions (Review in Phase 2)

*These decisions will be extracted from the 11 suspect kitty-specs during the Phase 2 audit. They may have been influenced by flat-semantics assumptions or shortcuts disguised as design decisions.*

**Specs to mine**: kitty-specs 006, 011, 013, 018, 020, 021, 022, 023, 024, 026, 030

---

## Decision Dependencies

From DESIGN_DECISIONS.md — decisions that must be resolved together:

- **D-001 + D-002 + D-007**: LCA is a parameter (D-001), Fork is explicit (D-002). Generated files (D-007) emit programs that receive LCA as a parameter and include explicit Fork calls.
- **D-003 + D-006**: `'r` varies per interpreter (D-003). Programs are `TransitionAlgebra<'r> -> 'r`. `onTransition` doesn't exist (D-006) — customization is through interpreters.
- **D-008 + analyzer rules**: If childOf uses value binding (D-008), FRANK102 (nonexistent parent reference) becomes largely unnecessary.
- **D-009 + #296 scope**: Both paths use the same `ValidationAlgebra` interpreter and rules.
