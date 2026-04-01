module internal Frank.Statecharts.Smcat.Generator

open Frank.Resources.Model
open Frank.Statecharts
open Frank.Statecharts.Ast

/// Options controlling smcat generation behavior.
type GenerateOptions =
    { ResourceName: string }

/// Error cases from the smcat generator.
type GeneratorError =
    | UnrecognizedMachineType of typeName: string

/// Synthetic source position for all generated AST nodes (not from parsed text).
let private syntheticPos : SourcePosition = { Line = 0; Column = 0 }

/// Check whether the boxed Machine is a StateMachine<_,_,_> record.
let private isStateMachineType (machine: obj) : bool =
    let t = machine.GetType()
    t.IsGenericType && t.GetGenericTypeDefinition().Name.StartsWith("StateMachine`")

/// Generate a StatechartDocument AST from StateMachineMetadata.
/// Follows the Wsd.Generator pattern but includes smcat-specific
/// initial-to-first-state and state-to-final transition elements.
let generate (options: GenerateOptions) (metadata: StateMachineMetadata) : Result<StatechartDocument, GeneratorError> =
    // Validate boxed Machine type
    if not (isStateMachineType metadata.Machine) then
        Error(UnrecognizedMachineType(metadata.Machine.GetType().FullName))
    else

    // Extract state names from StateHandlerMap keys
    let stateNames = metadata.StateHandlerMap |> Map.toList |> List.map fst

    // Order states: initial state first, then others alphabetically
    let others =
        stateNames
        |> List.filter (fun s -> s <> metadata.InitialStateKey)
        |> List.sort

    let orderedStates = metadata.InitialStateKey :: others

    // Initial pseudo-state declaration
    let initialStateDecl =
        [ StateDecl
              { Identifier = Some "initial"
                Label = None
                Kind = Initial
                Children = []
                Activities = None
                Position = Some syntheticPos
                Annotations = [ SmcatAnnotation(SmcatStateType(Initial, Explicit)) ] } ]

    // Regular domain state declarations (no SmcatStateType annotation)
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

    // Determine which states are final
    let hasFinalTransitions =
        orderedStates
        |> List.exists (fun stateName ->
            match Map.tryFind stateName metadata.StateMetadataMap with
            | Some info when info.IsFinal -> true
            | _ -> false)

    // Final pseudo-state declaration (only emitted when there are final transitions)
    let finalStateDecl =
        if hasFinalTransitions then
            [ StateDecl
                  { Identifier = Some "final"
                    Label = None
                    Kind = Final
                    Children = []
                    Activities = None
                    Position = Some syntheticPos
                    Annotations = [ SmcatAnnotation(SmcatStateType(Final, Explicit)) ] } ]
        else
            []

    // Initial transition: initial => <firstState> with InitialTransition annotation
    let initialTransition =
        [ TransitionElement
              { Source = "initial"
                Target = Some metadata.InitialStateKey
                Event = None
                Guard = None
                GuardHref = None
                Action = None
                Parameters = []
                Position = Some syntheticPos
                Annotations = [ SmcatAnnotation(SmcatTransition InitialTransition) ] } ]

    // Self-transitions for each (state, httpMethod) handler pair with SelfTransition annotation
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
                          Position = Some syntheticPos
                          Annotations = [ SmcatAnnotation(SmcatTransition SelfTransition) ] })
            | None -> [])

    // Final state transitions: <state> => final with FinalTransition annotation
    let finalTransitions =
        orderedStates
        |> List.collect (fun stateName ->
            match Map.tryFind stateName metadata.StateMetadataMap with
            | Some info when info.IsFinal ->
                [ TransitionElement
                      { Source = stateName
                        Target = Some "final"
                        Event = None
                        Guard = None
                        GuardHref = None
                        Action = None
                        Parameters = []
                        Position = Some syntheticPos
                        Annotations = [ SmcatAnnotation(SmcatTransition FinalTransition) ] } ]
            | _ -> [])

    let allElements =
        initialStateDecl @ stateElements @ finalStateDecl @ initialTransition @ transitionElements @ finalTransitions

    Ok
        { Title = Some options.ResourceName
          InitialStateId = Some metadata.InitialStateKey
          Elements = allElements
          DataEntries = []
          Annotations = [] }
