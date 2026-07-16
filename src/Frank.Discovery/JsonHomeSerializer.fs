module internal Frank.Discovery.JsonHomeSerializer

open System.Text
open System.Text.Json
open System.Text.RegularExpressions

let private rfc6570Operators = Set.ofList [ '+'; '#'; '.'; '/'; ';'; '?'; '&' ]

let private stripModifier (varspec: string) : string =
    if varspec.EndsWith("*") then
        varspec.[.. varspec.Length - 2]
    else
        let colonIdx = varspec.IndexOf(':')
        if colonIdx >= 0 then varspec.[.. colonIdx - 1] else varspec

let private parseExpression (expr: string) : string list =
    let body =
        if expr.Length > 0 && Set.contains expr.[0] rfc6570Operators then
            expr.[1..]
        else
            expr

    body.Split(',')
    |> Array.toList
    |> List.map stripModifier
    |> List.filter (fun s -> s <> "")

/// Extract bare variable names from a URI Template per RFC 6570.
/// `{id}` → ["id"]; `{+base}` → ["base"]; `{x,y}` → ["x"; "y"]; `{x:3}` → ["x"]; `{list*}` → ["list"].
let extractTemplateVars (template: string) : string list =
    if isNull template then
        invalidArg (nameof template) "URI template must not be null"

    Regex.Matches(template, @"\{([^}]+)\}")
    |> Seq.cast<Match>
    |> Seq.collect (fun m -> parseExpression m.Groups.[1].Value)
    |> Seq.toList

let private writeHrefVar (writer: Utf8JsonWriter) (href: string) (hrefVars: Map<string, string>) (v: string) : unit =
    match hrefVars |> Map.tryFind v with
    | None ->
        invalidOp
            $"JSON Home template variable '{v}' in href '{href}' has no derived meaning IRI. Ensure the semantic model maps this field."
    | Some meaning -> writer.WriteString(v, meaning)

let private writeHrefVars (writer: Utf8JsonWriter) (href: string) (hrefVars: Map<string, string>) : unit =
    let vars = extractTemplateVars href

    if not vars.IsEmpty then
        writer.WritePropertyName("href-vars")
        writer.WriteStartObject()

        for v in vars do
            writeHrefVar writer href hrefVars v

        writer.WriteEndObject()

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
            writeHrefVars writer r.Href r.HrefVars
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
