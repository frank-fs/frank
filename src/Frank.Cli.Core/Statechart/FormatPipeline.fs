module Frank.Cli.Core.Statechart.FormatPipeline

open Frank.Statecharts
open Frank.Statecharts.Ast
open Frank.Resources.Model
open Frank.Statecharts.Validation
open Frank.Cli.Core.Statechart.StatechartError

/// All supported statechart formats.
let allFormats: FormatTag list = [ Wsd; Alps; AlpsXml; Scxml; Smcat; XState ]

/// Extract a filename-safe slug from a route template.
/// e.g., "/games/{id}" -> "games", "/api/orders/{orderId}" -> "api-orders"
let resourceSlug (routeTemplate: string) : string =
    routeTemplate.TrimStart('/')
    |> _.Split('/')
    |> Array.filter (fun s -> not (s.StartsWith("{") && s.EndsWith("}")))
    |> Array.map (fun s -> s.Replace(" ", "-"))
    |> String.concat "-"
    |> fun s -> if System.String.IsNullOrEmpty(s) then "resource" else s

/// Synthetic source position for compiler-extracted AST nodes.
let private syntheticPos : SourcePosition = { Line = 0; Column = 0 }

/// Build a StatechartDocument directly from an ExtractedStatechart.
/// No runtime StateMachineMetadata needed — works from compiler-extracted data.
let private buildDocumentFromExtracted (resourceName: string) (extracted: ExtractedStatechart) : StatechartDocument =
    let orderedStates =
        let others =
            extracted.StateNames
            |> List.filter (fun s -> s <> extracted.InitialStateKey)
            |> List.sort
        extracted.InitialStateKey :: others

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
        if extracted.GuardNames.IsEmpty then []
        else
            let pairs = extracted.GuardNames |> List.map (fun name -> (name, "*"))
            [ NoteElement
                { Target = extracted.InitialStateKey
                  Content = ""
                  Position = Some syntheticPos
                  Annotations =
                    [ WsdAnnotation(WsdNotePosition Over)
                      WsdAnnotation(WsdGuardData pairs) ] } ]

    let transitionElements =
        orderedStates
        |> List.collect (fun stateName ->
            match Map.tryFind stateName extracted.StateMetadata with
            | Some info ->
                info.AllowedMethods
                |> List.map (fun httpMethod ->
                    TransitionElement
                        { Source = stateName
                          Target = Some stateName
                          Event = Some httpMethod
                          Guard = None
                          Action = None
                          Parameters = []
                          Position = Some syntheticPos
                          Annotations =
                            [ WsdAnnotation(WsdTransitionStyle { ArrowStyle = Solid; Direction = Forward }) ] })
            | None -> [])

    { Title = Some resourceName
      InitialStateId = Some extracted.InitialStateKey
      Elements = stateElements @ guardElements @ transitionElements
      DataEntries = []
      Annotations = [] }

/// Generate format text from compiler-extracted metadata.
/// Builds a StatechartDocument directly, then dispatches to format serializers.
let generateFormatFromExtracted (format: FormatTag) (resourceName: string) (extracted: ExtractedStatechart) : Result<string, StatechartError> =
    let doc = buildDocumentFromExtracted resourceName extracted
    match format with
    | Wsd -> Ok(Wsd.Serializer.serialize doc)
    | Alps -> Ok(Frank.Statecharts.Alps.JsonGenerator.generateAlpsJson doc)
    | AlpsXml -> Ok(Frank.Statecharts.Alps.XmlGenerator.generateAlpsXml doc)
    | Scxml -> Ok(Frank.Statecharts.Scxml.Generator.generate doc)
    | Smcat -> Ok(Smcat.Serializer.serialize doc)
    | XState -> Ok(XState.Serializer.serialize doc)
