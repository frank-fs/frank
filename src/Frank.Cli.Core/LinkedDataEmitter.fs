module Frank.Cli.Core.LinkedDataEmitter

open System
open Frank.Semantic
open Frank.Semantic.LockFile

// ── @context rendering ──────────────────────────────────────────────────────

/// Build the JSON-LD @context string from the model's Using set and Prefixes map.
/// For each `using` prefix, include the external base URI (trimmed of trailing slash).
/// Returns Error if any using prefix is not in Prefixes.
let private buildContext (model: ResolvedModel) : Result<string, string> =
    let rec loop (remaining: string list) (acc: string list) =
        match remaining with
        | [] -> Ok(List.rev acc)
        | prefix :: rest ->
            match Map.tryFind prefix model.Prefixes with
            | None -> Error $"using prefix '{prefix}' not found in Prefixes"
            | Some baseUri ->
                let uri = baseUri.AbsoluteUri.TrimEnd('/')
                loop rest (uri :: acc)

    match loop (Set.toList model.Using) [] with
    | Error e -> Error e
    | Ok uris ->
        let items = uris |> List.map (fun u -> "\"" + u + "\"") |> String.concat ","
        Ok("{\"@context\":[" + items + "]}")

// ── Triple rendering ─────────────────────────────────────────────────────────

/// Escape a string for use in an F# string literal (double-quoted).
let private esc (s: string) : string =
    s.Replace("\\", "\\\\").Replace("\"", "\\\"")

/// Render one triple assertion: g.Assert(g.CreateUriNode(UriFactory.Create("S")), pred, obj)
let private assertTriple (subject: string) (predNs: string) (predLocal: string) (objExpr: string) : string =
    let s = "UriFactory.Create(\"" + esc subject + "\")"
    let p = $"{predNs}:{predLocal}"

    "    g.Assert(Triple(g.CreateUriNode("
    + s
    + "), "
    + "g.CreateUriNode(g.ResolveQName(\""
    + p
    + "\")), "
    + objExpr
    + "))"

/// Render a URI node expression.
let private uriNode (iri: string) : string =
    "g.CreateUriNode(UriFactory.Create(\"" + esc iri + "\"))"

/// Render a QName node expression (uses g.ResolveQName).
let private qnameNode (qname: string) : string =
    "g.CreateUriNode(g.ResolveQName(\"" + esc qname + "\"))"

/// Render all triples for one resolved resource. Returns [] if no ClassIri.
let private typeTriples (r: ResolvedResource) : string list =
    match r.ClassIri with
    | None -> []
    | Some classUri ->
        let subjIri = classUri.AbsoluteUri
        let typeTriple = assertTriple subjIri "rdf" "type" (qnameNode "owl:Class")

        let equivTriple =
            match r.EquivalentClass with
            | None -> []
            | Some equivUri -> [ assertTriple subjIri "owl" "equivalentClass" (uriNode equivUri.AbsoluteUri) ]

        let seeAlsoTriples =
            r.SeeAlso
            |> List.map (fun u -> assertTriple subjIri "rdfs" "seeAlso" (uriNode u.AbsoluteUri))

        typeTriple :: equivTriple @ seeAlsoTriples

/// Render all triples for one resolved field. Returns [] if no field Iri.
let private fieldTriples (subjIri: string) (f: ResolvedField) : string list =
    match f.Iri with
    | None -> []
    | Some fieldUri ->
        let fieldIri = fieldUri.AbsoluteUri
        let propTriple = assertTriple fieldIri "rdf" "type" (qnameNode "rdf:Property")
        let domainTriple = assertTriple fieldIri "rdfs" "domain" (uriNode subjIri)
        [ propTriple; domainTriple ]

/// Collect all triple lines for all resources in the model. Returns Error on first failure.
let private collectTriples (resources: ResolvedResource list) : string list =
    let resourceLines (r: ResolvedResource) =
        let tLines = typeTriples r

        let fLines =
            match r.ClassIri with
            | None -> []
            | Some classUri -> r.Fields |> List.collect (fieldTriples classUri.AbsoluteUri)

        tLines @ fLines

    resources |> List.collect resourceLines

// ── Namespace setup rendering ───────────────────────────────────────────────

/// Render the namespace registration lines for well-known ontology prefixes.
let private namespaceSetup: string list =
    [ "    g.NamespaceMap.AddNamespace(\"rdf\", UriFactory.Create(\"http://www.w3.org/1999/02/22-rdf-syntax-ns#\"))"
      "    g.NamespaceMap.AddNamespace(\"rdfs\", UriFactory.Create(\"http://www.w3.org/2000/01/rdf-schema#\"))"
      "    g.NamespaceMap.AddNamespace(\"owl\", UriFactory.Create(\"http://www.w3.org/2002/07/owl#\"))" ]

// ── Module assembly ──────────────────────────────────────────────────────────

/// Assemble the full F# module source string.
let private assembleModule (moduleName: string) (contextJson: string) (tripleLines: string list) : string =
    let graphBody =
        match tripleLines with
        | [] -> "    g"
        | lines -> (String.concat "\n" lines) + "\n    g"

    String.concat
        "\n"
        [ $"module {moduleName}"
          ""
          "open VDS.RDF"
          "open VDS.RDF.Parsing"
          ""
          "let jsonLdContext : string ="
          "    \"\"\"" + contextJson + "\"\"\""
          ""
          "let graph : IGraph ="
          "    let g = new Graph()"
          (String.concat "\n" namespaceSetup)
          graphBody
          "" ]

// ── Public API ───────────────────────────────────────────────────────────────

/// Emit a GeneratedLinkedData F# module from a vocabulary registry and lock file.
///
/// moduleName — the F# module name to emit (e.g. "TicTacToe.GeneratedLinkedData")
/// registry   — the VocabularyRegistry providing prefix→URI mappings, Using set,
///              SeeAlso, and EquivalentClasses (keyed by FSharpType FullName string)
/// lock       — the resolved lock file
///
/// Returns Ok with the F# source string, or Error if any IRI references an unknown prefix.
let emit (moduleName: string) (registry: VocabularyRegistry) (lock: LockFile) : Result<string, string> =
    if String.IsNullOrWhiteSpace moduleName then
        invalidArg (nameof moduleName) "moduleName must not be empty"

    match ResolvedModel.build registry lock with
    | Error e -> Error e
    | Ok model ->
        match buildContext model with
        | Error e -> Error e
        | Ok contextJson ->
            let tripleLines = collectTriples model.Resources
            Ok(assembleModule moduleName contextJson tripleLines)
