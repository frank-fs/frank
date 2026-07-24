module Frank.Cli.Core.Accept

open System.Text.Encodings.Web
open System.Text.Json
open System.Text.Json.Nodes
open Frank.Semantic
open Frank.Semantic.LockFile
open Frank.Semantic.VocabClassifier
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
      Status: MappingStatus option
      Shape: ResolvedShape }

type ResolvedDoc =
    { SchemaVersion: int
      Resolved: ResolvedEntry list }

type RejectedEntry = { FSharpType: string; Reason: string }

/// Location context for a vocab warning: the type (simple name) and optional field that references the namespace.
/// None at the record level means no mapping reference was found (status path).
type VocabWarningLocation = { Type: string; Field: string option }

/// Warning emitted when a referenced vocabulary namespace is Undereferenceable.
/// State is typed as VocabState and stringified only at the JSON boundary.
/// Location is None when no mapping reference to this namespace was found (status scan path).
type VocabWarning =
    { Prefix: string
      State: VocabState
      Iri: string
      Location: VocabWarningLocation option
      Hint: string }

type AcceptSummary =
    { Merged: int
      Excluded: int
      Rejected: RejectedEntry list
      Unchanged: int
      AlreadyConfirmed: int
      FieldsUnresolved: int
      Warnings: VocabWarning list }

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

        let statusResult =
            match optionalString node "status" with
            | None -> Ok None
            | Some s ->
                LockFile.mappingStatusFromString s
                |> Result.mapError (fun e -> $"resolved[{i}]: {e}")
                |> Result.map Some

        let shapeResult =
            rejectBothCasesAndFields node
            |> Result.bind (fun () ->
                match optionalString node "shape" with
                | Some tag -> parseShapeByTag tag node
                | None -> parseShapeLegacy node)

        statusResult
        |> Result.bind (fun status ->
            shapeResult
            |> Result.mapError (fun e -> $"resolved[{i}]: {e}")
            |> Result.map (fun shape ->
                { FSharpType = fsType
                  Iri = iri
                  Status = status
                  Shape = shape })))

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
                    with :? System.InvalidOperationException ->
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
      Rt = None
      Shape = buildShape source entry.Shape }

let private buildExcludedMapping (source: MappingSource) (entry: ResolvedEntry) : Mapping =
    { FSharpType = entry.FSharpType
      Iri = None
      Confidence = 1.0
      Source = source
      Status = Excluded
      Alternates = []
      Rt = None
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

let private checkAbsoluteIri
    (oracle: TermOracle)
    (casesAllowed: Set<string>)
    (pos: IriPosition)
    (absIri: string)
    : string option =
    if not (isCoveredByOracle oracle.CoveredBases absIri) then
        None
    else
        let allowed = allowedForPosition oracle casesAllowed pos

        if Set.contains absIri allowed then
            None
        elif isInAnyCategory oracle absIri then
            let expected = positionLabel pos
            Some $"term '{absIri}' exists in the vocabulary but not as a {expected} (used in {pos} position)"
        else
            Some $"term '{absIri}' not found in vocabulary; check spelling"

let private checkIri
    (prefixes: Map<string, System.Uri>)
    (oracle: TermOracle)
    (casesAllowed: Set<string>)
    (pos: IriPosition)
    (iri: string)
    : string option =
    // Absolute IRIs in field/case positions are treated as pre-resolved (item 2).
    // TypePos still requires CURIE form — FIX2 invariant: absolute type IRIs are rejected
    // so that authors receive an actionable error message rather than silent misbehavior.
    if iri.Contains("://") && pos <> TypePos then
        checkAbsoluteIri oracle casesAllowed pos iri
    else
        match VocabularyRegistry.tryResolveIri prefixes (Some iri) with
        | Error msg -> Some $"unresolvable iri '{iri}': {msg}; use CURIE form (e.g. schema:Foo)"
        | Ok(Some absUri) -> checkAbsoluteIri oracle casesAllowed pos absUri.AbsoluteUri
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

let internal prefixOfCurie (iri: string) : string option =
    if iri.Contains("://") then
        None
    else
        match iri.IndexOf(':') with
        | -1 -> None
        | idx -> Some iri.[.. idx - 1]

let private simpleTypeName (fsType: string) : string =
    match fsType.LastIndexOf('.') with
    | -1 -> fsType
    | idx -> fsType.[idx + 1 ..]

/// Format the "host-it" hint for a vocabulary namespace IRI.
/// Strips a trailing '#' so the hint names the dereferenceable document, not the fragment root.
let vocabWarningHint (iri: string) : string =
    let derefTarget = if iri.EndsWith("#") then iri.[.. iri.Length - 2] else iri
    $"publish the vocabulary namespace at {derefTarget} as dereferenceable RDF"

// Returns (prefix, simpleTypeName, fieldName option) for each IRI in the entry using the given
// prefix-extraction function. Type-level IRI → field = None. Field/case/payload IRI → field = Some name.
let private extractIriContexts
    (getPrefix: string -> string option)
    (e: ResolvedEntry)
    : (string * string * string option) list =
    let tname = simpleTypeName e.FSharpType

    let fromFields (fs: ResolvedField list) =
        fs
        |> List.choose (fun f -> f.Iri |> Option.bind getPrefix |> Option.map (fun p -> p, tname, Some f.Name))

    let fromCase (c: ResolvedCase) =
        let caseCtx =
            c.Iri
            |> Option.bind getPrefix
            |> Option.map (fun p -> p, tname, Some c.Name)
            |> Option.toList

        caseCtx @ fromFields c.Payload

    let typeCtx =
        e.Iri
        |> Option.bind getPrefix
        |> Option.map (fun p -> p, tname, None)
        |> Option.toList

    let shapeCtxs =
        match e.Shape with
        | ResolvedShape.Record fs -> fromFields fs
        | ResolvedShape.Union cs -> cs |> List.collect fromCase

    typeCtx @ shapeCtxs

// Extract (prefix, typeName, fieldName) for CURIE-spelled references.
let private iriContextsFromEntry = extractIriContexts prefixOfCurie

// Match an absolute IRI against DeclaredPrefixes values, returning the matching prefix key.
// Requires a namespace boundary delimiter (#/) so "tictactoe-extra#..." is not attributed to "tictactoe".
let private namespacePrefixOfAbsIri (declaredPrefixes: Map<string, string>) (iri: string) : string option =
    if not (iri.Contains("://")) then
        None
    else
        declaredPrefixes
        |> Map.tryFindKey (fun _ nsIri ->
            iri.StartsWith(nsIri, System.StringComparison.Ordinal)
            && (nsIri.EndsWith("#", System.StringComparison.Ordinal)
                || nsIri.EndsWith("/", System.StringComparison.Ordinal)
                || (iri.Length > nsIri.Length
                    && (iri.[nsIri.Length] = '#' || iri.[nsIri.Length] = '/'))))

// Extract (prefix, typeName, fieldName) for absolute-IRI-spelled references (item 2).
let private absIriContextsFromEntry (declaredPrefixes: Map<string, string>) =
    extractIriContexts (namespacePrefixOfAbsIri declaredPrefixes)

// Classify referenced prefixes via the SHARED classifyReferencedVocab (single oracle).
// Warn set = { Undereferenceable } only. Decision is VocabState only; location is enrichment.
let private collectVocabWarnings (lf: LockFile) (entries: ResolvedEntry list) : VocabWarning list =
    let curieCtxs = entries |> List.collect iriContextsFromEntry
    let absCtxs = entries |> List.collect (absIriContextsFromEntry lf.DeclaredPrefixes)
    let allCtxs = curieCtxs @ absCtxs
    let uniquePrefixes = allCtxs |> List.map (fun (p, _, _) -> p) |> List.distinct

    if List.isEmpty uniquePrefixes then
        []
    else
        let now = System.DateTimeOffset.UtcNow
        let states = classifyReferencedVocab lf now uniquePrefixes
        let stateMap = List.zip uniquePrefixes states |> Map.ofList

        allCtxs
        |> List.choose (fun (prefix, typeName, fieldName) ->
            match Map.tryFind prefix stateMap with
            | Some VocabState.Undereferenceable ->
                // Item 1: skip when prefix has no declared IRI — avoids emitting a non-IRI token as iri.
                // Live guard: buildPrefixMap (used in partitionByIri) includes Vocabularies keys, so a
                // CURIE whose prefix is a Vocabulary key but absent from DeclaredPrefixes resolves,
                // survives partitioning, and reaches here as Undereferenceable (lookupEntry keys on
                // DeclaredPrefixes → None). Returning None suppresses the warning rather than substituting
                // the bare prefix label as a namespace IRI.
                match Map.tryFind prefix lf.DeclaredPrefixes with
                | None -> None
                | Some iri ->
                    Some
                        { Prefix = prefix
                          State = VocabState.Undereferenceable
                          Iri = iri
                          Location = Some { Type = typeName; Field = fieldName }
                          Hint = vocabWarningHint iri }
            | _ -> None)
        |> List.distinctBy (fun w ->
            w.Prefix, w.Location |> Option.map (fun l -> l.Type), w.Location |> Option.bind (fun l -> l.Field))

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

    let toExclude = inLock |> List.filter (fun e -> e.Status = Some Excluded)
    let notExcluded = inLock |> List.filter (fun e -> e.Status <> Some Excluded)

    let nullIriRejected =
        notExcluded
        |> List.filter (fun e -> e.Iri.IsNone)
        |> List.map (fun e ->
            { FSharpType = e.FSharpType
              Reason = "iri is required for a confirmed mapping" })

    let withIri = notExcluded |> List.filter (fun e -> e.Iri.IsSome)

    let prefixes = buildPrefixMap lf.Vocabularies lf.DeclaredPrefixes

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

    let warnings = collectVocabWarnings lf toMerge

    let summary =
        { Merged = toMerge.Length
          Excluded = toExclude.Length
          Rejected = notInLock @ nullIriRejected @ iriRejected
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

// ── Public: shared vocab-warning JSON helpers ─────────────────────────────────

let private vocabWarningToJsonObject (w: VocabWarning) : JsonObject =
    let entry = JsonObject()
    entry.Add("prefix", JsonValue.Create w.Prefix)
    entry.Add("state", JsonValue.Create(vocabStateToString w.State))
    entry.Add("iri", JsonValue.Create w.Iri)

    match w.Location with
    | Some loc ->
        entry.Add("type", JsonValue.Create loc.Type)

        match loc.Field with
        | Some f -> entry.Add("field", JsonValue.Create f)
        | None -> entry.Add("field", JsonNode.op_Implicit null)
    | None ->
        entry.Add("type", JsonNode.op_Implicit null)
        entry.Add("field", JsonNode.op_Implicit null)

    entry.Add("hint", JsonValue.Create w.Hint)
    entry

/// Serialize a list of VocabWarnings as a JSON array string.
/// Used by the status --format json path (standalone array) and accept --format json (embedded in summary).
let vocabWarningsToJson (warnings: VocabWarning list) : string =
    let arr = JsonArray()

    for w in warnings do
        arr.Add(vocabWarningToJsonObject w)

    let opts = JsonSerializerOptions(WriteIndented = false)
    arr.ToJsonString(opts)

let private conventionDiagnosticToJsonObject (d: ConventionDiagnostic) : JsonObject =
    let entry = JsonObject()

    match d with
    | EquivalentClassCollapse(fsharpType, explicitIri) ->
        entry.Add("notice", JsonValue.Create "equivalentClassCollapse")
        entry.Add("fsharpType", JsonValue.Create fsharpType)
        entry.Add("explicitIri", JsonValue.Create explicitIri)
    | AmbiguousLocalNameDropped(category, localName, iris) ->
        entry.Add("notice", JsonValue.Create "ambiguousLocalNameDropped")
        entry.Add("category", JsonValue.Create category)
        entry.Add("localName", JsonValue.Create localName)
        let irisArr = JsonArray()

        for iri in iris do
            irisArr.Add(JsonValue.Create iri)

        entry.Add("iris", irisArr)

    entry

/// Serialize one ConventionDiagnostic as a single-line JSON object string. Mirrors
/// vocabWarningToJsonObject's JsonObject-based construction — string fields aren't
/// guaranteed quote/backslash-free (F# generic type FullNames can contain
/// backticks/angle brackets), so this serializes properly instead of hand-built
/// printfn string interpolation. Callers print one of these per diagnostic, one per line
/// (see Frank.Cli's printConventionDiagnostics) — never wrapped in a JSON array.
let conventionDiagnosticToJson (d: ConventionDiagnostic) : string =
    let opts = JsonSerializerOptions(WriteIndented = false)
    (conventionDiagnosticToJsonObject d).ToJsonString(opts)

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
        warningsArr.Add(vocabWarningToJsonObject w)

    let root = JsonObject()
    root.Add("merged", JsonValue.Create s.Merged)
    root.Add("excluded", JsonValue.Create s.Excluded)
    root.Add("rejected", rejectedArr)
    root.Add("unchanged", JsonValue.Create s.Unchanged)
    root.Add("alreadyConfirmed", JsonValue.Create s.AlreadyConfirmed)
    root.Add("fieldsUnresolved", JsonValue.Create s.FieldsUnresolved)
    root.Add("warnings", warningsArr)
    root.ToJsonString summaryWriteOptions
