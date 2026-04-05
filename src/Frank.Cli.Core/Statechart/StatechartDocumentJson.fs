module Frank.Cli.Core.Statechart.StatechartDocumentJson

open System.IO
open System.Text
open System.Text.Json
open Frank.Statecharts.Ast
open Frank.Statecharts.Validation
open Frank.Cli.Core.Statechart.FormatDetector

let private stateKindToString (kind: StateKind) : string =
    match kind with
    | Regular -> "Regular"
    | Initial -> "Initial"
    | Final -> "Final"
    | Parallel -> "Parallel"
    | ShallowHistory -> "ShallowHistory"
    | DeepHistory -> "DeepHistory"
    | Choice -> "Choice"
    | ForkJoin -> "ForkJoin"
    | Terminate -> "Terminate"
    | Composite -> "Composite"

let private writeOptional (w: Utf8JsonWriter) (name: string) (value: string option) =
    match value with
    | Some v -> w.WriteString(name, v)
    | None -> w.WriteNull(name)

/// Write a string property only when Some; omit entirely when None.
let private writePresent (w: Utf8JsonWriter) (name: string) (value: string option) =
    value |> Option.iter (fun v -> w.WriteString(name, v))

let rec private writeStateNode (w: Utf8JsonWriter) (state: StateNode) =
    w.WriteStartObject()
    w.WriteString("type", "state")
    writeOptional w "identifier" state.Identifier
    w.WriteString("kind", stateKindToString state.Kind)
    writeOptional w "label" state.Label

    if not state.Children.IsEmpty then
        w.WriteStartArray("children")

        for child in state.Children do
            writeStateNode w child

        w.WriteEndArray()

    w.WriteEndObject()

let private writeTransition (w: Utf8JsonWriter) (t: TransitionEdge) =
    w.WriteStartObject()
    w.WriteString("type", "transition")
    w.WriteString("source", t.Source)
    writeOptional w "target" t.Target
    writeOptional w "event" t.Event
    writeOptional w "guard" t.Guard
    writeOptional w "action" t.Action
    writePresent w "senderRole" t.SenderRole
    writePresent w "receiverRole" t.ReceiverRole
    writePresent w "payloadType" t.PayloadType
    w.WriteEndObject()

let private writeNote (w: Utf8JsonWriter) (note: NoteContent) =
    w.WriteStartObject()
    w.WriteString("type", "note")
    w.WriteString("target", note.Target)
    w.WriteString("content", note.Content)
    w.WriteEndObject()

let private writeDirective (w: Utf8JsonWriter) (directive: Directive) =
    w.WriteStartObject()
    w.WriteString("type", "directive")

    match directive with
    | TitleDirective(title, _) ->
        w.WriteString("directiveType", "title")
        w.WriteString("value", title)
    | AutoNumberDirective _ -> w.WriteString("directiveType", "autoNumber")

    w.WriteEndObject()

let rec private writeElement (w: Utf8JsonWriter) (el: StatechartElement) =
    match el with
    | StateDecl s -> writeStateNode w s
    | TransitionElement t -> writeTransition w t
    | NoteElement n -> writeNote w n
    | GroupElement g -> writeGroup w g
    | DirectiveElement d -> writeDirective w d

and private writeGroup (w: Utf8JsonWriter) (group: GroupBlock) =
    w.WriteStartObject()
    w.WriteString("type", "group")
    w.WriteString("kind", sprintf "%A" group.Kind)
    w.WriteStartArray("branches")

    for branch in group.Branches do
        w.WriteStartObject()
        writeOptional w "condition" branch.Condition
        w.WriteStartArray("elements")

        for el in branch.Elements do
            writeElement w el

        w.WriteEndArray()
        w.WriteEndObject()

    w.WriteEndArray()
    w.WriteEndObject()

let private writeDocumentInline (writer: Utf8JsonWriter) (doc: StatechartDocument) =
    writer.WriteStartObject()
    writeOptional writer "title" doc.Title
    writeOptional writer "initialStateId" doc.InitialStateId

    writer.WriteStartArray("elements")

    for el in doc.Elements do
        writeElement writer el

    writer.WriteEndArray()

    writer.WriteEndObject()

/// Serialize a StatechartDocument to indented JSON.
let serializeDocument (doc: StatechartDocument) : string =
    use stream = new MemoryStream()
    use writer = new Utf8JsonWriter(stream, JsonWriterOptions(Indented = true))
    writeDocumentInline writer doc
    writer.Flush()
    Encoding.UTF8.GetString(stream.ToArray())

let private writePosition (w: Utf8JsonWriter) (pos: SourcePosition option) =
    match pos with
    | Some p ->
        w.WriteNumber("line", p.Line)
        w.WriteNumber("column", p.Column)
    | None ->
        w.WriteNull("line")
        w.WriteNull("column")

let private writeParseFailure (w: Utf8JsonWriter) (f: ParseFailure) =
    w.WriteStartObject()
    writePosition w f.Position
    w.WriteString("description", f.Description)
    w.WriteString("expected", f.Expected)
    w.WriteString("found", f.Found)
    w.WriteEndObject()

let private writeParseWarning (w: Utf8JsonWriter) (warn: ParseWarning) =
    w.WriteStartObject()
    writePosition w warn.Position
    w.WriteString("description", warn.Description)
    writeOptional w "suggestion" warn.Suggestion
    w.WriteEndObject()

/// Serialize a ParseResult (document + errors + warnings) to indented JSON.
let serializeParseResult (result: ParseResult) : string =
    use stream = new MemoryStream()
    use writer = new Utf8JsonWriter(stream, JsonWriterOptions(Indented = true))

    writer.WriteStartObject()

    writer.WritePropertyName("document")
    writeDocumentInline writer result.Document

    writer.WriteStartArray("errors")

    for e in result.Errors do
        writeParseFailure writer e

    writer.WriteEndArray()

    writer.WriteStartArray("warnings")

    for w in result.Warnings do
        writeParseWarning writer w

    writer.WriteEndArray()

    writer.WriteEndObject()
    writer.Flush()
    Encoding.UTF8.GetString(stream.ToArray())

/// Serialize a ParseResult with a detected format tag.
let serializeParseResultWithFormat (result: ParseResult) (format: FormatTag) : string =
    use stream = new MemoryStream()
    use writer = new Utf8JsonWriter(stream, JsonWriterOptions(Indented = true))

    writer.WriteStartObject()
    writer.WriteString("format", FormatTag.toLower format)

    writer.WritePropertyName("document")
    writeDocumentInline writer result.Document

    writer.WriteStartArray("errors")

    for e in result.Errors do
        writeParseFailure writer e

    writer.WriteEndArray()

    writer.WriteStartArray("warnings")

    for w in result.Warnings do
        writeParseWarning writer w

    writer.WriteEndArray()

    writer.WriteEndObject()
    writer.Flush()
    Encoding.UTF8.GetString(stream.ToArray())
