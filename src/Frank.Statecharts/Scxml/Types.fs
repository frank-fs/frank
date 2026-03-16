module internal Frank.Statecharts.Scxml.Types

/// 1-based line and column position in source SCXML text.
[<Struct>]
type SourcePosition = { Line: int; Column: int }

/// W3C SCXML transition type attribute (section 3.5).
type ScxmlTransitionType =
    | Internal
    | External

/// History pseudo-state type attribute (section 3.10).
type ScxmlHistoryKind =
    | Shallow
    | Deep

/// Derived state classification based on element name and child presence.
type ScxmlStateKind =
    | Simple    // <state> with no child states (atomic)
    | Compound  // <state> with child states
    | Parallel  // <parallel>
    | Final     // <final>

/// A name/expression pair from <data id="..." expr="...">.
type DataEntry =
    { Id: string
      Expression: string option
      Position: SourcePosition option }

/// A transition parsed from <transition event="..." cond="..." target="..." type="...">.
type ScxmlTransition =
    { Event: string option
      Guard: string option
      Targets: string list
      TransitionType: ScxmlTransitionType
      Position: SourcePosition option }

/// A history pseudo-state parsed from <history id="..." type="...">.
type ScxmlHistory =
    { Id: string
      Kind: ScxmlHistoryKind
      DefaultTransition: ScxmlTransition option
      Position: SourcePosition option }

/// An invocation annotation parsed from <invoke type="..." src="..." id="...">.
type ScxmlInvoke =
    { InvokeType: string option
      Src: string option
      Id: string option
      Position: SourcePosition option }

/// A state node parsed from <state>, <parallel>, or <final>.
type ScxmlState =
    { Id: string option
      Kind: ScxmlStateKind
      InitialId: string option
      Transitions: ScxmlTransition list
      Children: ScxmlState list
      DataEntries: DataEntry list
      HistoryNodes: ScxmlHistory list
      InvokeNodes: ScxmlInvoke list
      Position: SourcePosition option }

/// Root type representing a complete parsed SCXML document.
type ScxmlDocument =
    { Name: string option
      InitialId: string option
      DatamodelType: string option
      Binding: string option
      States: ScxmlState list
      DataEntries: DataEntry list
      Position: SourcePosition option }

/// Structured error result for malformed XML or invalid SCXML structure.
type ParseError =
    { Description: string
      Position: SourcePosition option }

/// Non-fatal issues detected during parsing.
type ParseWarning =
    { Description: string
      Position: SourcePosition option
      Suggestion: string option }

/// Result type combining the document with any errors/warnings.
type ScxmlParseResult =
    { Document: ScxmlDocument option
      Errors: ParseError list
      Warnings: ParseWarning list }
