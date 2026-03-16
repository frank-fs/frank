module internal Frank.Statecharts.Smcat.Generator

open Frank.Statecharts.Smcat.Types

/// Determines whether an smcat name requires quoting (contains characters
/// that are not alphanumeric, underscore, dot, or hyphen).
let private needsQuoting (name: string) =
    name
    |> Seq.exists (fun c -> not (System.Char.IsLetterOrDigit c || c = '_' || c = '.' || c = '-'))

/// Wraps a name in double quotes if it contains special characters.
let private quoteName (name: string) =
    if needsQuoting name then
        sprintf "\"%s\"" name
    else
        name

/// Format a transition label from optional event, guard, and action components.
/// Returns None if all components are absent (no label needed).
/// Format: "event [guard] / action" with absent components omitted.
let internal formatLabel
    (eventName: string option)
    (guardName: string option)
    (actionName: string option)
    : string option =
    match eventName, guardName, actionName with
    | None, None, None -> None
    | Some e, None, None -> Some e
    | Some e, Some g, None -> Some(sprintf "%s [%s]" e g)
    | Some e, None, Some a -> Some(sprintf "%s / %s" e a)
    | Some e, Some g, Some a -> Some(sprintf "%s [%s] / %s" e g a)
    | None, Some g, None -> Some(sprintf "[%s]" g)
    | None, Some g, Some a -> Some(sprintf "[%s] / %s" g a)
    | None, None, Some a -> Some(sprintf "/ %s" a)

/// Format a single transition line with source, target, and optional label.
let internal formatTransition (source: string) (target: string) (label: string option) : string =
    match label with
    | Some l -> sprintf "%s => %s: %s;" (quoteName source) (quoteName target) l
    | None -> sprintf "%s => %s;" (quoteName source) (quoteName target)

/// Information about a state for the generator. Provides the data the generator
/// needs without depending on the StateMachine/StateInfo types (which compile
/// after this module in the F# compilation order).
type GeneratorStateInfo<'State> =
    { State: 'State
      IsFinal: bool }

/// Generate valid smcat text from state machine components.
///
/// Because StateMachine<'S,'E,'C> compiles after this module and stores its
/// Transition function as a closure (not a declarative transition table),
/// the caller must provide decomposed components:
///
/// - initialState: The initial state of the machine.
/// - stateInfos: List of states with their IsFinal flag.
/// - guardNames: Function mapping (source, event) to an optional guard name.
/// - stateNames: Function converting a state value to its smcat name.
/// - eventNames: Function converting an event value to its smcat name.
/// - transitions: Explicit list of (source, event, target) triples to render.
///
/// Output format:
/// - One statement per line, semicolon terminators.
/// - Initial transition emitted first: "initial => <state>;"
/// - Regular transitions in provided order.
/// - Final state transitions emitted last: "<state> => final;"
let generate<'State, 'Event when 'State: equality and 'State: comparison>
    (initialState: 'State)
    (stateInfos: GeneratorStateInfo<'State> list)
    (guardNames: 'State -> 'Event -> string option)
    (stateNames: 'State -> string)
    (eventNames: 'Event -> string)
    (transitions: ('State * 'Event * 'State) list)
    : string =
    let lines = ResizeArray<string>()

    // 1. Emit initial transition first.
    let initialName = stateNames initialState
    lines.Add(sprintf "initial => %s;" (quoteName initialName))

    // 2. Identify final states.
    let finalStateSet =
        stateInfos
        |> List.filter (fun si -> si.IsFinal)
        |> List.map (fun si -> si.State)
        |> Set.ofList

    // 3. Emit regular transitions in provided order.
    for (source, event, target) in transitions do
        let sourceName = stateNames source
        let targetName = stateNames target
        let evtName = eventNames event
        let guard = guardNames source event
        // Actions are not directly available from StateMachine metadata;
        // the caller can encode action info in the guard name function if needed.
        let label = formatLabel (Some evtName) guard None
        lines.Add(formatTransition sourceName targetName label)

    // 4. Emit final state transitions last.
    for finalState in finalStateSet do
        let finalStateName = stateNames finalState
        lines.Add(formatTransition finalStateName "final" None)

    lines |> String.concat "\n"
