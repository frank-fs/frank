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
          DeclaredPrefixes: Map<string, string>
          Mappings: Mapping list }

    // ── DU ↔ string maps (total, defined once) ────────────────────────────────

    let private sourceToString =
        Map.ofList [ Convention, "convention"; Llm, "llm"; Manual, "manual" ]

    let private stringToSource =
        Map.ofList [ "convention", Convention; "llm", Llm; "manual", Manual ]

    let private statusToString =
        Map.ofList
            [ Confirmed, "confirmed"
              Proposed, "proposed"
              Unresolved, "unresolved"
              Excluded, "excluded" ]

    let private stringToStatus =
        Map.ofList
            [ "confirmed", Confirmed
              "proposed", Proposed
              "unresolved", Unresolved
              "excluded", Excluded ]

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

    let isDecided (status: MappingStatus) : bool = status = Confirmed || status = Excluded

    // ── JSON deserialization (pure) ───────────────────────────────────────────

    let parseIso8601 (s: string) : Result<DateTimeOffset, string> =
        match DateTimeOffset.TryParse(s) with
        | true, dto -> Ok dto
        | false, _ -> Error $"invalid ISO 8601 timestamp: '{s}'"

    let requireString (node: JsonNode) (key: string) : Result<string, string> =
        match node.[key] with
        | null -> Error $"missing field '{key}'"
        | n ->
            try
                Ok(n.GetValue<string>())
            with _ ->
                Error $"field '{key}' is not a string"

    let optionalString (node: JsonNode) (key: string) : string option =
        match node.[key] with
        | null -> None
        | n ->
            try
                let s = n.GetValue<string>()
                if s = null then None else Some s
            with _ ->
                None

    let requireFloat (node: JsonNode) (key: string) : Result<float, string> =
        match node.[key] with
        | null -> Error $"missing field '{key}'"
        | n ->
            try
                Ok(n.GetValue<float>())
            with _ ->
                Error $"field '{key}' is not a number"

    let private parseAlternates (node: JsonNode) : Result<string list, string> =
        match node with
        | null -> Ok []
        | :? JsonArray as arr ->
            arr
            |> Seq.mapi (fun i x ->
                if isNull x then
                    Error $"alternates[{i}]: not a string"
                else
                    try
                        Ok(x.GetValue<string>())
                    with _ ->
                        Error $"alternates[{i}]: not a string")
            |> Seq.fold
                (fun acc r ->
                    match acc, r with
                    | Error e, _ -> Error e
                    | _, Error e -> Error e
                    | Ok xs, Ok x -> Ok(x :: xs))
                (Ok [])
            |> Result.map List.rev
        | _ -> Error "field 'alternates' must be an array"

    let private parseFieldMapping (node: JsonNode) : Result<FieldMapping, string> =
        requireString node "name"
        |> Result.bind (fun name ->
            let iri = optionalString node "iri"

            requireFloat node "confidence"
            |> Result.bind (fun confidence ->
                requireString node "source"
                |> Result.bind mappingSourceFromString
                |> Result.bind (fun source ->
                    requireString node "status"
                    |> Result.bind mappingStatusFromString
                    |> Result.map (fun status ->
                        { Name = name
                          Iri = iri
                          Confidence = confidence
                          Source = source
                          Status = status }))))

    let private parseFieldMappings (node: JsonNode) : Result<FieldMapping list, string> =
        match node with
        | null -> Ok []
        | :? JsonArray as elements ->
            elements
            |> Seq.mapi (fun i el -> parseFieldMapping el |> Result.mapError (fun e -> $"fields[{i}]: {e}"))
            |> Seq.fold
                (fun acc r ->
                    match acc, r with
                    | Error e, _ -> Error e
                    | _, Error e -> Error e
                    | Ok xs, Ok x -> Ok(x :: xs))
                (Ok [])
            |> Result.map List.rev
        | _ -> Error "field 'fields' must be an array"

    let private parseCaseMapping (node: JsonNode) : Result<CaseMapping, string> =
        requireString node "name"
        |> Result.bind (fun name ->
            let iri = optionalString node "iri"

            requireFloat node "confidence"
            |> Result.bind (fun confidence ->
                requireString node "source"
                |> Result.bind mappingSourceFromString
                |> Result.bind (fun source ->
                    requireString node "status"
                    |> Result.bind mappingStatusFromString
                    |> Result.bind (fun status ->
                        parseFieldMappings node.["payload"]
                        |> Result.map (fun payload ->
                            { Name = name
                              Iri = iri
                              Confidence = confidence
                              Source = source
                              Status = status
                              Payload = payload })))))

    let private parseCaseMappings (node: JsonNode) : Result<CaseMapping list, string> =
        match node with
        | null -> Ok []
        | :? JsonArray as elements ->
            elements
            |> Seq.mapi (fun i el -> parseCaseMapping el |> Result.mapError (fun e -> $"cases[{i}]: {e}"))
            |> Seq.fold
                (fun acc r ->
                    match acc, r with
                    | Error e, _ -> Error e
                    | _, Error e -> Error e
                    | Ok xs, Ok x -> Ok(x :: xs))
                (Ok [])
            |> Result.map List.rev
        | _ -> Error "field 'cases' must be an array"

    let private parseShape (node: JsonNode) : Result<MappingShape, string> =
        match optionalString node "shape" with
        | Some "union" -> parseCaseMappings node.["cases"] |> Result.map MappingShape.Union
        | Some "record"
        | None -> parseFieldMappings node.["fields"] |> Result.map MappingShape.Record
        | Some other -> Error $"unknown shape '{other}'"

    let private parseMapping (node: JsonNode) : Result<Mapping, string> =
        requireString node "fsharpType"
        |> Result.bind (fun fsType ->
            let iri = optionalString node "iri"

            requireFloat node "confidence"
            |> Result.bind (fun confidence ->
                requireString node "source"
                |> Result.bind mappingSourceFromString
                |> Result.bind (fun source ->
                    requireString node "status"
                    |> Result.bind mappingStatusFromString
                    |> Result.bind (fun status ->
                        parseShape node
                        |> Result.bind (fun shape ->
                            parseAlternates node.["alternates"]
                            |> Result.map (fun alternates ->
                                { FSharpType = fsType
                                  Iri = iri
                                  Confidence = confidence
                                  Source = source
                                  Status = status
                                  Alternates = alternates
                                  Shape = shape }))))))

    let private parseMappingList (node: JsonNode) : Result<Mapping list, string> =
        match node with
        | :? JsonArray as elements ->
            elements
            |> Seq.mapi (fun i el -> parseMapping el |> Result.mapError (fun e -> $"mappings[{i}]: {e}"))
            |> Seq.fold
                (fun acc r ->
                    match acc, r with
                    | Error e, _ -> Error e
                    | _, Error e -> Error e
                    | Ok xs, Ok x -> Ok(x :: xs))
                (Ok [])
            |> Result.map List.rev
        | _ -> Error "field 'mappings' must be an array"

    let private parseVocabEntry (node: JsonNode) : Result<VocabularyEntry, string> =
        requireString node "uri"
        |> Result.bind (fun uri ->
            requireString node "fetchedAt"
            |> Result.bind parseIso8601
            |> Result.bind (fun fetchedAt ->
                requireString node "hash"
                |> Result.map (fun hash ->
                    { Uri = uri
                      FetchedAt = fetchedAt
                      Hash = hash })))

    let private parseDeclaredPrefixes (node: JsonNode) : Result<Map<string, string>, string> =
        match node with
        | null -> Ok Map.empty
        | :? JsonObject as obj ->
            obj
            |> Seq.fold
                (fun acc kvp ->
                    match acc with
                    | Error e -> Error e
                    | Ok m ->
                        try
                            Ok(Map.add kvp.Key (kvp.Value.GetValue<string>()) m)
                        with _ ->
                            Error $"declaredPrefixes['{kvp.Key}']: not a string")
                (Ok Map.empty)
        | _ -> Error "field 'declaredPrefixes' must be an object"

    let private parseVocabularies (node: JsonNode) : Result<Map<string, VocabularyEntry>, string> =
        match node with
        | null -> Ok Map.empty
        | :? JsonObject as obj ->
            obj
            |> Seq.fold
                (fun acc kvp ->
                    match acc with
                    | Error e -> Error e
                    | Ok m ->
                        match parseVocabEntry kvp.Value with
                        | Error e -> Error $"vocabularies['{kvp.Key}']: {e}"
                        | Ok v -> Ok(Map.add kvp.Key v m))
                (Ok Map.empty)
        | _ -> Error "field 'vocabularies' must be an object"

    let private supportedVersions = Set.ofList [ 1 ]

    let private parseDoc (json: string) : Result<LockFile, string> =
        let rootResult =
            try
                Ok(JsonNode.Parse json)
            with ex ->
                Error $"JSON parse error: {ex.Message}"

        rootResult
        |> Result.bind (fun node ->
            match node with
            | :? JsonObject ->
                let schemaVersionNode = node.["schemaVersion"]

                if schemaVersionNode = null then
                    Error "lock file: schemaVersion is required"
                else
                    let versionResult =
                        try
                            Ok(schemaVersionNode.GetValue<int>())
                        with _ ->
                            Error "lock file: schemaVersion must be an integer"

                    versionResult
                    |> Result.bind (fun version ->
                        if not (Set.contains version supportedVersions) then
                            Error $"lock file schema version {version} not supported by this CLI"
                        else
                            requireString node "generated"
                            |> Result.bind parseIso8601
                            |> Result.bind (fun generated ->
                                parseVocabularies node.["vocabularies"]
                                |> Result.bind (fun vocabularies ->
                                    parseDeclaredPrefixes node.["declaredPrefixes"]
                                    |> Result.bind (fun declaredPrefixes ->
                                        parseMappingList node.["mappings"]
                                        |> Result.map (fun mappings ->
                                            { SchemaVersion = version
                                              Generated = generated
                                              Vocabularies = vocabularies
                                              DeclaredPrefixes = declaredPrefixes
                                              Mappings = mappings })))))
            | _ -> Error "lock file: root must be a JSON object")

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

    let private serializeCaseMapping (c: CaseMapping) : JsonObject =
        let obj = JsonObject()
        obj.Add("name", JsonValue.Create c.Name)
        obj.Add("iri", c.Iri |> Option.map JsonValue.Create<string> |> Option.toObj)
        obj.Add("confidence", JsonValue.Create c.Confidence)
        obj.Add("source", JsonValue.Create(mappingSourceToString c.Source))
        obj.Add("status", JsonValue.Create(mappingStatusToString c.Status))

        let payload = JsonArray()

        for f in c.Payload do
            payload.Add(serializeFieldMapping f)

        obj.Add("payload", payload)
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

        match m.Shape with
        | MappingShape.Record fs ->
            obj.Add("shape", JsonValue.Create "record")
            let fields = JsonArray()

            for f in fs do
                fields.Add(serializeFieldMapping f)

            obj.Add("fields", fields)
        | MappingShape.Union cases ->
            obj.Add("shape", JsonValue.Create "union")
            let arr = JsonArray()

            for c in cases do
                arr.Add(serializeCaseMapping c)

            obj.Add("cases", arr)

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

        let declaredPrefixesObj = JsonObject()

        for key in lf.DeclaredPrefixes |> Map.toSeq |> Seq.map fst |> Seq.sort do
            declaredPrefixesObj.Add(key, JsonValue.Create lf.DeclaredPrefixes.[key])

        root.Add("declaredPrefixes", declaredPrefixesObj)

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
          Unresolved: int
          Excluded: int }

    let countByStatus (mappings: Mapping list) : StatusCounts =
        let tally (acc: StatusCounts) (m: Mapping) =
            match m.Status with
            | Confirmed ->
                { acc with
                    Confirmed = acc.Confirmed + 1 }
            | Proposed -> { acc with Proposed = acc.Proposed + 1 }
            | Unresolved ->
                { acc with
                    Unresolved = acc.Unresolved + 1 }
            | Excluded -> { acc with Excluded = acc.Excluded + 1 }

        List.fold
            tally
            { Confirmed = 0
              Proposed = 0
              Unresolved = 0
              Excluded = 0 }
            mappings

    // ── Pure merge ────────────────────────────────────────────────────────────

    let private mergeFields (existing: FieldMapping list) (resolved: FieldMapping list) : FieldMapping list =
        let resolvedByName = resolved |> List.map (fun f -> f.Name, f) |> Map.ofList

        existing
        |> List.map (fun f ->
            match Map.tryFind f.Name resolvedByName with
            | Some r -> r
            | None -> f)

    let private mergeShape (existing: MappingShape) (resolved: MappingShape) : MappingShape =
        match existing, resolved with
        | MappingShape.Record ef, MappingShape.Record rf -> MappingShape.Record(mergeFields ef rf)
        | MappingShape.Union ec, MappingShape.Union rc ->
            let rByName = rc |> List.map (fun c -> c.Name, c) |> Map.ofList

            ec
            |> List.map (fun c ->
                match Map.tryFind c.Name rByName with
                | Some r ->
                    { r with
                        Payload = mergeFields c.Payload r.Payload }
                | None -> c)
            |> MappingShape.Union
        // Shape kind changed between lock versions (type went record<->union in source);
        // no field-level merge is meaningful — take the freshly resolved shape wholesale.
        | _ -> resolved

    let private mergeOneMapping (existing: Mapping) (resolved: Mapping) : Mapping =
        { existing with
            Iri = resolved.Iri
            Confidence = resolved.Confidence
            Source = resolved.Source
            Status = resolved.Status
            Shape = mergeShape existing.Shape resolved.Shape }

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
