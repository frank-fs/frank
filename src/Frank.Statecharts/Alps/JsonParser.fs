module internal Frank.Statecharts.Alps.JsonParser

open System.Text.Json
open Frank.Statecharts.Ast

// ---------------------------------------------------------------------------
// Private intermediate types for JSON parsing pass (T004)
// ---------------------------------------------------------------------------

/// Private intermediate type for JSON parsing pass.
type private ParsedDescriptor =
    { Id: string option
      Type: string option // raw string, not DU
      Href: string option
      ReturnType: string option
      DocFormat: string option
      DocValue: string option
      Children: ParsedDescriptor list
      Extensions: ParsedExtension list
      Links: ParsedLink list }

and private ParsedExtension =
    { Id: string
      Href: string option
      Value: string option }

and private ParsedLink =
    { Rel: string
      Href: string }

// ---------------------------------------------------------------------------
// Pass 1: JSON to intermediate records
// ---------------------------------------------------------------------------

/// Try to get a string property from a JSON element, returning None if missing.
let private tryGetString (elem: JsonElement) (name: string) : string option =
    match elem.TryGetProperty(name) with
    | true, prop when prop.ValueKind = JsonValueKind.String -> Some(prop.GetString())
    | _ -> None

/// Try to get an array property from a JSON element, returning empty list if missing.
let private tryGetArray (elem: JsonElement) (name: string) : JsonElement list =
    match elem.TryGetProperty(name) with
    | true, prop when prop.ValueKind = JsonValueKind.Array ->
        [ for item in prop.EnumerateArray() -> item ]
    | _ -> []

/// Parse an ALPS extension element to intermediate type.
let private parseExtension (elem: JsonElement) : ParsedExtension =
    { Id = elem.GetProperty("id").GetString()
      Href = tryGetString elem "href"
      Value = tryGetString elem "value" }

/// Parse an ALPS link element to intermediate type.
let private parseLink (elem: JsonElement) : ParsedLink =
    { Rel = elem.GetProperty("rel").GetString()
      Href = elem.GetProperty("href").GetString() }

/// Parse a single descriptor to intermediate type, recursively parsing nested children.
let rec private parseDescriptor (elem: JsonElement) : ParsedDescriptor =
    let docFormat, docValue =
        match elem.TryGetProperty("doc") with
        | true, doc when doc.ValueKind = JsonValueKind.Object ->
            (tryGetString doc "format", Some(doc.GetProperty("value").GetString()))
        | _ -> (None, None)

    { Id = tryGetString elem "id"
      Type = tryGetString elem "type"
      Href = tryGetString elem "href"
      ReturnType = tryGetString elem "rt"
      DocFormat = docFormat
      DocValue = docValue
      Children = tryGetArray elem "descriptor" |> List.map parseDescriptor
      Extensions = tryGetArray elem "ext" |> List.map parseExtension
      Links = tryGetArray elem "link" |> List.map parseLink }

// ---------------------------------------------------------------------------
// Pass 2: State classification heuristics (T005, ported from Mapper.fs)
// ---------------------------------------------------------------------------

/// Check whether a raw type string represents a transition type.
let private isTransitionTypeStr (typeStr: string option) =
    match typeStr with
    | Some "safe" | Some "unsafe" | Some "idempotent" -> true
    | _ -> false

/// Collect the set of descriptor ids that are referenced as rt targets.
let private collectRtTargets (descriptors: ParsedDescriptor list) : Set<string> =
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
let private isStateDescriptor (rtTargets: Set<string>) (d: ParsedDescriptor) =
    (d.Type.IsNone || d.Type = Some "semantic")
    && d.Id.IsSome
    && (d.Children |> List.exists (fun c -> isTransitionTypeStr c.Type)
        || Set.contains d.Id.Value rtTargets
        || d.Children |> List.exists (fun c -> c.Href.IsSome && c.Id.IsNone))

/// Build a map from descriptor id to ParsedDescriptor for href resolution.
let private buildDescriptorIndex (descriptors: ParsedDescriptor list) : Map<string, ParsedDescriptor> =
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
// Transition extraction (T006, ported from Mapper.fs)
// ---------------------------------------------------------------------------

/// Strip '#' prefix from a local ALPS reference.
let private resolveRt (rt: string option) : string option =
    rt |> Option.map (fun r -> if r.StartsWith("#") then r.Substring(1) else r)

/// Extract a guard label from ext elements (first ext with id="guard").
let private extractGuard (exts: ParsedExtension list) : string option =
    exts
    |> List.tryFind (fun e -> e.Id = "guard")
    |> Option.bind (fun e -> e.Value)

/// Extract parameter descriptor ids from a descriptor's children.
/// Parameters are children that are href-only references (no id, no type).
let private extractParameters (children: ParsedDescriptor list) : string list =
    children
    |> List.choose (fun d ->
        match d.Href, d.Id with
        | Some href, None -> Some(if href.StartsWith("#") then href.Substring(1) else href)
        | _ -> None)

/// Map raw type string to AlpsTransitionKind.
let private toTransitionKind (typeStr: string option) : AlpsTransitionKind =
    match typeStr with
    | Some "safe" -> AlpsTransitionKind.Safe
    | Some "idempotent" -> AlpsTransitionKind.Idempotent
    | _ -> AlpsTransitionKind.Unsafe // default

// ---------------------------------------------------------------------------
// Annotation extraction (T007)
// ---------------------------------------------------------------------------

/// Build state-level annotations from a parsed descriptor.
let private buildStateAnnotations (d: ParsedDescriptor) : Annotation list =
    let docAnnotation =
        match d.DocValue with
        | Some value -> [ AlpsAnnotation(AlpsDocumentation(d.DocFormat, value)) ]
        | None -> []

    let extAnnotations =
        d.Extensions
        |> List.map (fun e -> AlpsAnnotation(AlpsExtension(e.Id, e.Href, e.Value)))

    let linkAnnotations =
        d.Links
        |> List.map (fun l -> AlpsAnnotation(AlpsLink(l.Rel, l.Href)))

    docAnnotation @ extAnnotations @ linkAnnotations

/// Build transition-level annotations from a resolved descriptor and original child.
let private buildTransitionAnnotations
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

    // 4. AlpsExtension -- in document order, excluding guards
    let extAnnotations =
        resolved.Extensions
        |> List.filter (fun e -> e.Id <> "guard")
        |> List.map (fun e -> AlpsAnnotation(AlpsExtension(e.Id, e.Href, e.Value)))

    typeAnnotation @ hrefAnnotation @ docAnnotation @ extAnnotations

// ---------------------------------------------------------------------------
// Transition and state building
// ---------------------------------------------------------------------------

/// Resolve an href-only descriptor to the actual descriptor in the index.
let private resolveDescriptor (index: Map<string, ParsedDescriptor>) (d: ParsedDescriptor) : ParsedDescriptor option =
    match d.Href, d.Id with
    | Some href, None ->
        let resolvedId = if href.StartsWith("#") then href.Substring(1) else href
        Map.tryFind resolvedId index
    | _ -> Some d

/// Extract transitions from a state descriptor's children.
let private extractTransitions
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
let private toStateNode (d: ParsedDescriptor) : StateNode =
    { Identifier = d.Id |> Option.defaultValue ""
      Label = d.DocValue
      Kind = StateKind.Regular
      Children = []
      Activities = None
      Position = None
      Annotations = buildStateAnnotations d }

// ---------------------------------------------------------------------------
// Public API (T008)
// ---------------------------------------------------------------------------

/// An empty StatechartDocument used as best-effort in error cases.
let private emptyDoc : StatechartDocument =
    { Title = None
      InitialStateId = None
      Elements = []
      DataEntries = []
      Annotations = [] }

/// Parse an ALPS JSON document into a shared AST ParseResult.
let parseAlpsJson (json: string) : ParseResult =
    try
        use doc = JsonDocument.Parse(json)
        let root = doc.RootElement

        if root.ValueKind <> JsonValueKind.Object then
            { Document = emptyDoc
              Errors =
                  [ { Position = None
                      Description = "Expected JSON object at root, got " + root.ValueKind.ToString()
                      Expected = "JSON object"
                      Found = root.ValueKind.ToString()
                      CorrectiveExample = """{"alps": {"version": "1.0", "descriptor": [...]}}""" } ]
              Warnings = [] }
        else
            match root.TryGetProperty("alps") with
            | true, alps ->
                // -- Pass 1: Parse JSON to intermediate records --
                let version = tryGetString alps "version"

                let rootDocFormat, rootDocValue =
                    match alps.TryGetProperty("doc") with
                    | true, docElem when docElem.ValueKind = JsonValueKind.Object ->
                        (tryGetString docElem "format", Some(docElem.GetProperty("value").GetString()))
                    | _ -> (None, None)

                let rootLinks = tryGetArray alps "link" |> List.map parseLink
                let rootExtensions = tryGetArray alps "ext" |> List.map parseExtension
                let descriptors = tryGetArray alps "descriptor" |> List.map parseDescriptor

                // -- Pass 2: Classify descriptors and build StatechartDocument --
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
                    rootExtensions
                    |> List.map (fun e -> AlpsAnnotation(AlpsExtension(e.Id, e.Href, e.Value)))

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

                let statechartDoc =
                    { Title = rootDocValue
                      InitialStateId = None
                      Elements = stateElements @ transitionElements
                      DataEntries = []
                      Annotations = documentAnnotations }

                { Document = statechartDoc
                  Errors = []
                  Warnings = [] }

            | false, _ ->
                { Document = emptyDoc
                  Errors =
                      [ { Position = None
                          Description = "Missing 'alps' root object"
                          Expected = "'alps' property"
                          Found = "JSON object without 'alps'"
                          CorrectiveExample = """{"alps": {"version": "1.0", "descriptor": [...]}}""" } ]
                  Warnings = [] }
    with :? JsonException as ex ->
        { Document = emptyDoc
          Errors =
              [ { Position = None
                  Description = ex.Message
                  Expected = "valid JSON"
                  Found = "malformed JSON"
                  CorrectiveExample = """{"alps": {"version": "1.0", "descriptor": [...]}}""" } ]
          Warnings = [] }
