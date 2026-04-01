module internal Frank.Statecharts.XState.Deserializer

open System.Text.Json
open Frank.Statecharts.Ast

/// Try to get a string property from a JSON element, returning None if missing.
let private tryGetString (elem: JsonElement) (name: string) : string option =
    match elem.TryGetProperty(name) with
    | true, prop when prop.ValueKind = JsonValueKind.String -> Some(prop.GetString())
    | _ -> None

/// Try to get an object property from a JSON element, returning None if missing.
let private tryGetObject (elem: JsonElement) (name: string) : JsonElement option =
    match elem.TryGetProperty(name) with
    | true, prop when prop.ValueKind = JsonValueKind.Object -> Some prop
    | _ -> None

/// Parse the "on" property of a state to produce TransitionEdge values.
let private parseTransitions (stateId: string) (stateElem: JsonElement) : TransitionEdge list =
    match tryGetObject stateElem "on" with
    | None -> []
    | Some onObj ->
        [ for prop in onObj.EnumerateObject() do
              let eventName = prop.Name

              let target =
                  if prop.Value.ValueKind = JsonValueKind.String then
                      Some(prop.Value.GetString())
                  else
                      None

              { Source = stateId
                Target = target
                Event = if eventName = "" then None else Some eventName
                Guard = None
                GuardHref = None
                Action = None
                Parameters = []
                Position = None
                Annotations = [] } ]

/// Determine the StateKind from the "type" property.
let private parseStateKind (stateElem: JsonElement) : StateKind =
    match tryGetString stateElem "type" with
    | Some "final" -> Final
    | Some "parallel" -> Parallel
    | _ -> Regular

/// Extract an optional label from the "meta.description" property.
let private parseLabel (stateElem: JsonElement) : string option =
    tryGetObject stateElem "meta"
    |> Option.bind (fun meta -> tryGetString meta "description")

/// Parse a single state from an XState JSON "states" object property.
let private parseState (stateId: string) (stateElem: JsonElement) : StateNode =
    { Identifier = Some stateId
      Label = parseLabel stateElem
      Kind = parseStateKind stateElem
      Children = []
      Activities = None
      Position = None
      Annotations = [] }

/// Create an empty StatechartDocument.
let private emptyDocument =
    { Title = None
      InitialStateId = None
      Elements = []
      DataEntries = []
      Annotations = [] }

/// Deserialize XState v5 JSON text to a ParseResult.
let deserialize (json: string) : ParseResult =
    try
        use doc = JsonDocument.Parse(json)
        let root = doc.RootElement

        if root.ValueKind <> JsonValueKind.Object then
            { Document = emptyDocument
              Errors =
                [ { Position = None
                    Description = "Expected JSON object at root, got " + root.ValueKind.ToString()
                    Expected = "JSON object"
                    Found = root.ValueKind.ToString()
                    CorrectiveExample = """{ "id": "example", "initial": "idle", "states": {} }""" } ]
              Warnings = [] }
        else
            let title = tryGetString root "id"
            let initial = tryGetString root "initial"

            match tryGetObject root "states" with
            | None ->
                { Document =
                    { emptyDocument with
                        Title = title
                        InitialStateId = initial }
                  Errors = []
                  Warnings =
                    [ { Position = None
                        Description = "Missing 'states' property; document has no states"
                        Suggestion = Some "Add a 'states' object with state definitions" } ] }
            | Some statesObj ->
                let mutable states: StateNode list = []
                let mutable transitions: TransitionEdge list = []

                for prop in statesObj.EnumerateObject() do
                    let stateId = prop.Name
                    let stateElem = prop.Value

                    let stateNode = parseState stateId stateElem
                    states <- states @ [ stateNode ]

                    let stateTransitions = parseTransitions stateId stateElem
                    transitions <- transitions @ stateTransitions

                let elements =
                    (states |> List.map StateDecl)
                    @ (transitions |> List.map TransitionElement)

                let document =
                    { Title = title
                      InitialStateId = initial
                      Elements = elements
                      DataEntries = []
                      Annotations = [] }

                { Document = document
                  Errors = []
                  Warnings = [] }

    with :? JsonException as ex ->
        { Document = emptyDocument
          Errors =
            [ { Position = None
                Description = ex.Message
                Expected = "Valid JSON"
                Found = "Malformed JSON"
                CorrectiveExample = """{ "id": "example", "initial": "idle", "states": {} }""" } ]
          Warnings = [] }
