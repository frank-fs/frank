module Frank.Cli.Core.Unified.OpenApiConsistencyValidator

open System
open System.Text.Json
open Frank.Cli.Core.Analysis
open Frank.Statecharts.Unified
open Frank.Statecharts.Validation

type FieldDiscrepancy =
    | UnmappedField of typeName: string * fieldName: string
    | OrphanProperty of schemaName: string * propertyName: string
    | TypeMismatch of typeName: string * fieldName: string * expected: string * actual: string
    | RouteDiscrepancy of unifiedRoute: string * openApiPath: string

type ConsistencyResult =
    { Discrepancies: FieldDiscrepancy list
      CheckedTypes: int
      CheckedProperties: int
      IsConsistent: bool }

let private toCamelCase (s: string) =
    if String.IsNullOrEmpty s then s
    elif Char.IsLower s.[0] then s
    else string (Char.ToLowerInvariant s.[0]) + s.[1..]

/// Map an AnalyzedField.Kind to the expected JSON Schema type string.
let rec private expectedSchemaType (kind: FieldKind) : string =
    match kind with
    | Primitive "xsd:string" -> "string"
    | Primitive "xsd:integer" -> "integer"
    | Primitive "xsd:long" -> "integer"
    | Primitive "xsd:double" | Primitive "xsd:float" -> "number"
    | Primitive "xsd:boolean" -> "boolean"
    | Primitive "xsd:decimal" -> "number"
    | Primitive "xsd:dateTime" | Primitive "xsd:date" | Primitive "xsd:time" -> "string"
    | Primitive "xsd:duration" -> "string"
    | Primitive "xsd:anyURI" -> "string"
    | Primitive "xsd:base64Binary" -> "string"
    | Primitive _ -> "string"
    | Guid -> "string"
    | Optional inner -> expectedSchemaType inner
    | Collection _ -> "array"
    | Reference _ -> "object"

/// Compare a single type's fields against a JSON Schema object's properties.
let private compareTypeToSchema
    (typeName: string)
    (fields: AnalyzedField list)
    (schemaProperties: JsonElement)
    : FieldDiscrepancy list =

    let fieldNames =
        fields
        |> List.map (fun f -> toCamelCase f.Name)
        |> Set.ofList

    let fieldByCamelName =
        fields
        |> List.map (fun f -> toCamelCase f.Name, f)
        |> Map.ofList

    let schemaPropertyNames =
        if schemaProperties.ValueKind = JsonValueKind.Object then
            [ for prop in schemaProperties.EnumerateObject() -> prop.Name ]
            |> Set.ofList
        else
            Set.empty

    let unmapped =
        Set.difference fieldNames schemaPropertyNames
        |> Set.toList
        |> List.map (fun f -> UnmappedField(typeName, f))

    let orphans =
        Set.difference schemaPropertyNames fieldNames
        |> Set.toList
        |> List.map (fun p -> OrphanProperty(typeName, p))

    let mismatches =
        Set.intersect fieldNames schemaPropertyNames
        |> Set.toList
        |> List.choose (fun name ->
            match Map.tryFind name fieldByCamelName with
            | None -> None
            | Some field ->
                let schemaProp = schemaProperties.GetProperty(name)
                let expected = expectedSchemaType field.Kind

                match schemaProp.TryGetProperty("type") with
                | true, actualType ->
                    let actual = actualType.GetString()

                    if expected <> actual then
                        Some(TypeMismatch(typeName, name, expected, actual))
                    else
                        None
                | _ -> None) // Schema uses $ref or oneOf -- skip type comparison

    unmapped @ orphans @ mismatches

/// Compare unified model type info against an OpenAPI schema document.
let validate
    (unifiedTypes: AnalyzedType list)
    (openApiSchemas: JsonElement)
    : ConsistencyResult =

    let mutable checkedProperties = 0

    let discrepancies =
        unifiedTypes
        |> List.collect (fun analyzedType ->
            let shortName = analyzedType.ShortName

            match analyzedType.Kind with
            | Record fields ->
                match openApiSchemas.TryGetProperty(shortName) with
                | true, schemaObj ->
                    match schemaObj.TryGetProperty("properties") with
                    | true, props ->
                        checkedProperties <- checkedProperties + fields.Length
                        compareTypeToSchema shortName fields props
                    | _ -> []
                | _ ->
                    // Type not found in OpenAPI schemas at all
                    fields
                    |> List.map (fun f -> UnmappedField(shortName, toCamelCase f.Name))
            | DiscriminatedUnion _ ->
                // DU types typically map to oneOf schemas - skip detailed comparison
                []
            | Enum _ ->
                // Enum types map to string with enum constraint - skip
                [])

    { Discrepancies = discrepancies
      CheckedTypes = unifiedTypes.Length
      CheckedProperties = checkedProperties
      IsConsistent = discrepancies.IsEmpty }

/// Convert ConsistencyResult to a ValidationReport for consistent CLI output.
let toValidationReport (result: ConsistencyResult) : ValidationReport =
    let checks =
        result.Discrepancies
        |> List.map (fun d ->
            match d with
            | UnmappedField(typeName, fieldName) ->
                { Name = $"openapi.field.%s{typeName}.%s{fieldName}"
                  Status = Fail
                  Reason = Some $"F# field '%s{fieldName}' on type '%s{typeName}' is not in OpenAPI schema" }
            | OrphanProperty(schemaName, propName) ->
                { Name = $"openapi.property.%s{schemaName}.%s{propName}"
                  Status = Fail
                  Reason = Some $"OpenAPI property '%s{propName}' on schema '%s{schemaName}' has no corresponding F# field" }
            | TypeMismatch(typeName, fieldName, expected, actual) ->
                { Name = $"openapi.type.%s{typeName}.%s{fieldName}"
                  Status = Fail
                  Reason = Some $"Type mismatch for '%s{fieldName}': F# expects '%s{expected}', OpenAPI has '%s{actual}'" }
            | RouteDiscrepancy(unified, openApi) ->
                { Name = $"openapi.route.%s{unified}"
                  Status = Fail
                  Reason = Some $"Route mismatch: unified model has '%s{unified}', OpenAPI has '%s{openApi}'" })

    let passCount = result.CheckedTypes + result.CheckedProperties - result.Discrepancies.Length

    let passChecks =
        if passCount > 0 && result.Discrepancies.IsEmpty then
            [ { Name = "openapi.consistency"
                Status = Pass
                Reason = Some $"All %d{result.CheckedTypes} types and %d{result.CheckedProperties} properties are consistent" } ]
        else
            []

    { TotalChecks = result.CheckedTypes + result.CheckedProperties
      TotalSkipped = 0
      TotalFailures = result.Discrepancies.Length
      Checks = passChecks @ checks
      Failures = [] }
