module Frank.Cli.Core.Accept

open System.Text.Json.Nodes
open Frank.Semantic
open Frank.Semantic.LockFile

type ResolvedField = { Name: string; Iri: string option }

type ResolvedEntry =
    { FSharpType: string
      Iri: string option
      Fields: ResolvedField list }

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

let private parseFieldsArray (node: JsonNode) : Result<ResolvedField list, string> =
    match node with
    | null -> Ok []
    | :? JsonArray as arr ->
        arr
        |> Seq.mapi (fun i el -> parseField el |> Result.mapError (fun e -> $"fields[{i}]: {e}"))
        |> Seq.fold
            (fun acc r ->
                match acc, r with
                | Error e, _ -> Error e
                | _, Error e -> Error e
                | Ok xs, Ok x -> Ok(xs @ [ x ]))
            (Ok [])
    | _ -> Error "fields must be an array"

let private parseEntry (i: int) (node: JsonNode) : Result<ResolvedEntry, string> =
    requireString node "fsharpType"
    |> Result.mapError (fun _ -> $"resolved[{i}]: fsharpType is required")
    |> Result.bind (fun fsType ->
        parseFieldsArray node.["fields"]
        |> Result.mapError (fun e -> $"resolved[{i}]: {e}")
        |> Result.map (fun fields ->
            { FSharpType = fsType
              Iri = optionalString node "iri"
              Fields = fields }))

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

let private buildMapping (source: MappingSource) (entry: ResolvedEntry) (iri: string) : Mapping =
    { FSharpType = entry.FSharpType
      Iri = Some iri
      Confidence = 1.0
      Source = source
      Status = Confirmed
      Alternates = []
      Fields = entry.Fields |> List.map (buildFieldMapping source) }

let private countUnresolvedFields (mappings: Mapping list) (types: Set<string>) : int =
    mappings
    |> List.filter (fun m -> Set.contains m.FSharpType types)
    |> List.sumBy (fun m -> m.Fields |> List.filter (fun f -> f.Status = Unresolved) |> List.length)

// ── Public: apply ─────────────────────────────────────────────────────────────

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

    let toMerge = inLock |> List.filter (fun e -> e.Iri.IsSome)

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
          Rejected = notInLock @ nullIriRejected
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
