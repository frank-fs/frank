namespace Frank.Semantic

open System
open System.IO
open System.Text.Json
open System.Text.Json.Nodes

// ── Lock file types ───────────────────────────────────────────────────────────

module LockFile =

    type VocabularyEntry =
        { Uri: string
          FetchedAt: DateTimeOffset
          Hash: string }

    type LockFile =
        { SchemaVersion: int
          Generated: DateTimeOffset
          Vocabularies: Map<string, VocabularyEntry>
          Mappings: Mapping list }

    // ── DU ↔ string maps (total, defined once) ────────────────────────────────

    let private sourceToString =
        Map.ofList [ Convention, "convention"; Llm, "llm"; Manual, "manual" ]

    let private stringToSource =
        Map.ofList [ "convention", Convention; "llm", Llm; "manual", Manual ]

    let private statusToString =
        Map.ofList [ Confirmed, "confirmed"; Proposed, "proposed"; Unresolved, "unresolved" ]

    let private stringToStatus =
        Map.ofList [ "confirmed", Confirmed; "proposed", Proposed; "unresolved", Unresolved ]

    let mappingSourceToString (s: MappingSource) : string = Map.find s sourceToString

    let mappingSourceFromString (s: string) : Result<MappingSource, string> =
        match Map.tryFind s stringToSource with
        | Some v -> Ok v
        | None -> Error $"unknown mapping source '{s}'"

    let mappingStatusToString (s: MappingStatus) : string = Map.find s statusToString

    let mappingStatusFromString (s: string) : Result<MappingStatus, string> =
        match Map.tryFind s stringToStatus with
        | Some v -> Ok v
        | None -> Error $"unknown mapping status '{s}'"

    // ── JSON deserialization (pure) ───────────────────────────────────────────

    let private parseIso8601 (s: string) : Result<DateTimeOffset, string> =
        match DateTimeOffset.TryParse(s) with
        | true, dto -> Ok dto
        | false, _ -> Error $"invalid ISO 8601 timestamp: '{s}'"

    let private requireString (node: JsonNode) (key: string) : Result<string, string> =
        match node.[key] with
        | null -> Error $"missing field '{key}'"
        | n ->
            try
                Ok(n.GetValue<string>())
            with _ ->
                Error $"field '{key}' is not a string"

    let private optionalString (node: JsonNode) (key: string) : string option =
        match node.[key] with
        | null -> None
        | n ->
            try
                let s = n.GetValue<string>()
                if s = null then None else Some s
            with _ ->
                None

    let private requireFloat (node: JsonNode) (key: string) : Result<float, string> =
        match node.[key] with
        | null -> Error $"missing field '{key}'"
        | n ->
            try
                Ok(n.GetValue<float>())
            with _ ->
                Error $"field '{key}' is not a number"

    let private parseFieldMapping (node: JsonNode) : Result<FieldMapping, string> =
        match requireString node "name" with
        | Error e -> Error e
        | Ok name ->
            let iri = optionalString node "iri"

            match requireFloat node "confidence" with
            | Error e -> Error e
            | Ok confidence ->
                match requireString node "source" with
                | Error e -> Error e
                | Ok sourceStr ->
                    match mappingSourceFromString sourceStr with
                    | Error e -> Error e
                    | Ok source ->
                        match requireString node "status" with
                        | Error e -> Error e
                        | Ok statusStr ->
                            match mappingStatusFromString statusStr with
                            | Error e -> Error e
                            | Ok status ->
                                Ok
                                    { Name = name
                                      Iri = iri
                                      Confidence = confidence
                                      Source = source
                                      Status = status }

    let private parseFieldMappings (node: JsonNode) : Result<FieldMapping list, string> =
        if node = null then
            Ok []
        else
            let elements = node.AsArray()
            let mutable acc: Result<FieldMapping list, string> = Ok []
            let mutable i = 0

            while i < elements.Count && Result.isOk acc do
                match parseFieldMapping elements.[i] with
                | Error e -> acc <- Error $"fields[{i}]: {e}"
                | Ok f ->
                    acc <- acc |> Result.map (fun xs -> xs @ [ f ])
                    i <- i + 1

            acc

    let private parseMapping (node: JsonNode) : Result<Mapping, string> =
        match requireString node "fsharpType" with
        | Error e -> Error e
        | Ok fsType ->
            let iri = optionalString node "iri"

            match requireFloat node "confidence" with
            | Error e -> Error e
            | Ok confidence ->
                match requireString node "source" with
                | Error e -> Error e
                | Ok sourceStr ->
                    match mappingSourceFromString sourceStr with
                    | Error e -> Error e
                    | Ok source ->
                        match requireString node "status" with
                        | Error e -> Error e
                        | Ok statusStr ->
                            match mappingStatusFromString statusStr with
                            | Error e -> Error e
                            | Ok status ->
                                match parseFieldMappings node.["fields"] with
                                | Error e -> Error e
                                | Ok fields ->
                                    let alternates =
                                        match node.["alternates"] with
                                        | null -> []
                                        | n ->
                                            n.AsArray()
                                            |> Seq.choose (fun x ->
                                                if isNull x then None else Some(x.GetValue<string>()))
                                            |> Seq.toList

                                    Ok
                                        { FSharpType = fsType
                                          Iri = iri
                                          Confidence = confidence
                                          Source = source
                                          Status = status
                                          Alternates = alternates
                                          Fields = fields }

    let private parseMappingList (node: JsonNode) : Result<Mapping list, string> =
        let elements = node.AsArray()
        let mutable acc: Result<Mapping list, string> = Ok []
        let mutable i = 0

        while i < elements.Count && Result.isOk acc do
            match parseMapping elements.[i] with
            | Error e -> acc <- Error $"mappings[{i}]: {e}"
            | Ok m ->
                acc <- acc |> Result.map (fun xs -> xs @ [ m ])
                i <- i + 1

        acc

    let private parseVocabEntry (node: JsonNode) : Result<VocabularyEntry, string> =
        match requireString node "uri" with
        | Error e -> Error e
        | Ok uri ->
            match requireString node "fetchedAt" with
            | Error e -> Error e
            | Ok fetchedAtStr ->
                match parseIso8601 fetchedAtStr with
                | Error e -> Error e
                | Ok fetchedAt ->
                    match requireString node "hash" with
                    | Error e -> Error e
                    | Ok hash ->
                        Ok
                            { Uri = uri
                              FetchedAt = fetchedAt
                              Hash = hash }

    let private parseVocabularies (node: JsonNode) : Result<Map<string, VocabularyEntry>, string> =
        if node = null then
            Ok Map.empty
        else
            let kvps = node.AsObject() |> Seq.toList
            let mutable acc: Result<Map<string, VocabularyEntry>, string> = Ok Map.empty

            for kvp in kvps do
                match acc with
                | Error _ -> ()
                | Ok m ->
                    match parseVocabEntry kvp.Value with
                    | Error e -> acc <- Error $"vocabularies['{kvp.Key}']: {e}"
                    | Ok v -> acc <- Ok(Map.add kvp.Key v m)

            acc

    let private supportedVersions = Set.ofList [ 1 ]

    let private parseDoc (json: string) : Result<LockFile, string> =
        let rootResult =
            try
                Ok(JsonNode.Parse json)
            with ex ->
                Error $"JSON parse error: {ex.Message}"

        match rootResult with
        | Error e -> Error e
        | Ok node ->
            let version =
                try
                    node.["schemaVersion"].GetValue<int>()
                with _ ->
                    -1

            if not (Set.contains version supportedVersions) then
                Error $"lock file schema version {version} not supported by this CLI"
            else

                match requireString node "generated" with
                | Error e -> Error e
                | Ok generatedStr ->
                    match parseIso8601 generatedStr with
                    | Error e -> Error e
                    | Ok generated ->
                        match parseVocabularies node.["vocabularies"] with
                        | Error e -> Error e
                        | Ok vocabularies ->
                            match parseMappingList node.["mappings"] with
                            | Error e -> Error e
                            | Ok mappings ->
                                Ok
                                    { SchemaVersion = version
                                      Generated = generated
                                      Vocabularies = vocabularies
                                      Mappings = mappings }

    // ── JSON serialization (pure, deterministic) ──────────────────────────────

    let private writeOptions =
        JsonSerializerOptions(
            WriteIndented = true,
            Encoder = Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping
        )

    let private isoFormat = "yyyy-MM-ddTHH:mm:ssK"

    let private formatIso8601 (dto: DateTimeOffset) : string = dto.ToString(isoFormat)

    let private serializeFieldMapping (f: FieldMapping) : JsonObject =
        let obj = JsonObject()
        obj.Add("name", JsonValue.Create f.Name)
        obj.Add("iri", f.Iri |> Option.map JsonValue.Create<string> |> Option.toObj)
        obj.Add("confidence", JsonValue.Create f.Confidence)
        obj.Add("source", JsonValue.Create(mappingSourceToString f.Source))
        obj.Add("status", JsonValue.Create(mappingStatusToString f.Status))
        obj

    let private serializeMapping (m: Mapping) : JsonObject =
        let obj = JsonObject()
        obj.Add("fsharpType", JsonValue.Create m.FSharpType)
        obj.Add("iri", m.Iri |> Option.map JsonValue.Create<string> |> Option.toObj)
        obj.Add("confidence", JsonValue.Create m.Confidence)
        obj.Add("source", JsonValue.Create(mappingSourceToString m.Source))
        obj.Add("status", JsonValue.Create(mappingStatusToString m.Status))

        let alternates = JsonArray()

        for a in m.Alternates do
            alternates.Add(JsonValue.Create a)

        obj.Add("alternates", alternates)

        let fields = JsonArray()

        for f in m.Fields do
            fields.Add(serializeFieldMapping f)

        obj.Add("fields", fields)
        obj

    let private serializeVocabEntry (v: VocabularyEntry) : JsonObject =
        let obj = JsonObject()
        obj.Add("uri", JsonValue.Create v.Uri)
        obj.Add("fetchedAt", JsonValue.Create(formatIso8601 v.FetchedAt))
        obj.Add("hash", JsonValue.Create v.Hash)
        obj

    let private serializeDoc (lf: LockFile) : JsonObject =
        let root = JsonObject()
        root.Add("schemaVersion", JsonValue.Create lf.SchemaVersion)
        root.Add("generated", JsonValue.Create(formatIso8601 lf.Generated))

        let vocabs = JsonObject()

        for key in lf.Vocabularies |> Map.toSeq |> Seq.map fst |> Seq.sort do
            vocabs.Add(key, serializeVocabEntry lf.Vocabularies.[key])

        root.Add("vocabularies", vocabs)

        let mappings = JsonArray()

        for m in lf.Mappings do
            mappings.Add(serializeMapping m)

        root.Add("mappings", mappings)
        root

    // ── Effectful I/O ─────────────────────────────────────────────────────────

    /// Read and validate a lock file from disk.
    /// Returns Error with message on version mismatch, missing fields, or malformed JSON.
    let read (path: string) : Result<LockFile, string> =
        if String.IsNullOrWhiteSpace path then
            invalidArg (nameof path) "path must not be empty"

        try
            let json = File.ReadAllText path
            parseDoc json
        with ex ->
            Error $"could not read lock file '{path}': {ex.Message}"

    /// Write a lock file to disk with deterministic serialization.
    /// Vocabularies keys are sorted alphabetically. Mappings preserve given order.
    let write (path: string) (lf: LockFile) : unit =
        if String.IsNullOrWhiteSpace path then
            invalidArg (nameof path) "path must not be empty"

        let root = serializeDoc lf
        let json = root.ToJsonString writeOptions
        File.WriteAllText(path, json)

    // ── Status counts ─────────────────────────────────────────────────────────

    type StatusCounts =
        { Confirmed: int
          Proposed: int
          Unresolved: int }

    let countByStatus (mappings: Mapping list) : StatusCounts =
        { Confirmed = mappings |> List.filter (fun m -> m.Status = Confirmed) |> List.length
          Proposed = mappings |> List.filter (fun m -> m.Status = Proposed) |> List.length
          Unresolved = mappings |> List.filter (fun m -> m.Status = Unresolved) |> List.length }

    // ── Pure merge ────────────────────────────────────────────────────────────

    let private mergeFields (existing: FieldMapping list) (resolved: FieldMapping list) : FieldMapping list =
        let resolvedByName = resolved |> List.map (fun f -> f.Name, f) |> Map.ofList

        existing
        |> List.map (fun f ->
            match Map.tryFind f.Name resolvedByName with
            | Some r -> r
            | None -> f)

    let private mergeOneMapping (existing: Mapping) (resolved: Mapping) : Mapping =
        { existing with
            Iri = resolved.Iri
            Confidence = resolved.Confidence
            Source = resolved.Source
            Status = resolved.Status
            Fields = mergeFields existing.Fields resolved.Fields }

    /// Merge resolved mappings into an existing lock file.
    /// Matching is by FSharpType. Unmatched existing entries are kept.
    /// New resolved entries (not in existing) are appended.
    /// Pure: returns a new LockFile, leaves lf unchanged.
    let merge (lf: LockFile) (resolved: Mapping list) : LockFile =
        let resolvedByType = resolved |> List.map (fun m -> m.FSharpType, m) |> Map.ofList

        let updatedExisting =
            lf.Mappings
            |> List.map (fun m ->
                match Map.tryFind m.FSharpType resolvedByType with
                | Some r -> mergeOneMapping m r
                | None -> m)

        let existingTypes = lf.Mappings |> List.map (fun m -> m.FSharpType) |> Set.ofList

        let newEntries =
            resolved |> List.filter (fun m -> not (Set.contains m.FSharpType existingTypes))

        { lf with
            Mappings = updatedExisting @ newEntries }
