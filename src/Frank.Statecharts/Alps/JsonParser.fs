module internal Frank.Statecharts.Alps.JsonParser

open System.Text.Json
open Frank.Statecharts.Alps.Types

/// Try to get a string property from a JSON element, returning None if missing.
let private tryGetString (elem: JsonElement) (name: string) : string option =
    match elem.TryGetProperty(name) with
    | true, prop when prop.ValueKind = JsonValueKind.String -> Some(prop.GetString())
    | _ -> None

/// Try to get an array property from a JSON element, returning empty list if missing.
let private tryGetArray (elem: JsonElement) (name: string) : JsonElement list =
    match elem.TryGetProperty(name) with
    | true, prop when prop.ValueKind = JsonValueKind.Array ->
        [ for item in prop.EnumerateArray() -> item ]
    | _ -> []

/// Parse a descriptor type string to DescriptorType DU.
let private parseDescriptorType (typeStr: string) : DescriptorType =
    match typeStr.ToLowerInvariant() with
    | "semantic" -> Semantic
    | "safe" -> Safe
    | "unsafe" -> Unsafe
    | "idempotent" -> Idempotent
    | _ -> Semantic // default to Semantic for unknown types (forward-compat)

/// Parse an ALPS documentation element.
let private parseDocumentation (elem: JsonElement) : AlpsDocumentation =
    { Format = tryGetString elem "format"
      Value = elem.GetProperty("value").GetString() }

/// Try to get documentation from a JSON element, returning None if no doc property.
let private tryGetDoc (elem: JsonElement) : AlpsDocumentation option =
    match elem.TryGetProperty("doc") with
    | true, doc when doc.ValueKind = JsonValueKind.Object -> Some(parseDocumentation doc)
    | _ -> None

/// Parse an ALPS extension element.
let private parseExtension (elem: JsonElement) : AlpsExtension =
    { Id = elem.GetProperty("id").GetString()
      Href = tryGetString elem "href"
      Value = tryGetString elem "value" }

/// Parse an ALPS link element.
let private parseLink (elem: JsonElement) : AlpsLink =
    { Rel = elem.GetProperty("rel").GetString()
      Href = elem.GetProperty("href").GetString() }

/// Parse a single descriptor, recursively parsing nested children.
let rec private parseDescriptor (elem: JsonElement) : Descriptor =
    { Id = tryGetString elem "id"
      Type =
          match tryGetString elem "type" with
          | Some t -> parseDescriptorType t
          | None -> Semantic // FR-006: default to Semantic
      Href = tryGetString elem "href"
      ReturnType = tryGetString elem "rt"
      Documentation = tryGetDoc elem
      Descriptors = tryGetArray elem "descriptor" |> List.map parseDescriptor
      Extensions = tryGetArray elem "ext" |> List.map parseExtension
      Links = tryGetArray elem "link" |> List.map parseLink }

/// Parse an ALPS JSON document into a typed AlpsDocument AST.
let parseAlpsJson (json: string) : Result<AlpsDocument, AlpsParseError list> =
    try
        use doc = JsonDocument.Parse(json)
        let root = doc.RootElement

        if root.ValueKind <> JsonValueKind.Object then
            Error [ { Description = "Expected JSON object at root, got " + root.ValueKind.ToString(); Position = None } ]
        else
            match root.TryGetProperty("alps") with
            | true, alps ->
                let alpsDoc =
                    { Version = tryGetString alps "version"
                      Documentation = tryGetDoc alps
                      Descriptors = tryGetArray alps "descriptor" |> List.map parseDescriptor
                      Links = tryGetArray alps "link" |> List.map parseLink
                      Extensions = tryGetArray alps "ext" |> List.map parseExtension }

                Ok alpsDoc
            | false, _ ->
                Error [ { Description = "Missing 'alps' root object"; Position = None } ]
    with :? JsonException as ex ->
        Error [ { Description = ex.Message; Position = None } ]
