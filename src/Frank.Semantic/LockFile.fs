namespace Frank.Semantic

open System
open System.IO
open System.Text.Json
open System.Text.Json.Serialization

// ── Domain types ────────────────────────────────────────────────────────────

type MappingSource =
    | Convention
    | Llm
    | Manual

type MappingStatus =
    | Confirmed
    | Proposed
    | Unresolved

type FieldMapping =
    { [<JsonPropertyName("name")>]
      Name: string
      [<JsonPropertyName("iri")>]
      Iri: string
      [<JsonPropertyName("confidence")>]
      Confidence: float
      [<JsonPropertyName("source")>]
      Source: MappingSource
      [<JsonPropertyName("status")>]
      Status: MappingStatus
      [<JsonPropertyName("pattern")>]
      Pattern: string option }

type TypeMapping =
    { [<JsonPropertyName("fsharpType")>]
      FsharpType: string
      [<JsonPropertyName("iri")>]
      Iri: string
      [<JsonPropertyName("confidence")>]
      Confidence: float
      [<JsonPropertyName("source")>]
      Source: MappingSource
      [<JsonPropertyName("status")>]
      Status: MappingStatus
      [<JsonPropertyName("fields")>]
      Fields: FieldMapping list }

type VocabularyEntry =
    { [<JsonPropertyName("uri")>]
      Uri: string
      [<JsonPropertyName("fetchedAt")>]
      FetchedAt: string option
      [<JsonPropertyName("hash")>]
      Hash: string option }

type LockFile =
    { [<JsonPropertyName("schemaVersion")>]
      SchemaVersion: int
      [<JsonPropertyName("generated")>]
      Generated: DateTimeOffset
      [<JsonPropertyName("vocabularies")>]
      Vocabularies: Map<string, VocabularyEntry>
      [<JsonPropertyName("mappings")>]
      Mappings: TypeMapping list }

// ── Serialization helpers ────────────────────────────────────────────────────

module private Serialization =

    let private sourceToJson =
        function
        | Convention -> "convention"
        | Llm -> "llm"
        | Manual -> "manual"

    let private statusToJson =
        function
        | Confirmed -> "confirmed"
        | Proposed -> "proposed"
        | Unresolved -> "unresolved"

    let private sourceFromJson (s: string) =
        match s with
        | "convention" -> Convention
        | "llm" -> Llm
        | "manual" -> Manual
        | other -> invalidArg "source" $"Unknown source value: '{other}'"

    let private statusFromJson (s: string) =
        match s with
        | "confirmed" -> Confirmed
        | "proposed" -> Proposed
        | "unresolved" -> Unresolved
        | other -> invalidArg "status" $"Unknown status value: '{other}'"

    let private writeOptionalString (writer: Utf8JsonWriter) (name: string) (value: string option) =
        match value with
        | None -> writer.WriteNull(name)
        | Some s -> writer.WriteString(name, s)

    let private writeFieldMapping (writer: Utf8JsonWriter) (f: FieldMapping) =
        writer.WriteStartObject()
        writer.WriteString("name", f.Name)
        writer.WriteString("iri", f.Iri)
        writer.WriteNumber("confidence", f.Confidence)
        writer.WriteString("source", sourceToJson f.Source)
        writer.WriteString("status", statusToJson f.Status)
        writeOptionalString writer "pattern" f.Pattern
        writer.WriteEndObject()

    let private writeTypeMapping (writer: Utf8JsonWriter) (m: TypeMapping) =
        writer.WriteStartObject()
        writer.WriteString("fsharpType", m.FsharpType)
        writer.WriteString("iri", m.Iri)
        writer.WriteNumber("confidence", m.Confidence)
        writer.WriteString("source", sourceToJson m.Source)
        writer.WriteString("status", statusToJson m.Status)
        writer.WriteStartArray("fields")

        for f in m.Fields do
            writeFieldMapping writer f

        writer.WriteEndArray()
        writer.WriteEndObject()

    let private writeVocabularyEntry (writer: Utf8JsonWriter) (key: string) (v: VocabularyEntry) =
        writer.WritePropertyName(key)
        writer.WriteStartObject()
        writer.WriteString("uri", v.Uri)
        writeOptionalString writer "fetchedAt" v.FetchedAt
        writeOptionalString writer "hash" v.Hash
        writer.WriteEndObject()

    let serialize (lf: LockFile) : string =
        let opts = JsonWriterOptions(Indented = true)
        use ms = new MemoryStream()
        use writer = new Utf8JsonWriter(ms, opts)
        writer.WriteStartObject()
        writer.WriteNumber("schemaVersion", lf.SchemaVersion)
        writer.WriteString("generated", lf.Generated.ToString("O"))
        writer.WriteStartObject("vocabularies")

        for KeyValue(key, entry) in (lf.Vocabularies |> Map.toSeq |> Seq.sortBy fst |> dict) do
            writeVocabularyEntry writer key entry

        writer.WriteEndObject()
        writer.WriteStartArray("mappings")

        for m in lf.Mappings do
            writeTypeMapping writer m

        writer.WriteEndArray()
        writer.WriteEndObject()
        writer.Flush()
        System.Text.Encoding.UTF8.GetString(ms.ToArray())

    let private readFieldMapping (el: JsonElement) : FieldMapping =
        let tryStr (name: string) =
            let mutable prop = Unchecked.defaultof<JsonElement>

            if el.TryGetProperty(name, &prop) && prop.ValueKind <> JsonValueKind.Null then
                Some(prop.GetString())
            else
                None

        { Name = el.GetProperty("name").GetString()
          Iri = el.GetProperty("iri").GetString()
          Confidence = el.GetProperty("confidence").GetDouble()
          Source = el.GetProperty("source").GetString() |> sourceFromJson
          Status = el.GetProperty("status").GetString() |> statusFromJson
          Pattern = tryStr "pattern" }

    let private readTypeMapping (el: JsonElement) : TypeMapping =
        let fields =
            el.GetProperty("fields").EnumerateArray()
            |> Seq.map readFieldMapping
            |> List.ofSeq

        { FsharpType = el.GetProperty("fsharpType").GetString()
          Iri = el.GetProperty("iri").GetString()
          Confidence = el.GetProperty("confidence").GetDouble()
          Source = el.GetProperty("source").GetString() |> sourceFromJson
          Status = el.GetProperty("status").GetString() |> statusFromJson
          Fields = fields }

    let private readVocabularyEntry (el: JsonElement) : VocabularyEntry =
        let tryStr (name: string) =
            let mutable prop = Unchecked.defaultof<JsonElement>

            if el.TryGetProperty(name, &prop) && prop.ValueKind <> JsonValueKind.Null then
                Some(prop.GetString())
            else
                None

        { Uri = el.GetProperty("uri").GetString()
          FetchedAt = tryStr "fetchedAt"
          Hash = tryStr "hash" }

    let deserialize (json: string) : Result<LockFile, string> =
        try
            let doc = JsonDocument.Parse(json)
            let root = doc.RootElement

            let schemaVersion = root.GetProperty("schemaVersion").GetInt32()

            if schemaVersion <> 1 then
                Error $"lock file schema version {schemaVersion} not supported by this CLI"
            else
                let generated = root.GetProperty("generated").GetString() |> DateTimeOffset.Parse

                let vocabularies =
                    root.GetProperty("vocabularies").EnumerateObject()
                    |> Seq.map (fun prop -> prop.Name, readVocabularyEntry prop.Value)
                    |> Map.ofSeq

                let mappings =
                    root.GetProperty("mappings").EnumerateArray()
                    |> Seq.map readTypeMapping
                    |> List.ofSeq

                Ok
                    { SchemaVersion = schemaVersion
                      Generated = generated
                      Vocabularies = vocabularies
                      Mappings = mappings }
        with ex ->
            Error $"Failed to parse lock file: {ex.Message}"

// ── Public module ────────────────────────────────────────────────────────────

module LockFile =

    /// Reads a lock file from disk. Returns Error for malformed JSON or unsupported schema version.
    let read (path: string) : Result<LockFile, string> =
        try
            let json = File.ReadAllText(path)
            Serialization.deserialize json
        with ex ->
            Error $"Failed to read lock file at '{path}': {ex.Message}"

    /// Writes a lock file to disk with deterministic field ordering. Creates parent directories if needed.
    let write (path: string) (lockFile: LockFile) : unit =
        let dir = Path.GetDirectoryName(path)

        if not (String.IsNullOrEmpty dir) && not (Directory.Exists dir) then
            Directory.CreateDirectory(dir) |> ignore

        let json = Serialization.serialize lockFile
        File.WriteAllText(path, json)

    /// Merges resolved TypeMappings into an existing LockFile.
    ///
    /// Rules:
    /// - confirmed Llm/Manual entries from existing are preserved unchanged
    /// - Convention entries from resolved replace their counterpart in existing
    /// - New entries in resolved absent from existing are appended
    /// - Entries in existing with no counterpart in resolved are preserved
    let merge (existing: LockFile) (resolved: TypeMapping list) : LockFile =
        let resolvedByType = resolved |> List.map (fun m -> m.FsharpType, m) |> Map.ofList

        let survivingExisting =
            existing.Mappings
            |> List.choose (fun m ->
                match m.Status, m.Source with
                | Confirmed, (Llm | Manual) -> Some m
                | _ ->
                    if resolvedByType |> Map.containsKey m.FsharpType then
                        None
                    else
                        Some m)

        let existingTypes = existing.Mappings |> List.map _.FsharpType |> Set.ofList

        let newEntries =
            resolved
            |> List.filter (fun m -> not (existingTypes |> Set.contains m.FsharpType))

        let replacements =
            resolved
            |> List.filter (fun m ->
                existingTypes |> Set.contains m.FsharpType
                && not (
                    existing.Mappings
                    |> List.exists (fun e ->
                        e.FsharpType = m.FsharpType
                        && e.Status = Confirmed
                        && (e.Source = Llm || e.Source = Manual))
                ))

        { existing with
            Mappings = survivingExisting @ replacements @ newEntries }
