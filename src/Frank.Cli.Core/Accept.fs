module Frank.Cli.Core.Accept

open System.Text.Json.Nodes
open Frank.Semantic
open Frank.Semantic.LockFile

type ResolvedField = { Name: string; Iri: string option }

type ResolvedCase =
    { Name: string
      Iri: string option
      Payload: ResolvedField list }

type ResolvedShape =
    | RecordShape of ResolvedField list
    | UnionShape of ResolvedCase list

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
                | Ok xs, Ok x -> Ok(xs @ [ x ]))
            (Ok [])
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
            | null -> parseFieldsArray node.["fields"] |> Result.map RecordShape
            | casesNode -> parseCasesArray casesNode |> Result.map UnionShape

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
            | Ok xs, Ok x -> Ok(xs @ [ x ]))
        (Ok [])

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
    | RecordShape fs -> MappingShape.Record(fs |> List.map (buildFieldMapping source))
    | UnionShape cs -> MappingShape.Union(cs |> List.map (buildCaseMapping source))

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

let private checkIri (prefixes: Map<string, System.Uri>) (label: string) (iri: string) : string option =
    match VocabularyRegistry.tryResolveIri prefixes (Some iri) with
    | Error msg -> Some $"unresolvable {label} '{iri}': {msg}; use CURIE form (e.g. schema:Foo)"
    | Ok _ -> None

let private firstCaseIriError (prefixes: Map<string, System.Uri>) (c: ResolvedCase) : string option =
    match c.Iri |> Option.bind (checkIri prefixes "case iri") with
    | Some err -> Some err
    | None ->
        c.Payload
        |> List.tryPick (fun f -> f.Iri |> Option.bind (checkIri prefixes "payload iri"))

let private firstShapeIriError (prefixes: Map<string, System.Uri>) (shape: ResolvedShape) : string option =
    match shape with
    | RecordShape fs ->
        fs
        |> List.tryPick (fun f -> f.Iri |> Option.bind (checkIri prefixes "field iri"))
    | UnionShape cs -> cs |> List.tryPick (firstCaseIriError prefixes)

let private firstIriError (prefixes: Map<string, System.Uri>) (e: ResolvedEntry) : string option =
    match e.Iri |> Option.bind (checkIri prefixes "iri") with
    | Some err -> Some err
    | None -> firstShapeIriError prefixes e.Shape

let private partitionByIri
    (prefixes: Map<string, System.Uri>)
    (entries: ResolvedEntry list)
    : RejectedEntry list * ResolvedEntry list =
    let folder (rejected, ok) e =
        match firstIriError prefixes e with
        | Some reason ->
            ({ FSharpType = e.FSharpType
               Reason = reason }
             :: rejected),
            ok
        | None -> rejected, (e :: ok)

    let rejected, ok = List.fold folder ([], []) entries
    List.rev rejected, List.rev ok

let apply (lf: LockFile) (doc: ResolvedDoc) (source: MappingSource) : LockFile * AcceptSummary =
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

    let iriRejected, toMerge = partitionByIri prefixes withIri

    let alreadyConfirmed =
        toMerge
        |> List.filter (fun e ->
            lf.Mappings
            |> List.exists (fun m -> m.FSharpType = e.FSharpType && m.Status = Confirmed))
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

// ── Public: summaryToJson ─────────────────────────────────────────────────────

let summaryToJson (s: AcceptSummary) : string =
    let rejectedItems =
        s.Rejected
        |> List.map (fun r -> $"""  {{"fsharpType":"{r.FSharpType}","reason":"{r.Reason}"}}""")
        |> String.concat ","

    let rejectedArr = if s.Rejected.IsEmpty then "[]" else $"[{rejectedItems}]"

    $"""{{"merged":{s.Merged},"rejected":{rejectedArr},"unchanged":{s.Unchanged},"alreadyConfirmed":{s.AlreadyConfirmed},"fieldsUnresolved":{s.FieldsUnresolved}}}"""
