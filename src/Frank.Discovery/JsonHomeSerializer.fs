module Frank.Discovery.JsonHomeSerializer

open System.Text
open System.Text.Json
open System.Text.RegularExpressions

/// Extract URI Template variable names from a template string.
/// E.g. "/games/{id}/moves/{moveId}" → ["id"; "moveId"].
let private templateVarNames (template: string) : string list =
    Regex.Matches(template, @"\{([^}]+)\}")
    |> Seq.cast<Match>
    |> Seq.map (fun m -> m.Groups.[1].Value)
    |> Seq.toList

/// Serialize resource entries to a JSON Home document. A URI Template (contains
/// '{') is written as `href-template` with a companion `href-vars` object (JSON
/// Home draft §4.2). A fixed URI is written as `href` (RFC draft-nottingham
/// -json-home-06).
let serialize (resources: JsonHomeResource list) : string =
    use ms = new System.IO.MemoryStream()
    use writer = new Utf8JsonWriter(ms)
    writer.WriteStartObject()
    writer.WritePropertyName("resources")
    writer.WriteStartObject()

    for r in resources do
        writer.WritePropertyName(r.Relation)
        writer.WriteStartObject()

        if r.Href.Contains "{" then
            writer.WriteString("href-template", r.Href)
            let vars = templateVarNames r.Href

            if not vars.IsEmpty then
                writer.WritePropertyName("href-vars")
                writer.WriteStartObject()

                for v in vars do
                    match r.HrefVars |> Map.tryFind v with
                    | None ->
                        invalidOp
                            $"JSON Home template variable '{v}' in href '{r.Href}' has no derived meaning IRI. Ensure the semantic model maps this field."
                    | Some meaning -> writer.WriteString(v, meaning)

                writer.WriteEndObject()
        else
            writer.WriteString("href", r.Href)

        writer.WritePropertyName("hints")
        writer.WriteStartObject()
        writer.WritePropertyName("allow")
        writer.WriteStartArray()

        for m in r.Allow do
            writer.WriteStringValue(m)

        writer.WriteEndArray()
        writer.WriteEndObject()
        writer.WriteEndObject()

    writer.WriteEndObject()
    writer.WriteEndObject()
    writer.Flush()
    Encoding.UTF8.GetString(ms.ToArray())
