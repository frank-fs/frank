module internal Frank.Statecharts.Smcat.Mapper

// Open smcat types first, then Ast last so that the shared Ast names
// (SourcePosition, ParseResult, ParseFailure, ParseWarning, TransitionElement,
// StateDecl, StatechartElement) win when unqualified.  Smcat-only names that
// shadow Ast names are referenced with the full Frank.Statecharts.Smcat.Types prefix.
open Frank.Statecharts.Smcat.Types
open Frank.Statecharts.Ast

// ---------------------------------------------------------------------------
// Helper functions
// ---------------------------------------------------------------------------

/// Convert a smcat SourcePosition (always present, never option) to a shared
/// AST SourcePosition option.  The smcat type is referenced fully-qualified
/// because opening Ast last means unqualified "SourcePosition" is the Ast one.
let private toAstPosition
    (pos: Frank.Statecharts.Smcat.Types.SourcePosition)
    : SourcePosition option =
    Some { Line = pos.Line; Column = pos.Column }

/// Convert a shared AST SourcePosition option back to a smcat SourcePosition.
/// Uses {Line=0; Column=0} when the option is None.
let private fromAstPosition
    (pos: SourcePosition option)
    : Frank.Statecharts.Smcat.Types.SourcePosition =
    match pos with
    | Some p ->
        { Frank.Statecharts.Smcat.Types.SourcePosition.Line   = p.Line
          Frank.Statecharts.Smcat.Types.SourcePosition.Column = p.Column }
    | None ->
        { Frank.Statecharts.Smcat.Types.SourcePosition.Line   = 0
          Frank.Statecharts.Smcat.Types.SourcePosition.Column = 0 }

/// Map smcat StateType to shared AST StateKind.
let private toStateKind (st: StateType) : StateKind =
    match st with
    | StateType.Regular        -> StateKind.Regular
    | StateType.Initial        -> StateKind.Initial
    | StateType.Final          -> StateKind.Final
    | StateType.ShallowHistory -> StateKind.ShallowHistory
    | StateType.DeepHistory    -> StateKind.DeepHistory
    | StateType.Choice         -> StateKind.Choice
    | StateType.ForkJoin       -> StateKind.ForkJoin
    | StateType.Terminate      -> StateKind.Terminate

/// Map shared AST StateKind back to smcat StateType.
/// StateKind.Parallel has no smcat equivalent; falls back to Regular.
let private fromStateKind (kind: StateKind) : StateType =
    match kind with
    | StateKind.Regular        -> StateType.Regular
    | StateKind.Initial        -> StateType.Initial
    | StateKind.Final          -> StateType.Final
    | StateKind.ShallowHistory -> StateType.ShallowHistory
    | StateKind.DeepHistory    -> StateType.DeepHistory
    | StateKind.Choice         -> StateType.Choice
    | StateKind.ForkJoin       -> StateType.ForkJoin
    | StateKind.Terminate      -> StateType.Terminate
    | StateKind.Parallel       -> StateType.Regular // No smcat parallel; degrade to Regular

/// Map a smcat StateActivity option to a shared AST StateActivities option.
/// Each single-valued option field becomes a zero-or-one-element list.
let private toStateActivities (activity: StateActivity option) : StateActivities option =
    match activity with
    | None -> None
    | Some a ->
        let entry  = a.Entry |> Option.map List.singleton |> Option.defaultValue []
        let exit   = a.Exit  |> Option.map List.singleton |> Option.defaultValue []
        let doActs = a.Do    |> Option.map List.singleton |> Option.defaultValue []
        Some { Entry = entry; Exit = exit; Do = doActs }

/// Map shared AST StateActivities option back to smcat StateActivity option.
/// Takes the first element of each list (smcat supports only one per kind).
let private fromStateActivities (activities: StateActivities option) : StateActivity option =
    match activities with
    | None -> None
    | Some a ->
        Some { Entry = a.Entry |> List.tryHead
               Exit  = a.Exit  |> List.tryHead
               Do    = a.Do    |> List.tryHead }

/// Map a smcat SmcatAttribute to a shared AST Annotation.
/// key="color"  -> SmcatAnnotation(SmcatColor)
/// key="label"  -> SmcatAnnotation(SmcatStateLabel)
/// other keys   -> SmcatAnnotation(SmcatActivity(key, value))
let private toAnnotation (attr: SmcatAttribute) : Annotation =
    match attr.Key.ToLowerInvariant() with
    | "color" -> SmcatAnnotation(SmcatColor attr.Value)
    | "label" -> SmcatAnnotation(SmcatStateLabel attr.Value)
    | kind    -> SmcatAnnotation(SmcatActivity(kind, attr.Value))

/// Map a shared AST Annotation back to a smcat SmcatAttribute.
/// Non-smcat annotations are dropped.
let private fromAnnotation (ann: Annotation) : SmcatAttribute option =
    match ann with
    | SmcatAnnotation(SmcatColor v)           -> Some { Key = "color"; Value = v }
    | SmcatAnnotation(SmcatStateLabel v)      -> Some { Key = "label"; Value = v }
    | SmcatAnnotation(SmcatActivity(kind, b)) -> Some { Key = kind;    Value = b }
    | _                                        -> None

// ---------------------------------------------------------------------------
// toStatechartDocument
// ---------------------------------------------------------------------------

/// Recursively convert a smcat SmcatDocument to a list of shared AST StatechartElements.
/// CommentElements are dropped (no AST representation).
let rec private toAstElements (doc: SmcatDocument) : StatechartElement list =
    doc.Elements
    |> List.choose (fun el ->
        match el with
        | StateDeclaration s ->
            let children =
                s.Children
                |> Option.map toChildStateNodes
                |> Option.defaultValue []

            let node : StateNode =
                { Identifier = s.Name
                  Label       = s.Label
                  Kind        = toStateKind s.StateType
                  Children    = children
                  Activities  = toStateActivities s.Activities
                  Position    = toAstPosition s.Position
                  Annotations = s.Attributes |> List.map toAnnotation }

            Some(StateDecl node)

        // Qualify to pick the smcat TransitionElement DU case, not the Ast one.
        | Frank.Statecharts.Smcat.Types.TransitionElement t ->
            let label =
                t.Label
                |> Option.defaultValue { Event = None; Guard = None; Action = None }

            let edge : TransitionEdge =
                { Source      = t.Source
                  Target      = Some t.Target
                  Event       = label.Event
                  Guard       = label.Guard
                  Action      = label.Action
                  Parameters  = []
                  Position    = toAstPosition t.Position
                  Annotations = t.Attributes |> List.map toAnnotation }

            Some(TransitionElement edge)

        | CommentElement _ -> None)

/// Convert a smcat SmcatDocument into a list of child StateNodes (recursive).
/// Nested transitions and comments are not promoted to the child list; only
/// state declarations become StateNode children.
and private toChildStateNodes (doc: SmcatDocument) : StateNode list =
    doc.Elements
    |> List.choose (fun el ->
        match el with
        | StateDeclaration s ->
            let children =
                s.Children
                |> Option.map toChildStateNodes
                |> Option.defaultValue []

            let node : StateNode =
                { Identifier = s.Name
                  Label       = s.Label
                  Kind        = toStateKind s.StateType
                  Children    = children
                  Activities  = toStateActivities s.Activities
                  Position    = toAstPosition s.Position
                  Annotations = s.Attributes |> List.map toAnnotation }

            Some node
        | _ -> None)

/// Convert a smcat-format ParseFailure to a shared AST ParseFailure.
/// Fully-qualified input type to resolve the shadowed name.
let private toAstFailure
    (f: Frank.Statecharts.Smcat.Types.ParseFailure)
    : ParseFailure =
    { Position          = toAstPosition f.Position
      Description       = f.Description
      Expected          = f.Expected
      Found             = f.Found
      CorrectiveExample = f.CorrectiveExample }

/// Convert a smcat-format ParseWarning to a shared AST ParseWarning.
/// Fully-qualified input type to resolve the shadowed name.
let private toAstWarning
    (w: Frank.Statecharts.Smcat.Types.ParseWarning)
    : ParseWarning =
    { Position    = toAstPosition w.Position
      Description = w.Description
      Suggestion  = w.Suggestion }

/// Convert a smcat ParseResult to a shared AST ParseResult.
/// Fully-qualified input type to resolve the shadowed name.
let toStatechartDocument
    (result: Frank.Statecharts.Smcat.Types.ParseResult)
    : ParseResult =
    let elements = toAstElements result.Document

    let doc : StatechartDocument =
        { Title          = None
          InitialStateId = None
          Elements       = elements
          DataEntries    = []
          Annotations    = [] }

    { Document = doc
      Errors   = result.Errors   |> List.map toAstFailure
      Warnings = result.Warnings |> List.map toAstWarning }

// ---------------------------------------------------------------------------
// fromStatechartDocument
// ---------------------------------------------------------------------------

/// Recursively convert a shared AST StateNode back to a smcat StateDeclaration SmcatElement.
let rec private stateNodeToSmcatElement (node: StateNode) : SmcatElement =
    let children : SmcatDocument option =
        match node.Children with
        | [] -> None
        | kids ->
            let childElements = kids |> List.map stateNodeToSmcatElement
            Some { SmcatDocument.Elements = childElements }

    StateDeclaration
        { SmcatState.Name       = node.Identifier
          SmcatState.Label      = node.Label
          SmcatState.StateType  = fromStateKind node.Kind
          SmcatState.Activities = fromStateActivities node.Activities
          SmcatState.Attributes = node.Annotations |> List.choose fromAnnotation
          SmcatState.Children   = children
          SmcatState.Position   = fromAstPosition node.Position }

/// Convert a shared AST TransitionEdge back to a smcat TransitionElement SmcatElement.
/// Transitions with no Target are dropped (smcat requires a concrete target identifier).
let private transitionEdgeToSmcatElement (edge: TransitionEdge) : SmcatElement option =
    match edge.Target with
    | None -> None
    | Some target ->
        let label : TransitionLabel option =
            match edge.Event, edge.Guard, edge.Action with
            | None, None, None -> None
            | e, g, a ->
                Some { TransitionLabel.Event  = e
                       TransitionLabel.Guard  = g
                       TransitionLabel.Action = a }

        Some(
            Frank.Statecharts.Smcat.Types.TransitionElement
                { SmcatTransition.Source     = edge.Source
                  SmcatTransition.Target     = target
                  SmcatTransition.Label      = label
                  SmcatTransition.Attributes = edge.Annotations |> List.choose fromAnnotation
                  SmcatTransition.Position   = fromAstPosition edge.Position })

/// Convert a shared AST StatechartDocument back to a smcat SmcatDocument.
let fromStatechartDocument (doc: StatechartDocument) : SmcatDocument =
    let elements =
        doc.Elements
        |> List.choose (fun el ->
            match el with
            | StateDecl node ->
                Some(stateNodeToSmcatElement node)
            | TransitionElement edge ->
                transitionEdgeToSmcatElement edge
            | _ -> None)

    { SmcatDocument.Elements = elements }
