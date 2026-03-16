module internal Frank.Statecharts.Wsd.Generator

open Frank.Statecharts
open Frank.Statecharts.Wsd.Types

/// Options controlling WSD generation behavior.
type GenerateOptions =
    { ResourceName: string }

/// Synthetic source position for all generated AST nodes (not from parsed text).
let private syntheticPos : SourcePosition = { Line = 0; Column = 0 }

/// Generate a WSD Diagram AST from StateMachineMetadata.
let generate (options: GenerateOptions) (metadata: StateMachineMetadata) : Diagram =
    // Extract state names from StateHandlerMap keys
    let stateNames = metadata.StateHandlerMap |> Map.toList |> List.map fst

    // Order states: initial state first, then others alphabetically (FR-004)
    // InitialStateKey is always included as the first participant, even if absent from the handler map
    let others =
        stateNames
        |> List.filter (fun s -> s <> metadata.InitialStateKey)
        |> List.sort

    let orderedStates = metadata.InitialStateKey :: others

    // Build participant list with Explicit = true (prevents parser implicit-participant warnings)
    let participants =
        orderedStates
        |> List.map (fun name ->
            { Name = name
              Alias = None
              Explicit = true
              Position = syntheticPos })

    let participantElements = participants |> List.map ParticipantDecl

    // Use precomputed guard names from metadata (FR-007)
    let guardNames = metadata.GuardNames

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

    { Title = Some options.ResourceName
      AutoNumber = false
      Participants = participants
      Elements = allElements }

