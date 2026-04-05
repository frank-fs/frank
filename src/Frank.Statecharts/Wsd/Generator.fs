module internal Frank.Statecharts.Wsd.Generator

open Frank.Resources.Model
open Frank.Statecharts
open Frank.Statecharts.Ast

/// Options controlling WSD generation behavior.
type GenerateOptions = { ResourceName: string }

/// Error cases from the WSD generator.
type GeneratorError = UnrecognizedMachineType of typeName: string

/// Synthetic source position for all generated AST nodes (not from parsed text).
let private syntheticPos: SourcePosition = { Line = 0; Column = 0 }

/// Check whether the boxed Machine is a StateMachine<_,_,_> record (FR-011).
let private isStateMachineType (machine: obj) : bool =
    let t = machine.GetType()
    t.IsGenericType && t.GetGenericTypeDefinition().Name.StartsWith("StateMachine`")

/// Generate a StatechartDocument AST from StateMachineMetadata.
let generate (options: GenerateOptions) (metadata: StateMachineMetadata) : Result<StatechartDocument, GeneratorError> =
    // Validate boxed Machine type (FR-011)
    if not (isStateMachineType metadata.Machine) then
        Error(UnrecognizedMachineType(metadata.Machine.GetType().FullName))
    else

        // Extract state names from StateHandlerMap keys
        let stateNames = metadata.StateHandlerMap |> Map.toList |> List.map fst

        // Order states: initial state first, then others alphabetically (FR-004)
        // InitialStateKey is always included as the first participant, even if absent from the handler map
        let others =
            stateNames |> List.filter (fun s -> s <> metadata.InitialStateKey) |> List.sort

        let orderedStates = metadata.InitialStateKey :: others

        // Build state declarations (participants in WSD terms)
        let stateElements =
            orderedStates
            |> List.map (fun name ->
                StateDecl
                    { Identifier = Some name
                      Label = None
                      Kind = Regular
                      Children = []
                      Activities = None
                      Position = Some syntheticPos
                      Annotations = [] })

        // Use precomputed guard names from metadata (FR-007)
        let guardNames = metadata.GuardNames

        let guardElements =
            if guardNames.IsEmpty then
                []
            else
                let pairs = guardNames |> List.map (fun name -> (name, "*"))

                [ NoteElement
                      { Target = metadata.InitialStateKey
                        Content = ""
                        Position = Some syntheticPos
                        Annotations = [ WsdAnnotation(WsdNotePosition Over); WsdAnnotation(WsdGuardData pairs) ] } ]

        // Build transition elements from StateHandlerMap (FR-005)
        // Each (state, httpMethod) pair becomes a self-transition (DD-05: state-capability diagram)
        let transitionElements =
            orderedStates
            |> List.collect (fun stateName ->
                match Map.tryFind stateName metadata.StateHandlerMap with
                | Some handlers ->
                    handlers
                    |> List.map (fun (httpMethod, _) ->
                        TransitionElement
                            { Source = stateName
                              Target = Some stateName
                              Event = Some httpMethod
                              Guard = None
                              GuardHref = None
                              Action = None
                              Parameters = []
                              SenderRole = None
                              ReceiverRole = None
                              PayloadType = None
                              Position = Some syntheticPos
                              Annotations =
                                [ WsdAnnotation(
                                      WsdTransitionStyle
                                          { ArrowStyle = Solid
                                            Direction = Forward }
                                  ) ] })
                | None -> [])

        // Assemble the final document (FR-009: title from GenerateOptions)
        let allElements = stateElements @ guardElements @ transitionElements

        Ok
            { Title = Some options.ResourceName
              InitialStateId = Some metadata.InitialStateKey
              Elements = allElements
              DataEntries = []
              Annotations = [] }
