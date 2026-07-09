namespace Frank.Semantic

open System
open System.IO
open System.Text.Json
open System.Text.Json.Nodes

// ── Lock file types ───────────────────────────────────────────────────────────

module LockFile =

    // Invariant: v1 entries read with IsValidated=false so legacy locks are
    // never silently laundered into "validated" state (A-C6, A-C11).
    type ValidationStatus =
        { IsValidated: bool
          Reason: string option
          LastChecked: DateTimeOffset option }

    type VocabularyEntry =
        {
            Uri: string
            FetchedAt: DateTimeOffset
            Hash: string
            // Schema-v2 evidence fields (absent in v1 JSON; safe defaults applied on read)
            MediaType: string option
            Validated: ValidationStatus
            /// Populated by `frank semantic validate` (V2/V3); consumed by the #378 analyzer for
            /// term-level dereferenceability. None = not yet fetched or parsed (unknown);
            /// Some Set.empty = vocabulary parsed but asserts no terms (suppresses Undereferenceable check).
            /// The V1 classifier does not read this field — Terms are captured in the lock for #378 to consume.
            Terms: Set<string> option
            HttpStatus: int option
            Owned: bool
            ETag: string option
            LastModified: string option
        }

    // Default for v1 backward-compat and test construction.
    // IsValidated=false with explicit reason; never trusted as validated.
    let v1Empty: VocabularyEntry =
        { Uri = ""
          FetchedAt = DateTimeOffset.MinValue
          Hash = ""
          MediaType = None
          Validated =
            { IsValidated = false
              Reason = Some "legacy-v1-unvalidated"
              LastChecked = None }
          Terms = None
          HttpStatus = None
          Owned = false
          ETag = None
          LastModified = None }

    type LockFile =
        { SchemaVersion: int
          Generated: DateTimeOffset
          Integrity: string option
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

    // ── JSON deserialization helpers (pure) ───────────────────────────────────

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
            with :? InvalidOperationException ->
                Error $"field '{key}' is not a string"

    let optionalString (node: JsonNode) (key: string) : string option =
        match node.[key] with
        | null -> None
        | n ->
            try
                let s = n.GetValue<string>()
                if s = null then None else Some s
            with :? InvalidOperationException ->
                None

    let requireFloat (node: JsonNode) (key: string) : Result<float, string> =
        match node.[key] with
        | null -> Error $"missing field '{key}'"
        | n ->
            try
                Ok(n.GetValue<float>())
            with :? InvalidOperationException ->
                Error $"field '{key}' is not a number"

    let private optionalInt (node: JsonNode) (key: string) : int option =
        match node.[key] with
        | null -> None
        | n ->
            try
                Some(n.GetValue<int>())
            with :? InvalidOperationException ->
                None

    let private optionalBool (node: JsonNode) (key: string) : bool option =
        match node.[key] with
        | null -> None
        | n ->
            try
                Some(n.GetValue<bool>())
            with :? InvalidOperationException ->
                None

    // ── ValidationStatus JSON ─────────────────────────────────────────────────

    let private parseValidationStatus (node: JsonNode) : ValidationStatus =
        match node.["validated"] with
        | null ->
            // v1 default: unvalidated, never silently trusted
            { IsValidated = false
              Reason = Some "legacy-v1-unvalidated"
              LastChecked = None }
        | vNode ->
            let isValidated = optionalBool vNode "isValidated" |> Option.defaultValue false

            let reason = optionalString vNode "reason"

            let lastChecked =
                optionalString vNode "lastChecked"
                |> Option.bind (fun s ->
                    match parseIso8601 s with
                    | Ok dto -> Some dto
                    | Error _ -> None)

            { IsValidated = isValidated
              Reason = reason
              LastChecked = lastChecked }

    let private serializeValidationStatus (vs: ValidationStatus) : JsonObject =
        let obj = JsonObject()
        obj.Add("isValidated", JsonValue.Create vs.IsValidated)

        match vs.Reason with
        | None -> obj.Add("reason", JsonValue.Create<string>(null))
        | Some r -> obj.Add("reason", JsonValue.Create r)

        match vs.LastChecked with
        | None -> obj.Add("lastChecked", JsonValue.Create<string>(null))
        | Some dto -> obj.Add("lastChecked", JsonValue.Create(dto.ToString("yyyy-MM-ddTHH:mm:ssK")))

        obj

    // ── Terms JSON ────────────────────────────────────────────────────────────

    let private parseTerms (node: JsonNode) : Set<string> option =
        match node.["terms"] with
        | null -> None
        | :? JsonArray as arr ->
            arr
            |> Seq.choose (fun x ->
                if isNull x then
                    None
                else
                    try
                        Some(x.GetValue<string>())
                    with :? InvalidOperationException ->
                        None)
            |> Set.ofSeq
            |> Some
        | _ -> None

    let private serializeTerms (terms: Set<string> option) : JsonNode =
        match terms with
        | None -> JsonValue.Create<string>(null) :> JsonNode
        | Some s ->
            let arr = JsonArray()

            for t in s |> Set.toList |> List.sort do
                arr.Add(JsonValue.Create t)

            arr

    // ── Mapping deserialization ───────────────────────────────────────────────

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
                    with :? InvalidOperationException ->
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
            let rt = optionalString node "rt"

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
                                  Rt = rt
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

    // ── VocabularyEntry deserialization ───────────────────────────────────────

    let private parseVocabEntry (version: int) (node: JsonNode) : Result<VocabularyEntry, string> =
        requireString node "uri"
        |> Result.bind (fun uri ->
            requireString node "fetchedAt"
            |> Result.bind parseIso8601
            |> Result.bind (fun fetchedAt ->
                requireString node "hash"
                |> Result.map (fun hash ->
                    if version >= 2 then
                        // v2: read all evidence fields; optional ones default to None/false
                        { Uri = uri
                          FetchedAt = fetchedAt
                          Hash = hash
                          MediaType = optionalString node "mediaType"
                          Validated = parseValidationStatus node
                          Terms = parseTerms node
                          HttpStatus = optionalInt node "httpStatus"
                          Owned = optionalBool node "owned" |> Option.defaultValue false
                          ETag = optionalString node "etag"
                          LastModified = optionalString node "lastModified" }
                    else
                        // v1: only uri/fetchedAt/hash; apply unvalidated defaults
                        { v1Empty with
                            Uri = uri
                            FetchedAt = fetchedAt
                            Hash = hash })))

    let private parseDeclaredPrefixValue (key: string) (value: JsonNode) : Result<string, string> =
        match value with
        | null -> Error $"declaredPrefixes['{key}']: not a string"
        | v ->
            try
                Ok(v.GetValue<string>())
            with :? InvalidOperationException ->
                Error $"declaredPrefixes['{key}']: not a string"

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
                        match parseDeclaredPrefixValue kvp.Key kvp.Value with
                        | Error e -> Error e
                        | Ok v -> Ok(Map.add kvp.Key v m))
                (Ok Map.empty)
        | _ -> Error "field 'declaredPrefixes' must be an object"

    let private parseVocabularies (version: int) (node: JsonNode) : Result<Map<string, VocabularyEntry>, string> =
        match node with
        | null -> Ok Map.empty
        | :? JsonObject as obj ->
            obj
            |> Seq.fold
                (fun acc kvp ->
                    match acc with
                    | Error e -> Error e
                    | Ok m ->
                        match parseVocabEntry version kvp.Value with
                        | Error e -> Error $"vocabularies['{kvp.Key}']: {e}"
                        | Ok v -> Ok(Map.add kvp.Key v m))
                (Ok Map.empty)
        | _ -> Error "field 'vocabularies' must be an object"

    let private supportedVersions = Set.ofList [ 1; 2 ]

    let private parseSchemaVersion (node: JsonNode) : Result<int, string> =
        let versionNode = node.["schemaVersion"]

        if versionNode = null then
            Error "lock file: schemaVersion is required"
        else
            try
                let v = versionNode.GetValue<int>()

                if Set.contains v supportedVersions then
                    Ok v
                else
                    Error $"lock file schema version {v} not supported by this CLI"
            with :? InvalidOperationException ->
                Error "lock file: schemaVersion must be an integer"

    let private parseBody (node: JsonNode) (version: int) : Result<LockFile, string> =
        let integrity = optionalString node "integrity"

        requireString node "generated"
        |> Result.bind parseIso8601
        |> Result.bind (fun generated ->
            parseVocabularies version node.["vocabularies"]
            |> Result.bind (fun vocabularies ->
                parseDeclaredPrefixes node.["declaredPrefixes"]
                |> Result.bind (fun declaredPrefixes ->
                    parseMappingList node.["mappings"]
                    |> Result.map (fun mappings ->
                        { SchemaVersion = version
                          Generated = generated
                          Integrity = integrity
                          Vocabularies = vocabularies
                          DeclaredPrefixes = declaredPrefixes
                          Mappings = mappings }))))

    let private parseDoc (json: string) : Result<LockFile, string> =
        (try
            Ok(JsonNode.Parse json)
         with ex ->
             Error $"JSON parse error: {ex.Message}")
        |> Result.bind (fun node ->
            match node with
            | :? JsonObject -> parseSchemaVersion node |> Result.bind (parseBody node)
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
        obj.Add("rt", m.Rt |> Option.map JsonValue.Create<string> |> Option.toObj)

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

    let private serializeVocabEntry (version: int) (v: VocabularyEntry) : JsonObject =
        let obj = JsonObject()
        obj.Add("uri", JsonValue.Create v.Uri)
        obj.Add("fetchedAt", JsonValue.Create(formatIso8601 v.FetchedAt))
        obj.Add("hash", JsonValue.Create v.Hash)

        if version >= 2 then
            match v.MediaType with
            | None -> ()
            | Some mt -> obj.Add("mediaType", JsonValue.Create mt)

            obj.Add("validated", serializeValidationStatus v.Validated)
            obj.Add("terms", serializeTerms v.Terms)

            match v.HttpStatus with
            | None -> ()
            | Some s -> obj.Add("httpStatus", JsonValue.Create s)

            obj.Add("owned", JsonValue.Create v.Owned)

            match v.ETag with
            | None -> ()
            | Some e -> obj.Add("etag", JsonValue.Create e)

            match v.LastModified with
            | None -> ()
            | Some lm -> obj.Add("lastModified", JsonValue.Create lm)

        obj

    let private serializeDoc (lf: LockFile) : JsonObject =
        let root = JsonObject()
        root.Add("schemaVersion", JsonValue.Create lf.SchemaVersion)
        root.Add("generated", JsonValue.Create(formatIso8601 lf.Generated))

        match lf.Integrity with
        | None -> ()
        | Some h -> root.Add("integrity", JsonValue.Create h)

        let vocabs = JsonObject()

        for key in lf.Vocabularies |> Map.toSeq |> Seq.map fst |> Seq.sort do
            vocabs.Add(key, serializeVocabEntry lf.SchemaVersion lf.Vocabularies.[key])

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

    // ── Integrity ─────────────────────────────────────────────────────────────

    let private canonicalBytes (lf: LockFile) : byte[] =
        let root = serializeDoc { lf with Integrity = None }
        let json = root.ToJsonString writeOptions
        Text.Encoding.UTF8.GetBytes json

    /// Compute the SHA-256 integrity hash of a lock file's canonical form.
    /// Invariant to the lock's Integrity field value — always hashes with Integrity = None.
    let computeIntegrity (lf: LockFile) : string = Hashing.sha256Hex (canonicalBytes lf)

    /// Return a new lock with Integrity stamped to the computed hash.
    let withIntegrity (lf: LockFile) : LockFile =
        { lf with
            Integrity = Some(computeIntegrity lf) }

    /// Verify the stored Integrity against the recomputed hash.
    /// None → Error "lock is unstamped; regenerate"
    /// Mismatch → Error "lock appears hand-edited; regenerate"
    let verifyIntegrity (lf: LockFile) : Result<unit, string> =
        match lf.Integrity with
        | None -> Error "lock is unstamped; regenerate"
        | Some stored ->
            let computed = computeIntegrity lf

            if stored = computed then
                Ok()
            else
                Error "lock appears hand-edited; regenerate"

    /// Verify integrity only if the lock carries a stamp; pass through unstamped legacy locks.
    /// Use this at load-time: unstamped v1 locks (no Integrity field) are legacy, not tampered.
    let verifyIfStamped (lf: LockFile) : Result<unit, string> =
        match lf.Integrity with
        | None -> Ok()
        | Some _ -> verifyIntegrity lf

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
    /// v2 vocabulary entries include all evidence fields; v1 entries include only uri/fetchedAt/hash.
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

    type PackageGroup =
        { Namespace: string
          Counts: StatusCounts
          Vocabs: (string * int) list }

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

    // ── Package grouping ─────────────────────────────────────────────────────

    /// Derive the F# namespace from a fully-qualified type name.
    /// "A.B.C" → "A.B"; "A" → "(global)".
    let namespaceOf (fsharpType: string) : string =
        let lastDot = fsharpType.LastIndexOf('.')

        if lastDot < 0 then
            "(global)"
        else
            fsharpType.[.. lastDot - 1]

    /// Extract a vocabulary key from a mapping IRI.
    /// An IRI whose substring before the first ':' is a member of knownPrefixes is a CURIE:
    ///   "schema:Game" with knownPrefixes containing "schema" → "schema".
    /// All other strings are treated as absolute IRIs and keyed by the full IRI.
    let private vocabKeyOf (knownPrefixes: Set<string>) (iri: string) : string =
        let colonIdx = iri.IndexOf(':')

        if colonIdx < 0 then
            iri
        else
            let prefix = iri.[.. colonIdx - 1]
            if Set.contains prefix knownPrefixes then prefix else iri

    /// Group mappings by derived namespace and aggregate status counts and vocab usage.
    /// Groups are sorted by namespace; vocabs within each group are sorted by key.
    let countByPackage (knownPrefixes: Set<string>) (mappings: Mapping list) : PackageGroup list =
        let byNs = mappings |> List.groupBy (fun m -> namespaceOf m.FSharpType)

        byNs
        |> List.map (fun (ns, ms) ->
            let counts = countByStatus ms

            let vocabs =
                ms
                |> List.choose (fun m -> m.Iri)
                |> List.distinct
                |> List.map (vocabKeyOf knownPrefixes)
                |> List.groupBy id
                |> List.map (fun (key, xs) -> key, List.length xs)
                |> List.sortBy fst

            { Namespace = ns
              Counts = counts
              Vocabs = vocabs })
        |> List.sortBy (fun g -> g.Namespace)

    // ── Prefix utilities ─────────────────────────────────────────────────────

    /// Build the combined prefix map from vocabularies and declared prefixes.
    /// Declared prefixes take precedence over vocabulary entries on key conflict.
    let buildPrefixMap
        (vocabularies: Map<string, VocabularyEntry>)
        (declaredPrefixes: Map<string, string>)
        : Map<string, Uri> =
        let fromVocabs = vocabularies |> Map.map (fun _ entry -> Uri(entry.Uri))
        let fromDeclared = declaredPrefixes |> Map.map (fun _ uri -> Uri(uri))
        Map.fold (fun acc k v -> Map.add k v acc) fromVocabs fromDeclared

    // ── Pure merge ────────────────────────────────────────────────────────────

    let private mergeFields (existing: FieldMapping list) (resolved: FieldMapping list) : FieldMapping list =
        let resolvedByName = resolved |> List.map (fun f -> f.Name, f) |> Map.ofList
        let existingNames = existing |> List.map (fun f -> f.Name) |> Set.ofList

        let updated =
            existing
            |> List.map (fun f ->
                match Map.tryFind f.Name resolvedByName with
                | Some r -> r
                | None -> f)

        let newFields =
            resolved |> List.filter (fun f -> not (Set.contains f.Name existingNames))

        updated @ newFields

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
        // Shape kind changed (record↔union): take freshly resolved shape wholesale.
        | _ -> resolved

    let private mergeOneMapping (existing: Mapping) (resolved: Mapping) : Mapping =
        { existing with
            Iri = resolved.Iri
            Confidence = resolved.Confidence
            Source = resolved.Source
            Status = resolved.Status
            Rt = existing.Rt
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
