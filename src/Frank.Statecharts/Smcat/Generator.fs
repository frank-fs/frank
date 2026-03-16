module internal Frank.Statecharts.Smcat.Generator

open Frank.Statecharts
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

/// Options controlling smcat generation behavior.
type GenerateOptions =
    { ResourceName: string }

/// Extract guard names from the boxed StateMachine's Guards field via reflection.
let private extractGuardNames (machine: obj) : string list =
    let fields = FSharp.Reflection.FSharpValue.GetRecordFields(machine)
    let guardsObj = fields.[3]

    match guardsObj with
    | :? System.Collections.IEnumerable as guards ->
        [ for g in guards do
              let nameField = g.GetType().GetProperty("Name")
              yield nameField.GetValue(g) :?> string ]
    | _ -> []

/// Extract StateMetadata (Map<'State, StateInfo>) from the boxed StateMachine via reflection.
let private extractStateMetadata (machine: obj) : Map<string, StateInfo> =
    let fields = FSharp.Reflection.FSharpValue.GetRecordFields(machine)
    let metadataObj = fields.[4]

    match metadataObj with
    | :? System.Collections.IEnumerable as entries ->
        [ for kvp in entries do
              let kvpType = kvp.GetType()
              let key = kvpType.GetProperty("Key").GetValue(kvp)
              let value = kvpType.GetProperty("Value").GetValue(kvp)
              yield (string key, value :?> StateInfo) ]
        |> Map.ofList
    | _ -> Map.empty

/// Generate valid smcat text from StateMachineMetadata.
///
/// Output format:
/// - One statement per line, semicolon terminators.
/// - Initial transition emitted first: "initial => <state>;"
/// - Self-messages for each (state, HTTP method) handler pair.
/// - Guard annotations as note-style comments.
/// - Final state transitions emitted last: "<state> => final;"
let generate (options: GenerateOptions) (metadata: StateMachineMetadata) : string =
    let lines = ResizeArray<string>()

    // Order states: initial first, others alphabetically
    let stateNames = metadata.StateHandlerMap |> Map.toList |> List.map fst

    let others =
        stateNames
        |> List.filter (fun s -> s <> metadata.InitialStateKey)
        |> List.sort

    let orderedStates = metadata.InitialStateKey :: others

    // 1. Emit initial transition
    lines.Add(sprintf "initial => %s;" (quoteName metadata.InitialStateKey))

    // 2. Extract state metadata for IsFinal detection
    let stateMetadata = extractStateMetadata metadata.Machine

    // 3. Extract guard names
    let guardNames = extractGuardNames metadata.Machine

    // 4. Emit self-messages for each (state, httpMethod) handler pair
    for stateName in orderedStates do
        match Map.tryFind stateName metadata.StateHandlerMap with
        | Some handlers ->
            for (httpMethod, _) in handlers do
                let label = formatLabel (Some httpMethod) None None
                lines.Add(formatTransition stateName stateName label)
        | None -> ()

    // 5. Emit final state transitions last
    for stateName in orderedStates do
        match Map.tryFind stateName stateMetadata with
        | Some info when info.IsFinal -> lines.Add(formatTransition stateName "final" None)
        | _ -> ()

    lines |> String.concat "\n"
