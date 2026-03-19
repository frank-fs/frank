module Frank.Cli.Core.Unified.UnifiedAlpsGenerator

open System.IO
open System.Text
open System.Text.Json
open System.Text.RegularExpressions
open Frank.Cli.Core.Analysis
open Frank.Statecharts.Unified

// ============================================================================
// Schema.org Vocabulary Alignment (T027)
//
// Extracted from VocabularyAligner.fs -- pure data + pure function, no RDF graph
// mutation. Shared logic so both VocabularyAligner and this module can use it.
// ============================================================================

let private splitCamelCase (name: string) =
    Regex.Replace(name, "([a-z])([A-Z])", "$1 $2").ToLowerInvariant()

let private normalizeFieldName (name: string) =
    splitCamelCase(name).Replace(" ", "").ToLowerInvariant()

let private alignmentMap: (string list * string) list =
    [ ([ "name"; "title" ], "https://schema.org/name")
      ([ "description"; "summary"; "body" ], "https://schema.org/description")
      ([ "email"; "emailaddress" ], "https://schema.org/email")
      ([ "url"; "uri"; "website"; "homepage" ], "https://schema.org/url")
      ([ "price"; "cost"; "amount" ], "https://schema.org/price")
      ([ "createdat"; "datecreated"; "created" ], "https://schema.org/dateCreated")
      ([ "updatedat"; "datemodified"; "modified" ], "https://schema.org/dateModified")
      ([ "image"; "imageurl"; "photo" ], "https://schema.org/image")
      ([ "telephone"; "phone" ], "https://schema.org/telephone") ]

/// Try to find a Schema.org alignment for a field name.
let tryFindAlignment (fieldName: string) : string option =
    let normalized = normalizeFieldName fieldName

    alignmentMap
    |> List.tryFind (fun (names, _) -> names |> List.contains normalized)
    |> Option.map snd

// ============================================================================
// IANA-Precedence Link Relation Derivation (T028)
// ============================================================================

/// Map an HTTP method to an IANA-registered link relation type, if applicable.
/// Only `self`, `edit`, and `collection` are IANA-registered.
/// `create` and `delete` are NOT IANA-registered -- use ALPS fragment URIs for those.
let private ianaRelationForMethod (method: string) (isSingleResource: bool) : string option =
    match method.ToUpperInvariant() with
    | "GET" when isSingleResource -> Some "self"
    | "GET" -> Some "collection"
    | "PUT" -> Some "edit"
    | "PATCH" -> Some "edit"
    | _ -> None

/// Determine if a route template represents a single resource (has a parameter).
let private isSingleResourceRoute (routeTemplate: string) : bool =
    routeTemplate.Contains("{")

/// Derive a link relation type for a given HTTP method and resource context.
/// IANA-registered relations take precedence; otherwise use ALPS fragment URIs.
let deriveRelationType (baseUri: string) (resourceSlug: string) (method: string) (stateKey: string option) : string =
    let isSingle = isSingleResourceRoute ""  // Will be called with route info separately
    match ianaRelationForMethod method isSingle with
    | Some rel -> rel
    | None ->
        let descriptorId =
            match stateKey with
            | Some state -> $"{state}-{method.ToLowerInvariant()}{resourceSlug}"
            | None -> $"{method.ToLowerInvariant()}{resourceSlug}"

        $"{baseUri}/{resourceSlug}#{descriptorId}"

/// Derive a link relation type using full route context.
let deriveRelationTypeForRoute
    (baseUri: string)
    (resourceSlug: string)
    (routeTemplate: string)
    (method: string)
    (stateKey: string option)
    : string =
    let isSingle = isSingleResourceRoute routeTemplate

    match ianaRelationForMethod method isSingle with
    | Some rel -> rel
    | None ->
        let descriptorId =
            match stateKey with
            | Some state -> $"{state}-{method.ToLowerInvariant()}{resourceSlug |> fun s -> s.Substring(0, 1).ToUpperInvariant() + s.Substring(1)}"
            | None -> $"{method.ToLowerInvariant()}{resourceSlug |> fun s -> s.Substring(0, 1).ToUpperInvariant() + s.Substring(1)}"

        $"{baseUri}/{resourceSlug}#{descriptorId}"

// ============================================================================
// Transition Descriptor ID Generation
// ============================================================================

/// Generate a human-readable transition descriptor id from method and resource slug.
let private transitionDescriptorId (method: string) (resourceSlug: string) (stateKey: string option) : string =
    let capitalizedSlug =
        if resourceSlug.Length > 0 then
            resourceSlug.Substring(0, 1).ToUpperInvariant() + resourceSlug.Substring(1)
        else
            resourceSlug

    let verb =
        match method.ToUpperInvariant() with
        | "GET" -> "get"
        | "POST" -> "create"
        | "PUT" -> "update"
        | "DELETE" -> "delete"
        | "PATCH" -> "patch"
        | other -> other.ToLowerInvariant()

    match stateKey with
    | Some state -> $"{state}-{verb}{capitalizedSlug}"
    | None -> $"{verb}{capitalizedSlug}"

/// Map an HTTP method to an ALPS transition type string.
let private alpsTransitionType (method: string) : string =
    match method.ToUpperInvariant() with
    | "GET" | "HEAD" | "OPTIONS" -> "safe"
    | "PUT" | "DELETE" | "PATCH" -> "idempotent"
    | "POST" -> "unsafe"
    | _ -> "unsafe"

// ============================================================================
// JSON Writing Helpers
// ============================================================================

/// Write a semantic descriptor for a single field.
let private writeFieldDescriptor (writer: Utf8JsonWriter) (field: AnalyzedField) =
    writer.WriteStartObject()
    writer.WriteString("id", field.Name)
    writer.WriteString("type", "semantic")

    match tryFindAlignment field.Name with
    | Some schemaUri -> writer.WriteString("href", schemaUri)
    | None -> ()

    writer.WriteEndObject()

/// Collect all top-level semantic descriptor ids from type info for the `rt` field.
let private collectSemanticIds (typeInfo: AnalyzedType list) : string list =
    typeInfo
    |> List.collect (fun t ->
        match t.Kind with
        | Record fields -> fields |> List.map _.Name
        | DiscriminatedUnion _cases -> [ t.ShortName ]
        | Enum _values -> [ t.ShortName ])
    |> List.distinct

/// Write a transition descriptor.
let private writeTransitionDescriptor
    (writer: Utf8JsonWriter)
    (descriptorId: string)
    (transitionType: string)
    (semanticIds: string list)
    (rel: string)
    =
    writer.WriteStartObject()
    writer.WriteString("id", descriptorId)
    writer.WriteString("type", transitionType)

    // rt links to semantic descriptors
    if not semanticIds.IsEmpty then
        let rtValue =
            semanticIds
            |> List.map (fun id -> "#" + id)
            |> String.concat " "

        writer.WriteString("rt", rtValue)

    // Link relation
    if rel <> "" then
        writer.WritePropertyName("link")
        writer.WriteStartArray()
        writer.WriteStartObject()
        writer.WriteString("rel", rel)
        writer.WriteString("href", descriptorId)
        writer.WriteEndObject()
        writer.WriteEndArray()

    writer.WriteEndObject()

// ============================================================================
// Core ALPS Generation (T026, T029)
// ============================================================================

/// Generate an ALPS JSON document from a UnifiedResource and base URI.
/// Returns Ok with the ALPS JSON string, or Error with parse failure messages.
let generate (resource: UnifiedResource) (baseUri: string) : Result<string, string list> =
    use stream = new MemoryStream()
    use writer = new Utf8JsonWriter(stream, JsonWriterOptions(Indented = true))

    writer.WriteStartObject()
    writer.WritePropertyName("alps")
    writer.WriteStartObject()
    writer.WriteString("version", "1.0")

    // Collect all semantic descriptor ids for rt references
    let semanticIds = collectSemanticIds resource.TypeInfo

    let hasTypeDescriptors = not resource.TypeInfo.IsEmpty
    let hasTransitions = not resource.HttpCapabilities.IsEmpty

    if hasTypeDescriptors || hasTransitions then
        writer.WritePropertyName("descriptor")
        writer.WriteStartArray()

        // ── Type descriptors (semantic) ──
        for analyzedType in resource.TypeInfo do
            match analyzedType.Kind with
            | Record fields ->
                // Emit each record field as a top-level semantic descriptor
                for field in fields do
                    writeFieldDescriptor writer field

            | DiscriminatedUnion cases ->
                // Emit the DU as a parent semantic descriptor with nested case descriptors
                writer.WriteStartObject()
                writer.WriteString("id", analyzedType.ShortName)
                writer.WriteString("type", "semantic")

                if not cases.IsEmpty then
                    writer.WritePropertyName("descriptor")
                    writer.WriteStartArray()

                    for duCase in cases do
                        writer.WriteStartObject()
                        writer.WriteString("id", duCase.Name)
                        writer.WriteString("type", "semantic")

                        if not duCase.Fields.IsEmpty then
                            writer.WritePropertyName("descriptor")
                            writer.WriteStartArray()

                            for field in duCase.Fields do
                                writeFieldDescriptor writer field

                            writer.WriteEndArray()

                        writer.WriteEndObject()

                    writer.WriteEndArray()

                writer.WriteEndObject()

            | Enum values ->
                // Emit enum as a semantic descriptor with value descriptors
                writer.WriteStartObject()
                writer.WriteString("id", analyzedType.ShortName)
                writer.WriteString("type", "semantic")

                if not values.IsEmpty then
                    writer.WritePropertyName("descriptor")
                    writer.WriteStartArray()

                    for value in values do
                        writer.WriteStartObject()
                        writer.WriteString("id", value)
                        writer.WriteString("type", "semantic")
                        writer.WriteEndObject()

                    writer.WriteEndArray()

                writer.WriteEndObject()

        // ── Transition descriptors ──
        match resource.Statechart with
        | None ->
            // Plain resource (T029): emit transitions without state scoping
            for cap in resource.HttpCapabilities do
                let descriptorId =
                    transitionDescriptorId cap.Method resource.ResourceSlug None

                let transType = alpsTransitionType cap.Method

                let rel =
                    deriveRelationTypeForRoute
                        baseUri
                        resource.ResourceSlug
                        resource.RouteTemplate
                        cap.Method
                        None

                writeTransitionDescriptor writer descriptorId transType semanticIds rel

        | Some _sc ->
            // Stateful resource: emit state-scoped transitions
            for cap in resource.HttpCapabilities do
                let descriptorId =
                    transitionDescriptorId cap.Method resource.ResourceSlug cap.StateKey

                let transType = alpsTransitionType cap.Method

                let rel =
                    deriveRelationTypeForRoute
                        baseUri
                        resource.ResourceSlug
                        resource.RouteTemplate
                        cap.Method
                        cap.StateKey

                writeTransitionDescriptor writer descriptorId transType semanticIds rel

        writer.WriteEndArray()

    writer.WriteEndObject()
    writer.WriteEndObject()
    writer.Flush()

    let json = Encoding.UTF8.GetString(stream.ToArray())

    // ── Round-trip validation (T030) ──
    let parseResult =
        Frank.Statecharts.Alps.JsonParser.parseAlpsJson json

    if parseResult.Errors.IsEmpty then
        Ok json
    else
        let errorMessages =
            parseResult.Errors
            |> List.map (fun e -> e.Description)

        Error errorMessages
