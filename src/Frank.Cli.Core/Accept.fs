module Frank.Cli.Core.Accept

open System.Text.Json.Nodes
open Frank.Semantic
open Frank.Semantic.LockFile

type ResolvedField = { Name: string; Iri: string option }

type ResolvedEntry =
    { FSharpType: string
      Iri: string option
      Fields: ResolvedField list }

type ResolvedDoc = { SchemaVersion: int; Resolved: ResolvedEntry list }

type AcceptSummary =
    { Merged: int
      Rejected: string list
      Unchanged: int }

// ── Parse helpers ─────────────────────────────────────────────────────────────

let private supportedVersion = 1

let private parseFields (node: JsonNode) : ResolvedField list =
    if node = null then
        []
    else
        [ for item in node.AsArray() do
              let name =
                  match item.["name"] with
                  | null -> ""
                  | n -> n.GetValue<string>()

              let iri =
                  match item.["iri"] with
                  | null -> None
                  | n ->
                      try
                          Some(n.GetValue<string>())
                      with _ ->
                          None

              yield { Name = name; Iri = iri } ]

let private parseEntry (i: int) (node: JsonNode) : Result<ResolvedEntry, string> =
    let fsType =
        match node.["fsharpType"] with
        | null -> ""
        | n -> try n.GetValue<string>() with _ -> ""

    if System.String.IsNullOrEmpty fsType then
        Error $"resolved[{i}]: fsharpType is required"
    else

    let iri =
        match node.["iri"] with
        | null -> None
        | n ->
            try
                let s = n.GetValue<string>()
                if s = null then None else Some s
            with _ ->
                None

    if iri.IsNone && isNull node.["iri"] then
        Error $"resolved[{i}]: iri is required"
    else

    Ok
        { FSharpType = fsType
          Iri = iri
          Fields = parseFields node.["fields"] }

let private parseEntries (arr: JsonArray) : Result<ResolvedEntry list, string> =
    let mutable acc : Result<ResolvedEntry list, string> = Ok []
    let mutable i = 0

    while i < arr.Count && Result.isOk acc do
        match parseEntry i arr.[i] with
        | Error e -> acc <- Error e
        | Ok entry ->
            acc <- acc |> Result.map (fun xs -> xs @ [ entry ])
            i <- i + 1

    acc

// ── Public: parseResolved ─────────────────────────────────────────────────────

let parseResolved (json: string) : Result<ResolvedDoc, string> =
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

    if version <> supportedVersion then
        Error $"schema version {version} not supported"
    else

    let resolvedNode = node.["resolved"]

    if resolvedNode = null then
        Ok { SchemaVersion = version; Resolved = [] }
    else

    match parseEntries (resolvedNode.AsArray()) with
    | Error e -> Error e
    | Ok entries -> Ok { SchemaVersion = version; Resolved = entries }

// ── Apply helpers ─────────────────────────────────────────────────────────────

let private buildFieldMapping (source: MappingSource) (rf: ResolvedField) : FieldMapping =
    { Name = rf.Name
      Iri = rf.Iri
      Confidence = 1.0
      Source = source
      Status = Confirmed }

let private buildMapping (source: MappingSource) (entry: ResolvedEntry) : Mapping =
    { FSharpType = entry.FSharpType
      Iri = entry.Iri
      Confidence = 1.0
      Source = source
      Status = Confirmed
      Alternates = []
      Fields = entry.Fields |> List.map (buildFieldMapping source) }

// ── Public: apply ─────────────────────────────────────────────────────────────

let apply (lf: LockFile) (doc: ResolvedDoc) (source: MappingSource) : LockFile * AcceptSummary =
    let lockTypes = lf.Mappings |> List.map (fun m -> m.FSharpType) |> Set.ofList
    let known, unknown = doc.Resolved |> List.partition (fun e -> Set.contains e.FSharpType lockTypes)
    let knownMappings = known |> List.map (buildMapping source)
    let knownTypes = known |> List.map (fun e -> e.FSharpType) |> Set.ofList
    let unchanged = lf.Mappings |> List.filter (fun m -> not (Set.contains m.FSharpType knownTypes)) |> List.length

    let updated = LockFile.merge lf knownMappings

    let summary =
        { Merged = known.Length
          Rejected = unknown |> List.map (fun e -> e.FSharpType)
          Unchanged = unchanged }

    updated, summary
