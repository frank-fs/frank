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

let private parseShapeByTag (i: int) (tag: string) (node: JsonNode) : Result<ResolvedShape, string> =
    match tag with
    | "union" ->
        match node.["cases"] with
        | null -> Error $"resolved[{i}]: shape:union but 'cases' key is absent"
        | casesNode -> parseCasesArray casesNode |> Result.map ResolvedShape.Union
    | "record" -> parseFieldsArray node.["fields"] |> Result.map ResolvedShape.Record
    | other -> Error $"resolved[{i}]: unknown shape '{other}'"

let private parseShapeLegacy (i: int) (node: JsonNode) : Result<ResolvedShape, string> =
    let hasCases = not (isNull node.["cases"])
    let hasFields = not (isNull node.["fields"])

    match hasCases, hasFields with
    | true, true -> Error $"resolved[{i}]: entry has both 'cases' and 'fields'; specify one"
    | true, false -> parseCasesArray node.["cases"] |> Result.map ResolvedShape.Union
    | false, _ -> parseFieldsArray node.["fields"] |> Result.map ResolvedShape.Record

let private parseEntry (i: int) (node: JsonNode) : Result<ResolvedEntry, string> =
    requireString node "fsharpType"
    |> Result.mapError (fun _ -> $"resolved[{i}]: fsharpType is required")
    |> Result.bind (fun fsType ->
        let iri = optionalString node "iri"

        let shapeResult =
            match optionalString node "shape" with
            | Some tag ->
                let hasCases = not (isNull node.["cases"])
                let hasFields = not (isNull node.["fields"])

                if hasCases && hasFields then
                    Error $"resolved[{i}]: entry has both 'cases' and 'fields'; specify one"
                else
                    parseShapeByTag i tag node
            | None -> parseShapeLegacy i node

        shapeResult
        |> Result.mapError (fun e ->
            if e.StartsWith($"resolved[{i}]:", System.StringComparison.Ordinal) then
                e
            else
                $"resolved[{i}]: {e}")
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

/// Term existence oracle built from cached vocabulary graphs.
/// Classes/Properties/Individuals hold absolute IRI strings per category.
/// CoveredBases = base URI strings (e.g. "https://schema.org/") whose cache was loaded.
/// An empty oracle (all Set.empty, CoveredBases=[]) disables existence checking (back-compat).
type TermOracle =
    { Classes: Set<string>
      Properties: Set<string>
      Individuals: Set<string>
      CoveredBases: string list }

let private emptyTermOracle =
    { Classes = Set.empty
      Properties = Set.empty
      Individuals = Set.empty
      CoveredBases = [] }

type private IriPosition =
    | TypePos
    | FieldPos
    | CasePos

/// "Covered" means we hold a cache for this namespace — NOT authoritative term identity.
/// This is deliberately fail-open: an IRI from an uncached namespace is never rejected,
/// even if it would be absent from the vocabulary. Do NOT normalize http/https schemes
/// here; fail-open is the correct safe behavior for offline/uncached scenarios.
let private isCoveredByOracle (coveredBases: string list) (absIri: string) : bool =
    coveredBases
    |> List.exists (fun b -> absIri.StartsWith(b, System.StringComparison.Ordinal))

let private positionLabel (pos: IriPosition) : string =
    match pos with
    | TypePos -> "class"
    | FieldPos -> "property"
    | CasePos -> "class-or-individual"

let private allowedForPosition (oracle: TermOracle) (pos: IriPosition) : Set<string> =
    match pos with
    | TypePos -> oracle.Classes
    | FieldPos -> oracle.Properties
    | CasePos -> Set.union oracle.Classes oracle.Individuals

let private isInAnyCategory (oracle: TermOracle) (absIri: string) : bool =
    Set.contains absIri oracle.Classes
    || Set.contains absIri oracle.Properties
    || Set.contains absIri oracle.Individuals

let private checkIri
    (prefixes: Map<string, System.Uri>)
    (oracle: TermOracle)
    (pos: IriPosition)
    (iri: string)
    : string option =
    match VocabularyRegistry.tryResolveIri prefixes (Some iri) with
    | Error msg -> Some $"unresolvable iri '{iri}': {msg}; use CURIE form (e.g. schema:Foo)"
    | Ok(Some absUri) ->
        let absIri = absUri.AbsoluteUri

        if not (isCoveredByOracle oracle.CoveredBases absIri) then
            None
        else
            let allowed = allowedForPosition oracle pos

            if Set.contains absIri allowed then
                None
            elif isInAnyCategory oracle absIri then
                let expected = positionLabel pos
                Some $"term '{iri}' exists in the vocabulary but not as a {expected} (used in {pos} position)"
            else
                Some $"term '{iri}' not found in vocabulary; check spelling"
    | Ok None -> None

let private firstCaseIriError
    (prefixes: Map<string, System.Uri>)
    (oracle: TermOracle)
    (c: ResolvedCase)
    : string option =
    match c.Iri |> Option.bind (checkIri prefixes oracle CasePos) with
    | Some err -> Some err
    | None ->
        c.Payload
        |> List.tryPick (fun f -> f.Iri |> Option.bind (checkIri prefixes oracle FieldPos))

let private firstShapeIriError
    (prefixes: Map<string, System.Uri>)
    (oracle: TermOracle)
    (shape: ResolvedShape)
    : string option =
    match shape with
    | ResolvedShape.Record fs ->
        fs
        |> List.tryPick (fun f -> f.Iri |> Option.bind (checkIri prefixes oracle FieldPos))
    | ResolvedShape.Union cs -> cs |> List.tryPick (firstCaseIriError prefixes oracle)

let private firstIriError (prefixes: Map<string, System.Uri>) (oracle: TermOracle) (e: ResolvedEntry) : string option =
    match e.Iri |> Option.bind (checkIri prefixes oracle TypePos) with
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
    let folder (acc: TermOracle) (prefix: string) (entry: VocabularyEntry) =
        match loadCachedGraph cacheDir prefix with
        | None -> acc
        | Some(Error _) -> acc
        | Some(Ok graph) ->
            let termIris = ConventionEngine.extractTermIris graph

            { Classes = Set.union acc.Classes termIris.ClassIris
              Properties = Set.union acc.Properties termIris.PropertyIris
              Individuals = Set.union acc.Individuals termIris.IndividualIris
              CoveredBases = entry.Uri :: acc.CoveredBases }

    Map.fold folder emptyTermOracle vocabs

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
