module internal Frank.Statecharts.Alps.JsonGenerator

open System.IO
open System.Text
open System.Text.Json
open Frank.Statecharts.Ast
open Frank.Statecharts.Alps.GeneratorCommon

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
        | Some guard -> (Classification.GuardExtId, None, Some guard) :: nonGuardExts
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
        transitions |> List.groupBy (fun t -> t.Source) |> Map.ofList

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
                        let href = tryGetDescriptorHref t |> Option.defaultValue ("#" + eventName)

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
