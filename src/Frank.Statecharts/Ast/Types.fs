namespace Frank.Statecharts.Ast

// Shared AST types for cross-format statechart representation (spec 020).
// This is a minimal stub sufficient for the validation module (spec 021).
// Full implementation will be provided by spec 020.

[<Struct>]
type SourcePosition = { Line: int; Column: int }

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

type HistoryKind =
    | Shallow
    | Deep

type ArrowStyle =
    | Solid
    | Dashed

type Direction =
    | Forward
    | Deactivating

type TransitionStyle =
    { ArrowStyle: ArrowStyle
      Direction: Direction }

type WsdNotePosition =
    | Over
    | LeftOf
    | RightOf

type WsdMeta =
    | WsdTransitionStyle of TransitionStyle
    | WsdNotePosition of WsdNotePosition

type AlpsTransitionKind =
    | Safe
    | Unsafe
    | Idempotent

type AlpsMeta =
    | AlpsTransitionType of AlpsTransitionKind
    | AlpsDescriptorHref of string
    | AlpsExtension of name: string * value: string

type ScxmlMeta =
    | ScxmlInvoke of invokeType: string * src: string option
    | ScxmlHistory of id: string * historyKind: HistoryKind
    | ScxmlNamespace of string

type SmcatMeta =
    | SmcatColor of string
    | SmcatStateLabel of string
    | SmcatActivity of kind: string * body: string

type XStateMeta =
    | XStateAction of string
    | XStateService of string

type Annotation =
    | WsdAnnotation of WsdMeta
    | AlpsAnnotation of AlpsMeta
    | ScxmlAnnotation of ScxmlMeta
    | SmcatAnnotation of SmcatMeta
    | XStateAnnotation of XStateMeta

type StateActivities =
    { Entry: string list
      Exit: string list
      Do: string list }

type StateNode =
    { Identifier: string
      Label: string option
      Kind: StateKind
      Children: StateNode list
      Activities: StateActivities option
      Position: SourcePosition option
      Annotations: Annotation list }

type TransitionEdge =
    { Source: string
      Target: string option
      Event: string option
      Guard: string option
      Action: string option
      Parameters: string list
      Position: SourcePosition option
      Annotations: Annotation list }

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

type StatechartDocument =
    { Title: string option
      InitialStateId: string option
      Elements: StatechartElement list
      DataEntries: DataEntry list
      Annotations: Annotation list }
