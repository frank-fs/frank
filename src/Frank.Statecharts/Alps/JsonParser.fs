module Frank.Statecharts.Alps.JsonParser

open System.Text.Json
open Frank.Statecharts.Ast
open Frank.Statecharts.Alps.Classification

// ---------------------------------------------------------------------------
// Pass 1: JSON to intermediate records
// ---------------------------------------------------------------------------

/// Try to get a string property from a JSON element, returning None if missing.
let private tryGetString (elem: JsonElement) (name: string) : string option =
    match elem.TryGetProperty(name) with
    | true, prop when prop.ValueKind = JsonValueKind.String -> Some(prop.GetString())
    | _ -> None

/// Try to get an array property from a JSON element, returning empty list if missing.
let private tryGetArray (elem: JsonElement) (name: string) : JsonElement list =
    match elem.TryGetProperty(name) with
    | true, prop when prop.ValueKind = JsonValueKind.Array -> [ for item in prop.EnumerateArray() -> item ]
    | _ -> []

/// Parse an ALPS extension element to intermediate type.
let private parseExtension (elem: JsonElement) : ParsedExtension =
    { Id = elem.GetProperty("id").GetString()
      Href = tryGetString elem "href"
      Value = tryGetString elem "value" }

/// Parse an ALPS link element to intermediate type.
let private parseLink (elem: JsonElement) : ParsedLink =
    { Rel = elem.GetProperty("rel").GetString()
      Href = elem.GetProperty("href").GetString() }

/// Parse a single descriptor to intermediate type, recursively parsing nested children.
let rec private parseDescriptor (elem: JsonElement) : ParsedDescriptor =
    let docFormat, docValue =
        match elem.TryGetProperty("doc") with
        | true, doc when doc.ValueKind = JsonValueKind.Object ->
            (tryGetString doc "format", Some(doc.GetProperty("value").GetString()))
        | _ -> (None, None)

    { Id = tryGetString elem "id"
      Type = tryGetString elem "type"
      Href = tryGetString elem "href"
      Def = tryGetString elem "def"
      ReturnType = tryGetString elem "rt"
      DocFormat = docFormat
      DocValue = docValue
      Children = tryGetArray elem "descriptor" |> List.map parseDescriptor
      Extensions = tryGetArray elem "ext" |> List.map parseExtension
      Links = tryGetArray elem "link" |> List.map parseLink }

// ---------------------------------------------------------------------------
// Public API (T008)
// ---------------------------------------------------------------------------

/// An empty StatechartDocument used as best-effort in error cases.
let private emptyDoc: StatechartDocument =
    { Title = None
      InitialStateId = None
      Elements = []
      DataEntries = []
      Annotations = [] }

/// Parse an ALPS JSON document into a shared AST ParseResult.
let parseAlpsJson (json: string) : ParseResult =
    try
        use doc = JsonDocument.Parse(json)
        let root = doc.RootElement

        if root.ValueKind <> JsonValueKind.Object then
            { Document = emptyDoc
              Errors =
                [ { Position = None
                    Description = "Expected JSON object at root, got " + root.ValueKind.ToString()
                    Expected = "JSON object"
                    Found = root.ValueKind.ToString()
                    CorrectiveExample = """{"alps": {"version": "1.0", "descriptor": [...]}}""" } ]
              Warnings = [] }
        else
            match root.TryGetProperty("alps") with
            | true, alps ->
                // -- Pass 1: Parse JSON to intermediate records --
                let version = tryGetString alps "version"

                let rootDocFormat, rootDocValue =
                    match alps.TryGetProperty("doc") with
                    | true, docElem when docElem.ValueKind = JsonValueKind.Object ->
                        (tryGetString docElem "format", Some(docElem.GetProperty("value").GetString()))
                    | _ -> (None, None)

                let rootLinks = tryGetArray alps "link" |> List.map parseLink
                let rootExtensions = tryGetArray alps "ext" |> List.map parseExtension
                let descriptors = tryGetArray alps "descriptor" |> List.map parseDescriptor

                // -- Pass 2: Classify descriptors and build StatechartDocument --
                let statechartDoc =
                    classifyDescriptors descriptors version rootDocFormat rootDocValue rootLinks rootExtensions

                { Document = statechartDoc
                  Errors = []
                  Warnings = [] }

            | false, _ ->
                { Document = emptyDoc
                  Errors =
                    [ { Position = None
                        Description = "Missing 'alps' root object"
                        Expected = "'alps' property"
                        Found = "JSON object without 'alps'"
                        CorrectiveExample = """{"alps": {"version": "1.0", "descriptor": [...]}}""" } ]
                  Warnings = [] }
    with :? JsonException as ex ->
        { Document = emptyDoc
          Errors =
            [ { Position = None
                Description = ex.Message
                Expected = "valid JSON"
                Found = "malformed JSON"
                CorrectiveExample = """{"alps": {"version": "1.0", "descriptor": [...]}}""" } ]
          Warnings = [] }
