module internal Frank.Statecharts.Smcat.Generator

open System.IO
open Frank.Statecharts
open Frank.Statecharts.Smcat.Types

/// Determines whether an smcat name requires quoting (contains characters
/// that are not alphanumeric, underscore, dot, or hyphen).
let private needsQuoting (name: string) =
    name
    |> Seq.exists (fun c -> not (System.Char.IsLetterOrDigit c || c = '_' || c = '.' || c = '-'))

/// Wraps a name in double quotes if it contains special characters,
/// escaping any embedded double quotes.
let private quoteName (name: string) =
    if needsQuoting name then
        sprintf "\"%s\"" (name.Replace("\"", "\\\""))
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

/// Generate valid smcat text from StateMachineMetadata.
/// Uses precomputed GuardNames and StateMetadataMap — no reflection needed.
///
/// Output format:
/// - One statement per line, semicolon terminators.
/// - Initial transition emitted first: "initial => <state>;"
/// - Self-messages for each (state, HTTP method) handler pair.
/// - Final state transitions emitted last: "<state> => final;"
let generate (options: GenerateOptions) (metadata: StateMachineMetadata) : string =
    let sb = System.Text.StringBuilder()

    // Order states: initial first, others alphabetically
    let stateNames = metadata.StateHandlerMap |> Map.toList |> List.map fst

    let others =
        stateNames
        |> List.filter (fun s -> s <> metadata.InitialStateKey)
        |> List.sort

    let orderedStates = metadata.InitialStateKey :: others

    // 1. Emit initial transition
    sb.Append(sprintf "initial => %s;" (quoteName metadata.InitialStateKey)) |> ignore

    // 2. Emit self-messages for each (state, httpMethod) handler pair
    for stateName in orderedStates do
        match Map.tryFind stateName metadata.StateHandlerMap with
        | Some handlers ->
            for (httpMethod, _) in handlers do
                sb.Append('\n') |> ignore
                let label = formatLabel (Some httpMethod) None None
                sb.Append(formatTransition stateName stateName label) |> ignore
        | None -> ()

    // 3. Emit final state transitions last (using precomputed StateMetadataMap)
    for stateName in orderedStates do
        match Map.tryFind stateName metadata.StateMetadataMap with
        | Some info when info.IsFinal ->
            sb.Append('\n') |> ignore
            sb.Append(formatTransition stateName "final" None) |> ignore
        | _ -> ()

    sb.ToString()

/// Generate valid smcat text and write directly to a TextWriter.
/// The caller owns the writer lifecycle.
let generateTo (writer: TextWriter) (options: GenerateOptions) (metadata: StateMachineMetadata) : unit =
    writer.Write(generate options metadata)
