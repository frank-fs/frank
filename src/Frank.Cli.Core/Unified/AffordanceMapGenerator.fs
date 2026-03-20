module Frank.Cli.Core.Unified.AffordanceMapGenerator

open System
open System.IO
open System.Text
open System.Text.Json
open Frank.Resources.Model

// ══════════════════════════════════════════════════════════════════════════════
// Composite Key Generation (T033)
// ══════════════════════════════════════════════════════════════════════════════

/// Build a composite key from route template and state key.
/// Format: "{routeTemplate}|{stateKey}" per Research R3.
let compositeKey (routeTemplate: string) (stateKey: string) : string =
    sprintf "%s%s%s" routeTemplate AffordanceMap.KeySeparator stateKey

// ══════════════════════════════════════════════════════════════════════════════
// Profile URL Derivation (T035)
// ══════════════════════════════════════════════════════════════════════════════

/// Derive the ALPS profile URL from a base URI and resource slug.
let profileUrl (baseUri: string) (slug: string) : string =
    let trimmed = baseUri.TrimEnd('/')
    sprintf "%s/%s" trimmed slug

/// Derive a slug for multi-segment routes.
/// "/games/{gameId}" -> "games", "/health" -> "health",
/// "/api/v1/games/{id}" -> "api-v1-games"
let deriveSlug (routeTemplate: string) : string =
    let segments =
        routeTemplate.TrimStart('/').Split('/')
        |> Array.filter (fun s -> not (s.StartsWith("{") && s.EndsWith("}")))
        |> Array.filter (fun s -> not (String.IsNullOrWhiteSpace s))

    match segments with
    | [||] -> "root"
    | [| single |] -> single
    | multiple -> String.Join("-", multiple)

// ══════════════════════════════════════════════════════════════════════════════
// Link Relation Population (T034)
// ══════════════════════════════════════════════════════════════════════════════

/// Build link relations for a set of HTTP capabilities.
let private buildLinkRelations
    (routeTemplate: string)
    (capabilities: HttpCapability list)
    : AffordanceLinkRelation list =
    capabilities
    |> List.map (fun cap ->
        { Rel = cap.LinkRelation
          Href = routeTemplate
          Method = cap.Method
          Title = None })

// ══════════════════════════════════════════════════════════════════════════════
// Entry Generation (T032)
// ══════════════════════════════════════════════════════════════════════════════

/// Build affordance map entries for a single unified resource.
let private buildEntries (resource: UnifiedResource) (baseUri: string) : AffordanceMapEntry list =
    let slug = resource.ResourceSlug
    let profile = profileUrl baseUri slug

    match resource.Statechart with
    | Some sc ->
        // Stateful resource: one entry per state
        sc.StateNames
        |> List.map (fun stateName ->
            let capsForState =
                resource.HttpCapabilities
                |> List.filter (fun cap ->
                    match cap.StateKey with
                    | Some sk -> sk = stateName
                    | None -> true) // Capabilities with no state key apply to all states

            let methods =
                capsForState
                |> List.map _.Method
                |> List.distinct
                |> List.sort

            let linkRels = buildLinkRelations resource.RouteTemplate capsForState

            { RouteTemplate = resource.RouteTemplate
              StateKey = stateName
              AllowedMethods = methods
              LinkRelations = linkRels
              ProfileUrl = profile })

    | None ->
        // Stateless resource: single entry with "*" state key
        let methods =
            resource.HttpCapabilities
            |> List.map _.Method
            |> List.distinct
            |> List.sort

        let linkRels = buildLinkRelations resource.RouteTemplate resource.HttpCapabilities

        [ { RouteTemplate = resource.RouteTemplate
            StateKey = AffordanceMap.WildcardStateKey
            AllowedMethods = methods
            LinkRelations = linkRels
            ProfileUrl = profile } ]

// ══════════════════════════════════════════════════════════════════════════════
// JSON Output (T032)
// ══════════════════════════════════════════════════════════════════════════════

let private writeLinkRelation (writer: Utf8JsonWriter) (lr: AffordanceLinkRelation) =
    writer.WriteStartObject()
    writer.WriteString("rel", lr.Rel)
    writer.WriteString("href", lr.Href)
    writer.WriteString("method", lr.Method)
    match lr.Title with
    | Some t -> writer.WriteString("title", t)
    | None -> ()
    writer.WriteEndObject()

let private writeEntry (writer: Utf8JsonWriter) (entry: AffordanceMapEntry) =
    let key = compositeKey entry.RouteTemplate entry.StateKey
    writer.WritePropertyName(key)
    writer.WriteStartObject()

    // allowedMethods
    writer.WriteStartArray("allowedMethods")
    for m in entry.AllowedMethods do
        writer.WriteStringValue(m)
    writer.WriteEndArray()

    // linkRelations
    writer.WriteStartArray("linkRelations")
    for lr in entry.LinkRelations do
        writeLinkRelation writer lr
    writer.WriteEndArray()

    // profileUrl
    writer.WriteString("profileUrl", entry.ProfileUrl)

    writer.WriteEndObject()

/// Generate the affordance map JSON from a list of unified resources.
/// Accepts an optional generatedAt timestamp for testability (defaults to UtcNow).
let generate
    (resources: UnifiedResource list)
    (baseUri: string)
    (generatedAt: DateTimeOffset option)
    : string =
    let timestamp =
        generatedAt |> Option.defaultWith (fun () -> DateTimeOffset.UtcNow)

    let allEntries =
        resources
        |> List.collect (fun r -> buildEntries r baseUri)

    use stream = new MemoryStream()
    use writer = new Utf8JsonWriter(stream, JsonWriterOptions(Indented = true))

    writer.WriteStartObject()
    writer.WriteString("version", AffordanceMap.currentVersion)
    writer.WriteString("baseUri", baseUri)
    writer.WriteString("generatedAt", timestamp.ToString("o"))

    // entries object (keyed by composite key)
    writer.WriteStartObject("entries")
    for entry in allEntries do
        writeEntry writer entry
    writer.WriteEndObject()

    writer.WriteEndObject()
    writer.Flush()

    Encoding.UTF8.GetString(stream.ToArray())

/// Generate AffordanceMapEntry list (for use by other modules that need the structured data).
let generateEntries (resources: UnifiedResource list) (baseUri: string) : AffordanceMapEntry list =
    resources |> List.collect (fun r -> buildEntries r baseUri)
