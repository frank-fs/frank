namespace Frank.Statecharts.Ast

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

// -- WSD annotation payload types (FR-020) --

/// Arrow style for WSD transitions.
type ArrowStyle =
    | Solid
    | Dashed

/// Direction for WSD transitions.
type Direction =
    | Forward
    | Deactivating

/// WSD transition style combining arrow style and direction (FR-020).
type TransitionStyle =
    { ArrowStyle: ArrowStyle
      Direction: Direction }

/// WSD note position relative to participant.
type WsdNotePosition =
    | Over
    | LeftOf
    | RightOf

/// WSD-specific annotation metadata.
type WsdMeta =
    | WsdTransitionStyle of TransitionStyle
    | WsdNotePosition of WsdNotePosition
    | WsdGuardData of pairs: (string * string) list

// -- ALPS annotation stub (D-010) --

/// ALPS transition kind annotation.
type AlpsTransitionKind =
    | Safe
    | Unsafe
    | Idempotent

/// ALPS-specific annotation metadata.
type AlpsMeta =
    | AlpsTransitionType of AlpsTransitionKind
    | AlpsDescriptorHref of string
    | AlpsExtension of id: string * href: string option * value: string option
    | AlpsDocumentation of format: string option * value: string
    | AlpsLink of rel: string * href: string
    | AlpsDataDescriptor of id: string * doc: (string option * string) option
    | AlpsVersion of string

// -- SCXML annotation stub --

/// SCXML-specific annotation metadata.
type ScxmlMeta =
    | ScxmlInvoke of invokeType: string * src: string option * id: string option
    | ScxmlHistory of id: string * historyKind: HistoryKind * defaultTarget: string option
    | ScxmlNamespace of string
    | ScxmlTransitionType of isInternal: bool
    | ScxmlMultiTarget of targets: string list
    | ScxmlDatamodelType of datamodel: string
    | ScxmlBinding of binding: string
    | ScxmlInitial of initialId: string

// -- smcat annotation types --

/// Tracks whether a state's type was declared via [type="..."] attribute
/// or inferred from naming convention / default.
type SmcatTypeOrigin =
    | Explicit
    | Inferred

/// Semantic role of a transition in smcat format.
type SmcatTransitionKind =
    | InitialTransition
    | FinalTransition
    | SelfTransition
    | ExternalTransition
    | InternalTransition

/// smcat-specific annotation metadata.
/// Carries state type origin tracking, transition semantic roles,
/// visual attributes, and custom key-value pairs.
type SmcatMeta =
    | SmcatColor of string
    | SmcatStateLabel of string
    | SmcatCustomAttribute of key: string * value: string
    | SmcatStateType of kind: StateKind * origin: SmcatTypeOrigin
    | SmcatTransition of SmcatTransitionKind

// -- XState annotation stub --

/// XState-specific annotation metadata.
/// To be fleshed out by XState parser spec.
type XStateMeta =
    | XStateAction of string
    | XStateService of string

/// Format-specific annotation discriminated union (FR-006).
/// Each case carries typed data rather than stringly-typed values.
type Annotation =
    | WsdAnnotation of WsdMeta
    | AlpsAnnotation of AlpsMeta
    | ScxmlAnnotation of ScxmlMeta
    | SmcatAnnotation of SmcatMeta
    | XStateAnnotation of XStateMeta

// ---------------------------------------------------------------------------
// Supporting record types and DUs (T002)
// ---------------------------------------------------------------------------

/// Entry, exit, and do activities for a state.
type StateActivities =
    { Entry: string list
      Exit: string list
      Do: string list }

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
    { Identifier: string option
      Label: string option
      Kind: StateKind
      Children: StateNode list
      Activities: StateActivities option
      Position: SourcePosition option
      Annotations: Annotation list }

/// A directed edge between states (FR-003).
and TransitionEdge =
    { Source: string
      Target: string option
      Event: string option
      Guard: string option
      Action: string option
      Parameters: string list
      Position: SourcePosition option
      Annotations: Annotation list }

/// Root AST node representing a complete parsed statechart (FR-001).
/// An empty document (no states, no transitions) is valid.
type StatechartDocument =
    { Title: string option
      InitialStateId: string option
      Elements: StatechartElement list
      DataEntries: DataEntry list
      Annotations: Annotation list }

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
