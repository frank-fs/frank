# Research: Shared Statechart AST (Spec 020)

**Date**: 2026-03-15
**Status**: Complete
**Researcher**: Claude (spec-kitty.research)

## Executive Summary

This research investigates the design of a shared AST for the Frank.Statecharts spec pipeline, covering five statechart notation formats (WSD, ALPS, SCXML, smcat, XState JSON). The existing WSD parser's AST types in `src/Frank.Statecharts/Wsd/Types.fs` serve as the starting point. The shared AST must represent the superset of concepts across all five formats while allowing partial population by any single parser.

---

## Research Area 1: Existing WSD AST Types

### Current Type Inventory

The WSD parser defines these semantic types in `src/Frank.Statecharts/Wsd/Types.fs`:

| Type | Purpose | Shared AST Mapping |
|------|---------|-------------------|
| `SourcePosition` (struct) | Line/column tracking | Reuse directly (generalize to shared namespace) |
| `ArrowStyle` (DU: Solid/Dashed) | WSD arrow rendering | Move to WSD annotation |
| `Direction` (DU: Forward/Deactivating) | WSD arrow semantics | Move to WSD annotation |
| `Participant` (record) | Named entity (state analog) | Becomes `StateNode` |
| `Message` (record) | Directed communication (transition analog) | Becomes `TransitionEdge` |
| `GuardAnnotation` (record) | Key-value guard conditions | Becomes guard fields on `TransitionEdge` or annotation |
| `NotePosition` (DU: Over/LeftOf/RightOf) | Note placement | Move to WSD annotation |
| `Note` (record) | Attached commentary with optional guard | Becomes `NoteContent` in `StatechartElement` |
| `GroupKind` (DU: 7 cases) | Control flow grouping type | Reuse directly (shared concept) |
| `GroupBranch` (record) | Branch within group | Reuse directly |
| `Group` (record) | Grouped elements | Becomes `GroupBlock` |
| `DiagramElement` (DU: 6 cases) | Ordered element in diagram | Becomes `StatechartElement` |
| `Diagram` (record) | Root document | Becomes `StatechartDocument` |
| `ParseFailure` (record) | Structured error | Reuse directly (shared concept) |
| `ParseWarning` (record) | Structured warning | Reuse directly (shared concept) |
| `ParseResult` (record) | Parse output wrapper | Generalize to `ParseResult<StatechartDocument>` |

### What Must Stay WSD-Specific

- `TokenKind`, `Token` (lexer infrastructure) -- per spec clarification
- `ArrowStyle`, `Direction` -- WSD rendering concepts, become `WsdAnnotation` data
- `NotePosition` -- WSD layout concept, becomes part of WSD annotation on notes

### What Generalizes Directly

- `SourcePosition` -- universal concept, all parsers need it
- `GroupKind` -- WSD and smcat both use grouping constructs
- `GroupBranch` / `Group` structure -- reused as `GroupBlock` with branches
- `ParseFailure` / `ParseWarning` / `ParseResult` -- uniform error reporting for all parsers

### Key Migration Observations

1. The WSD `Participant` maps to `StateNode` with `StateKind.Regular` (WSD has no state type classification).
2. WSD `Message` maps to `TransitionEdge` with source/target as participant names, event as label, and arrow style/direction as WSD annotations.
3. WSD `Note` with guard becomes: the guard content populates `TransitionEdge` guard fields or standalone guard annotations; the note text becomes `NoteContent`.
4. WSD `Diagram.Participants` list becomes redundant -- participants are inferred from `StateNode` declarations in the element list. The parser must still emit `StateDecl` elements for explicit participant declarations.
5. WSD `Diagram.AutoNumber` has no shared AST equivalent -- becomes a `DirectiveElement` or WSD-specific document annotation.

**Decision D-001**: `SourcePosition` moves to the shared `Ast` namespace. The WSD lexer/parser types (`Token`, `TokenKind`) import it from there instead of defining their own copy.

**Decision D-002**: `ArrowStyle` and `Direction` become payload data inside `WsdAnnotation` on `TransitionEdge` nodes. They are not shared concepts.

**Decision D-003**: `GroupKind` is a shared concept (WSD and smcat both use grouping). It moves to the shared AST with the same 7 cases.

---

## Research Area 2: Format Capability Superset

### Capability Matrix

| Concept | WSD | ALPS | SCXML | smcat | XState |
|---------|-----|------|-------|-------|--------|
| **States (named entities)** | Participants | Descriptors (semantic) | `<state>` elements | State names | State nodes |
| **Transitions** | Messages (arrows) | Transition descriptors (safe/unsafe/idempotent) | `<transition>` elements | `=>` arrows | Event handlers in `on:` |
| **Events** | Message labels | - (implicit from descriptor names) | `event` attribute | Event portion of label | Event names (UPPERCASE convention) |
| **Guards/Conditions** | `note over` with `[guard:]` | `ext` elements (extensions) | `cond` attribute on `<transition>` | `[condition]` in label | `cond` property |
| **Actions** | - (no explicit action concept) | - (implied by descriptor type) | `<onentry>`, `<onexit>`, transition content | `/ action` in label | `actions`, `entry`, `exit` arrays |
| **Data/Context** | Message parameters | Semantic descriptors (type/value) | `<datamodel>` / `<data>` elements | - (no data model) | `context` object |
| **Hierarchy/Nesting** | - (no hierarchy) | Nested descriptors | `<state>` nesting, `<parallel>`, `<history>` | Composite states (indentation) | Nested state nodes |
| **Initial state** | First participant (implicit) | - (no initial concept) | `initial` attribute / `<initial>` element | `initial` keyword | `initial` property |
| **Final state** | - (no final concept) | - (no final concept) | `<final>` element | `final` keyword | `type: 'final'` |
| **Parallel states** | - | - | `<parallel>` element | Composite with parallel | `type: 'parallel'` |
| **History states** | - | - | `<history type="shallow\|deep">` | `history` / `deephistory` keywords | History states |
| **Choice pseudo-states** | - | - | - (via conditional transitions) | `choice` keyword | - (via guards) |
| **Fork/Join** | - | - | - (via parallel) | `forkjoin` keyword | - |
| **Invoke/Send** | - | - | `<invoke>`, `<send>` | - | `invoke` |
| **Grouping blocks** | alt/opt/loop/par/break/critical/ref | - | - | Composite states | - |
| **Transition style** | Solid/Dashed/Forward/Deactivating arrows | safe/unsafe/idempotent | internal/external | - | - |
| **Activities** | - | - | `<onentry>`, `<onexit>` | `entry/`, `exit/`, `do/` | `entry`, `exit` |

### Superset State Types (StateKind DU)

From the matrix above, the union of all state types across formats:

1. **Regular** -- default state (all formats)
2. **Initial** -- initial pseudo-state (SCXML, smcat, XState)
3. **Final** -- final state (SCXML, smcat, XState)
4. **Parallel** -- parallel/orthogonal region (SCXML, smcat, XState)
5. **ShallowHistory** -- shallow history pseudo-state (SCXML, smcat)
6. **DeepHistory** -- deep history pseudo-state (SCXML, smcat)
7. **Choice** -- choice pseudo-state (smcat)
8. **ForkJoin** -- fork/join pseudo-state (smcat)
9. **Terminate** -- terminate pseudo-state (smcat)

**Decision D-004**: `StateKind` DU has 9 cases as listed above. The spec originally proposed `History of HistoryKind` with a sub-DU, but splitting into `ShallowHistory` and `DeepHistory` directly is simpler and avoids an unnecessary wrapper type (FR-019 in the spec lists them separately). However, the spec's FR-019 says to define `HistoryKind` with `Shallow`/`Deep` cases -- we follow the spec as written.

**Revision to D-004**: Per FR-005 in the spec, the cases are: `Regular`, `Initial`, `Final`, `Parallel`, `ShallowHistory`, `DeepHistory`, `Choice`, `ForkJoin`, `Terminate`. FR-019 separately defines `HistoryKind` DU with `Shallow`/`Deep` for use elsewhere (e.g., SCXML annotations). The `StateKind` DU uses flat cases, not wrapped.

### Partial Population Strategy

Each format populates only the fields it can represent:

- **smcat**: States, transitions, guards, events, actions, hierarchy, initial/final/choice/history -- but NO data model
- **ALPS**: States (descriptors), transitions, transition type annotations -- but NO initial state, NO workflow ordering, NO hierarchy in the WSD sense
- **SCXML**: FULL population (most expressive format) -- states, transitions, guards, data model, hierarchy, initial, final, parallel, history, invoke
- **WSD**: States (participants), transitions (messages), guards, grouping blocks -- but NO hierarchy, NO final state
- **XState**: States, transitions, guards, actions, context, hierarchy, initial, final, parallel -- but NO grouping blocks

**Decision D-005**: Use `option` types for fields that some formats cannot populate. Use empty lists (`[]`) for collection fields that some formats leave unpopulated. Never use `null`. This is idiomatic F# and provides compile-time safety.

---

## Research Area 3: AST Design Patterns in F#

### Pattern 1: Option Types for Partial Fields (Recommended)

The standard F# approach for fields that may or may not be populated:

```fsharp
type StateNode =
    { Identifier: string
      Label: string option          // not all formats provide labels
      Kind: StateKind
      Children: StateNode list      // empty if no hierarchy
      Position: SourcePosition option  // None if programmatically constructed
      Annotations: Annotation list }   // empty if no format-specific data
```

Advantages:
- Compile-time safety (no nulls)
- Pattern matching on `Some`/`None`
- Structural equality works correctly
- Clear semantic intent ("this field may be absent")

### Pattern 2: Empty Collections for "Zero or More" Fields

For fields that are collections (children, annotations, parameters):

```fsharp
Children: StateNode list    // empty list = no children (leaf state)
Parameters: string list     // empty list = no parameters
Annotations: Annotation list // empty list = no format-specific data
```

Advantages:
- No need for `option` wrapping on collections
- `List.isEmpty` check is clean
- Structural equality with empty lists works correctly

### Pattern 3: F# Compiler's Trivia Pattern (Considered, Not Adopted)

The F# compiler uses a "trivia" record attached to AST nodes for non-semantic metadata (source positions, comments, formatting). This separates "essential" from "incidental" data.

For our use case, this would mean:

```fsharp
type StateNodeTrivia =
    { Position: SourcePosition option
      Annotations: Annotation list }

type StateNode =
    { Identifier: string
      Kind: StateKind
      Children: StateNode list
      Trivia: StateNodeTrivia }
```

**Decision D-006**: We do NOT adopt the trivia pattern. Source positions and annotations are important enough for our use case (error reporting, roundtripping) that they belong directly on the AST nodes. The trivia pattern adds indirection without clear benefit for our scope. This aligns with the spec's FR-002/FR-003 which list position and annotations as direct fields.

### Pattern 4: Discriminated Union for Heterogeneous Element Lists

The existing WSD parser uses `DiagramElement` DU for ordered elements. This pattern generalizes well:

```fsharp
type StatechartElement =
    | StateDecl of StateNode
    | TransitionElement of TransitionEdge
    | NoteElement of NoteContent
    | GroupElement of GroupBlock
    | DirectiveElement of Directive
```

**Decision D-007**: The `StatechartElement` DU preserves source ordering of elements (important for WSD where order implies sequence). This matches FR-016 in the spec.

---

## Research Area 4: Source Position Tracking

### Current WSD Approach

The WSD parser already defines `SourcePosition` as a struct with `Line` and `Column` fields. Every token and every AST node carries a `SourcePosition`. This is simple and effective.

### F# Compiler Approach

The F# compiler uses `range` (a struct with start/end positions plus file index). This is more complex than needed for our use case -- we parse single documents, not multi-file projects.

### Recommended Approach

Keep the existing `SourcePosition` struct design but move it to the shared namespace:

```fsharp
[<Struct>]
type SourcePosition = { Line: int; Column: int }
```

Make it `option` on all AST node types:

```fsharp
type StateNode = { ...; Position: SourcePosition option }
```

- `Some pos` -- set by parsers during construction
- `None` -- set by generators or programmatic construction (tests)

**Decision D-008**: `SourcePosition` remains a simple `Line`/`Column` struct. No file name tracking (single-document parsing). No range tracking (start-only positions are sufficient for error messages). This matches FR-007 in the spec.

### Migration Impact

The WSD `Types.fs` currently defines `SourcePosition` in the `Frank.Statecharts.Wsd.Types` module. The shared AST will define it in `Frank.Statecharts.Ast`. The WSD lexer/token types will import from the shared location.

**Decision D-009**: The WSD `Token` struct keeps its own `Position: SourcePosition` field but uses the shared `SourcePosition` type. The WSD-specific `Types.fs` no longer defines `SourcePosition`.

---

## Research Area 5: Format-Specific Annotations

### Design Requirement

Each format has concepts that do not generalize. The spec requires typed DUs (not stringly-typed dictionaries) for format-specific metadata.

### Annotation DU Design

```fsharp
type Annotation =
    | WsdAnnotation of WsdMeta
    | AlpsAnnotation of AlpsMeta
    | ScxmlAnnotation of ScxmlMeta
    | SmcatAnnotation of SmcatMeta
    | XStateAnnotation of XStateMeta
```

Where each `*Meta` type is a DU or record carrying format-specific data:

```fsharp
type TransitionStyle =
    { ArrowStyle: ArrowStyle
      Direction: Direction }

type WsdMeta =
    | WsdTransitionStyle of TransitionStyle
    | WsdNotePosition of NotePosition
    | WsdAutoNumber

type AlpsMeta =
    | AlpsTransitionType of AlpsTransitionKind  // Safe | Unsafe | Idempotent
    | AlpsDescriptorHref of string
    | AlpsExtension of name: string * value: string

type ScxmlMeta =
    | ScxmlInvoke of invokeType: string * src: string option
    | ScxmlHistory of id: string * historyKind: HistoryKind
    | ScxmlNamespace of string

type SmcatMeta =
    | SmcatColor of string
    | SmcatLabel of string
    | SmcatActivity of kind: string * body: string

type XStateMeta =
    | XStateContext of obj   // boxed context value
    | XStateAction of string
    | XStateService of string
```

**Decision D-010**: The `Annotation` DU is open for extension. This spec defines the structure and WSD cases fully. Other format cases (ALPS, SCXML, smcat, XState) are defined as stubs with documented intent -- they will be fleshed out by their respective parser specs. This matches the spec's assumptions section.

**Decision D-011**: Annotations are stored as a `list` on each AST node, not as a single `option`. Multiple annotations from the same or different formats can coexist on one node. This supports merge scenarios and avoids loss of information.

### Why Not Stringly-Typed?

A `Map<string, string>` approach would lose type safety:
- Misspelled keys are silent bugs
- No exhaustive matching
- No compile-time guarantees about value types
- Harder to refactor

The DU approach gives:
- Exhaustive pattern matching
- Compiler warnings on unhandled cases
- Typed payloads per annotation kind
- Documentation through type signatures

---

## Research Area 6: Relationship to StateMachineMetadata

### What StateMachineMetadata Is

`StateMachineMetadata` (defined in `src/Frank.Statecharts/StatefulResourceBuilder.fs`) is a **runtime** type used as endpoint metadata in ASP.NET Core. It contains:

- `Machine: obj` -- boxed `StateMachine<'S,'E,'C>` (the runtime state machine definition)
- `StateHandlerMap` -- maps state names to HTTP method/handler pairs
- `ResolveInstanceId` -- extracts instance keys from HTTP requests
- `TransitionObservers` -- observers for state change events
- `GetCurrentStateKey` / `EvaluateGuards` / `ExecuteTransition` -- runtime operations

### What the Shared AST Is

The shared AST (`StatechartDocument`) is a **parse-time** intermediate representation:

- Populated by format parsers from text/XML/JSON input
- Consumed by generators to produce format-specific output
- Used by the cross-format validator to compare ASTs from different parsers
- Does NOT execute state machines or handle HTTP requests

### The Boundary

```
Source Text/XML/JSON
    |
    v
[Format Parser] --> StatechartDocument (shared AST)
    |                       |
    v                       v
[Generator] --> Output   [Validator] --> Consistency report
                            |
                            v
                   [Mapping Step] --> StateMachine<'S,'E,'C> --> StateMachineMetadata
```

The mapping from `StatechartDocument` to `StateMachine<'S,'E,'C>` is a separate concern, likely a future spec. It involves:
- Converting `StateNode` identifiers to DU cases or enum values
- Converting `TransitionEdge` to transition functions
- Converting `DataEntry` to context types
- Resolving guard annotations to guard predicates

**Decision D-012**: The shared AST has NO dependency on `StateMachine<'S,'E,'C>` or `StateMachineMetadata`. They live in separate namespaces with no imports between them. The mapping step is out of scope for spec 020.

**Decision D-013**: The shared AST types are in `Frank.Statecharts.Ast` namespace (under `src/Frank.Statecharts/Ast/`). The runtime types remain in `Frank.Statecharts` namespace. No circular dependencies.

---

## Open Questions and Risks

### Open Questions

1. **OQ-001**: Should `GroupKind` support extensibility for future grouping constructs beyond the 7 WSD/smcat cases? Current answer: No -- the 7 cases (Alt, Opt, Loop, Par, Break, Critical, Ref) cover UML interaction fragments. If a new format needs more, the DU can be extended.

2. **OQ-002**: Should `StatechartDocument` carry a format origin marker (e.g., `SourceFormat: FormatKind option`) so downstream consumers know which parser produced it? This could help the cross-format validator. Current answer: Not in this spec -- the validator knows which parser it invoked.

3. **OQ-003**: How should the WSD parser handle `AutoNumber` in the shared AST? It is not a statechart concept -- it is a WSD rendering directive. Options: (a) `DirectiveElement` in the element list, (b) WSD annotation on the document, (c) drop it. Current answer: `DirectiveElement` with a `Directive` DU case for `AutoNumber` and `Title`, keeping it in the element list for order preservation.

4. **OQ-004**: Should `NoteContent` in the shared AST carry the guard annotation it parsed from, or should the parser split notes-with-guards into separate `NoteElement` + guard annotations on the relevant transition? Current answer: Per the spec, notes are a distinct element type (`NoteElement of NoteContent`), and guards extracted from notes are associated with transitions/states separately. The `NoteContent` record carries the remaining text after guard extraction.

### Risks

1. **R-001 (Low)**: The `Annotation` DU will grow as each format parser is implemented. Each addition is a binary-breaking change. Mitigation: All format parser specs are within the same project, so binary compatibility is not a concern for internal consumers. External consumers should depend on the package version.

2. **R-002 (Medium)**: The WSD parser migration (User Story 2) touches every test file in `test/Frank.Statecharts.Tests/Wsd/`. All test assertions reference WSD-specific types (`Participant`, `Message`, `Diagram`, etc.) that will be renamed. Mitigation: Mechanical rename with compiler guidance -- the F# compiler will flag every reference to removed types.

3. **R-003 (Low)**: The `StatechartElement` DU ordering approach means consumers must walk the element list to find states/transitions rather than accessing dedicated lists. Mitigation: Provide helper functions (like the existing `messages`, `notes`, `groups` helpers in tests) as module-level functions on the AST.

---

## Decision Register

| ID | Decision | Rationale | Evidence |
|----|----------|-----------|----------|
| D-001 | `SourcePosition` moves to shared `Ast` namespace | Universal concept needed by all parsers | WSD already defines it; SCXML/smcat/XState all track positions |
| D-002 | `ArrowStyle`/`Direction` become WSD annotation payload | Only WSD uses arrow rendering styles | Capability matrix shows no other format has this concept |
| D-003 | `GroupKind` is a shared concept with 7 cases | WSD and smcat both use grouping constructs | UML interaction fragments are standard |
| D-004 | `StateKind` DU has 9 flat cases | Covers superset of state types across all 5 formats | smcat schema defines all pseudo-state types |
| D-005 | Use `option` for optional fields, empty lists for collections | Idiomatic F#, compile-time safe, no nulls | Standard F# community practice |
| D-006 | No trivia pattern; position/annotations on nodes directly | Simpler; matches spec FR-002/FR-003 | F# compiler uses trivia but our scope is smaller |
| D-007 | `StatechartElement` DU preserves source ordering | WSD source order implies sequence; other formats may too | Existing WSD `DiagramElement` pattern |
| D-008 | `SourcePosition` is Line/Column struct, no file/range | Single-document parsing; start positions sufficient | Current WSD implementation proves this works |
| D-009 | WSD `Token` uses shared `SourcePosition` type | Eliminates duplicate type definition | DRY principle |
| D-010 | `Annotation` DU is extensible; WSD cases defined, others stubbed | Other format parsers are future specs | Spec assumptions section |
| D-011 | Annotations are a `list` on each node | Multiple annotations can coexist; supports merges | Spec FR-006 allows format-specific metadata per node |
| D-012 | Shared AST has no dependency on runtime types | Clean separation of parse-time and runtime concerns | `StateMachineMetadata` is endpoint metadata, not AST |
| D-013 | AST types in `Frank.Statecharts.Ast` namespace | Follows per-format sub-directory pattern (like `Wsd/`) | Spec clarification: "Under `Ast/` sub-directory" |

---

## Sources

- [F# Discriminated Unions - Microsoft Learn](https://learn.microsoft.com/en-us/dotnet/fsharp/language-reference/discriminated-unions)
- [F# Discriminated Unions - F# for fun and profit](https://fsharpforfunandprofit.com/posts/discriminated-unions/)
- [W3C SCXML Specification](https://www.w3.org/TR/scxml/)
- [ALPS Specification Draft](http://alps.io/spec/drafts/draft-01.html)
- [state-machine-cat (smcat) GitHub](https://github.com/sverweij/state-machine-cat)
- [smcat AST Schema](https://github.com/sverweij/state-machine-cat/blob/main/tools/smcat-ast.schema.json)
- [XState Documentation](https://xstate.js.org/)
- [F# Compiler SyntaxTree Design](https://fsharp.github.io/fsharp-compiler-docs/fcs/untypedtree.html)
- [Changing the F# AST (trivia pattern)](https://fsharp.github.io/fsharp-compiler-docs/changing-the-ast.html)
- Existing codebase: `src/Frank.Statecharts/Wsd/Types.fs` (WSD AST types)
- Existing codebase: `src/Frank.Statecharts/StatefulResourceBuilder.fs` (StateMachineMetadata runtime type)
