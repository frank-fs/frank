module internal Frank.Statecharts.Alps.Classification

open Frank.Statecharts.Ast

// ---------------------------------------------------------------------------
// Shared intermediate types for ALPS parsing passes (JSON and XML)
// ---------------------------------------------------------------------------

/// Intermediate type for ALPS parsing pass.
type ParsedDescriptor =
    { Id: string option
      Type: string option // raw string, not DU
      Href: string option
      ReturnType: string option
      DocFormat: string option
      DocValue: string option
      Children: ParsedDescriptor list
      Extensions: ParsedExtension list
      Links: ParsedLink list }

and ParsedExtension =
    { Id: string
      Href: string option
      Value: string option }

and ParsedLink =
    { Rel: string
      Href: string }

// ---------------------------------------------------------------------------
// Pass 2: State classification heuristics (T005, ported from Mapper.fs)
// ---------------------------------------------------------------------------

/// Check whether a raw type string represents a transition type.
let isTransitionTypeStr (typeStr: string option) =
    match typeStr with
    | Some "safe" | Some "unsafe" | Some "idempotent" -> true
    | _ -> false

/// Collect the set of descriptor ids that are referenced as rt targets.
let collectRtTargets (descriptors: ParsedDescriptor list) : Set<string> =
    let rec collect (acc: Set<string>) (descs: ParsedDescriptor list) =
        descs
        |> List.fold
            (fun s d ->
                let s' =
                    match d.ReturnType with
                    | Some rt ->
                        let target = if rt.StartsWith("#") then rt.Substring(1) else rt
                        Set.add target s
                    | None -> s

                collect s' d.Children)
            acc

    collect Set.empty descriptors

/// A top-level semantic descriptor is a "state" if it contains
/// transition-type children, is referenced as an rt target,
/// or has href-only children.
let isStateDescriptor (rtTargets: Set<string>) (d: ParsedDescriptor) =
    (d.Type.IsNone || d.Type = Some "semantic")
    && d.Id.IsSome
    && (d.Children |> List.exists (fun c -> isTransitionTypeStr c.Type)
        || Set.contains d.Id.Value rtTargets
        || d.Children |> List.exists (fun c -> c.Href.IsSome && c.Id.IsNone))

/// Build a map from descriptor id to ParsedDescriptor for href resolution.
let buildDescriptorIndex (descriptors: ParsedDescriptor list) : Map<string, ParsedDescriptor> =
    let rec collectAll (acc: Map<string, ParsedDescriptor>) (descs: ParsedDescriptor list) =
        descs
        |> List.fold
            (fun m d ->
                let m' =
                    match d.Id with
                    | Some id -> Map.add id d m
                    | None -> m

                collectAll m' d.Children)
            acc

    collectAll Map.empty descriptors

// ---------------------------------------------------------------------------
// ALPS extension vocabulary IDs (T008)
// ---------------------------------------------------------------------------

[<Literal>]
let GuardExtId = "guard"

[<Literal>]
let ProjectedRoleExtId = "projectedRole"

[<Literal>]
let ProtocolStateExtId = "protocolState"

[<Literal>]
let AvailableInStatesExtId = "availableInStates"

[<Literal>]
let ClientObligationExtId = "clientObligation"

[<Literal>]
let AdvancesProtocolExtId = "advancesProtocol"

[<Literal>]
let DualOfExtId = "dualOf"

[<Literal>]
let CutPointExtId = "cutPoint"

// ---------------------------------------------------------------------------
// Transition extraction (T006, ported from Mapper.fs)
// ---------------------------------------------------------------------------

/// Strip '#' prefix from a local ALPS reference.
let resolveRt (rt: string option) : string option =
    rt |> Option.map (fun r -> if r.StartsWith("#") then r.Substring(1) else r)

/// Extract a guard label from ext elements (first ext with id="guard").
let extractGuard (exts: ParsedExtension list) : string option =
    exts
    |> List.tryFind (fun e -> e.Id = GuardExtId)
    |> Option.bind (fun e -> e.Value)

/// Extract parameter descriptor ids from a descriptor's children.
/// Parameters are children that are href-only references (no id, no type).
let extractParameters (children: ParsedDescriptor list) : string list =
    children
    |> List.choose (fun d ->
        match d.Href, d.Id with
        | Some href, None -> Some(if href.StartsWith("#") then href.Substring(1) else href)
        | _ -> None)

/// Map raw type string to AlpsTransitionKind.
let toTransitionKind (typeStr: string option) : AlpsTransitionKind =
    match typeStr with
    | Some "safe" -> AlpsTransitionKind.Safe
    | Some "idempotent" -> AlpsTransitionKind.Idempotent
    | _ -> AlpsTransitionKind.Unsafe // default

// ---------------------------------------------------------------------------
// Extension classification (T008)
// ---------------------------------------------------------------------------

/// Classify a parsed extension into a typed AlpsMeta DU case.
/// Known extension ids get typed cases; unknown fall back to AlpsExtension.
/// Note: these extension types do not carry href in the ALPS extension vocabulary;
/// href is preserved only for unknown extensions via AlpsExtension fallback.
let classifyExtension (ext: ParsedExtension) : Annotation =
    let value = ext.Value |> Option.defaultValue ""

    match ext.Id with
    | GuardExtId -> AlpsAnnotation(AlpsGuardExt value)
    | ProjectedRoleExtId | ProtocolStateExtId -> AlpsAnnotation(AlpsRole(ext.Id, value))
    | AvailableInStatesExtId ->
        let states =
            value.Split(',', System.StringSplitOptions.RemoveEmptyEntries ||| System.StringSplitOptions.TrimEntries)
            |> Array.toList

        AlpsAnnotation(AlpsAvailableInStates states)
    | ClientObligationExtId | AdvancesProtocolExtId | DualOfExtId | CutPointExtId ->
        AlpsAnnotation(AlpsDuality(ext.Id, value))
    | _ -> AlpsAnnotation(AlpsExtension(ext.Id, ext.Href, ext.Value))

// ---------------------------------------------------------------------------
// Annotation extraction (T007)
// ---------------------------------------------------------------------------

/// Build state-level annotations from a parsed descriptor.
let buildStateAnnotations (d: ParsedDescriptor) : Annotation list =
    let docAnnotation =
        match d.DocValue with
        | Some value -> [ AlpsAnnotation(AlpsDocumentation(d.DocFormat, value)) ]
        | None -> []

    let extAnnotations =
        d.Extensions |> List.map classifyExtension

    let linkAnnotations =
        d.Links
        |> List.map (fun l -> AlpsAnnotation(AlpsLink(l.Rel, l.Href)))

    docAnnotation @ extAnnotations @ linkAnnotations

/// Build transition-level annotations from a resolved descriptor and original child.
let buildTransitionAnnotations
    (kind: AlpsTransitionKind)
    (originalChild: ParsedDescriptor)
    (resolved: ParsedDescriptor)
    : Annotation list =
    // 1. AlpsTransitionType -- always first
    let typeAnnotation = [ AlpsAnnotation(AlpsTransitionType kind) ]

    // 2. AlpsDescriptorHref -- if original child was href-only
    let hrefAnnotation =
        match originalChild.Href, originalChild.Id with
        | Some href, None -> [ AlpsAnnotation(AlpsDescriptorHref href) ]
        | _ -> []

    // 3. AlpsDocumentation -- if present on resolved descriptor
    let docAnnotation =
        match resolved.DocValue with
        | Some value -> [ AlpsAnnotation(AlpsDocumentation(resolved.DocFormat, value)) ]
        | None -> []

    // 4. Typed extensions -- in document order, excluding guards
    //    (guards flow through TransitionEdge.Guard; AlpsGuardExt is for state/doc-level only)
    let extAnnotations =
        resolved.Extensions
        |> List.filter (fun e -> e.Id <> GuardExtId)
        |> List.map classifyExtension

    typeAnnotation @ hrefAnnotation @ docAnnotation @ extAnnotations

// ---------------------------------------------------------------------------
// Transition and state building
// ---------------------------------------------------------------------------

/// Resolve an href-only descriptor to the actual descriptor in the index.
let resolveDescriptor (index: Map<string, ParsedDescriptor>) (d: ParsedDescriptor) : ParsedDescriptor option =
    match d.Href, d.Id with
    | Some href, None ->
        let resolvedId = if href.StartsWith("#") then href.Substring(1) else href
        Map.tryFind resolvedId index
    | _ -> Some d

/// Extract transitions from a state descriptor's children.
let extractTransitions
    (index: Map<string, ParsedDescriptor>)
    (stateId: string)
    (children: ParsedDescriptor list)
    : TransitionEdge list =
    children
    |> List.choose (fun child ->
        let resolved = resolveDescriptor index child

        match resolved with
        | Some r when isTransitionTypeStr r.Type ->
            let kind = toTransitionKind r.Type

            Some
                { Source = stateId
                  Target = resolveRt r.ReturnType
                  Event = r.Id
                  Guard = extractGuard r.Extensions
                  Action = None
                  Parameters = extractParameters r.Children
                  Position = None
                  Annotations = buildTransitionAnnotations kind child r }
        | _ -> None)

/// Convert a state descriptor to a StateNode.
let toStateNode (d: ParsedDescriptor) : StateNode =
    { Identifier = d.Id
      Label = d.DocValue
      Kind = StateKind.Regular
      Children = []
      Activities = None
      Position = None
      Annotations = buildStateAnnotations d }

// ---------------------------------------------------------------------------
// Pass 2 entry point: shared between JSON and XML parsers
// ---------------------------------------------------------------------------

/// Classify parsed descriptors into a StatechartDocument.
/// Pass 2 of the ALPS parsing pipeline, shared between JSON and XML parsers.
let classifyDescriptors
    (descriptors: ParsedDescriptor list)
    (version: string option)
    (rootDocFormat: string option)
    (rootDocValue: string option)
    (rootLinks: ParsedLink list)
    (rootExtensions: ParsedExtension list)
    : StatechartDocument =
    let rtTargets = collectRtTargets descriptors
    let index = buildDescriptorIndex descriptors

    let stateDescriptors =
        descriptors |> List.filter (isStateDescriptor rtTargets)

    let nonStateDescriptors =
        descriptors
        |> List.filter (fun d ->
            d.Id.IsSome
            && not (isStateDescriptor rtTargets d)
            && (d.Type.IsNone || d.Type = Some "semantic"))

    // Build state elements
    let stateElements =
        stateDescriptors |> List.map (fun d -> StateDecl(toStateNode d))

    // Build transition elements from each state's children
    let transitionElements =
        stateDescriptors
        |> List.collect (fun d ->
            let stateId = d.Id |> Option.defaultValue ""
            extractTransitions index stateId d.Children)
        |> List.map TransitionElement

    // Build document-level annotations
    let versionAnnotation =
        match version with
        | Some v -> [ AlpsAnnotation(AlpsVersion v) ]
        | None -> []

    let docAnnotation =
        match rootDocValue with
        | Some value -> [ AlpsAnnotation(AlpsDocumentation(rootDocFormat, value)) ]
        | None -> []

    let linkAnnotations =
        rootLinks
        |> List.map (fun l -> AlpsAnnotation(AlpsLink(l.Rel, l.Href)))

    let extAnnotations =
        rootExtensions |> List.map classifyExtension

    let dataDescriptorAnnotations =
        nonStateDescriptors
        |> List.map (fun d ->
            let doc =
                match d.DocValue with
                | Some value -> Some(d.DocFormat, value)
                | None -> None

            AlpsAnnotation(AlpsDataDescriptor(d.Id.Value, doc)))

    let documentAnnotations =
        versionAnnotation
        @ docAnnotation
        @ linkAnnotations
        @ extAnnotations
        @ dataDescriptorAnnotations

    { Title = rootDocValue
      InitialStateId = None
      Elements = stateElements @ transitionElements
      DataEntries = []
      Annotations = documentAnnotations }
