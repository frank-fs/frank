module internal Frank.Statecharts.XState.Serializer

open System.IO
open System.Text
open System.Text.Json
open Frank.Statecharts.Ast

/// Collect all StateNode values from a list of StatechartElements.
let private collectStates (elements: StatechartElement list) : StateNode list =
    elements
    |> List.choose (fun e ->
        match e with
        | StateDecl s -> Some s
        | _ -> None)

/// Collect all TransitionEdge values from a list of StatechartElements.
let private collectTransitions (elements: StatechartElement list) : TransitionEdge list =
    elements
    |> List.choose (fun e ->
        match e with
        | TransitionElement t -> Some t
        | _ -> None)

/// Group transitions by source state, returning a map from state identifier
/// to the list of transitions originating from that state.
let private transitionsBySource (transitions: TransitionEdge list) : Map<string, TransitionEdge list> =
    transitions
    |> List.groupBy (fun t -> t.Source)
    |> Map.ofList

/// Write the "on" property for a state's transitions.
let private writeTransitions (writer: Utf8JsonWriter) (transitions: TransitionEdge list) =
    writer.WritePropertyName("on")
    writer.WriteStartObject()

    for t in transitions do
        let eventName = t.Event |> Option.defaultValue ""

        match t.Target with
        | Some target -> writer.WriteString(eventName, target)
        | None -> writer.WriteString(eventName, t.Source) // self-transition

    writer.WriteEndObject()

/// Write a single state object within the "states" property.
let private writeState
    (writer: Utf8JsonWriter)
    (transMap: Map<string, TransitionEdge list>)
    (state: StateNode)
    =
    writer.WritePropertyName(state.Identifier)
    writer.WriteStartObject()

    // Write transitions if any exist for this state
    match transMap |> Map.tryFind state.Identifier with
    | Some transitions when not transitions.IsEmpty -> writeTransitions writer transitions
    | _ -> ()

    // Write "type": "final" for final states
    match state.Kind with
    | Final -> writer.WriteString("type", "final")
    | _ -> ()

    // Write meta.description if label is present
    match state.Label with
    | Some label ->
        writer.WritePropertyName("meta")
        writer.WriteStartObject()
        writer.WriteString("description", label)
        writer.WriteEndObject()
    | None -> ()

    writer.WriteEndObject()

/// Serialize a StatechartDocument to XState v5 JSON format.
let serialize (document: StatechartDocument) : string =
    let states = collectStates document.Elements
    let transitions = collectTransitions document.Elements
    let transMap = transitionsBySource transitions

    use stream = new MemoryStream()
    use writer = new Utf8JsonWriter(stream, JsonWriterOptions(Indented = true))

    writer.WriteStartObject()

    // id
    let id = document.Title |> Option.defaultValue "statechart"
    writer.WriteString("id", id)

    // initial
    let initial = document.InitialStateId |> Option.defaultValue ""
    writer.WriteString("initial", initial)

    // states
    writer.WritePropertyName("states")
    writer.WriteStartObject()

    for state in states do
        writeState writer transMap state

    writer.WriteEndObject()

    writer.WriteEndObject()
    writer.Flush()

    Encoding.UTF8.GetString(stream.ToArray())
