module Frank.Cli.Core.Clarify

open System.Text
open System.Text.Encodings.Web
open System.Text.Json
open System.Text.Json.Nodes
open Frank.Semantic
open Frank.Semantic.LockFile

// ── JSON helpers ──────────────────────────────────────────────────────────────

let private writeOptions =
    JsonSerializerOptions(WriteIndented = true, Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping)

let private iriNode (iri: string option) : JsonNode =
    match iri with
    | Some s -> JsonValue.Create s
    | None -> JsonValue.Create<obj> null

let private fieldNode (f: FieldMapping) : JsonObject =
    let obj = JsonObject()
    obj.Add("name", JsonValue.Create f.Name)
    obj.Add("iri", iriNode f.Iri)
    obj.Add("confidence", JsonValue.Create f.Confidence)
    obj.Add("status", JsonValue.Create(LockFile.mappingStatusToString f.Status))
    obj

let private fieldsArray (fields: FieldMapping list) : JsonArray =
    let arr = JsonArray()

    for f in fields do
        arr.Add(fieldNode f)

    arr

let private candidatesArray (alternates: string list) : JsonArray =
    let arr = JsonArray()

    for a in alternates do
        arr.Add(JsonValue.Create a)

    arr

let private unresolvedNode (m: Mapping) : JsonObject =
    let obj = JsonObject()
    obj.Add("fsharpType", JsonValue.Create m.FSharpType)
    obj.Add("candidates", candidatesArray m.Alternates)
    obj.Add("fields", fieldsArray m.Fields)
    obj

let private proposedNode (m: Mapping) : JsonObject =
    let obj = JsonObject()
    obj.Add("fsharpType", JsonValue.Create m.FSharpType)
    obj.Add("currentCandidate", iriNode m.Iri)
    obj.Add("confidence", JsonValue.Create m.Confidence)
    obj.Add("candidates", candidatesArray m.Alternates)
    obj.Add("fields", fieldsArray m.Fields)
    obj

let private partitionMappings (mappings: Mapping list) : Mapping list * Mapping list =
    mappings
    |> List.fold
        (fun (unresolved, proposed) m ->
            match m.Status with
            | Unresolved -> (m :: unresolved, proposed)
            | Proposed -> (unresolved, m :: proposed)
            | Confirmed
            | Excluded -> (unresolved, proposed))
        ([], [])
    |> fun (u, p) -> (List.rev u, List.rev p)

// ── Public API ────────────────────────────────────────────────────────────────

let toJson (lf: LockFile) : string =
    let unresolved, proposed = partitionMappings lf.Mappings
    let root = JsonObject()
    root.Add("schemaVersion", JsonValue.Create 1)

    let unresolvedArr = JsonArray()

    for m in unresolved do
        unresolvedArr.Add(unresolvedNode m)

    root.Add("unresolved", unresolvedArr)

    let proposedArr = JsonArray()

    for m in proposed do
        proposedArr.Add(proposedNode m)

    root.Add("proposed", proposedArr)
    root.ToJsonString writeOptions

let private templateFieldNode (f: FieldMapping) : JsonObject =
    let obj = JsonObject()
    obj.Add("name", JsonValue.Create f.Name)
    obj.Add("iri", iriNode f.Iri)
    obj

let private templateResolvedNode (m: Mapping) : JsonObject =
    let obj = JsonObject()
    obj.Add("fsharpType", JsonValue.Create m.FSharpType)
    obj.Add("iri", iriNode m.Iri)

    let fields = JsonArray()

    for f in m.Fields do
        fields.Add(templateFieldNode f)

    obj.Add("fields", fields)
    obj

let toResolvedTemplate (lf: LockFile) : string =
    let unresolved, proposed = partitionMappings lf.Mappings
    let root = JsonObject()
    root.Add("schemaVersion", JsonValue.Create 1)

    let resolvedArr = JsonArray()

    for m in proposed do
        resolvedArr.Add(templateResolvedNode m)

    for m in unresolved do
        resolvedArr.Add(templateResolvedNode m)

    root.Add("resolved", resolvedArr)
    root.ToJsonString writeOptions

// ── Markdown helpers ──────────────────────────────────────────────────────────

let private iriText (iri: string option) : string = iri |> Option.defaultValue "null"

let private fieldTable (fields: FieldMapping list) : string =
    let sb = StringBuilder()
    sb.AppendLine("| Field | IRI | Confidence | Status |") |> ignore
    sb.AppendLine("|-------|-----|------------|--------|") |> ignore

    for f in fields do
        let row =
            sprintf
                "| %s | %s | %.2f | %s |"
                f.Name
                (iriText f.Iri)
                f.Confidence
                (LockFile.mappingStatusToString f.Status)

        sb.AppendLine(row) |> ignore

    sb.ToString()

let private unresolvedSection (mappings: Mapping list) : string =
    let sb = StringBuilder()
    sb.AppendLine("## Unresolved") |> ignore

    for m in mappings do
        sb.AppendLine(sprintf "### %s" m.FSharpType) |> ignore

        if m.Alternates.IsEmpty then
            sb.AppendLine("Candidates: none") |> ignore
        else
            let joined = m.Alternates |> String.concat ", "
            sb.AppendLine(sprintf "Candidates: %s" joined) |> ignore

        sb.AppendLine(fieldTable m.Fields) |> ignore

    sb.ToString()

let private proposedSection (mappings: Mapping list) : string =
    let sb = StringBuilder()
    sb.AppendLine("## Proposed") |> ignore

    for m in mappings do
        sb.AppendLine(sprintf "### %s" m.FSharpType) |> ignore

        sb.AppendLine(sprintf "Current: %s (confidence %.2f)" (iriText m.Iri) m.Confidence)
        |> ignore

        sb.AppendLine(fieldTable m.Fields) |> ignore

    sb.ToString()

let toMarkdown (lf: LockFile) : string =
    let unresolved, proposed = partitionMappings lf.Mappings

    let sb = StringBuilder()
    sb.AppendLine("# Semantic clarify") |> ignore
    sb.AppendLine("Schema version: 1") |> ignore
    sb.AppendLine() |> ignore
    sb.Append(unresolvedSection unresolved) |> ignore
    sb.Append(proposedSection proposed) |> ignore
    sb.ToString()
