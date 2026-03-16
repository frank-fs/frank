module internal Frank.Statecharts.Alps.Mapper

open Frank.Statecharts.Alps.Types
open Frank.Statecharts.Ast

// ---------------------------------------------------------------------------
// Helper functions
// ---------------------------------------------------------------------------

/// Check whether a descriptor type represents a transition (safe/unsafe/idempotent).
let private isTransitionType (dt: DescriptorType) =
    match dt with
    | DescriptorType.Safe
    | DescriptorType.Unsafe
    | DescriptorType.Idempotent -> true
    | DescriptorType.Semantic -> false

/// Map ALPS DescriptorType to shared AST AlpsTransitionKind.
let private toTransitionKind (dt: DescriptorType) : AlpsTransitionKind =
    match dt with
    | DescriptorType.Safe -> AlpsTransitionKind.Safe
    | DescriptorType.Unsafe -> AlpsTransitionKind.Unsafe
    | DescriptorType.Idempotent -> AlpsTransitionKind.Idempotent
    | DescriptorType.Semantic -> failwith "Semantic is not a transition type"

/// Map shared AST AlpsTransitionKind back to ALPS DescriptorType.
let private fromTransitionKind (kind: AlpsTransitionKind) : DescriptorType =
    match kind with
    | AlpsTransitionKind.Safe -> DescriptorType.Safe
    | AlpsTransitionKind.Unsafe -> DescriptorType.Unsafe
    | AlpsTransitionKind.Idempotent -> DescriptorType.Idempotent

/// Strip '#' prefix from a local ALPS reference.
let private resolveRt (rt: string option) : string option =
    rt |> Option.map (fun r -> if r.StartsWith("#") then r.Substring(1) else r)

/// Extract a guard label from ext elements (first ext with id="guard").
let private extractGuard (exts: AlpsExtension list) : string option =
    exts
    |> List.tryFind (fun e -> e.Id = "guard")
    |> Option.bind (fun e -> e.Value)

/// Extract parameter descriptor ids from a descriptor's children.
/// Parameters are children that are href-only references (no id, no type).
let private extractParameters (children: Descriptor list) : string list =
    children
    |> List.choose (fun d ->
        match d.Href, d.Id with
        | Some href, None ->
            // Strip '#' prefix from href references
            Some(if href.StartsWith("#") then href.Substring(1) else href)
        | _ -> None)

// ---------------------------------------------------------------------------
// Identify state descriptors
// ---------------------------------------------------------------------------

/// Collect the set of descriptor ids that are referenced as rt targets.
let private collectRtTargets (descriptors: Descriptor list) : Set<string> =
    let rec collect (acc: Set<string>) (descs: Descriptor list) =
        descs
        |> List.fold
            (fun s d ->
                let s' =
                    match resolveRt d.ReturnType with
                    | Some target -> Set.add target s
                    | None -> s

                collect s' d.Descriptors)
            acc

    collect Set.empty descriptors

/// A top-level semantic descriptor is a "state" if it contains
/// transition-type children OR is referenced as an rt target.
let private isStateDescriptor (rtTargets: Set<string>) (d: Descriptor) =
    d.Type = DescriptorType.Semantic
    && d.Id.IsSome
    && (d.Descriptors |> List.exists (fun child -> isTransitionType child.Type)
        || Set.contains d.Id.Value rtTargets
        // Also include semantic descriptors whose children are only href refs
        // (they reference transitions defined elsewhere, like viewGame)
        || d.Descriptors
           |> List.exists (fun child -> child.Href.IsSome && child.Id.IsNone))

// ---------------------------------------------------------------------------
// Build a lookup of top-level descriptors by id for resolving href references
// ---------------------------------------------------------------------------

/// Build a map from descriptor id to descriptor for all top-level descriptors.
let private buildDescriptorIndex (descriptors: Descriptor list) : Map<string, Descriptor> =
    let rec collectAll (acc: Map<string, Descriptor>) (descs: Descriptor list) =
        descs
        |> List.fold
            (fun m d ->
                let m' =
                    match d.Id with
                    | Some id -> Map.add id d m
                    | None -> m

                collectAll m' d.Descriptors)
            acc

    collectAll Map.empty descriptors

// ---------------------------------------------------------------------------
// toStatechartDocument
// ---------------------------------------------------------------------------

/// Convert an ALPS-specific AlpsDocument to the shared StatechartDocument AST.
let toStatechartDocument (doc: AlpsDocument) : StatechartDocument =
    let rtTargets = collectRtTargets doc.Descriptors
    let index = buildDescriptorIndex doc.Descriptors

    /// Resolve an href-only descriptor to the actual descriptor in the index.
    let resolveDescriptor (d: Descriptor) : Descriptor option =
        match d.Href, d.Id with
        | Some href, None ->
            let resolvedId =
                if href.StartsWith("#") then href.Substring(1) else href

            Map.tryFind resolvedId index
        | _ -> Some d

    /// Extract transitions from a state descriptor's children.
    /// The stateId is the parent state (source of the transition).
    let extractTransitions (stateId: string) (children: Descriptor list) : TransitionEdge list =
        children
        |> List.choose (fun child ->
            // Resolve href references to actual descriptors
            let resolved = resolveDescriptor child

            match resolved with
            | Some r when isTransitionType r.Type ->
                let annotations =
                    [ AlpsAnnotation(AlpsTransitionType(toTransitionKind r.Type)) ]

                // Add href annotation if the original child was an href reference
                let annotations =
                    match child.Href with
                    | Some href when child.Id.IsNone ->
                        annotations @ [ AlpsAnnotation(AlpsDescriptorHref href) ]
                    | _ -> annotations

                Some
                    { Source = stateId
                      Target = resolveRt r.ReturnType
                      Event = r.Id
                      Guard = extractGuard r.Extensions
                      Action = None
                      Parameters = extractParameters r.Descriptors
                      Position = None
                      Annotations = annotations }
            | _ -> None)

    /// Convert a state descriptor to a StateNode.
    let toStateNode (d: Descriptor) : StateNode =
        { Identifier = d.Id |> Option.defaultValue ""
          Label = d.Documentation |> Option.map (fun doc -> doc.Value)
          Kind = StateKind.Regular // ALPS has no state type classification
          Children = [] // ALPS has no nested state hierarchy
          Activities = None
          Position = None
          Annotations = [] }

    // Identify state descriptors
    let stateDescriptors =
        doc.Descriptors |> List.filter (isStateDescriptor rtTargets)

    // Build state elements
    let stateElements =
        stateDescriptors
        |> List.map (fun d -> StateDecl(toStateNode d))

    // Build transition elements from each state's children
    let transitionElements =
        stateDescriptors
        |> List.collect (fun d ->
            let stateId = d.Id |> Option.defaultValue ""
            extractTransitions stateId d.Descriptors)
        |> List.map TransitionElement

    { Title = doc.Documentation |> Option.map (fun d -> d.Value)
      InitialStateId = None // ALPS limitation: no initial state concept
      Elements = stateElements @ transitionElements
      DataEntries = []
      Annotations = [] }

// ---------------------------------------------------------------------------
// fromStatechartDocument
// ---------------------------------------------------------------------------

/// Extract all StateNodes from a StatechartDocument's elements.
let private extractStateNodes (doc: StatechartDocument) : StateNode list =
    doc.Elements
    |> List.choose (fun el ->
        match el with
        | StateDecl s -> Some s
        | _ -> None)

/// Extract all TransitionEdges from a StatechartDocument's elements.
let private extractTransitionEdges (doc: StatechartDocument) : TransitionEdge list =
    doc.Elements
    |> List.choose (fun el ->
        match el with
        | TransitionElement t -> Some t
        | _ -> None)

/// Try to extract the ALPS transition kind from a transition's annotations.
let private tryGetAlpsTransitionKind (annotations: Annotation list) : AlpsTransitionKind option =
    annotations
    |> List.tryPick (fun ann ->
        match ann with
        | AlpsAnnotation(AlpsTransitionType kind) -> Some kind
        | _ -> None)

/// Convert a StatechartDocument back to an ALPS-specific AlpsDocument.
let fromStatechartDocument (doc: StatechartDocument) : AlpsDocument =
    let states = extractStateNodes doc
    let transitions = extractTransitionEdges doc

    // Group transitions by source state
    let transitionsBySource =
        transitions
        |> List.groupBy (fun t -> t.Source)
        |> Map.ofList

    /// Convert a TransitionEdge to an ALPS Descriptor.
    let toTransitionDescriptor (t: TransitionEdge) : Descriptor =
        let descriptorType =
            match tryGetAlpsTransitionKind t.Annotations with
            | Some kind -> fromTransitionKind kind
            | None -> DescriptorType.Unsafe // Default to Unsafe (POST is safest default for state-modifying actions)

        let extensions =
            match t.Guard with
            | Some guard -> [ { Id = "guard"; Href = None; Value = Some guard } ]
            | None -> []

        let paramDescriptors =
            t.Parameters
            |> List.map (fun p ->
                { Id = None
                  Type = DescriptorType.Semantic
                  Href = Some("#" + p)
                  ReturnType = None
                  Documentation = None
                  Descriptors = []
                  Extensions = []
                  Links = [] })

        { Id = t.Event
          Type = descriptorType
          Href = None
          ReturnType = t.Target |> Option.map (fun tgt -> "#" + tgt)
          Documentation = None
          Descriptors = paramDescriptors
          Extensions = extensions
          Links = [] }

    /// Convert a StateNode to an ALPS semantic Descriptor, embedding its transitions.
    let toStateDescriptor (s: StateNode) : Descriptor =
        let stateTransitions =
            transitionsBySource
            |> Map.tryFind s.Identifier
            |> Option.defaultValue []

        let transitionDescriptors =
            stateTransitions |> List.map toTransitionDescriptor

        { Id = Some s.Identifier
          Type = DescriptorType.Semantic
          Href = None
          ReturnType = None
          Documentation = s.Label |> Option.map (fun lbl -> { Format = Some "text"; Value = lbl })
          Descriptors = transitionDescriptors
          Extensions = []
          Links = [] }

    let stateDescriptors = states |> List.map toStateDescriptor

    { Version = Some "1.0"
      Documentation = doc.Title |> Option.map (fun t -> { Format = Some "text"; Value = t })
      Descriptors = stateDescriptors
      Links = []
      Extensions = [] }
