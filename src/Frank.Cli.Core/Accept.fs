module Frank.Cli.Core.Accept

open System.Text.Encodings.Web
open System.Text.Json
open System.Text.Json.Nodes
open Frank.Semantic
open Frank.Semantic.LockFile
open Frank.Semantic.VocabFetcher

type ResolvedField = { Name: string; Iri: string option }

type ResolvedCase =
    { Name: string
      Iri: string option
      Payload: ResolvedField list }

[<RequireQualifiedAccess>]
type ResolvedShape =
    | Record of ResolvedField list
    | Union of ResolvedCase list

type ResolvedEntry =
    { FSharpType: string
      Iri: string option
      Shape: ResolvedShape }

type ResolvedDoc =
    { SchemaVersion: int
      Resolved: ResolvedEntry list }

type RejectedEntry = { FSharpType: string; Reason: string }

type AcceptSummary =
    { Merged: int
      Rejected: RejectedEntry list
      Unchanged: int
      AlreadyConfirmed: int
      FieldsUnresolved: int }

// ── Parse helpers ─────────────────────────────────────────────────────────────

let private supportedVersion = 1

let private parseField (node: JsonNode) : Result<ResolvedField, string> =
    requireString node "name"
    |> Result.map (fun name ->
        { Name = name
          Iri = optionalString node "iri" })

let private parseJsonArray
    (label: string)
    (parseElement: int -> JsonNode -> Result<'a, string>)
    (node: JsonNode)
    : Result<'a list, string> =
    match node with
    | null -> Ok []
    | :? JsonArray as arr ->
        arr
        |> Seq.mapi (fun i el -> parseElement i el)
        |> Seq.fold
            (fun acc r ->
                match acc, r with
                | Error e, _ -> Error e
                | _, Error e -> Error e
                | Ok xs, Ok x -> Ok(x :: xs))
            (Ok [])
        |> Result.map List.rev
    | _ -> Error $"{label} must be an array"

let private parseFieldsArray (node: JsonNode) : Result<ResolvedField list, string> =
    parseJsonArray "fields" (fun i el -> parseField el |> Result.mapError (fun e -> $"fields[{i}]: {e}")) node

let private parseCase (node: JsonNode) : Result<ResolvedCase, string> =
    requireString node "name"
    |> Result.bind (fun name ->
        parseFieldsArray node.["payload"]
        |> Result.map (fun payload ->
            { Name = name
              Iri = optionalString node "iri"
              Payload = payload }))

let private parseCasesArray (node: JsonNode) : Result<ResolvedCase list, string> =
    parseJsonArray "cases" (fun i el -> parseCase el |> Result.mapError (fun e -> $"cases[{i}]: {e}")) node

let private parseEntry (i: int) (node: JsonNode) : Result<ResolvedEntry, string> =
    requireString node "fsharpType"
    |> Result.mapError (fun _ -> $"resolved[{i}]: fsharpType is required")
    |> Result.bind (fun fsType ->
        let iri = optionalString node "iri"

        let shapeResult =
            match node.["cases"] with
            | null -> parseFieldsArray node.["fields"] |> Result.map ResolvedShape.Record
            | casesNode -> parseCasesArray casesNode |> Result.map ResolvedShape.Union

        shapeResult
        |> Result.mapError (fun e -> $"resolved[{i}]: {e}")
        |> Result.map (fun shape ->
            { FSharpType = fsType
              Iri = iri
              Shape = shape }))

let private parseEntries (arr: JsonArray) : Result<ResolvedEntry list, string> =
    arr
    |> Seq.mapi (fun i el -> parseEntry i el)
    |> Seq.fold
        (fun acc r ->
            match acc, r with
            | Error e, _ -> Error e
            | _, Error e -> Error e
            | Ok xs, Ok x -> Ok(x :: xs))
        (Ok [])
    |> Result.map List.rev

// ── Public: parseResolved ─────────────────────────────────────────────────────

let parseResolved (json: string) : Result<ResolvedDoc, string> =
    let nodeResult =
        try
            Ok(JsonNode.Parse json)
        with ex ->
            Error $"JSON parse error: {ex.Message}"

    nodeResult
    |> Result.bind (fun node ->
        match node with
        | :? JsonObject ->
            let versionNode = node.["schemaVersion"]

            if versionNode = null then
                Error "resolved.json: schemaVersion is required"
            else
                let versionResult =
                    try
                        Ok(versionNode.GetValue<int>())
                    with _ ->
                        Error "resolved.json: schemaVersion must be an integer"

                versionResult
                |> Result.bind (fun v ->
                    if v <> supportedVersion then
                        Error $"schema version {v} not supported"
                    else
                        let resolvedNode = node.["resolved"]

                        match resolvedNode with
                        | null -> Ok { SchemaVersion = v; Resolved = [] }
                        | :? JsonArray as arr ->
                            parseEntries arr
                            |> Result.map (fun entries ->
                                { SchemaVersion = v
                                  Resolved = entries })
                        | _ -> Error "resolved.json: resolved must be an array")
        | _ -> Error "resolved.json: root must be a JSON object")

// ── Apply helpers ─────────────────────────────────────────────────────────────

let private buildFieldMapping (source: MappingSource) (rf: ResolvedField) : FieldMapping =
    match rf.Iri with
    | None ->
        { Name = rf.Name
          Iri = None
          Confidence = 0.0
          Source = source
          Status = Unresolved }
    | Some iri ->
        { Name = rf.Name
          Iri = Some iri
          Confidence = 1.0
          Source = source
          Status = Confirmed }

let private buildCaseMapping (source: MappingSource) (rc: ResolvedCase) : CaseMapping =
    let payload = rc.Payload |> List.map (buildFieldMapping source)

    match rc.Iri with
    | None ->
        { Name = rc.Name
          Iri = None
          Confidence = 0.0
          Source = source
          Status = Unresolved
          Payload = payload }
    | Some iri ->
        { Name = rc.Name
          Iri = Some iri
          Confidence = 1.0
          Source = source
          Status = Confirmed
          Payload = payload }

let private buildShape (source: MappingSource) (shape: ResolvedShape) : MappingShape =
    match shape with
    | ResolvedShape.Record fs -> MappingShape.Record(fs |> List.map (buildFieldMapping source))
    | ResolvedShape.Union cs -> MappingShape.Union(cs |> List.map (buildCaseMapping source))

let private buildMapping (source: MappingSource) (entry: ResolvedEntry) (iri: string) : Mapping =
    { FSharpType = entry.FSharpType
      Iri = Some iri
      Confidence = 1.0
      Source = source
      Status = Confirmed
      Alternates = []
      Shape = buildShape source entry.Shape }

let private countUnresolvedFields (mappings: Mapping list) (types: Set<string>) : int =
    mappings
    |> List.filter (fun m -> Set.contains m.FSharpType types)
    |> List.sumBy (fun m ->
        MappingShape.payloadFields m.Shape
        |> List.filter (fun f -> f.Status = Unresolved)
        |> List.length)

// ── Public: apply ─────────────────────────────────────────────────────────────

/// (knownTerms, coveredBases): term existence oracle built from cached vocab graphs.
/// knownTerms = absolute IRI strings of all known terms across all cached vocabs.
/// coveredBases = base URI strings (e.g. "https://schema.org/") whose cache was loaded.
/// An empty oracle (Set.empty, []) disables existence checking (back-compat).
type TermOracle = Set<string> * string list

let private isCoveredByOracle (coveredBases: string list) (absIri: string) : bool =
    coveredBases
    |> List.exists (fun b -> absIri.StartsWith(b, System.StringComparison.Ordinal))

let private checkIri
    (prefixes: Map<string, System.Uri>)
    (oracle: TermOracle)
    (label: string)
    (iri: string)
    : string option =
    match VocabularyRegistry.tryResolveIri prefixes (Some iri) with
    | Error msg -> Some $"unresolvable {label} '{iri}': {msg}; use CURIE form (e.g. schema:Foo)"
    | Ok(Some absUri) ->
        let knownTerms, coveredBases = oracle
        let absIri = absUri.AbsoluteUri

        if isCoveredByOracle coveredBases absIri && not (Set.contains absIri knownTerms) then
            Some $"term '{absIri}' not found in vocabulary; check spelling"
        else
            None
    | Ok None -> None

let private firstCaseIriError
    (prefixes: Map<string, System.Uri>)
    (oracle: TermOracle)
    (c: ResolvedCase)
    : string option =
    match c.Iri |> Option.bind (checkIri prefixes oracle "case iri") with
    | Some err -> Some err
    | None ->
        c.Payload
        |> List.tryPick (fun f -> f.Iri |> Option.bind (checkIri prefixes oracle "payload iri"))

let private firstShapeIriError
    (prefixes: Map<string, System.Uri>)
    (oracle: TermOracle)
    (shape: ResolvedShape)
    : string option =
    match shape with
    | ResolvedShape.Record fs ->
        fs
        |> List.tryPick (fun f -> f.Iri |> Option.bind (checkIri prefixes oracle "field iri"))
    | ResolvedShape.Union cs -> cs |> List.tryPick (firstCaseIriError prefixes oracle)

let private firstIriError (prefixes: Map<string, System.Uri>) (oracle: TermOracle) (e: ResolvedEntry) : string option =
    match e.Iri |> Option.bind (checkIri prefixes oracle "iri") with
    | Some err -> Some err
    | None -> firstShapeIriError prefixes oracle e.Shape

let private partitionByIri
    (prefixes: Map<string, System.Uri>)
    (oracle: TermOracle)
    (entries: ResolvedEntry list)
    : RejectedEntry list * ResolvedEntry list =
    let folder (rejected, ok) e =
        match firstIriError prefixes oracle e with
        | Some reason ->
            ({ FSharpType = e.FSharpType
               Reason = reason }
             :: rejected),
            ok
        | None -> rejected, (e :: ok)

    let rejected, ok = List.fold folder ([], []) entries
    List.rev rejected, List.rev ok

let apply (lf: LockFile) (doc: ResolvedDoc) (source: MappingSource) (oracle: TermOracle) : LockFile * AcceptSummary =
    let lockTypes = lf.Mappings |> List.map (fun m -> m.FSharpType) |> Set.ofList

    let notInLock =
        doc.Resolved
        |> List.filter (fun e -> not (Set.contains e.FSharpType lockTypes))
        |> List.map (fun e ->
            { FSharpType = e.FSharpType
              Reason = "not in lock file" })

    let inLock =
        doc.Resolved |> List.filter (fun e -> Set.contains e.FSharpType lockTypes)

    let nullIriRejected =
        inLock
        |> List.filter (fun e -> e.Iri.IsNone)
        |> List.map (fun e ->
            { FSharpType = e.FSharpType
              Reason = "iri is required for a confirmed mapping" })

    let withIri = inLock |> List.filter (fun e -> e.Iri.IsSome)

    let prefixes =
        lf.Vocabularies |> Map.map (fun _ (e: VocabularyEntry) -> System.Uri e.Uri)

    let iriRejected, toMerge = partitionByIri prefixes oracle withIri

    let confirmedTypes =
        lf.Mappings
        |> List.choose (fun m -> if m.Status = Confirmed then Some m.FSharpType else None)
        |> Set.ofList

    let alreadyConfirmed =
        toMerge
        |> List.filter (fun e -> Set.contains e.FSharpType confirmedTypes)
        |> List.length

    let mergedMappings =
        toMerge |> List.map (fun e -> buildMapping source e e.Iri.Value)

    let mergedTypes = toMerge |> List.map (fun e -> e.FSharpType) |> Set.ofList

    let unchanged =
        lf.Mappings
        |> List.filter (fun m -> not (Set.contains m.FSharpType mergedTypes))
        |> List.length

    let updated = LockFile.merge lf mergedMappings
    let fieldsUnresolved = countUnresolvedFields updated.Mappings mergedTypes

    let summary =
        { Merged = toMerge.Length
          Rejected = notInLock @ nullIriRejected @ iriRejected
          Unchanged = unchanged
          AlreadyConfirmed = alreadyConfirmed
          FieldsUnresolved = fieldsUnresolved }

    updated, summary

// ── Public: buildOracle ───────────────────────────────────────────────────────

/// Build a TermOracle from cached vocabulary graphs in cacheDir.
/// Vocabs with no cache file contribute nothing (offline / un-fetched).
/// The resulting oracle only enforces existence for namespaces whose cache loaded.
let buildOracle (vocabs: Map<string, VocabularyEntry>) (cacheDir: string) : TermOracle =
    let folder (terms: Set<string>, bases: string list) (prefix: string) (entry: VocabularyEntry) =
        match loadCachedGraph cacheDir prefix with
        | None -> terms, bases
        | Some(Error _) -> terms, bases
        | Some(Ok graph) ->
            let vocabTerms = ConventionEngine.extractVocabTerms graph

            let allIris =
                [ vocabTerms.Classes |> Map.toSeq |> Seq.map snd
                  vocabTerms.Properties |> Map.toSeq |> Seq.map snd
                  vocabTerms.Individuals |> Map.toSeq |> Seq.map snd ]
                |> Seq.concat

            let newTerms = allIris |> Seq.fold (fun s iri -> Set.add iri s) terms
            newTerms, (entry.Uri :: bases)

    Map.fold folder (Set.empty, []) vocabs

// ── Public: summaryToJson ─────────────────────────────────────────────────────

let private summaryWriteOptions =
    JsonSerializerOptions(WriteIndented = false, Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping)

let summaryToJson (s: AcceptSummary) : string =
    let rejectedArr = JsonArray()

    for r in s.Rejected do
        let entry = JsonObject()
        entry.Add("fsharpType", JsonValue.Create r.FSharpType)
        entry.Add("reason", JsonValue.Create r.Reason)
        rejectedArr.Add(entry)

    let root = JsonObject()
    root.Add("merged", JsonValue.Create s.Merged)
    root.Add("rejected", rejectedArr)
    root.Add("unchanged", JsonValue.Create s.Unchanged)
    root.Add("alreadyConfirmed", JsonValue.Create s.AlreadyConfirmed)
    root.Add("fieldsUnresolved", JsonValue.Create s.FieldsUnresolved)
    root.ToJsonString summaryWriteOptions
