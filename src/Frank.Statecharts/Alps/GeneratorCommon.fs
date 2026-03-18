module internal Frank.Statecharts.Alps.GeneratorCommon

open Frank.Statecharts.Ast

// ---------------------------------------------------------------------------
// Shared helpers for JSON and XML ALPS generators (Principle VIII)
// ---------------------------------------------------------------------------

/// Extract all StateNodes from document elements.
let extractStateNodes (doc: StatechartDocument) : StateNode list =
    doc.Elements
    |> List.choose (fun el ->
        match el with
        | StateDecl s -> Some s
        | _ -> None)

/// Extract all TransitionEdges from document elements.
let extractTransitionEdges (doc: StatechartDocument) : TransitionEdge list =
    doc.Elements
    |> List.choose (fun el ->
        match el with
        | TransitionElement t -> Some t
        | _ -> None)

/// Extract ALPS version from document annotations, defaulting to "1.0".
let extractVersion (annotations: Annotation list) : string =
    annotations
    |> List.tryPick (fun a ->
        match a with
        | AlpsAnnotation(AlpsVersion v) -> Some v
        | _ -> None)
    |> Option.defaultValue "1.0"

/// Extract transition type string from transition annotations, defaulting to "unsafe".
let transitionTypeStr (t: TransitionEdge) : string =
    t.Annotations
    |> List.tryPick (fun a ->
        match a with
        | AlpsAnnotation(AlpsTransitionType kind) ->
            match kind with
            | AlpsTransitionKind.Safe -> Some "safe"
            | AlpsTransitionKind.Unsafe -> Some "unsafe"
            | AlpsTransitionKind.Idempotent -> Some "idempotent"
        | _ -> None)
    |> Option.defaultValue "unsafe"

/// Try to get the AlpsDescriptorHref annotation from a transition.
let tryGetDescriptorHref (t: TransitionEdge) : string option =
    t.Annotations
    |> List.tryPick (fun ann ->
        match ann with
        | AlpsAnnotation(AlpsDescriptorHref href) -> Some href
        | _ -> None)

/// Check if a transition is a shared transition (has AlpsDescriptorHref).
let isSharedTransition (t: TransitionEdge) : bool =
    tryGetDescriptorHref t |> Option.isSome

/// Extract documentation annotation from an annotation list.
let tryGetDocAnnotation (annotations: Annotation list) : (string option * string) option =
    annotations
    |> List.tryPick (fun a ->
        match a with
        | AlpsAnnotation(AlpsDocumentation(fmt, value)) -> Some(fmt, value)
        | _ -> None)

/// Extract non-guard extension annotations from an annotation list.
let getExtAnnotations (annotations: Annotation list) : (string * string option * string option) list =
    annotations
    |> List.choose (fun a ->
        match a with
        | AlpsAnnotation(AlpsExtension(id, href, value)) -> Some(id, href, value)
        | _ -> None)

/// Extract link annotations from an annotation list.
let getLinkAnnotations (annotations: Annotation list) : (string * string) list =
    annotations
    |> List.choose (fun a ->
        match a with
        | AlpsAnnotation(AlpsLink(rel, href)) -> Some(rel, href)
        | _ -> None)

/// Extract data descriptor annotations from an annotation list.
let getDataDescriptors (annotations: Annotation list) : (string * (string option * string) option) list =
    annotations
    |> List.choose (fun a ->
        match a with
        | AlpsAnnotation(AlpsDataDescriptor(id, doc)) -> Some(id, doc)
        | _ -> None)

/// Re-add '#' prefix to rt values for local references.
let rtValue (target: string option) : string option =
    target
    |> Option.map (fun t ->
        if t.StartsWith("http://") || t.StartsWith("https://") then t
        else "#" + t)

/// Collect shared transitions: group by event name, take the first (canonical) transition.
let collectSharedTransitions (transitions: TransitionEdge list) : (string * TransitionEdge) list =
    transitions
    |> List.filter isSharedTransition
    |> List.choose (fun t ->
        match t.Event with
        | Some eventName -> Some(eventName, t)
        | None -> None)
    |> List.groupBy fst
    |> List.map (fun (eventName, group) -> eventName, group |> List.head |> snd)

/// Get the set of shared transition event names.
let sharedTransitionNames (sharedTransitions: (string * TransitionEdge) list) : Set<string> =
    sharedTransitions |> List.map fst |> Set.ofList
