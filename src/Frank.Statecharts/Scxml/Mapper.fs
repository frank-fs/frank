module internal Frank.Statecharts.Scxml.Mapper

open Frank.Statecharts.Scxml.Types
open Frank.Statecharts.Ast

// Type aliases to disambiguate the two types that share the same short name
// across the Frank.Statecharts.Scxml.Types and Frank.Statecharts.Ast namespaces.
type private ScxmlPos      = Frank.Statecharts.Scxml.Types.SourcePosition
type private AstPos        = Frank.Statecharts.Ast.SourcePosition
type private ScxmlDataE    = Frank.Statecharts.Scxml.Types.DataEntry
type private AstDataE      = Frank.Statecharts.Ast.DataEntry
type private ScxmlWarn     = Frank.Statecharts.Scxml.Types.ParseWarning
type private AstWarn       = Frank.Statecharts.Ast.ParseWarning
type private ScxmlErr      = Frank.Statecharts.Scxml.Types.ParseError
type private AstFailure    = Frank.Statecharts.Ast.ParseFailure

// ---------------------------------------------------------------------------
// Position conversion helpers
// ---------------------------------------------------------------------------

/// Convert a SCXML-specific SourcePosition to the shared AST SourcePosition.
let private toAstPosition (pos: ScxmlPos) : AstPos =
    { AstPos.Line = pos.Line; AstPos.Column = pos.Column }

/// Convert a shared AST SourcePosition to the SCXML-specific SourcePosition.
let private fromAstPosition (pos: AstPos) : ScxmlPos =
    { ScxmlPos.Line = pos.Line; ScxmlPos.Column = pos.Column }

// ---------------------------------------------------------------------------
// History kind conversion helpers
// ---------------------------------------------------------------------------

/// Convert a SCXML ScxmlHistoryKind to the shared AST HistoryKind.
let private toAstHistoryKind (kind: ScxmlHistoryKind) : HistoryKind =
    match kind with
    | ScxmlHistoryKind.Shallow -> Shallow
    | ScxmlHistoryKind.Deep    -> Deep

/// Convert a shared AST HistoryKind back to the SCXML ScxmlHistoryKind.
let private fromAstHistoryKind (kind: HistoryKind) : ScxmlHistoryKind =
    match kind with
    | Shallow -> ScxmlHistoryKind.Shallow
    | Deep    -> ScxmlHistoryKind.Deep

// ---------------------------------------------------------------------------
// DataEntry conversion helpers
// ---------------------------------------------------------------------------

/// Convert a SCXML DataEntry (Id field) to a shared AST DataEntry (Name field).
let private toAstDataEntry (entry: ScxmlDataE) : AstDataE =
    { AstDataE.Name       = entry.Id
      AstDataE.Expression = entry.Expression
      AstDataE.Position   = entry.Position |> Option.map toAstPosition }

/// Convert a shared AST DataEntry back to a SCXML DataEntry.
let private fromAstDataEntry (entry: AstDataE) : ScxmlDataE =
    { ScxmlDataE.Id         = entry.Name
      ScxmlDataE.Expression = entry.Expression
      ScxmlDataE.Position   = entry.Position |> Option.map fromAstPosition }

// ---------------------------------------------------------------------------
// toStatechartDocument helpers
// ---------------------------------------------------------------------------

/// Convert a SCXML ScxmlTransition to a shared AST TransitionEdge.
/// sourceId is the identifier of the owning state.
let private toTransitionEdge (sourceId: string) (t: ScxmlTransition) : TransitionEdge =
    { Source      = sourceId
      Target      = t.Targets |> List.tryHead
      Event       = t.Event
      Guard       = t.Guard
      Action      = None
      Parameters  = []
      Position    = t.Position |> Option.map toAstPosition
      Annotations = [] }

/// Recursively convert a ScxmlState to a shared AST StateNode and collect
/// all descendant TransitionEdges. Returns (StateNode, transitions-in-subtree).
let rec private toStateNodeAndTransitions
    (state: ScxmlState)
    : StateNode * TransitionEdge list =

    let stateId = state.Id |> Option.defaultValue ""

    // Map ScxmlStateKind → Ast.StateKind
    let astKind =
        match state.Kind with
        | ScxmlStateKind.Simple   -> Regular
        | ScxmlStateKind.Compound -> Regular
        | ScxmlStateKind.Parallel -> Parallel
        | ScxmlStateKind.Final    -> Final

    // Recursively convert child states
    let childResults     = state.Children |> List.map toStateNodeAndTransitions
    let childNodes       = childResults |> List.map fst
    let childTransitions = childResults |> List.collect snd

    // Convert history nodes to child StateNodes carrying a ScxmlHistory annotation
    let historyNodes =
        state.HistoryNodes
        |> List.map (fun h ->
            let historyKind =
                match h.Kind with
                | ScxmlHistoryKind.Shallow -> ShallowHistory
                | ScxmlHistoryKind.Deep    -> DeepHistory

            { Identifier  = h.Id
              Label        = None
              Kind         = historyKind
              Children     = []
              Activities   = None
              Position     = h.Position |> Option.map toAstPosition
              Annotations  =
                  [ ScxmlAnnotation(ScxmlHistory(h.Id, toAstHistoryKind h.Kind)) ] })

    // Convert invoke nodes to ScxmlAnnotation entries
    let invokeAnnotations =
        state.InvokeNodes
        |> List.map (fun inv ->
            let invokeType = inv.InvokeType |> Option.defaultValue ""
            ScxmlAnnotation(ScxmlInvoke(invokeType, inv.Src)))

    let stateNode =
        { Identifier  = stateId
          Label        = None
          Kind         = astKind
          Children     = childNodes @ historyNodes
          Activities   = None
          Position     = state.Position |> Option.map toAstPosition
          Annotations  = invokeAnnotations }

    // State transitions become separate TransitionEdge entries, not children
    let ownTransitions = state.Transitions |> List.map (toTransitionEdge stateId)

    stateNode, ownTransitions @ childTransitions

/// Recursively collect all DataEntry values from a state hierarchy plus the
/// document-level entries.
let private collectAllDataEntries
    (docEntries : ScxmlDataE list)
    (states     : ScxmlState list)
    : AstDataE list =

    let rec collectFromState (s: ScxmlState) =
        s.DataEntries @ (s.Children |> List.collect collectFromState)

    let allScxmlEntries =
        docEntries @ (states |> List.collect collectFromState)

    allScxmlEntries |> List.map toAstDataEntry

// ---------------------------------------------------------------------------
// Error / warning conversion helpers (toStatechartDocument direction)
// ---------------------------------------------------------------------------

/// Convert a SCXML ParseError to a shared AST ParseFailure.
let private toParseFailure (err: ScxmlErr) : AstFailure =
    { Position          = err.Position |> Option.map toAstPosition
      Description       = err.Description
      Expected          = ""
      Found             = ""
      CorrectiveExample = "" }

/// Convert a SCXML ParseWarning to a shared AST ParseWarning.
let private toParseWarning (warn: ScxmlWarn) : AstWarn =
    { AstWarn.Position    = warn.Position |> Option.map toAstPosition
      AstWarn.Description = warn.Description
      AstWarn.Suggestion  = warn.Suggestion }

// ---------------------------------------------------------------------------
// toStatechartDocument
// ---------------------------------------------------------------------------

/// Convert a SCXML ScxmlParseResult to the shared Ast.ParseResult.
/// When Document is None an empty StatechartDocument is returned so that the
/// result type is always populated (matching Ast.ParseResult contract).
let toStatechartDocument (result: ScxmlParseResult) : ParseResult =
    let errors   = result.Errors   |> List.map toParseFailure
    let warnings = result.Warnings |> List.map toParseWarning

    let doc =
        match result.Document with
        | None ->
            { Title          = None
              InitialStateId = None
              Elements       = []
              DataEntries    = []
              Annotations    = [] }
        | Some scxmlDoc ->
            let stateResults     = scxmlDoc.States |> List.map toStateNodeAndTransitions
            let stateNodes       = stateResults |> List.map fst
            let allTransitions   = stateResults |> List.collect snd

            let stateElements      = stateNodes    |> List.map StateDecl
            let transitionElements = allTransitions |> List.map TransitionElement

            let dataEntries =
                collectAllDataEntries scxmlDoc.DataEntries scxmlDoc.States

            { Title          = scxmlDoc.Name
              InitialStateId = scxmlDoc.InitialId
              Elements       = stateElements @ transitionElements
              DataEntries    = dataEntries
              Annotations    = [] }

    { Document = doc
      Errors   = errors
      Warnings = warnings }

// ---------------------------------------------------------------------------
// fromStatechartDocument helpers
// ---------------------------------------------------------------------------

/// Try to extract ScxmlHistory metadata from an Annotation list.
let private tryGetScxmlHistoryMeta
    (annotations: Annotation list)
    : (string * HistoryKind) option =
    annotations
    |> List.tryPick (fun ann ->
        match ann with
        | ScxmlAnnotation(ScxmlHistory(id, kind)) -> Some(id, kind)
        | _ -> None)

/// Convert a shared AST TransitionEdge back to a SCXML ScxmlTransition.
let private fromTransitionEdge (t: TransitionEdge) : ScxmlTransition =
    { Event          = t.Event
      Guard          = t.Guard
      Targets        = t.Target |> Option.toList
      TransitionType = External
      Position       = t.Position |> Option.map fromAstPosition }

/// Recursively convert a shared AST StateNode back to a SCXML ScxmlState.
/// Transitions for this state are looked up from the provided source-keyed map.
let rec private fromStateNode
    (transitionsBySource: Map<string, TransitionEdge list>)
    (state: StateNode)
    : ScxmlState =

    // Separate history child nodes from regular child nodes
    let isHistoryKind k =
        match k with
        | ShallowHistory | DeepHistory -> true
        | _ -> false

    let historyChildren, regularChildren =
        state.Children |> List.partition (fun c -> isHistoryKind c.Kind)

    // Reconstruct ScxmlHistory records from history child nodes
    let historyNodes =
        historyChildren
        |> List.map (fun h ->
            let kind =
                match h.Kind with
                | ShallowHistory -> ScxmlHistoryKind.Shallow
                | DeepHistory    -> ScxmlHistoryKind.Deep
                | _ -> ScxmlHistoryKind.Shallow // unreachable — guarded above

            // Use id from ScxmlHistory annotation when available;
            // fall back to the node's Identifier.
            let id =
                match tryGetScxmlHistoryMeta h.Annotations with
                | Some(annotationId, _) -> annotationId
                | None -> h.Identifier

            { Id               = id
              Kind              = kind
              DefaultTransition = None
              Position          = h.Position |> Option.map fromAstPosition })

    // Convert regular children recursively
    let childStates =
        regularChildren
        |> List.map (fromStateNode transitionsBySource)

    // Look up transitions that originate from this state
    let ownTransitions =
        transitionsBySource
        |> Map.tryFind state.Identifier
        |> Option.defaultValue []
        |> List.map fromTransitionEdge

    // Reconstruct ScxmlInvoke records from ScxmlAnnotation(ScxmlInvoke) annotations
    let invokeNodes =
        state.Annotations
        |> List.choose (fun ann ->
            match ann with
            | ScxmlAnnotation(ScxmlInvoke(invokeType, src)) ->
                Some
                    { InvokeType = if invokeType = "" then None else Some invokeType
                      Src        = src
                      Id         = None
                      Position   = None }
            | _ -> None)

    // Map Ast.StateKind → ScxmlStateKind
    // Regular/Initial/etc. use <state>; distinguish Simple vs Compound by child count.
    let scxmlKind =
        match state.Kind with
        | Final    -> ScxmlStateKind.Final
        | Parallel -> ScxmlStateKind.Parallel
        | _ ->
            if childStates |> List.isEmpty |> not then
                ScxmlStateKind.Compound
            else
                ScxmlStateKind.Simple

    { Id          = if state.Identifier = "" then None else Some state.Identifier
      Kind         = scxmlKind
      InitialId    = None
      Transitions  = ownTransitions
      Children     = childStates
      DataEntries  = []
      HistoryNodes = historyNodes
      InvokeNodes  = invokeNodes
      Position     = state.Position |> Option.map fromAstPosition }

// ---------------------------------------------------------------------------
// fromStatechartDocument
// ---------------------------------------------------------------------------

/// Convert a shared Ast.StatechartDocument back to a SCXML ScxmlDocument.
let fromStatechartDocument (doc: StatechartDocument) : ScxmlDocument =
    // Extract top-level StateNodes
    let stateNodes =
        doc.Elements
        |> List.choose (fun el ->
            match el with
            | StateDecl s -> Some s
            | _ -> None)

    // Extract all TransitionEdges and group them by source state id
    let transitionsBySource =
        doc.Elements
        |> List.choose (fun el ->
            match el with
            | TransitionElement t -> Some t
            | _ -> None)
        |> List.groupBy (fun t -> t.Source)
        |> Map.ofList

    // Convert top-level states recursively
    let scxmlStates =
        stateNodes
        |> List.map (fromStateNode transitionsBySource)

    // Convert document-level DataEntries
    let docDataEntries = doc.DataEntries |> List.map fromAstDataEntry

    { Name          = doc.Title
      InitialId     = doc.InitialStateId
      DatamodelType = None
      Binding       = None
      States        = scxmlStates
      DataEntries   = docDataEntries
      Position      = None }
