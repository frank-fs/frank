# Research: smcat Parser and Generator

**Feature**: 013-smcat-parser-generator
**Date**: 2026-03-15

## R-001: smcat Grammar and Syntax Rules

**Decision**: Implement a hand-written recursive-descent parser (not a parser generator) following the WSD pattern.

**Rationale**: The WSD parser in `src/Frank.Statecharts/Wsd/` uses a hand-written lexer and recursive-descent parser with mutable state. This approach is well-established in the codebase, provides precise error recovery and position tracking, and avoids external parser generator dependencies. smcat's grammar is regular enough for this approach (no ambiguous productions, clear statement terminators).

**Alternatives considered**:
- FParsec: Would add an external dependency (violates constitution III -- minimize dependencies) and makes structured error recovery harder
- Parser generator (ANTLR, etc.): Heavy external tooling, C# interop complexity, overkill for smcat's simple grammar
- FsLexYacc: F#-native but adds build-time code generation complexity

## R-002: smcat Pseudo-State Detection by Naming Convention

**Decision**: Detect pseudo-states by examining state name strings using the conventions from the state-machine-cat project.

**Rationale**: smcat does not use explicit type annotations for pseudo-states. Instead, it relies on naming conventions:
- Names containing `initial` -> `StateType.Initial`
- Names containing `final` -> `StateType.Final`
- Names containing `history` -> `StateType.ShallowHistory` (or `DeepHistory` if name contains `deep.history`)
- Names starting with `^` -> `StateType.Choice`
- Names starting with `]` -> `StateType.ForkJoin`
- Names containing `terminate` -> `StateType.Terminate`

This is documented in the smcat/state-machine-cat project and specified in FR-004.

**Alternatives considered**:
- Explicit type attributes (`[type=initial]`): smcat does support a `type` attribute on states, but the naming convention is the primary mechanism; attribute-based detection should be supported as a secondary signal
- Hard-coded state name list: Too brittle; convention-based matching is more flexible

## R-003: Transition Label Grammar

**Decision**: Parse transition labels with the grammar: `event [guard] / action` where each component is optional.

**Rationale**: The label appears after `: ` in a transition line. The grammar is:
```
label        ::= event? guard? action?
event        ::= text_until_bracket_or_slash
guard        ::= '[' text_until_closing_bracket ']'
action       ::= '/' text_to_end
```

Key edge cases:
- Label with only event: `source => target: start;`
- Label with only guard: `source => target: [isReady];`
- Label with only action: `source => target: / doSomething;`
- Label with event + guard: `source => target: start [isReady];`
- Label with event + action: `source => target: start / doSomething;`
- Label with all three: `source => target: start [isReady] / doSomething;`
- Empty label (just colon): `source => target:;`

This warrants a dedicated `LabelParser` module (analogous to `Wsd/GuardParser.fs`).

**Alternatives considered**:
- Inline label parsing in main parser: Would make the parser harder to test and the label grammar harder to reason about in isolation
- Regex-based parsing: Fragile with nested brackets or escaped characters

## R-004: Composite State Parsing (Recursive Nesting)

**Decision**: Parse composite states recursively, treating the `{ ... }` block as a nested `SmcatDocument`.

**Rationale**: smcat composite states contain full nested state machines:
```
parent {
  child1 => child2: event;
};
```

The parser will recursively call the top-level document parser when it encounters `{`, collecting elements until the matching `}`. The AST represents this as `SmcatState` with a `Children: SmcatDocument option` field. This mirrors the WSD parser's recursive `parseGroup`/`parseBranchBody` pattern.

Spec requires 5+ levels of nesting without stack overflow (SC-004). The recursive-descent approach handles this naturally; a depth counter with a warning at 50+ levels (matching WSD) prevents true runaway recursion.

**Alternatives considered**:
- Iterative parsing with explicit stack: More complex, no benefit for realistic nesting depths
- Flattened representation with parent references: Loses structural fidelity, complicates generation

## R-005: Attribute Parsing

**Decision**: Parse state and transition attributes as ordered key-value pairs from `[key=value key2=value2]` syntax.

**Rationale**: smcat supports attributes on both states and transitions in square brackets. Attribute values may be quoted strings. Known attribute keys for states: `type`, `label`, `color`, `active`, `class`. Known attribute keys for transitions: `color`, `width`, `type`, `class`. The parser treats attributes as opaque key-value pairs (no semantic validation of keys) to be forward-compatible with future smcat extensions.

**Important**: The attribute `[type=initial]` provides an explicit pseudo-state classification that overrides or confirms the naming convention detection (R-002).

**Alternatives considered**:
- Typed attribute DU with known keys: Too rigid; smcat may add new attributes
- Map<string, string>: Loses ordering; spec says "ordered key-value pairs"

## R-006: SourcePosition Duplication with WSD

**Decision**: Define `SourcePosition` in `Smcat/Types.fs` as a temporary duplicate of `Wsd/Types.fs.SourcePosition`. When spec 020 (Shared Statechart AST) lands, both will be replaced by the shared `SourcePosition`.

**Rationale**: The WSD parser defines `[<Struct>] type SourcePosition = { Line: int; Column: int }` in its own types module. The smcat parser needs the same type. Constitution VIII (No Duplicated Logic) flags this, but:
1. Both types are identical two-field structs
2. They live in different `internal` modules (no public API surface)
3. Spec 020 will unify them into `Frank.Statecharts.Ast.SourcePosition`
4. Extracting a shared module now creates a premature dependency that spec 020 is designed to resolve

This is tracked as a WATCH item in the Constitution Check and will be resolved by spec 020.

**Alternatives considered**:
- Extract shared SourcePosition now: Premature; spec 020 will define the canonical location
- Import WSD's SourcePosition: Cross-format dependency; smcat should not depend on WSD types

## R-007: ParseFailure/ParseWarning/ParseResult Duplication

**Decision**: Define smcat-specific `ParseFailure`, `ParseWarning`, and `ParseResult` types that mirror the WSD versions. Same rationale as R-006 -- spec 020 will unify them.

**Rationale**: The WSD parser defines these in `Wsd/Types.fs`. The smcat parser needs identical types. Short-lived duplication is acceptable because:
1. Both are simple record types with identical fields
2. Spec 020 defines the shared `ParseResult<StatechartDocument>` that will replace both
3. The smcat `ParseResult` wraps `SmcatDocument` (not `Diagram`), so the generic parameter differs

## R-008: Generator Output Format

**Decision**: Generate smcat text with one statement per line, semicolon terminators, and `initial =>` as the first transition when an initial state exists.

**Rationale**: The generator must produce valid smcat that re-parses to a semantically equivalent AST (SC-005). Output conventions:
- State declarations listed first (if explicit declarations are needed)
- `initial => <first_state>;` as the first transition line
- Each transition on its own line: `source => target: event [guard] / action;`
- Final state transitions: `<source> => final;`
- No trailing commas (semicolons only for consistency)
- 2-space indentation for composite state contents

**Alternatives considered**:
- Comma-separated statements: Valid smcat but less readable
- Minimal output (no explicit state declarations): Acceptable; states implied by transitions is valid smcat

## R-009: Mapper Design (Blocked on Spec 020)

**Decision**: The mapper module (`Smcat/Mapper.fs`) converts `SmcatDocument` to `StatechartDocument` (spec 020's shared AST). It is a separate module with a clear dependency boundary.

**Rationale**: The mapper:
- Converts `SmcatState` nodes to `StateNode` records
- Converts `SmcatTransition` nodes to `TransitionEdge` records
- Maps pseudo-state naming conventions to `StateKind` values
- Attaches `SmcatAnnotation` values for format-specific metadata (attributes, activities)
- Produces a `ParseResult<StatechartDocument>`

This module cannot be implemented until spec 020 defines `StatechartDocument`, `StateNode`, `TransitionEdge`, `StateKind`, `SmcatAnnotation`, and `ParseResult`. All other modules are independent.

**Alternatives considered**:
- Inline mapping in parser: Violates separation of concerns; parser should produce format-specific AST
- Skip mapper entirely: Mapper is required for cross-format validation and the CLI import pipeline
