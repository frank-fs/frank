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
      Excluded: int
      Rejected: RejectedEntry list
      Unchanged: int
      AlreadyConfirmed: int
      FieldsUnresolved: int
      Warnings: string list }

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

let private rejectBothCasesAndFields (node: JsonNode) : Result<unit, string> =
    let hasCases = not (isNull node.["cases"])
    let hasFields = not (isNull node.["fields"])

    if hasCases && hasFields then
        Error "entry has both 'cases' and 'fields'; specify one"
    else
        Ok()

let private parseShapeByTag (tag: string) (node: JsonNode) : Result<ResolvedShape, string> =
    match tag with
    | "union" ->
        match node.["cases"] with
        | null -> Error "shape:union but 'cases' key is absent"
        | casesNode -> parseCasesArray casesNode |> Result.map ResolvedShape.Union
    | "record" -> parseFieldsArray node.["fields"] |> Result.map ResolvedShape.Record
    | other -> Error $"unknown shape '{other}'"

let private parseShapeLegacy (node: JsonNode) : Result<ResolvedShape, string> =
    let hasCases = not (isNull node.["cases"])

    if hasCases then
        parseCasesArray node.["cases"] |> Result.map ResolvedShape.Union
    else
        parseFieldsArray node.["fields"] |> Result.map ResolvedShape.Record

let private parseEntry (i: int) (node: JsonNode) : Result<ResolvedEntry, string> =
    requireString node "fsharpType"
    |> Result.mapError (fun _ -> $"resolved[{i}]: fsharpType is required")
    |> Result.bind (fun fsType ->
        let iri = optionalString node "iri"

        let shapeResult =
            rejectBothCasesAndFields node
            |> Result.bind (fun () ->
                match optionalString node "shape" with
                | Some tag -> parseShapeByTag tag node
                | None -> parseShapeLegacy node)

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

let private buildExcludedMapping (source: MappingSource) (entry: ResolvedEntry) : Mapping =
    { FSharpType = entry.FSharpType
      Iri = None
      Confidence = 1.0
      Source = source
      Status = Excluded
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

let private allowedForPosition (oracle: TermOracle) (casesAllowed: Set<string>) (pos: IriPosition) : Set<string> =
    match pos with
    | TypePos -> oracle.Classes
    | FieldPos -> oracle.Properties
    | CasePos -> casesAllowed

let private isInAnyCategory (oracle: TermOracle) (absIri: string) : bool =
    Set.contains absIri oracle.Classes
    || Set.contains absIri oracle.Properties
    || Set.contains absIri oracle.Individuals

let private checkIri
    (prefixes: Map<string, System.Uri>)
    (oracle: TermOracle)
    (casesAllowed: Set<string>)
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
            let allowed = allowedForPosition oracle casesAllowed pos

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
    (casesAllowed: Set<string>)
    (c: ResolvedCase)
    : string option =
    match c.Iri |> Option.bind (checkIri prefixes oracle casesAllowed CasePos) with
    | Some err -> Some err
    | None ->
        c.Payload
        |> List.tryPick (fun f -> f.Iri |> Option.bind (checkIri prefixes oracle casesAllowed FieldPos))

let private firstShapeIriError
    (prefixes: Map<string, System.Uri>)
    (oracle: TermOracle)
    (casesAllowed: Set<string>)
    (shape: ResolvedShape)
    : string option =
    match shape with
    | ResolvedShape.Record fs ->
        fs
        |> List.tryPick (fun f -> f.Iri |> Option.bind (checkIri prefixes oracle casesAllowed FieldPos))
    | ResolvedShape.Union cs -> cs |> List.tryPick (firstCaseIriError prefixes oracle casesAllowed)

let private firstIriError
    (prefixes: Map<string, System.Uri>)
    (oracle: TermOracle)
    (casesAllowed: Set<string>)
    (e: ResolvedEntry)
    : string option =
    match e.Iri |> Option.bind (checkIri prefixes oracle casesAllowed TypePos) with
    | Some err -> Some err
    | None -> firstShapeIriError prefixes oracle casesAllowed e.Shape

let private partitionByIri
    (prefixes: Map<string, System.Uri>)
    (oracle: TermOracle)
    (entries: ResolvedEntry list)
    : RejectedEntry list * ResolvedEntry list =
    let casesAllowed = Set.union oracle.Classes oracle.Individuals

    let folder (rejected, ok) e =
        match firstIriError prefixes oracle casesAllowed e with
        | Some reason ->
            ({ FSharpType = e.FSharpType
               Reason = reason }
             :: rejected),
            ok
        | None -> rejected, (e :: ok)

    let rejected, ok = List.fold folder ([], []) entries
    List.rev rejected, List.rev ok

let private prefixOfCurie (iri: string) : string option =
    match iri.IndexOf(':') with
    | -1 -> None
    | idx -> Some iri.[.. idx - 1]

let private iriStringsFromEntry (e: ResolvedEntry) : string list =
    let fromFields (fs: ResolvedField list) = fs |> List.choose (fun f -> f.Iri)

    let fromCases (cs: ResolvedCase list) =
        cs |> List.collect (fun c -> (c.Iri |> Option.toList) @ fromFields c.Payload)

    (e.Iri |> Option.toList)
    @ match e.Shape with
      | ResolvedShape.Record fs -> fromFields fs
      | ResolvedShape.Union cs -> fromCases cs

let private collectPrefixWarnings (lf: LockFile) (entries: ResolvedEntry list) : string list =
    let fetchedKeys = lf.Vocabularies |> Map.toSeq |> Seq.map fst |> Set.ofSeq

    let unfetchedDeclared =
        lf.DeclaredPrefixes
        |> Map.filter (fun k _ -> not (Set.contains k fetchedKeys))
        |> Map.toSeq
        |> Seq.map fst
        |> Set.ofSeq

    entries
    |> List.collect iriStringsFromEntry
    |> List.choose (fun iri ->
        match prefixOfCurie iri with
        | Some prefix when Set.contains prefix unfetchedDeclared ->
            Some $"vocabulary '{prefix}' referenced but not published — host it or check the URL"
        | _ -> None)
    |> List.distinct

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

    let toExclude = inLock |> List.filter (fun e -> e.Iri.IsNone)
    let withIri = inLock |> List.filter (fun e -> e.Iri.IsSome)

    let prefixes =
        let fromVocabs =
            lf.Vocabularies |> Map.map (fun _ (e: VocabularyEntry) -> System.Uri e.Uri)

        let fromDeclared = lf.DeclaredPrefixes |> Map.map (fun _ uri -> System.Uri uri)
        Map.fold (fun acc k v -> Map.add k v acc) fromVocabs fromDeclared

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

    let excludedMappings = toExclude |> List.map (buildExcludedMapping source)

    let decidedTypes =
        (toMerge |> List.map (fun e -> e.FSharpType))
        @ (toExclude |> List.map (fun e -> e.FSharpType))
        |> Set.ofList

    let unchanged =
        lf.Mappings
        |> List.filter (fun m -> not (Set.contains m.FSharpType decidedTypes))
        |> List.length

    let updated = LockFile.merge lf (mergedMappings @ excludedMappings)

    let fieldsUnresolved =
        countUnresolvedFields updated.Mappings (toMerge |> List.map (fun e -> e.FSharpType) |> Set.ofList)

    let warnings = collectPrefixWarnings lf toMerge

    let summary =
        { Merged = toMerge.Length
          Excluded = toExclude.Length
          Rejected = notInLock @ iriRejected
          Unchanged = unchanged
          AlreadyConfirmed = alreadyConfirmed
          FieldsUnresolved = fieldsUnresolved
          Warnings = warnings }

    updated, summary

// ── Public: buildOracle ───────────────────────────────────────────────────────

/// Build a TermOracle from cached vocabulary graphs in cacheDir.
/// Vocabs with no cache file contribute nothing (offline / un-fetched).
/// The resulting oracle only enforces existence for namespaces whose cache loaded.
let buildOracle (vocabs: Map<string, VocabularyEntry>) (cacheDir: string) : TermOracle =
    let loaded =
        vocabs
        |> Map.toList
        |> List.choose (fun (prefix, entry) ->
            match loadCachedGraph cacheDir prefix with
            | Some(Ok graph) -> Some(entry, ConventionEngine.extractTermIris graph)
            | _ -> None)

    { Classes = loaded |> Seq.map (fun (_, t) -> t.ClassIris) |> Set.unionMany
      Properties = loaded |> Seq.map (fun (_, t) -> t.PropertyIris) |> Set.unionMany
      Individuals = loaded |> Seq.map (fun (_, t) -> t.IndividualIris) |> Set.unionMany
      CoveredBases = loaded |> List.map (fun (e, _) -> e.Uri) }

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

    let warningsArr = JsonArray()

    for w in s.Warnings do
        warningsArr.Add(JsonValue.Create w)

    let root = JsonObject()
    root.Add("merged", JsonValue.Create s.Merged)
    root.Add("excluded", JsonValue.Create s.Excluded)
    root.Add("rejected", rejectedArr)
    root.Add("unchanged", JsonValue.Create s.Unchanged)
    root.Add("alreadyConfirmed", JsonValue.Create s.AlreadyConfirmed)
    root.Add("fieldsUnresolved", JsonValue.Create s.FieldsUnresolved)
    root.Add("warnings", warningsArr)
    root.ToJsonString summaryWriteOptions
