module internal Frank.Statecharts.Wsd.Generator

open Frank.Statecharts
open Frank.Statecharts.Wsd.Types

/// Error cases for WSD generation from StateMachineMetadata.
type GeneratorError =
    | UnrecognizedMachineType of typeName: string
    | NoStatesFound of resourceName: string

/// Options controlling WSD generation behavior.
type GenerateOptions =
    { ResourceName: string }

/// Synthetic source position for all generated AST nodes (not from parsed text).
let private syntheticPos : SourcePosition = { Line = 0; Column = 0 }

/// Extract guard names from the boxed StateMachine's Guards field via reflection.
/// StateMachine<'S,'E,'C> record fields in declaration order:
///   0: Initial, 1: InitialContext, 2: Transition, 3: Guards, 4: StateMetadata
let private extractGuardNames (machine: obj) : string list =
    let fields = FSharp.Reflection.FSharpValue.GetRecordFields(machine)
    let guardsObj = fields.[3]

    match guardsObj with
    | :? System.Collections.IEnumerable as guards ->
        [ for g in guards do
              let nameField = g.GetType().GetProperty("Name")
              yield nameField.GetValue(g) :?> string ]
    | _ -> []

/// Generate a WSD Diagram AST from StateMachineMetadata.
/// Returns Ok(Diagram) on success or Error(GeneratorError) on failure.
let generate (options: GenerateOptions) (metadata: StateMachineMetadata) : Result<Diagram, GeneratorError> =
    // Validate the boxed Machine type is StateMachine<_,_,_>
    let machineType = metadata.Machine.GetType()

    let isStateMachine =
        machineType.IsGenericType
        && machineType.GetGenericTypeDefinition() = typedefof<StateMachine<_, _, _>>

    if not isStateMachine then
        Error(UnrecognizedMachineType machineType.FullName)
    else
        // Extract state names from StateHandlerMap keys
        let stateNames = metadata.StateHandlerMap |> Map.toList |> List.map fst

        // Order states: initial state first, then others alphabetically (FR-004)
        let orderedStates =
            let others =
                stateNames
                |> List.filter (fun s -> s <> metadata.InitialStateKey)
                |> List.sort

            if stateNames |> List.contains metadata.InitialStateKey then
                metadata.InitialStateKey :: others
            else
                // InitialStateKey not in handler map — still include as first participant
                metadata.InitialStateKey :: others

        // Check for empty states
        if orderedStates.IsEmpty then
            Error(NoStatesFound options.ResourceName)
        else
            // Build participant list with Explicit = true (prevents parser implicit-participant warnings)
            let participants =
                orderedStates
                |> List.map (fun name ->
                    { Name = name
                      Alias = None
                      Explicit = true
                      Position = syntheticPos })

            let participantElements = participants |> List.map ParticipantDecl

            // Extract guards via reflection on the boxed Machine (FR-007)
            let guardNames = extractGuardNames metadata.Machine

            let guardElements =
                if guardNames.IsEmpty then
                    []
                else
                    let pairs = guardNames |> List.map (fun name -> (name, "*"))
                    let guard = { Pairs = pairs; Position = syntheticPos }

                    [ NoteElement
                          { NotePosition = Over
                            Target = metadata.InitialStateKey
                            Content = ""
                            Guard = Some guard
                            Position = syntheticPos } ]

            // Build message elements from StateHandlerMap (FR-005)
            // Each (state, httpMethod) pair becomes a self-message (DD-05: state-capability diagram)
            let messageElements =
                orderedStates
                |> List.collect (fun stateName ->
                    match Map.tryFind stateName metadata.StateHandlerMap with
                    | Some handlers ->
                        handlers
                        |> List.map (fun (httpMethod, _) ->
                            MessageElement
                                { Sender = stateName
                                  Receiver = stateName
                                  ArrowStyle = Solid   // DD-03: default arrow style
                                  Direction = Forward   // DD-03: default direction
                                  Label = httpMethod
                                  Parameters = []
                                  Position = syntheticPos })
                    | None -> [])

            // Assemble the final Diagram (FR-009: title from GenerateOptions)
            let allElements = participantElements @ guardElements @ messageElements

            let diagram =
                { Title = Some options.ResourceName
                  AutoNumber = false
                  Participants = participants
                  Elements = allElements }

            Ok diagram
