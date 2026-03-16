namespace Frank.Statecharts.Ast

<<<<<<< HEAD
// ============================================================================
// Shared Statechart AST Types (Spec 020)
//
// Format-agnostic AST types used by all statechart format parsers
// (WSD, ALPS, SCXML, smcat, XState). Each parser populates the portions
// it can represent; unpopulated fields use option types or empty lists.
// ============================================================================

// ---------------------------------------------------------------------------
// Core type building blocks (T001)
// ---------------------------------------------------------------------------

/// 1-based source position from the original input text (FR-007).
/// Set by parsers during construction; None when AST is built programmatically.
[<Struct>]
type SourcePosition = { Line: int; Column: int }

/// History kind for SCXML <history> elements (FR-019).
/// Used by ScxmlMeta.ScxmlHistory to carry the type attribute.
type HistoryKind =
    | Shallow
    | Deep

/// State type classification covering the superset across all formats (FR-005).
/// StateKind uses flat ShallowHistory/DeepHistory cases rather than wrapping HistoryKind.
=======
// Shared AST types for cross-format statechart representation (spec 020).
// This is a minimal stub sufficient for the validation module (spec 021).
// Full implementation will be provided by spec 020.

[<Struct>]
type SourcePosition = { Line: int; Column: int }

>>>>>>> 021-cross-format-validator-WP05
type StateKind =
    | Regular
    | Initial
    | Final
    | Parallel
    | ShallowHistory
    | DeepHistory
    | Choice
    | ForkJoin
    | Terminate

<<<<<<< HEAD
// -- WSD annotation payload types (FR-020) --

/// Arrow style for WSD transitions.
=======
type HistoryKind =
    | Shallow
    | Deep

>>>>>>> 021-cross-format-validator-WP05
type ArrowStyle =
    | Solid
    | Dashed

<<<<<<< HEAD
/// Direction for WSD transitions.
=======
>>>>>>> 021-cross-format-validator-WP05
type Direction =
    | Forward
    | Deactivating

<<<<<<< HEAD
/// WSD transition style combining arrow style and direction (FR-020).
=======
>>>>>>> 021-cross-format-validator-WP05
type TransitionStyle =
    { ArrowStyle: ArrowStyle
      Direction: Direction }

<<<<<<< HEAD
/// WSD note position relative to participant.
=======
>>>>>>> 021-cross-format-validator-WP05
type WsdNotePosition =
    | Over
    | LeftOf
    | RightOf

<<<<<<< HEAD
/// WSD-specific annotation metadata.
=======
>>>>>>> 021-cross-format-validator-WP05
type WsdMeta =
    | WsdTransitionStyle of TransitionStyle
    | WsdNotePosition of WsdNotePosition

<<<<<<< HEAD
// -- ALPS annotation stub (D-010) --

/// ALPS transition kind annotation.
=======
>>>>>>> 021-cross-format-validator-WP05
type AlpsTransitionKind =
    | Safe
    | Unsafe
    | Idempotent

<<<<<<< HEAD
/// ALPS-specific annotation metadata.
/// To be fleshed out by ALPS parser spec.
=======
>>>>>>> 021-cross-format-validator-WP05
type AlpsMeta =
    | AlpsTransitionType of AlpsTransitionKind
    | AlpsDescriptorHref of string
    | AlpsExtension of name: string * value: string

<<<<<<< HEAD
// -- SCXML annotation stub --

/// SCXML-specific annotation metadata.
/// To be fleshed out by SCXML parser spec.
=======
>>>>>>> 021-cross-format-validator-WP05
type ScxmlMeta =
    | ScxmlInvoke of invokeType: string * src: string option
    | ScxmlHistory of id: string * historyKind: HistoryKind
    | ScxmlNamespace of string

<<<<<<< HEAD
// -- smcat annotation stub --

/// smcat-specific annotation metadata.
/// To be fleshed out by smcat parser spec.
=======
>>>>>>> 021-cross-format-validator-WP05
type SmcatMeta =
    | SmcatColor of string
    | SmcatStateLabel of string
    | SmcatActivity of kind: string * body: string

<<<<<<< HEAD
// -- XState annotation stub --

/// XState-specific annotation metadata.
/// To be fleshed out by XState parser spec.
=======
>>>>>>> 021-cross-format-validator-WP05
type XStateMeta =
    | XStateAction of string
    | XStateService of string

<<<<<<< HEAD
/// Format-specific annotation discriminated union (FR-006).
/// Each case carries typed data rather than stringly-typed values.
=======
>>>>>>> 021-cross-format-validator-WP05
type Annotation =
    | WsdAnnotation of WsdMeta
    | AlpsAnnotation of AlpsMeta
    | ScxmlAnnotation of ScxmlMeta
    | SmcatAnnotation of SmcatMeta
    | XStateAnnotation of XStateMeta

<<<<<<< HEAD
// ---------------------------------------------------------------------------
// Supporting record types and DUs (T002)
// ---------------------------------------------------------------------------

/// Entry, exit, and do activities for a state.
=======
>>>>>>> 021-cross-format-validator-WP05
type StateActivities =
    { Entry: string list
      Exit: string list
      Do: string list }

<<<<<<< HEAD
/// A data model variable (FR-004).
type DataEntry =
    { Name: string
      Expression: string option
      Position: SourcePosition option }

/// A textual note/comment attached to a participant or state.
type NoteContent =
    { Target: string
      Content: string
      Position: SourcePosition option
      Annotations: Annotation list }

/// Control flow grouping kind (FR-021).
/// Reused from WSD parser; all 7 cases correspond to UML interaction fragment types.
type GroupKind =
    | Alt
    | Opt
    | Loop
    | Par
    | Break
    | Critical
    | Ref

/// Format-specific directives that affect rendering but are not
/// statechart semantics.
type Directive =
    | TitleDirective of title: string * position: SourcePosition option
    | AutoNumberDirective of position: SourcePosition option

// ---------------------------------------------------------------------------
// Mutually recursive types (T003)
// ---------------------------------------------------------------------------

/// Ordered element within a statechart document (FR-016).
type StatechartElement =
    | StateDecl of StateNode
    | TransitionElement of TransitionEdge
    | NoteElement of NoteContent
    | GroupElement of GroupBlock
    | DirectiveElement of Directive

/// A branch within a GroupBlock, containing an optional condition
/// and child elements.
and GroupBranch =
    { Condition: string option
      Elements: StatechartElement list }

/// A control flow grouping structure (FR-015).
and GroupBlock =
    { Kind: GroupKind
      Branches: GroupBranch list
      Position: SourcePosition option }

/// A state within the statechart (FR-002).
and StateNode =
=======
type StateNode =
>>>>>>> 021-cross-format-validator-WP05
    { Identifier: string
      Label: string option
      Kind: StateKind
      Children: StateNode list
      Activities: StateActivities option
      Position: SourcePosition option
      Annotations: Annotation list }

<<<<<<< HEAD
/// A directed edge between states (FR-003).
and TransitionEdge =
=======
type TransitionEdge =
>>>>>>> 021-cross-format-validator-WP05
    { Source: string
      Target: string option
      Event: string option
      Guard: string option
      Action: string option
      Parameters: string list
      Position: SourcePosition option
      Annotations: Annotation list }

<<<<<<< HEAD
/// Root AST node representing a complete parsed statechart (FR-001).
/// An empty document (no states, no transitions) is valid.
=======
type DataEntry =
    { Name: string
      Expression: string option
      Position: SourcePosition option }

type NoteContent =
    { Target: string
      Content: string
      Position: SourcePosition option
      Annotations: Annotation list }

type GroupKind =
    | Alt
    | Opt
    | Loop
    | Par
    | Break
    | Critical
    | Ref

type GroupBranch =
    { Condition: string option
      Elements: StatechartElement list }

and GroupBlock =
    { Kind: GroupKind
      Branches: GroupBranch list
      Position: SourcePosition option }

and Directive =
    | TitleDirective of title: string * position: SourcePosition option
    | AutoNumberDirective of position: SourcePosition option

and StatechartElement =
    | StateDecl of StateNode
    | TransitionElement of TransitionEdge
    | NoteElement of NoteContent
    | GroupElement of GroupBlock
    | DirectiveElement of Directive

>>>>>>> 021-cross-format-validator-WP05
type StatechartDocument =
    { Title: string option
      InitialStateId: string option
      Elements: StatechartElement list
      DataEntries: DataEntry list
      Annotations: Annotation list }
<<<<<<< HEAD

// ---------------------------------------------------------------------------
// Parse result types (T004)
// ---------------------------------------------------------------------------

/// Error from a format parser (FR-008).
/// Position is option because some formats may not have position info
/// for certain errors (e.g., XML parse errors from a library).
type ParseFailure =
    { Position: SourcePosition option
      Description: string
      Expected: string
      Found: string
      CorrectiveExample: string }

/// Warning from a format parser (FR-009).
/// Position is option because some formats may not have position info.
type ParseWarning =
    { Position: SourcePosition option
      Description: string
      Suggestion: string option }

/// Uniform result type for all format parsers (FR-010).
/// Document is always present (best-effort, even on parse failure).
type ParseResult =
    { Document: StatechartDocument
      Errors: ParseFailure list
      Warnings: ParseWarning list }
=======
>>>>>>> 021-cross-format-validator-WP05
