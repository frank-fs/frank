module Frank.Cli.Core.LinkedDataEmitter

open System
open Frank.Semantic
open Frank.Semantic.LockFile

// ── IRI resolution ─────────────────────────────────────────────────────────

/// Resolve an optional prefixed IRI to its full string.
/// Error if prefixed and prefix unknown. Ok None if iriOpt is None.
let private resolveOptional (prefixes: Map<string, Uri>) (iriOpt: string option) : Result<string option, string> =
    match iriOpt with
    | None -> Ok None
    | Some iri ->
        match iri.IndexOf(':') with
        | -1 -> Ok(Some iri)
        | idx ->
            let prefix = iri.[.. idx - 1]
            let local = iri.[idx + 1 ..]

            match Map.tryFind prefix prefixes with
            | None -> Error $"Unknown prefix '{prefix}' in IRI '{iri}'"
            | Some baseUri -> Ok(Some(baseUri.AbsoluteUri + local))

// ── @context rendering ──────────────────────────────────────────────────────

/// Build the JSON-LD @context string from the Using set and Prefixes map.
/// For each `using` prefix, include the external base URI (trimmed of trailing slash).
/// Returns Error if any using prefix is not in Prefixes.
let private buildContext (registry: VocabularyRegistry) : Result<string, string> =
    let rec loop (remaining: string list) (acc: string list) =
        match remaining with
        | [] -> Ok(List.rev acc)
        | prefix :: rest ->
            match Map.tryFind prefix registry.Prefixes with
            | None -> Error $"using prefix '{prefix}' not found in Prefixes"
            | Some baseUri ->
                let uri = baseUri.AbsoluteUri.TrimEnd('/')
                loop rest (uri :: acc)

    match loop (Set.toList registry.Using) [] with
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

/// Render all triples for one type mapping. Returns Error on unknown prefix.
let private typeTriples
    (prefixes: Map<string, Uri>)
    (seeAlso: Map<string, Uri list>)
    (equivClasses: Map<string, Uri>)
    (m: Mapping)
    : Result<string list, string> =
    match resolveOptional prefixes m.Iri with
    | Error e -> Error $"type '{m.FSharpType}': {e}"
    | Ok None -> Ok []
    | Ok(Some subjIri) ->
        let typeTriple = assertTriple subjIri "rdf" "type" (qnameNode "owl:Class")

        let equivTriple =
            match Map.tryFind m.FSharpType equivClasses with
            | None -> []
            | Some equivUri -> [ assertTriple subjIri "owl" "equivalentClass" (uriNode equivUri.AbsoluteUri) ]

        let seeAlsoTriples =
            match Map.tryFind m.FSharpType seeAlso with
            | None -> []
            | Some uris ->
                uris
                |> List.map (fun u -> assertTriple subjIri "rdfs" "seeAlso" (uriNode u.AbsoluteUri))

        Ok(typeTriple :: equivTriple @ seeAlsoTriples)

/// Render all triples for one field mapping. Returns Error on unknown prefix.
let private fieldTriples
    (prefixes: Map<string, Uri>)
    (typeFSharpName: string)
    (subjIri: string)
    (f: FieldMapping)
    : Result<string list, string> =
    match resolveOptional prefixes f.Iri with
    | Error e -> Error $"field '{typeFSharpName}.{f.Name}': {e}"
    | Ok None -> Ok []
    | Ok(Some fieldIri) ->
        let propTriple = assertTriple fieldIri "rdf" "type" (qnameNode "rdf:Property")

        let domainTriple = assertTriple fieldIri "rdfs" "domain" (uriNode subjIri)

        Ok [ propTriple; domainTriple ]

/// Collect all triple lines for all mappings in the lock. Returns Error on first failure.
let private collectTriples
    (prefixes: Map<string, Uri>)
    (seeAlso: Map<string, Uri list>)
    (equivClasses: Map<string, Uri>)
    (mappings: Mapping list)
    : Result<string list, string> =
    let rec loop (remaining: Mapping list) (acc: string list) =
        match remaining with
        | [] -> Ok(List.rev acc)
        | m :: rest ->
            match typeTriples prefixes seeAlso equivClasses m with
            | Error e -> Error e
            | Ok tLines ->
                match resolveOptional prefixes m.Iri with
                | Error e -> Error $"type '{m.FSharpType}': {e}"
                | Ok subjOpt ->
                    let subjIri = defaultArg subjOpt ""

                    match collectFieldTriples prefixes m.FSharpType subjIri m.Fields [] with
                    | Error e -> Error e
                    | Ok fLines -> loop rest (List.rev (fLines @ tLines) @ acc)

    and collectFieldTriples
        (prefixes: Map<string, Uri>)
        (fsType: string)
        (subjIri: string)
        (fields: FieldMapping list)
        (acc: string list)
        : Result<string list, string> =
        match fields with
        | [] -> Ok(List.rev acc)
        | _ when String.IsNullOrEmpty subjIri -> Ok(List.rev acc)
        | f :: rest ->
            match fieldTriples prefixes fsType subjIri f with
            | Error e -> Error e
            | Ok lines -> collectFieldTriples prefixes fsType subjIri rest (List.rev lines @ acc)

    loop mappings []

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

    match buildContext registry with
    | Error e -> Error e
    | Ok contextJson ->
        match collectTriples registry.Prefixes registry.SeeAlso registry.EquivalentClasses lock.Mappings with
        | Error e -> Error e
        | Ok tripleLines -> Ok(assembleModule moduleName contextJson tripleLines)
