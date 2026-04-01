namespace Frank.Affordances

open Frank.Resources.Model
open Frank.Statecharts.Ast

/// Runtime projection of statechart formats from the canonical RuntimeStatechart data.
/// Builds a StatechartDocument from RuntimeResource, then dispatches to format serializers.
/// This is the runtime equivalent of FormatPipeline.generateFormatFromExtracted.
module StatechartProjection =

    /// Synthetic source position for runtime-projected AST nodes.
    let private syntheticPos: SourcePosition = { Line = 0; Column = 0 }

    /// Build a StatechartDocument from a RuntimeResource's statechart data.
    let buildDocument (resourceName: string) (sc: RuntimeStatechart) : StatechartDocument =
        let orderedStates =
            let others =
                sc.StateNames
                |> List.filter (fun s -> s <> sc.InitialStateKey)
                |> List.sort

            sc.InitialStateKey :: others

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

        let guardElements =
            if sc.GuardNames.IsEmpty then
                []
            else
                let pairs = sc.GuardNames |> List.map (fun name -> (name, "*"))

                [ NoteElement
                      { Target = sc.InitialStateKey
                        Content = ""
                        Position = Some syntheticPos
                        Annotations =
                          [ WsdAnnotation(WsdNotePosition Over)
                            WsdAnnotation(WsdGuardData pairs) ] } ]

        let transitionElements =
            orderedStates
            |> List.collect (fun stateName ->
                match Map.tryFind stateName sc.StateMetadata with
                | Some info ->
                    info.AllowedMethods
                    |> List.map (fun httpMethod ->
                        TransitionElement
                            { Source = stateName
                              Target = Some stateName
                              Event = Some httpMethod
                              Guard = None
                              GuardHref = None
                              Action = None
                              Parameters = []
                              Position = Some syntheticPos
                              Annotations =
                                [ WsdAnnotation(WsdTransitionStyle { ArrowStyle = Solid; Direction = Forward }) ] })
                | None -> [])

        { Title = Some resourceName
          InitialStateId = Some sc.InitialStateKey
          Elements = stateElements @ guardElements @ transitionElements
          DataEntries = []
          Annotations = [] }

    /// Supported statechart output formats.
    type StatechartFormat =
        | Wsd
        | Smcat
        | Scxml
        | XState
        | AlpsJson
        | AlpsXml

    /// Generate a statechart format string from a RuntimeResource's statechart data.
    /// Returns None for stateless resources (no statechart data).
    let generate (format: StatechartFormat) (resource: RuntimeResource) : string option =
        if RuntimeStatechart.isEmpty resource.Statechart then
            None
        else
            let doc = buildDocument resource.ResourceSlug resource.Statechart

            let text =
                match format with
                | Wsd -> Frank.Statecharts.Wsd.Serializer.serialize doc
                | Smcat -> Frank.Statecharts.Smcat.Serializer.serialize doc
                | Scxml -> Frank.Statecharts.Scxml.Generator.generate doc
                | XState -> Frank.Statecharts.XState.Serializer.serialize doc
                | AlpsJson -> Frank.Statecharts.Alps.JsonGenerator.generateAlpsJson doc
                | AlpsXml -> Frank.Statecharts.Alps.XmlGenerator.generateAlpsXml doc

            Some text

    /// Generate all statechart formats for a resource.
    /// Returns a map of format name to generated text.
    /// Empty map for stateless resources.
    let generateAll (resource: RuntimeResource) : Map<string, string> =
        if RuntimeStatechart.isEmpty resource.Statechart then
            Map.empty
        else
            let doc = buildDocument resource.ResourceSlug resource.Statechart

            [ ("wsd", Frank.Statecharts.Wsd.Serializer.serialize doc)
              ("smcat", Frank.Statecharts.Smcat.Serializer.serialize doc)
              ("scxml", Frank.Statecharts.Scxml.Generator.generate doc)
              ("xstate", Frank.Statecharts.XState.Serializer.serialize doc)
              ("alps-json", Frank.Statecharts.Alps.JsonGenerator.generateAlpsJson doc)
              ("alps-xml", Frank.Statecharts.Alps.XmlGenerator.generateAlpsXml doc) ]
            |> Map.ofList
