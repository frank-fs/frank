module internal Frank.Statecharts.Alps.JsonGenerator

open System.IO
open System.Text
open System.Text.Json
open Frank.Statecharts.Ast

// ---------------------------------------------------------------------------
// Helper: Extract StateNodes and TransitionEdges from StatechartDocument
// ---------------------------------------------------------------------------

/// Extract all StateNodes from elements.
let private extractStateNodes (doc: StatechartDocument) : StateNode list =
    doc.Elements
    |> List.choose (fun el ->
        match el with
        | StateDecl s -> Some s
        | _ -> None)

/// Extract all TransitionEdges from elements.
let private extractTransitionEdges (doc: StatechartDocument) : TransitionEdge list =
    doc.Elements
    |> List.choose (fun el ->
        match el with
        | TransitionElement t -> Some t
        | _ -> None)

// ---------------------------------------------------------------------------
// Helper: Annotation extraction
// ---------------------------------------------------------------------------

/// Extract ALPS version from document annotations, defaulting to "1.0".
let private extractVersion (annotations: Annotation list) : string =
    annotations
    |> List.tryPick (fun a ->
        match a with
        | AlpsAnnotation(AlpsVersion v) -> Some v
        | _ -> None)
    |> Option.defaultValue "1.0"

/// Extract transition type string from transition annotations, defaulting to "unsafe".
let private transitionTypeStr (t: TransitionEdge) : string =
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
let private tryGetDescriptorHref (t: TransitionEdge) : string option =
    t.Annotations
    |> List.tryPick (fun ann ->
        match ann with
        | AlpsAnnotation(AlpsDescriptorHref href) -> Some href
        | _ -> None)

/// Check if a transition is a shared transition (has AlpsDescriptorHref).
let private isSharedTransition (t: TransitionEdge) : bool =
    tryGetDescriptorHref t |> Option.isSome

/// Extract documentation annotation from an annotation list.
let private tryGetDocAnnotation (annotations: Annotation list) : (string option * string) option =
    annotations
    |> List.tryPick (fun a ->
        match a with
        | AlpsAnnotation(AlpsDocumentation(fmt, value)) -> Some(fmt, value)
        | _ -> None)

/// Extract non-guard extension annotations from an annotation list.
let private getExtAnnotations (annotations: Annotation list) : (string * string option * string option) list =
    annotations
    |> List.choose (fun a ->
        match a with
        | AlpsAnnotation(AlpsExtension(id, href, value)) -> Some(id, href, value)
        | _ -> None)

/// Extract link annotations from an annotation list.
let private getLinkAnnotations (annotations: Annotation list) : (string * string) list =
    annotations
    |> List.choose (fun a ->
        match a with
        | AlpsAnnotation(AlpsLink(rel, href)) -> Some(rel, href)
        | _ -> None)

/// Extract data descriptor annotations from an annotation list.
let private getDataDescriptors (annotations: Annotation list) : (string * (string option * string) option) list =
    annotations
    |> List.choose (fun a ->
        match a with
        | AlpsAnnotation(AlpsDataDescriptor(id, doc)) -> Some(id, doc)
        | _ -> None)

/// Re-add '#' prefix to rt values for local references.
let private rtValue (target: string option) : string option =
    target
    |> Option.map (fun t ->
        if t.StartsWith("http://") || t.StartsWith("https://") then t
        else "#" + t)

// ---------------------------------------------------------------------------
// JSON writing helpers
// ---------------------------------------------------------------------------

/// Write an ALPS documentation element from annotation data.
let private writeDocAnnotation (writer: Utf8JsonWriter) (annotations: Annotation list) =
    tryGetDocAnnotation annotations
    |> Option.iter (fun (fmt, value) ->
        writer.WritePropertyName("doc")
        writer.WriteStartObject()
        fmt |> Option.iter (fun f -> writer.WriteString("format", f))
        writer.WriteString("value", value)
        writer.WriteEndObject())

/// Write extension elements as a JSON array.
let private writeExtensions (writer: Utf8JsonWriter) (exts: (string * string option * string option) list) =
    if not exts.IsEmpty then
        writer.WritePropertyName("ext")
        writer.WriteStartArray()

        for (id, href, value) in exts do
            writer.WriteStartObject()
            writer.WriteString("id", id)
            href |> Option.iter (fun h -> writer.WriteString("href", h))
            value |> Option.iter (fun v -> writer.WriteString("value", v))
            writer.WriteEndObject()

        writer.WriteEndArray()

/// Write link elements as a JSON array.
let private writeLinks (writer: Utf8JsonWriter) (links: (string * string) list) =
    if not links.IsEmpty then
        writer.WritePropertyName("link")
        writer.WriteStartArray()

        for (rel, href) in links do
            writer.WriteStartObject()
            writer.WriteString("rel", rel)
            writer.WriteString("href", href)
            writer.WriteEndObject()

        writer.WriteEndArray()

/// Write a full transition descriptor (non-shared).
let private writeTransitionDescriptor (writer: Utf8JsonWriter) (t: TransitionEdge) =
    writer.WriteStartObject()

    t.Event |> Option.iter (fun id -> writer.WriteString("id", id))
    writer.WriteString("type", transitionTypeStr t)
    rtValue t.Target |> Option.iter (fun rt -> writer.WriteString("rt", rt))

    // Write transition-level documentation
    writeDocAnnotation writer t.Annotations

    // Write parameter descriptors as nested children
    if not t.Parameters.IsEmpty then
        writer.WritePropertyName("descriptor")
        writer.WriteStartArray()

        for param in t.Parameters do
            writer.WriteStartObject()
            writer.WriteString("href", "#" + param)
            writer.WriteEndObject()

        writer.WriteEndArray()

    // Write extensions: guard first, then other extensions from annotations
    let nonGuardExts = getExtAnnotations t.Annotations

    let extElements =
        match t.Guard with
        | Some guard -> ("guard", None, Some guard) :: nonGuardExts
        | None -> nonGuardExts

    writeExtensions writer extElements

    // Write transition-level links (unusual but possible)
    let transLinks = getLinkAnnotations t.Annotations
    writeLinks writer transLinks

    writer.WriteEndObject()

/// Write a data descriptor as a top-level semantic descriptor.
let private writeDataDescriptor (writer: Utf8JsonWriter) (id: string) (doc: (string option * string) option) =
    writer.WriteStartObject()
    writer.WriteString("id", id)
    writer.WriteString("type", "semantic")

    doc
    |> Option.iter (fun (fmt, value) ->
        writer.WritePropertyName("doc")
        writer.WriteStartObject()
        fmt |> Option.iter (fun f -> writer.WriteString("format", f))
        writer.WriteString("value", value)
        writer.WriteEndObject())

    writer.WriteEndObject()

// ---------------------------------------------------------------------------
// Shared transition deduplication (D-004)
// ---------------------------------------------------------------------------

/// Collect shared transitions: group by event name, take the first (canonical) transition.
let private collectSharedTransitions (transitions: TransitionEdge list) : (string * TransitionEdge) list =
    transitions
    |> List.filter isSharedTransition
    |> List.choose (fun t ->
        match t.Event with
        | Some eventName -> Some(eventName, t)
        | None -> None)
    |> List.groupBy fst
    |> List.map (fun (eventName, group) -> eventName, group |> List.head |> snd)

/// Get the set of shared transition event names.
let private sharedTransitionNames (sharedTransitions: (string * TransitionEdge) list) : Set<string> =
    sharedTransitions |> List.map fst |> Set.ofList

// ---------------------------------------------------------------------------
// Public API
// ---------------------------------------------------------------------------

/// Generate an ALPS JSON string from a StatechartDocument.
let generateAlpsJson (doc: StatechartDocument) : string =
    use stream = new MemoryStream()
    use writer = new Utf8JsonWriter(stream, JsonWriterOptions(Indented = true))

    let states = extractStateNodes doc
    let transitions = extractTransitionEdges doc

    // Group transitions by source state
    let transitionsBySource =
        transitions
        |> List.groupBy (fun t -> t.Source)
        |> Map.ofList

    // Identify shared transitions (D-004)
    let sharedTransitions = collectSharedTransitions transitions
    let sharedNames = sharedTransitionNames sharedTransitions

    // Extract document-level annotations
    let version = extractVersion doc.Annotations
    let dataDescriptors = getDataDescriptors doc.Annotations
    let docLinks = getLinkAnnotations doc.Annotations
    let docExts = getExtAnnotations doc.Annotations

    // Start writing JSON
    writer.WriteStartObject()
    writer.WritePropertyName("alps")
    writer.WriteStartObject()

    // Version
    writer.WriteString("version", version)

    // Document-level documentation
    writeDocAnnotation writer doc.Annotations

    // Descriptors array: data descriptors first, then states, then shared transitions
    let hasDescriptors =
        not dataDescriptors.IsEmpty
        || not states.IsEmpty
        || not sharedTransitions.IsEmpty

    if hasDescriptors then
        writer.WritePropertyName("descriptor")
        writer.WriteStartArray()

        // 1. Data descriptors
        for (id, docOpt) in dataDescriptors do
            writeDataDescriptor writer id docOpt

        // 2. State descriptors
        for state in states do
            writer.WriteStartObject()
            state.Identifier |> Option.iter (fun id -> writer.WriteString("id", id))
            writer.WriteString("type", "semantic")

            // State-level documentation
            writeDocAnnotation writer state.Annotations

            // State's transitions as child descriptors
            let stateTransitions =
                state.Identifier
                |> Option.bind (fun id -> Map.tryFind id transitionsBySource)
                |> Option.defaultValue []

            if not stateTransitions.IsEmpty then
                writer.WritePropertyName("descriptor")
                writer.WriteStartArray()

                for t in stateTransitions do
                    match t.Event with
                    | Some eventName when Set.contains eventName sharedNames ->
                        // Shared transition: emit href-only reference
                        let href =
                            tryGetDescriptorHref t
                            |> Option.defaultValue ("#" + eventName)

                        writer.WriteStartObject()
                        writer.WriteString("href", href)
                        writer.WriteEndObject()
                    | _ ->
                        // Regular transition: emit full descriptor
                        writeTransitionDescriptor writer t

                writer.WriteEndArray()

            // State-level extensions
            let stateExts = getExtAnnotations state.Annotations
            writeExtensions writer stateExts

            // State-level links
            let stateLinks = getLinkAnnotations state.Annotations
            writeLinks writer stateLinks

            writer.WriteEndObject()

        // 3. Shared transition descriptors (top-level)
        for (_eventName, canonical) in sharedTransitions do
            writeTransitionDescriptor writer canonical

        writer.WriteEndArray()

    // Document-level links
    writeLinks writer docLinks

    // Document-level extensions
    writeExtensions writer docExts

    writer.WriteEndObject()
    writer.WriteEndObject()
    writer.Flush()

    Encoding.UTF8.GetString(stream.ToArray())
